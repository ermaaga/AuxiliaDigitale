using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Identity;
using Auxilia.Application.Jobs;
using Auxilia.Application.Platform;
using Auxilia.Application.Platform.Modules;
using Auxilia.Diagnostics;
using Auxilia.Domain.Identity;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Results;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Auxilia.MigrationRunner.Cli;

/// <summary>
/// auxctl commands (tasks P1-09, E-01…E-06). Thin: parse, call the Managers, print the outcome. Connection strings
/// and secrets are never printed. Exit codes: 0 success, 1 usage, 2 failure.
/// </summary>
internal sealed class AuxctlCli
{
    public const int Success = 0;
    public const int UsageError = 1;
    public const int Failure = 2;

    /// <summary>Environment variable with the connection string of a database provided by a DBA (never on the command line).</summary>
    public const string ExistingDatabaseVariable = "AUXILIA_TENANT_CONNECTION";

    public const string Usage = """
        usage: auxctl <command>
          migrate catalog                 (then aligns catalog.modules with the module descriptors)
          migrate tenants (--tenant <slug> | --all)
          tenant provision --slug <slug> --name <display name> [--language it] [--time-zone Europe/Rome] [--existing-database]
                           (--existing-database reads the connection string from AUXILIA_TENANT_CONNECTION)
          tenant (suspend | reactivate | archive) --slug <slug>
          tenant list
          jobs list
          jobs run <job-code> (--tenant <slug> | --all)
          keys rotate                     (new token signing key; the previous one keeps validating for 2 h)
          clients add --client-id <id> --name <name> --type (WebBff | PlatformConsole | Mobile | Integration) [--origin <url>]
                      (a confidential client's secret is printed once)
          clients list
          platform users add --email <email> --name <display name>
                               (prints a one-use activation token: enrol TOTP and set the password in the console)
          platform users reset --email <email>     (clears password and TOTP, ends sessions, new activation token)
          platform users (enable | disable) --email <email>
          platform users list
          users reset-password --tenant <slug> --user <user name | e-mail> [--send-link]
                               (prints a one-use temporary password, to change at the next sign-in, and ends the
                                sessions; --send-link e-mails a reset link instead)
          users verify-legacy-hash         (reads a legacy BCrypt hash, then the password, from standard input)
          diagnostics registry [--output <file>]
        """;

    private readonly Func<IServiceProvider> services;
    private readonly TextWriter output;
    private readonly TextWriter error;
    private readonly TextReader input;

    /// <param name="input">Secrets are read from here (standard input), never from the command line.</param>
    public AuxctlCli(Func<IServiceProvider> services, TextWriter output, TextWriter error, TextReader? input = null)
    {
        this.services = services;
        this.output = output;
        this.error = error;
        this.input = input ?? TextReader.Null;
    }

    public async Task<int> RunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var command = CommandLine.Parse(args);
        try
        {
            return command switch
            {
                _ when command.Is("diagnostics", "registry") => await RegistryAsync(command),
                _ when command.Is("migrate", "catalog") => await MigrateCatalogAsync(cancellationToken),
                _ when command.Is("migrate", "tenants") => await MigrateTenantsAsync(command, cancellationToken),
                _ when command.Is("tenant", "provision") => await InScopeAsync(scope => ProvisionAsync(scope, command, cancellationToken)),
                _ when command.Is("tenant", "list") => await InScopeAsync(scope => ListTenantsAsync(scope, cancellationToken)),
                _ when command.Is("tenant") && command.Word(1) is "suspend" or "reactivate" or "archive" =>
                    await InScopeAsync(scope => ChangeStatusAsync(scope, command, cancellationToken)),
                _ when command.Is("keys", "rotate") => await InScopeAsync(scope => RotateKeysAsync(scope, cancellationToken)),
                _ when command.Is("clients", "add") => await InScopeAsync(scope => AddClientAsync(scope, command, cancellationToken)),
                _ when command.Is("clients", "list") => await InScopeAsync(scope => ListClientsAsync(scope, cancellationToken)),
                _ when command.Is("platform", "users") && command.Word(2) is "add" or "reset" or "enable" or "disable" or "list" =>
                    await InScopeAsync(scope => PlatformUsersAsync(scope, command, cancellationToken)),
                _ when command.Is("users", "reset-password") => await ResetUserPasswordAsync(command, cancellationToken),
                _ when command.Is("users", "verify-legacy-hash") => await InScopeAsync(VerifyLegacyHashAsync),
                _ when command.Is("jobs", "list") => await InScopeAsync(ListJobsAsync),
                _ when command.Is("jobs", "run") => await RunJobAsync(command, cancellationToken),
                _ => await UsageAsync(),
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Exceptions inside operations are already logged by IOperationRunner; log the others once here.
            if (!ExceptionLogging.IsLogged(exception))
            {
                Log.Host.UnhandledException(Logger(), exception, "auxctl", string.Join(' ', command.Words));
            }

            await error.WriteLineAsync($"error AUX-{EventCodes.Host.UnhandledException}: {exception.GetType().Name} (details in the logs)");
            return Failure;
        }
    }

