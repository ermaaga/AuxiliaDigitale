using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Identity;
using Auxilia.Domain.Platform;
using Auxilia.MigrationRunner;
using Auxilia.MigrationRunner.Cli;
using Auxilia.Persistence.Catalog;
using Auxilia.Persistence.Tenant;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

using Testcontainers.PostgreSql;

namespace Auxilia.Persistence.Tests.Auxctl;

/// <summary>auxctl end to end on PostgreSQL (tasks P1-09, skill auxilia-tenant-provisioning, D-02).</summary>
public sealed class AuxctlTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:18-alpine").WithDatabase("auxilia_catalog").Build();
    private IHost host = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await container.StartAsync();
        host = AuxctlHost.Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Catalog"] = container.GetConnectionString(),
            ["AuxiliaLogging:Storage"] = "none",
        });
        (await RunAsync("migrate", "catalog")).ExitCode.ShouldBe(AuxctlCli.Success);
    }

    public async ValueTask DisposeAsync()
    {
        host.Dispose();
        await container.DisposeAsync();
    }

    [Fact]
    public async Task MigrateCatalog_Twice_IsUpToDate()
    {
        var run = await RunAsync("migrate", "catalog");

        run.ExitCode.ShouldBe(AuxctlCli.Success);
        run.Output.ShouldContain("up to date");
    }

    [Fact]
    public async Task TenantLifecycle_EndToEnd()
    {
        var provision = await RunAsync("tenant", "provision", "--slug", "acme", "--name", "Acme S.r.l.");
        provision.ExitCode.ShouldBe(AuxctlCli.Success, provision.Error);
        provision.Output.ShouldContain("acme: Active");
        provision.Output.ShouldNotContain("Password", Case.Insensitive);

        (await RunAsync("tenant", "provision", "--slug", "beta", "--name", "Beta")).ExitCode.ShouldBe(AuxctlCli.Success);

        await using (var catalog = Catalog())
        {
            var acme = await catalog.Tenants.SingleAsync(tenant => tenant.Slug == "acme", Ct);
            acme.Status.ShouldBe(TenantStatus.Active);
            acme.SchemaVersion.ShouldNotBeNull();
            acme.ConnectionSecret.ShouldNotBeNull().ShouldNotContain("Password");
            (await catalog.TenantPlans.CountAsync(plan => plan.TenantId == acme.Id && plan.PlanId == Plan.StandardId, Ct)).ShouldBe(1);
            (await catalog.MigrationRuns.SingleAsync(run => run.TenantId == acme.Id, Ct)).Status.ShouldBe(MigrationRunStatus.Succeeded);
        }

        // Each tenant has its own database and role; a tenant role cannot connect to another tenant's database.
        var acmeConnection = await TenantConnectionStringAsync("acme");
        new NpgsqlConnectionStringBuilder(acmeConnection).Database.ShouldBe("auxilia_t_acme");
        (await TablesAsync(acmeConnection)).ShouldContain("ops.data_migrations_history");
        var intoBeta = new NpgsqlConnectionStringBuilder(acmeConnection) { Database = "auxilia_t_beta" }.ConnectionString;
        await Should.ThrowAsync<PostgresException>(async () =>
        {
            await using var connection = new NpgsqlConnection(intoBeta);
            await connection.OpenAsync(Ct);
        });

        var list = await RunAsync("tenant", "list");
        list.Output.ShouldContain("acme\tActive");
        list.Output.ShouldContain("beta\tActive");

        var migrateAll = await RunAsync("migrate", "tenants", "--all");
        migrateAll.ExitCode.ShouldBe(AuxctlCli.Success, migrateAll.Error);
        migrateAll.Output.ShouldContain("2 migrated, 0 failed");

        (await RunAsync("tenant", "suspend", "--slug", "acme")).ExitCode.ShouldBe(AuxctlCli.Success);
        (await RunAsync("migrate", "tenants", "--tenant", "acme")).ExitCode.ShouldBe(AuxctlCli.Success);
        (await RunAsync("tenant", "list")).Output.ShouldContain("acme\tSuspended");
        (await RunAsync("tenant", "reactivate", "--slug", "acme")).ExitCode.ShouldBe(AuxctlCli.Success);
        (await RunAsync("tenant", "archive", "--slug", "acme")).ExitCode.ShouldBe(AuxctlCli.Success);
        (await RunAsync("tenant", "list")).Output.ShouldContain("acme\tArchived");

        var migrateArchived = await RunAsync("migrate", "tenants", "--tenant", "acme");
        migrateArchived.ExitCode.ShouldBe(AuxctlCli.Failure);
        migrateArchived.Error.ShouldContain("AUX-11009");

        var again = await RunAsync("tenant", "provision", "--slug", "beta", "--name", "Beta");
        again.ExitCode.ShouldBe(AuxctlCli.Failure);
        again.Error.ShouldContain("AUX-11016");

        (await RunAsync("jobs", "list")).Output.ShouldStartWith("bus.outbox");
        var unknownJob = await RunAsync("jobs", "run", "cases.expiry", "--tenant", "beta");
        unknownJob.ExitCode.ShouldBe(AuxctlCli.Failure);
        unknownJob.Error.ShouldContain("AUX-26002");
    }

    [Theory]
    [InlineData("api", "AUX-11017")]
    [InlineData("Not_Valid", "AUX-11005")]
    public async Task Provision_InvalidOrReservedSlug_Fails(string slug, string code)
    {
        var run = await RunAsync("tenant", "provision", "--slug", slug, "--name", "X");

        run.ExitCode.ShouldBe(AuxctlCli.Failure);
        run.Error.ShouldContain(code);
    }

    [Fact]
    public async Task Provision_InterruptedTenant_IsResumed()
    {
        await using (var catalog = Catalog())
        {
            catalog.Tenants.Add(Domain.Platform.Tenant.Create(Guid.CreateVersion7(), "resumed", "Resumed", "it", "Europe/Rome").Value);
            await catalog.SaveChangesAsync(Ct);
        }

        var run = await RunAsync("tenant", "provision", "--slug", "resumed", "--name", "Resumed");

        run.ExitCode.ShouldBe(AuxctlCli.Success, run.Error);
        run.Output.ShouldContain("resumed: Active");
    }

    [Fact]
    public async Task Provision_ExistingDatabase_UsesTheProvidedConnection()
    {
        await using (var connection = new NpgsqlConnection(container.GetConnectionString()))
        {
            await connection.OpenAsync(Ct);
            await using var create = new NpgsqlCommand("create database provided_by_dba", connection);
            await create.ExecuteNonQueryAsync(Ct);
        }

        var provided = new NpgsqlConnectionStringBuilder(container.GetConnectionString()) { Database = "provided_by_dba" }.ConnectionString;
        Environment.SetEnvironmentVariable(AuxctlCli.ExistingDatabaseVariable, provided);
        try
        {
            var run = await RunAsync("tenant", "provision", "--slug", "dba-tenant", "--name", "DBA", "--existing-database");

            run.ExitCode.ShouldBe(AuxctlCli.Success, run.Error);
            (await TenantConnectionStringAsync("dba-tenant")).ShouldBe(provided);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AuxctlCli.ExistingDatabaseVariable, null);
        }
    }

    [Fact]
    public async Task KeysRotate_RetiresTheActiveKeyAndKeepsItPublished()
    {
        var first = await RunAsync("keys", "rotate");
        var second = await RunAsync("keys", "rotate");

        first.ExitCode.ShouldBe(AuxctlCli.Success, first.Error);
        second.ExitCode.ShouldBe(AuxctlCli.Success, second.Error);
        await using var catalog = Catalog();
        var keys = await catalog.SigningKeys.AsNoTracking().ToListAsync(Ct);
        keys.Count.ShouldBe(2);
        var active = keys.Where(key => key.RetiredAt == null).ShouldHaveSingleItem();
        second.Output.ShouldContain(active.Id);
        keys.Single(key => key.RetiredAt != null).PublishedUntil.ShouldNotBeNull();
        keys.ShouldAllBe(key => !key.PrivateKeyProtected.Contains("PRIVATE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Clients_AddPrintsTheSecretOnceAndStoresOnlyItsHash()
    {
        var added = await RunAsync("clients", "add", "--client-id", "web-bff", "--name", "Tenant web", "--type", "webbff", "--origin", "https://app.example.test/");
        var mobile = await RunAsync("clients", "add", "--client-id", "mobile", "--name", "Mobile", "--type", "Mobile");
        var duplicate = await RunAsync("clients", "add", "--client-id", "web-bff", "--name", "Again", "--type", "Mobile");
        var badType = await RunAsync("clients", "add", "--client-id", "x", "--name", "X", "--type", "Robot");
        var noCaptcha = await RunAsync("clients", "add", "--client-id", "site", "--name", "Site", "--type", "Integration", "--captcha", "none");

        added.ExitCode.ShouldBe(AuxctlCli.Success, added.Error);
        var secret = added.Output.Split("(shown only now): ")[1].Trim();
        mobile.Output.ShouldContain("public client, captcha altcha");
        noCaptcha.ExitCode.ShouldBe(AuxctlCli.Failure);
        duplicate.ExitCode.ShouldBe(AuxctlCli.Failure);
        duplicate.Error.ShouldContain("AUX-12027");
        badType.ExitCode.ShouldBe(AuxctlCli.UsageError);

        await using (var catalog = Catalog())
        {
            var web = await catalog.ClientApplications.AsNoTracking().SingleAsync(client => client.ClientId == "web-bff", Ct);
            web.SecretHash.ShouldNotBeNull().ShouldNotContain(secret);
            web.AllowedOrigins.ShouldBe(["https://app.example.test"]);
        }

        var list = await RunAsync("clients", "list");
        list.Output.ShouldContain("web-bff\tWebBff\tenabled\tcaptcha none\thttps://app.example.test\tTenant web");
        list.Output.ShouldContain("mobile\tMobile\tenabled\tcaptcha altcha\t-\tMobile");
        list.Output.ShouldNotContain(secret);
    }

    [Fact]
    public async Task PlatformUsers_AddResetDisableAndList()
    {
        var added = await RunAsync("platform", "users", "add", "--email", "ops@example.test", "--name", "Operations");
        var duplicate = await RunAsync("platform", "users", "add", "--email", "OPS@example.test", "--name", "Again");
        var reset = await RunAsync("platform", "users", "reset", "--email", "ops@example.test");
        var disabled = await RunAsync("platform", "users", "disable", "--email", "ops@example.test");
        var list = await RunAsync("platform", "users", "list");
        var missing = await RunAsync("platform", "users", "reset", "--email", "nobody@example.test");

        added.ExitCode.ShouldBe(AuxctlCli.Success, added.Error);
        var token = added.Output.Split("): ")[1].Trim();
        token.Length.ShouldBeGreaterThan(30);
        duplicate.ExitCode.ShouldBe(AuxctlCli.Failure);
        duplicate.Error.ShouldContain("AUX-12039");
        reset.ExitCode.ShouldBe(AuxctlCli.Success, reset.Error);
        reset.Output.ShouldNotContain(token);
        disabled.Output.ShouldContain("ops@example.test: disabled");
        list.Output.ShouldContain("ops@example.test\tdisabled\tOperations");
        missing.ExitCode.ShouldBe(AuxctlCli.Failure);

        await using var catalog = Catalog();
        (await catalog.PlatformUserTokens.CountAsync(Ct)).ShouldBe(2);
        (await catalog.PlatformUserTokens.CountAsync(item => item.UsedAt == null, Ct)).ShouldBe(1);
        (await catalog.PlatformUserTokens.AnyAsync(item => item.TokenHash.Contains(token), Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task UsersResetPassword_SetsATemporaryPasswordOrSendsALink()
    {
        (await RunAsync("tenant", "provision", "--slug", "users", "--name", "Users")).ExitCode.ShouldBe(AuxctlCli.Success);
        var userId = await AddTenantUserAsync("users", "mario.rossi", "mario@example.test");

        var temporary = await RunAsync("users", "reset-password", "--tenant", "users", "--user", "MARIO@example.test");
        var link = await RunAsync("users", "reset-password", "--tenant", "users", "--user", "mario.rossi", "--send-link");
        var unknown = await RunAsync("users", "reset-password", "--tenant", "users", "--user", "nobody");
        var noTenant = await RunAsync("users", "reset-password", "--tenant", "missing", "--user", "mario.rossi");
        var usage = await RunAsync("users", "reset-password", "--tenant", "users");

        temporary.ExitCode.ShouldBe(AuxctlCli.Success, temporary.Error);
        var password = temporary.Output.Split("sessions ended): ")[1].Trim();
        password.Length.ShouldBeGreaterThanOrEqualTo(16);
        // A new tenant has no e-mail account yet: the operator sees why the link was not sent (the success path is unit-tested).
        link.ExitCode.ShouldBe(AuxctlCli.Failure);
        link.Error.ShouldContain("AUX-25011");
        unknown.Error.ShouldContain("AUX-12006");
        noTenant.ExitCode.ShouldBe(AuxctlCli.Failure);
        usage.ExitCode.ShouldBe(AuxctlCli.UsageError);

        await using var dataSource = NpgsqlDataSource.Create(await TenantConnectionStringAsync("users"));
        await using var db = new TenantDbContext(TenantDbContextOptions.Create(dataSource));
        var user = await db.Set<User>().SingleAsync(item => item.Id == userId, Ct);
        user.MustChangePassword.ShouldBeTrue();
        user.PasswordHash!.ShouldNotContain(password);
        host.Services.GetRequiredService<IPasswordHasher>().Verify(user.PasswordHash!, user.PasswordFormat, password).ShouldNotBe(PasswordVerification.Failed);
    }

    [Fact]
    public async Task UsersVerifyLegacyHash_ReadsHashAndPasswordFromStandardInput()
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("legacy password", 4);

        var match = await RunWithInputAsync($"{hash}\nlegacy password\n", "users", "verify-legacy-hash");
        var noMatch = await RunWithInputAsync($"{hash}\nanother password\n", "users", "verify-legacy-hash");
        var empty = await RunWithInputAsync(string.Empty, "users", "verify-legacy-hash");

        (match.ExitCode, match.Output.Trim()).ShouldBe((AuxctlCli.Success, "match"));
        (noMatch.ExitCode, noMatch.Output.Trim()).ShouldBe((AuxctlCli.Failure, "no match"));
        empty.ExitCode.ShouldBe(AuxctlCli.UsageError);
    }

    [Fact]
    public async Task UnknownCommand_PrintsUsage()
    {
        var run = await RunAsync("tenant", "delete", "--slug", "x");

        run.ExitCode.ShouldBe(AuxctlCli.UsageError);
        run.Error.ShouldContain("usage: auxctl");
    }

    private Task<(int ExitCode, string Output, string Error)> RunAsync(params string[] args) => RunWithInputAsync(string.Empty, args);

    private async Task<(int ExitCode, string Output, string Error)> RunWithInputAsync(string input, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        using var reader = new StringReader(input);
        var exitCode = await new AuxctlCli(() => host.Services, output, error, reader).RunAsync(args, Ct);
        return (exitCode, output.ToString(), error.ToString());
    }

    private async Task<Guid> AddTenantUserAsync(string slug, string userName, string email)
    {
        await using var dataSource = NpgsqlDataSource.Create(await TenantConnectionStringAsync(slug));
        await using var db = new TenantDbContext(TenantDbContextOptions.Create(dataSource));
        var person = new Person(Guid.CreateVersion7(), "Mario", "Rossi", null);
        var user = User.Create(Guid.CreateVersion7(), person.Id, userName, email, "it", [TenantRole.Client], isActive: true).Value;
        user.SetPassword(host.Services.GetRequiredService<IPasswordHasher>().Hash("Old!Password123"), PasswordFormat.Identity, DateTimeOffset.UtcNow);
        db.Set<Person>().Add(person);
        db.Set<User>().Add(user);
        await db.SaveChangesAsync(Ct);
        return user.Id;
    }

    private CatalogDbContext Catalog()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>();
        CatalogPersistence.Configure(options, container.GetConnectionString());
        return new CatalogDbContext(options.Options);
    }

    private async Task<string> TenantConnectionStringAsync(string slug)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var tenant = await scope.ServiceProvider.GetRequiredService<ICatalogStore>().FindTenantAsync(slug, Ct);
        return scope.ServiceProvider.GetRequiredService<ITenantConnectionProtector>().Unprotect(tenant!.ConnectionSecret!);
    }

    private static async Task<List<string>> TablesAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("select table_schema || '.' || table_name from information_schema.tables where table_schema in ('ops', 'audit')", connection);
        var tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }
}
