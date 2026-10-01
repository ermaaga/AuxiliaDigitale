using Auxilia.Application;
using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Configuration;
using Auxilia.Domain.Configuration;
using Auxilia.Domain.Platform;
using Auxilia.Infrastructure;
using Auxilia.Persistence.Tenant;
using Auxilia.Persistence.Tenant.Operations;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

namespace Auxilia.Persistence.Tests.Tenant;

/// <summary>Branding images in <c>configuration.branding_assets</c> (F23), through the manager and the query service.</summary>
[Collection(TenantDatabaseGroup.Name)]
public sealed class BrandingPersistenceTests(TenantDatabaseFixture database)
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01];

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Images_AreStoredReplacedRemovedAndAudited()
    {
        await using var services = Services(ActorType.System);

        await using (var scope = services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IBrandingManager>();
            (await manager.SetAssetAsync(BrandingAssetKind.Logo, Png, Ct)).IsSuccess.ShouldBeTrue();
        }

        string firstVersion;
        await using (var scope = services.CreateAsyncScope())
        {
            var query = scope.ServiceProvider.GetRequiredService<IBrandingQueryService>();
            firstVersion = (await query.GetAsync(Ct)).LogoVersion!;
            var image = (await query.GetAssetAsync(BrandingAssetKind.Logo, Ct)).Value;
            image.Content.ShouldBe(Png);
            image.Version.ShouldBe(firstVersion);
            (await scope.ServiceProvider.GetRequiredService<IBrandingManager>().SetAssetAsync(BrandingAssetKind.Logo, Jpeg, Ct)).IsSuccess.ShouldBeTrue();
        }

        await using (var scope = services.CreateAsyncScope())
        {
            var query = scope.ServiceProvider.GetRequiredService<IBrandingQueryService>();
            (await query.GetAsync(Ct)).LogoVersion.ShouldNotBe(firstVersion);
            (await query.GetAssetAsync(BrandingAssetKind.Logo, Ct)).Value.ContentType.ShouldBe("image/jpeg");
            (await scope.ServiceProvider.GetRequiredService<IBrandingManager>().RemoveAssetAsync(BrandingAssetKind.Logo, Ct)).IsSuccess.ShouldBeTrue();
        }

        await using (var scope = services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<IBrandingQueryService>().GetAsync(Ct)).LogoVersion.ShouldBeNull();
        }

        await using var db = database.CreateContext();
        (await db.Set<BrandingAsset>().AnyAsync(asset => asset.Kind == BrandingAssetKind.Logo, Ct)).ShouldBeFalse();
        var changes = await db.Set<EntityChange>().Where(change => change.EntityType == nameof(BrandingAsset)).ToListAsync(Ct);
        var actions = changes.Select(change => change.Action).ToList();
        changes.First(change => change.Action == "Created").Changes.ShouldContain($"<{Png.Length} bytes>");
        actions.ShouldContain("Created");
        actions.ShouldContain("Updated");
        actions.ShouldContain("Deleted");
    }

    [Fact]
    public async Task Kind_IsUnique()
    {
        await using var db = database.CreateContext();
        db.Set<BrandingAsset>().Add(BrandingAsset.Create(Guid.CreateVersion7(), BrandingAssetKind.Background, Png).Value);
        db.Set<BrandingAsset>().Add(BrandingAsset.Create(Guid.CreateVersion7(), BrandingAssetKind.Background, Jpeg).Value);

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
    }

    private ServiceProvider Services(ActorType actor)
    {
        var tenant = new TenantInfo(Guid.Parse("0199a0b2-0000-7000-8000-0000000005e7"), "tenant-test", TenantStatus.Active, "it", "Europe/Rome");
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.Current.Returns(tenant);
        tenantContext.Tenant.Returns(tenant);
        tenantContext.IsResolved.Returns(true);
        var directory = Substitute.For<ITenantDirectory>();
        directory.GetProtectedConnectionStringAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns("protected");
        var protector = Substitute.For<ITenantConnectionProtector>();
        protector.Unprotect("protected").Returns(database.ConnectionString);
        var user = Substitute.For<ICurrentUser>();
        user.ActorType.Returns(actor);
        user.UserId.Returns(actor == ActorType.User ? Guid.Parse("0199a0b2-0000-7000-8000-0000000005e8") : null);
        var platform = Substitute.For<IPlatformSettingStore>();
        platform.GetValuesAsync(Arg.Any<CancellationToken>()).Returns(new Dictionary<string, string>());

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure();
        services.AddScoped(_ => tenantContext);
        services.AddScoped(_ => directory);
        services.AddSingleton(protector);
        services.AddScoped(_ => user);
        services.AddScoped(_ => platform);
        services.AddSingleton(Substitute.For<ISettingSecretProtector>());
        services.AddApplication();
                services.AddTenantPersistence();
        return services.BuildServiceProvider(validateScopes: true);
    }
}