    private async Task<int> RegistryAsync(CommandLine command)
    {
        var markdown = EventRegistry.RenderMarkdown();
        if (command.Option("output") is { } path)
        {
            await File.WriteAllTextAsync(path, markdown);
            await output.WriteLineAsync($"auxctl: registry written to {path}");
        }
        else
        {
            await output.WriteAsync(markdown);
        }

        return Success;
    }

    private async Task<int> MigrateCatalogAsync(CancellationToken cancellationToken)
    {
        var migrated = await InScopeAsync(async scope =>
        {
            var result = await scope.GetRequiredService<ITenantMigrationManager>().MigrateCatalogAsync(cancellationToken);
            return await ReportAsync(result, applied => applied.Count == 0 ? "catalog: up to date" : "catalog: applied " + string.Join(", ", applied));
        });
        if (migrated != Success)
        {
            return migrated;
        }

        return await InScopeAsync(async scope =>
        {
            var result = await scope.GetRequiredService<IModuleCatalogManager>().SyncAsync(cancellationToken);
            return await ReportAsync(result, report =>
                $"modules: {(report.Added.Count == 0 ? "no new modules" : "added " + string.Join(", ", report.Added))}"
                + (report.Unavailable.Count == 0 ? string.Empty : "; no longer deployed " + string.Join(", ", report.Unavailable)));
        });
    }

    private async Task<int> MigrateTenantsAsync(CommandLine command, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> slugs;
        if (command.Option("tenant") is { } slug)
        {
            slugs = [slug];
        }
        else if (command.Flag("all"))
        {
            slugs = await InScopeAsync(scope => scope.GetRequiredService<ITenantMigrationManager>().MigratableTenantsAsync(cancellationToken));
        }
        else
        {
            return await UsageAsync();
        }

        var failed = 0;
        foreach (var tenant in slugs)
        {
            try
            {
                var result = await InScopeAsync(scope => scope.GetRequiredService<ITenantMigrationManager>().MigrateTenantAsync(tenant, cancellationToken));
                failed += await ReportAsync(result, version => $"{tenant}: schema {version.SchemaVersion}, data {version.DataVersion ?? "-"}") == Success ? 0 : 1;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // One tenant failing does not stop the others; it is marked MigrationFailed and logged.
                failed++;
                await error.WriteLineAsync($"{tenant}: error AUX-{EventCodes.Runner.TenantMigrationFailed} ({exception.GetType().Name}, details in the logs)");
            }
        }

        await output.WriteLineAsync($"tenants: {slugs.Count - failed} migrated, {failed} failed");
        return failed == 0 ? Success : Failure;
    }

    private async Task<int> ProvisionAsync(IServiceProvider scope, CommandLine command, CancellationToken cancellationToken)
    {
        if (command.Option("slug") is not { } slug || command.Option("name") is not { } name)
        {
            return await UsageAsync();
        }

        string? existing = null;
        if (command.Flag("existing-database"))
        {
            existing = Environment.GetEnvironmentVariable(ExistingDatabaseVariable);
            if (string.IsNullOrWhiteSpace(existing))
            {
                await error.WriteLineAsync($"--existing-database needs the connection string in {ExistingDatabaseVariable}");
                return UsageError;
            }
        }

        var request = new ProvisionTenant(slug, name, command.Option("language") ?? "it", command.Option("time-zone") ?? "Europe/Rome", existing);
        var result = await scope.GetRequiredService<ITenantLifecycleManager>().ProvisionAsync(request, cancellationToken);
        return await ReportAsync(result, tenant => $"{tenant.Slug}: {tenant.Status} (connection string stored encrypted: ***)");
    }

