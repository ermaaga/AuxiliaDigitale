using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Domain.Platform;
using Auxilia.MigrationRunner;
using Auxilia.MigrationRunner.Cli;
using Auxilia.Persistence.Catalog;

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

        (await RunAsync("jobs", "list")).Output.ShouldContain("no recurring jobs registered");
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
    public async Task UnknownCommand_PrintsUsage()
    {
        var run = await RunAsync("tenant", "delete", "--slug", "x");

        run.ExitCode.ShouldBe(AuxctlCli.UsageError);
        run.Error.ShouldContain("usage: auxctl");
    }

    private async Task<(int ExitCode, string Output, string Error)> RunAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await new AuxctlCli(() => host.Services, output, error).RunAsync(args, Ct);
        return (exitCode, output.ToString(), error.ToString());
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
