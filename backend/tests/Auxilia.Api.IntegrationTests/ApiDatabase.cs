using Auxilia.Api.IntegrationTests;
using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Domain.Platform;
using Auxilia.Persistence.Catalog;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using NSubstitute;

using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(ApiDatabase))]

namespace Auxilia.Api.IntegrationTests;

/// <summary>
/// One PostgreSQL container for the whole API suite: the Catalog migrated and seeded with tenants in every status,
/// two of them (<c>tenant-a</c>, <c>tenant-b</c>) with their own database.
/// </summary>
public sealed class ApiDatabase : IAsyncLifetime
{
    public const string TenantA = "tenant-a";
    public const string TenantB = "tenant-b";
    public const string Suspended = "tenant-suspended";
    public const string Provisioning = "tenant-provisioning";
    public const string Archived = "tenant-archived";
    public const string TenantBCustomHost = "crm.studio-b.test";

    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:18-alpine").WithDatabase("auxilia_catalog").Build();

    private static ApiDatabase? instance;

    public static ApiDatabase Instance => instance ?? throw new InvalidOperationException("The API database fixture is not initialized.");

    public string CatalogConnectionString => container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await container.StartAsync();

        var user = Substitute.For<ICurrentUser>();
        user.ActorType.Returns(ActorType.System);
        var services = new ServiceCollection().AddLogging().AddScoped(_ => user);
        services.AddCatalogPersistence(CatalogConnectionString);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<ITenantConnectionProtector>();
        await catalog.Database.MigrateAsync();

        var tenantA = await AddTenantAsync(catalog, protector, TenantA, database: "tenant_a", tenant => tenant.Activate());
        var tenantB = await AddTenantAsync(catalog, protector, TenantB, database: "tenant_b", tenant => tenant.Activate());
        catalog.TenantDomains.Add(new TenantDomain(Guid.CreateVersion7(), tenantB.Id, TenantBCustomHost));
        await AddTenantAsync(catalog, protector, Suspended, database: null, tenant => { tenant.Activate(); tenant.Suspend(); });
        await AddTenantAsync(catalog, protector, Provisioning, database: null, _ => { });
        await AddTenantAsync(catalog, protector, Archived, database: null, tenant => tenant.Archive(DateTimeOffset.UtcNow));
        await catalog.SaveChangesAsync();
        _ = tenantA;

        instance = this;
    }

    public async ValueTask DisposeAsync() => await container.DisposeAsync();

    private async Task<Tenant> AddTenantAsync(
        CatalogDbContext catalog, ITenantConnectionProtector protector, string slug, string? database, Action<Tenant> lifecycle)
    {
        var tenant = Tenant.Create(Guid.CreateVersion7(), slug, slug, "it", "Europe/Rome").Value;
        if (database is not null)
        {
            await using var connection = new NpgsqlConnection(CatalogConnectionString);
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"create database {database}", connection);
            await create.ExecuteNonQueryAsync();

            var connectionString = new NpgsqlConnectionStringBuilder(CatalogConnectionString) { Database = database }.ConnectionString;
            tenant.SetConnectionSecret(protector.Protect(connectionString));
        }

        lifecycle(tenant);
        catalog.Tenants.Add(tenant);
        return tenant;
    }
}