    private async Task<int> ChangeStatusAsync(IServiceProvider scope, CommandLine command, CancellationToken cancellationToken)
    {
        if (command.Option("slug") is not { } slug)
        {
            return await UsageAsync();
        }

        var lifecycle = scope.GetRequiredService<ITenantLifecycleManager>();
        var action = command.Word(1)!;
        var result = action switch
        {
            "suspend" => await lifecycle.SuspendAsync(slug, cancellationToken),
            "reactivate" => await lifecycle.ReactivateAsync(slug, cancellationToken),
            _ => await lifecycle.ArchiveAsync(slug, cancellationToken),
        };

        return await ReportAsync(result, $"{slug}: {action} done");
    }

    private async Task<int> ListTenantsAsync(IServiceProvider scope, CancellationToken cancellationToken)
    {
        foreach (var tenant in await scope.GetRequiredService<ICatalogStore>().ListTenantsAsync(cancellationToken))
        {
            await output.WriteLineAsync($"{tenant.Slug}\t{tenant.Status}\tschema {tenant.SchemaVersion ?? "-"}\tdata {tenant.DataVersion ?? "-"}");
        }

        return Success;
    }

    private async Task<int> RotateKeysAsync(IServiceProvider scope, CancellationToken cancellationToken)
    {
        var result = await scope.GetRequiredService<ISigningKeyManager>().RotateAsync(cancellationToken);
        return await ReportAsync(result, keyId => $"signing key {keyId} is now active");
    }

    private async Task<int> AddClientAsync(IServiceProvider scope, CommandLine command, CancellationToken cancellationToken)
    {
        if (command.Option("client-id") is not { } clientId
            || command.Option("name") is not { } name
            || !Enum.TryParse<ClientApplicationType>(command.Option("type"), ignoreCase: true, out var type)
            || !Enum.IsDefined(type))
        {
            return await UsageAsync();
        }

        var origins = command.Option("origin") is { } origin ? new[] { origin } : [];
        var result = await scope.GetRequiredService<IClientApplicationManager>()
            .AddAsync(new NewClientApplication(clientId, name, type, origins), cancellationToken);
        return await ReportAsync(result, added => added.Secret is { } secret
            ? $"{added.Client.ClientId}: added ({added.Client.Type}); client secret (shown only now): {secret}"
            : $"{added.Client.ClientId}: added ({added.Client.Type}, public client)");
    }

    private async Task<int> ListClientsAsync(IServiceProvider scope, CancellationToken cancellationToken)
    {
        foreach (var client in await scope.GetRequiredService<IClientApplicationManager>().ListAsync(cancellationToken))
        {
            var origins = client.AllowedOrigins.Length == 0 ? "-" : string.Join(',', client.AllowedOrigins);
            await output.WriteLineAsync($"{client.ClientId}\t{client.Type}\t{(client.IsEnabled ? "enabled" : "disabled")}\t{origins}\t{client.Name}");
        }

        return Success;
    }

    private async Task<int> PlatformUsersAsync(IServiceProvider scope, CommandLine command, CancellationToken cancellationToken)
    {
        var users = scope.GetRequiredService<IPlatformUserManager>();
        var action = command.Word(2)!;
        if (action == "list")
        {
            foreach (var user in await users.ListAsync(cancellationToken))
            {
                var state = !user.IsActive ? "disabled" : user.IsEnrolled ? "active" : "pending activation";
                await output.WriteLineAsync($"{user.Email}\t{state}\t{user.DisplayName}\tlast sign-in {user.LastLoginAt?.ToString("u", System.Globalization.CultureInfo.InvariantCulture) ?? "-"}");
            }

            return Success;
        }

        if (command.Option("email") is not { } email)
        {
            return await UsageAsync();
        }

        switch (action)
        {
            case "add" when command.Option("name") is { } name:
                return await ReportAsync(await users.AddAsync(email, name, cancellationToken), Describe);
            case "reset":
                return await ReportAsync(await users.ResetAsync(email, cancellationToken), Describe);
            case "enable" or "disable":
                return await ReportAsync(await users.SetActiveAsync(email, action == "enable", cancellationToken), $"{email}: {action}d");
            default:
                return await UsageAsync();
        }

        static string Describe(PlatformUserActivation activation) =>
            $"{activation.User.Email}: activation token (shown only now, valid until {activation.ExpiresAt:u}): {activation.ActivationToken}";
    }

