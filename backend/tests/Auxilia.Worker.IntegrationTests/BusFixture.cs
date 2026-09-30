using System.Collections.Concurrent;

using Auxilia.Application;
using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Jobs;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Domain.Platform;
using Auxilia.Infrastructure;
using Auxilia.Infrastructure.Messaging;
using Auxilia.Persistence.Catalog;
using Auxilia.Persistence.Tenant;
using Auxilia.SharedKernel.Results;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

using NSubstitute;

using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace Auxilia.Worker.IntegrationTests;

/// <summary>
/// RabbitMQ + PostgreSQL (Catalog and one tenant database), a running Worker host and an "Api" service provider that
/// produces messages through the outbox. Test jobs record what they did in <see cref="Journal"/>.
/// </summary>
public sealed class BusFixture : IAsyncLifetime
{
    public const string TenantSlug = "bus-tenant";

    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18-alpine").WithDatabase("auxilia_catalog").Build();
    private readonly RabbitMqContainer rabbitMq = new RabbitMqBuilder("rabbitmq:4-management-alpine").Build();
    private IHost? worker;

    public static ConcurrentDictionary<string, int> Journal { get; } = new(StringComparer.Ordinal);

    public string RabbitMqConnectionString => rabbitMq.GetConnectionString();

    public string TenantConnectionString { get; private set; } = string.Empty;

    public ServiceProvider Api { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(postgres.StartAsync(), rabbitMq.StartAsync());

        var catalog = postgres.GetConnectionString();
        TenantConnectionString = new NpgsqlConnectionStringBuilder(catalog) { Database = "bus_tenant" }.ConnectionString;
        await using (var connection = new NpgsqlConnection(catalog))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand("create database bus_tenant", connection);
            await create.ExecuteNonQueryAsync();
        }

        await using (var dataSource = NpgsqlDataSource.Create(TenantConnectionString))
        await using (var tenantDb = new TenantDbContext(TenantDbContextOptions.Create(dataSource)))
        {
            await tenantDb.Database.MigrateAsync();
        }

        Api = BuildApi(catalog);
        await using (var scope = Api.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            await db.Database.MigrateAsync();
            var tenant = Tenant.Create(Guid.CreateVersion7(), TenantSlug, "Bus", "it", "Europe/Rome").Value;
            tenant.SetConnectionSecret(scope.ServiceProvider.GetRequiredService<ITenantConnectionProtector>().Protect(TenantConnectionString));
            tenant.Activate();
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
        }

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Testing" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Catalog"] = catalog,
            ["ConnectionStrings:RabbitMq"] = RabbitMqConnectionString,
        });
        builder.Services.AddWorkerServices(builder.Configuration, new MessageBusOptions
        {
            MaxDeliveryAttempts = 2,
            SecondLevelRetryDelays = [TimeSpan.FromSeconds(1)],
            WorkersPerQueue = 2,
        });
        AddTestJobs(builder.Services);
        worker = builder.Build();
        await worker.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (worker is not null)
        {
            await worker.StopAsync();
            worker.Dispose();
        }

        await Api.DisposeAsync();
        await Task.WhenAll(postgres.DisposeAsync().AsTask(), rabbitMq.DisposeAsync().AsTask());
    }

    public TenantDbContext TenantDb() => new(TenantDbContextOptions.Create(NpgsqlDataSource.Create(TenantConnectionString)));

    /// <summary>A request scope of the "Api" for the test tenant and a tenant user.</summary>
    public async Task<AsyncServiceScope> TenantScopeAsync()
    {
        var scope = Api.CreateAsyncScope();
        var tenant = await scope.ServiceProvider.GetRequiredService<ITenantDirectory>().FindBySlugAsync(TenantSlug, CancellationToken.None);
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().Set(tenant!);
        return scope;
    }

    private ServiceProvider BuildApi(string catalog)
    {
        var user = Substitute.For<ICurrentUser>();
        user.ActorType.Returns(ActorType.User);
        user.UserId.Returns(Guid.Parse("0199a0b2-0000-7000-8000-00000000b055"));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => user);
        services.AddApplication();
        services.AddInfrastructure();
        services.AddCatalogPersistence(catalog);
        services.AddTenantPersistence();
        services.AddMessageBusClient(RabbitMqConnectionString);
        return services.BuildServiceProvider();
    }

    private static void AddTestJobs(IServiceCollection services)
    {
        services.AddScoped<IRecurringJob>(_ => new TestJob("test.count", _ => Task.FromResult(Result.Success("counted"))));
        services.AddScoped<IRecurringJob>(_ => new TestJob("test.flaky", attempt => attempt <= 3
            ? throw new InvalidOperationException("transient failure " + attempt)
            : Task.FromResult(Result.Success("recovered"))));
        services.AddScoped<IRecurringJob>(_ => new TestJob("test.broken", _ => throw new InvalidOperationException("always fails")));
    }

    private sealed class TestJob(string code, Func<int, Task<Result<string>>> run) : IRecurringJob
    {
        public string Code => code;

        public string Description => "test job";

        public string SuggestedFrequency => "never";

        public Task<Result<string>> RunAsync(CancellationToken cancellationToken) =>
            run(Journal.AddOrUpdate(code, 1, (_, count) => count + 1));
    }
}

[CollectionDefinition(Name)]
public sealed class BusGroup : ICollectionFixture<BusFixture>
{
    public const string Name = "Message bus";
}
