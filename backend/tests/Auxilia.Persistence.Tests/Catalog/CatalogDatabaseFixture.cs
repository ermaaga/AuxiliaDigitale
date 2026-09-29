using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Persistence.Catalog;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using Testcontainers.PostgreSql;

namespace Auxilia.Persistence.Tests.Catalog;

/// <summary>A PostgreSQL container with the catalog migrated from zero, shared by the catalog tests.</summary>
public sealed class CatalogDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("auxilia_catalog")
        .Build();

    public static readonly Guid ActorId = Guid.Parse("0199a0b2-0000-7000-8000-00000000a11c");

    public string ConnectionString => container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await container.StartAsync();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync() => await container.DisposeAsync();

    /// <summary>A service provider as the hosts build it, with a platform user as the current actor.</summary>
    public ServiceProvider CreateServices(Action<IServiceCollection>? configure = null)
    {
        var user = Substitute.For<ICurrentUser>();
        user.ActorType.Returns(ActorType.Platform);
        user.UserId.Returns(ActorId);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => user);
        services.AddCatalogPersistence(ConnectionString);
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    public CatalogDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>();
        CatalogPersistence.Configure(options, ConnectionString);
        return new CatalogDbContext(options.Options);
    }
}

[CollectionDefinition(Name)]
public sealed class CatalogDatabaseGroup : ICollectionFixture<CatalogDatabaseFixture>
{
    public const string Name = "Catalog database";
}
