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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

using Testcontainers.PostgreSql;

namespace Auxilia.Worker.IntegrationTests;

/// <summary>
/// <see cref="MessagingMode.InProcess"/> (ADR 0028): PostgreSQL only (Catalog with the queue table and one tenant
/// database) and one host that, like the Api, both produces messages through the outbox and consumes them. Test jobs
/// record what they did in <see cref="Journal"/> (their own codes: the RabbitMQ fixture runs in parallel).
/// </summary>
public sealed class InProcessBusFixture : IAsyncLifetime
{
    public const string TenantSlug = "inprocess-tenant";

    public static readonly Guid HostUserId = Guid.Parse("0199a0b2-0000-7000-8000-0000000c0de1");

    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18-alpine").WithDatabase("auxilia_catalog").Build();

    public static ConcurrentDictionary<string, int> Journal { get; } = new(StringComparer.Ordinal);

    /// <summary>The <see cref="ICurrentUser"/> each test job saw while running (inside a message handler).</summary>
    public static ConcurrentQueue<(ActorType ActorType, Guid? UserId)> JobCallers { get; } = new();

    public string CatalogConnectionString => postgres.GetConnectionString();

    public string TenantConnectionString { get; private set; } = string.Empty;

    public IHost Host { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await postgres.StartAsync();

        TenantConnectionString = new NpgsqlConnectionStringBuilder(CatalogConnectionString) { Database = "inprocess_tenant" }.ConnectionString;
        await using (var connection = new NpgsqlConnection(CatalogConnectionString))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand("create database inprocess_tenant", connection);
            await create.ExecuteNonQueryAsync();
        }

        await using (var dataSource = NpgsqlDataSource.Create(TenantConnectionString))
        await using (var tenantDb = new TenantDbContext(TenantDbContextOptions.Create(dataSource)))
        {
            await tenantDb.Database.MigrateAsync();
        }

        Host = BuildHost();
        await using (var scope = Host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            await db.Database.MigrateAsync();
            var tenant = Tenant.Create(Guid.CreateVersion7(), TenantSlug, "InProcess", "it", "Europe/Rome").Value;
            tenant.SetConnectionSecret(scope.ServiceProvider.GetRequiredService<ITenantConnectionProtector>().Protect(TenantConnectionString));
            tenant.Activate();
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
        }

        await Host.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Host.StopAsync();
        Host.Dispose();
        await postgres.DisposeAsync();
    }

    /// <summary>Stops the host (as when the Api scales to zero), runs <paramref name="whileStopped"/>, starts a new one.</summary>
    public async Task RestartAsync(Func<Task> whileStopped)
    {
        await Host.StopAsync();
        Host.Dispose();
        await whileStopped();
        Host = BuildHost();
        await Host.StartAsync();
    }

    /// <summary>A request scope of the host for the test tenant (the caller is the host user, as an HTTP request).</summary>
    public async Task<AsyncServiceScope> TenantScopeAsync(IServiceProvider? services = null)
    {
        var scope = (services ?? Host.Services).CreateAsyncScope();
        var tenant = await scope.ServiceProvider.GetRequiredService<ITenantDirectory>().FindBySlugAsync(TenantSlug, CancellationToken.None);
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().Set(tenant!);
        return scope;
    }

    public TenantDbContext TenantDb() => new(TenantDbContextOptions.Create(NpgsqlDataSource.Create(TenantConnectionString)));

    /// <summary>auxctl in InProcess mode: send-only, writes to the queue table.</summary>
    public ServiceProvider BuildSendOnlyClient()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ICurrentUser, HostUser>();
        services.AddApplication();
        services.AddInfrastructure();
        services.AddCatalogPersistence(CatalogConnectionString);
        services.AddTenantPersistence();
        services.AddInProcessMessageBusClient(CatalogConnectionString);
        return services.BuildServiceProvider();
    }

    private IHost BuildHost()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Testing" });
        builder.Services.AddScoped<ICurrentUser, HostUser>();
        builder.Services.AddApplication();
        builder.Services.AddInfrastructure();
        builder.Services.AddCatalogPersistence(CatalogConnectionString);
        builder.Services.AddTenantPersistence();
        builder.Services.AddInProcessMessageBus<HostUser>(CatalogConnectionString, new MessageBusOptions
        {
            MaxDeliveryAttempts = 2,
            SecondLevelRetryDelays = [TimeSpan.FromSeconds(1)],
            WorkersPerQueue = 1,
        });
        AddTestJobs(builder.Services);
        return builder.Build();
    }

    private static void AddTestJobs(IServiceCollection services)
    {
        services.AddScoped<IRecurringJob>(provider => new TestJob("inprocess.count", provider.GetRequiredService<ICurrentUser>(), _ => Task.FromResult(Result.Success("counted"))));
        services.AddScoped<IRecurringJob>(provider => new TestJob("inprocess.flaky", provider.GetRequiredService<ICurrentUser>(), attempt => attempt <= 3
            ? throw new InvalidOperationException("transient failure " + attempt)
            : Task.FromResult(Result.Success("recovered"))));
        services.AddScoped<IRecurringJob>(provider => new TestJob("inprocess.broken", provider.GetRequiredService<ICurrentUser>(), _ => throw new InvalidOperationException("always fails")));
    }

    /// <summary>The caller of the host outside message handlers (stands for <c>HttpCurrentUser</c> of the Api).</summary>
    public sealed class HostUser : ICurrentUser
    {
        public ActorType ActorType => ActorType.User;

        public Guid? UserId => HostUserId;
    }

    private sealed class TestJob(string code, ICurrentUser caller, Func<int, Task<Result<string>>> run) : IRecurringJob
    {
        public string Code => code;

        public string Description => "test job";

        public string SuggestedFrequency => "never";

        public Task<Result<string>> RunAsync(CancellationToken cancellationToken)
        {
            JobCallers.Enqueue((caller.ActorType, caller.UserId));
            return run(Journal.AddOrUpdate(code, 1, (_, count) => count + 1));
        }
    }
}

[CollectionDefinition(Name)]
public sealed class InProcessBusGroup : ICollectionFixture<InProcessBusFixture>
{
    public const string Name = "In-process message bus";
}