    private async Task<int> ResetUserPasswordAsync(CommandLine command, CancellationToken cancellationToken)
    {
        if (command.Option("tenant") is not { } slug || command.Option("user") is not { } user)
        {
            return await UsageAsync();
        }

        var sendLink = command.Flag("send-link");
        return await InTenantAsync(slug, async scope =>
        {
            var result = await scope.GetRequiredService<IAccountLinkManager>().ResetPasswordByOperatorAsync(user, sendLink, cancellationToken);
            return await ReportAsync(result, reset => reset.TemporaryPassword is { } password
                ? $"{slug}/{reset.UserName}: temporary password (shown only now, to change at the next sign-in; sessions ended): {password}"
                : $"{slug}/{reset.UserName}: reset link e-mailed");
        }, cancellationToken);
    }

    private async Task<int> VerifyLegacyHashAsync(IServiceProvider scope)
    {
        var hash = (await input.ReadLineAsync())?.Trim();
        var password = await input.ReadLineAsync();
        if (string.IsNullOrEmpty(hash) || password is null)
        {
            await error.WriteLineAsync("verify-legacy-hash reads the BCrypt hash and then the password, one per line, from standard input");
            return UsageError;
        }

        var verification = scope.GetRequiredService<IPasswordHasher>().Verify(hash, PasswordFormat.LegacyBcrypt, password);
        if (verification == PasswordVerification.Failed)
        {
            await output.WriteLineAsync("no match");
            return Failure;
        }

        await output.WriteLineAsync("match");
        return Success;
    }

    private async Task<int> ListJobsAsync(IServiceProvider scope)
    {
        var jobs = scope.GetRequiredService<IJobRunner>().Jobs;
        if (jobs.Count == 0)
        {
            await output.WriteLineAsync("no recurring jobs registered");
        }

        foreach (var job in jobs)
        {
            await output.WriteLineAsync($"{job.Code}\t{job.SuggestedFrequency}\t{job.Description}");
        }

        return Success;
    }

    private async Task<int> RunJobAsync(CommandLine command, CancellationToken cancellationToken)
    {
        if (command.Word(2) is not { } code)
        {
            return await UsageAsync();
        }

        IReadOnlyList<string> slugs;
        if (command.Option("tenant") is { } slug)
        {
            slugs = [slug];
        }
        else if (command.Flag("all"))
        {
            slugs = await InScopeAsync(async scope => (await scope.GetRequiredService<ICatalogStore>().ListTenantsAsync(cancellationToken))
                .Where(tenant => tenant.Status == Domain.Platform.TenantStatus.Active)
                .Select(tenant => tenant.Slug)
                .ToArray());
        }
        else
        {
            return await UsageAsync();
        }

        var failed = 0;
        foreach (var tenantSlug in slugs)
        {
            var exitCode = await InTenantAsync(tenantSlug, async scope =>
            {
                var result = await scope.GetRequiredService<IJobRunner>().RunAsync(code, cancellationToken);
                return await ReportAsync(result, summary => $"{tenantSlug}: {code} succeeded ({summary})");
            }, cancellationToken);
            failed += exitCode == Success ? 0 : 1;
        }

        return failed == 0 ? Success : Failure;
    }

    private async Task<int> ReportAsync<TValue>(Result<TValue> result, Func<TValue, string> describe)
    {
        if (result.IsSuccess)
        {
            await output.WriteLineAsync(describe(result.Value));
            return Success;
        }

        await error.WriteLineAsync($"error {result.Error!.DisplayCode}: {result.Error.Description}");
        return Failure;
    }

    private Task<int> ReportAsync(Result result, string success) =>
        ReportAsync(result.IsSuccess ? Result.Success(success) : Result.Failure<string>(result.Error!), text => text);

    private async Task<int> UsageAsync()
    {
        await error.WriteLineAsync(Usage);
        return UsageError;
    }

    private async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = services().CreateAsyncScope();
        return await work(scope.ServiceProvider);
    }

    /// <summary>One scope per tenant: a scope never changes tenant.</summary>
    private Task<int> InTenantAsync(string slug, Func<IServiceProvider, Task<int>> work, CancellationToken cancellationToken) =>
        InScopeAsync(async scope =>
        {
            var tenant = await scope.GetRequiredService<ITenantDirectory>().FindBySlugAsync(slug, cancellationToken);
            if (tenant is null)
            {
                return await ReportAsync(Result.Failure(Errors.Tenancy.TenantNotFound()), string.Empty);
            }

            scope.GetRequiredService<ITenantContextSetter>().Set(tenant);
            using (Logger().BeginScope(new Dictionary<string, object?> { ["TenantSlug"] = tenant.Slug }))
            {
                return await work(scope);
            }
        });

    private ILogger Logger() => services().GetRequiredService<ILoggerFactory>().CreateLogger("auxctl");
}
