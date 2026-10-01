using Auxilia.Application.Abstractions.Configuration;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Application.Configuration;
using Auxilia.Application.Execution;
using Auxilia.Application.Tests.Execution;
using Auxilia.Diagnostics;
using Auxilia.Domain.Configuration;

using Microsoft.Extensions.Logging.Abstractions;

namespace Auxilia.Application.Tests.Configuration;

public sealed class BrandingTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01];

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Fixture
    {
        public Fixture(bool withTenant = true)
        {
            Harness = new SettingsHarness(withTenant ? SettingsHarness.Acme : null);
            Manager = new BrandingManager(
                new OperationRunner(NullLogger<OperationRunner>.Instance, Harness.CurrentUser, new NoOperationTransactionFactory(), [], TimeProvider.System),
                Store, Harness.TenantContext, Harness.Cache);
            Query = new BrandingQueryService(new BrandingCache(Harness.Cache, Harness.TenantContext, Harness.Provider, Store), Store, Harness.TenantContext);
        }

        public SettingsHarness Harness { get; }

        public InMemoryBrandingAssetStore Store { get; } = new();

        public BrandingManager Manager { get; }

        public BrandingQueryService Query { get; }
    }

    [Fact]
    public async Task Get_Defaults_WithoutImages()
    {
        var branding = await new Fixture().Query.GetAsync(Ct);

        branding.AppName.ShouldBe("Auxilia Digitale");
        branding.UseAppName.ShouldBeTrue();
        branding.ThemeFill.ShouldBe("Gradient");
        branding.PrimaryColor.ShouldBe("#667eea");
        branding.AccentColor.ShouldBe("#764ba2");
        branding.Background.Kind.ShouldBe("Gradient");
        branding.Background.ImageVersion.ShouldBeNull();
        branding.LogoVersion.ShouldBeNull();
    }

    [Fact]
    public async Task Get_TenantSettings_AreApplied()
    {
        var fixture = new Fixture();
        fixture.Harness.Stores.Tenant["branding.appName"] = "\"Studio Rossi\"";
        fixture.Harness.Stores.Tenant["branding.background.kind"] = "\"Image\"";
        fixture.Harness.Stores.Tenant["branding.theme.primaryColor"] = "\"not-a-colour\"";

        var branding = await fixture.Query.GetAsync(Ct);

        branding.AppName.ShouldBe("Studio Rossi");
        branding.Background.Kind.ShouldBe("Image");
        branding.PrimaryColor.ShouldBe("#667eea");
    }

    [Fact]
    public async Task SetAsset_StoresTheImageAndRefreshesTheCachedVersion()
    {
        var fixture = new Fixture();
        (await fixture.Query.GetAsync(Ct)).LogoVersion.ShouldBeNull();

        (await fixture.Manager.SetAssetAsync(BrandingAssetKind.Logo, Png, Ct)).IsSuccess.ShouldBeTrue();

        fixture.Harness.Cache.Invalidated.ShouldBe(["t:acme:configuration"]);
        var version = (await fixture.Query.GetAsync(Ct)).LogoVersion;
        version.ShouldNotBeNull();
        version.Length.ShouldBe(16);

        var image = (await fixture.Query.GetAssetAsync(BrandingAssetKind.Logo, Ct)).Value;
        image.ContentType.ShouldBe("image/png");
        image.Content.ShouldBe(Png);
        image.Version.ShouldBe(version);
    }

    [Fact]
    public async Task SetAsset_Again_ReplacesTheImage()
    {
        var fixture = new Fixture();
        await fixture.Manager.SetAssetAsync(BrandingAssetKind.Background, Png, Ct);
        var first = (await fixture.Query.GetAsync(Ct)).Background.ImageVersion;

        (await fixture.Manager.SetAssetAsync(BrandingAssetKind.Background, Jpeg, Ct)).IsSuccess.ShouldBeTrue();

        fixture.Store.Assets.Count.ShouldBe(1);
        (await fixture.Query.GetAsync(Ct)).Background.ImageVersion.ShouldNotBe(first);
        (await fixture.Query.GetAssetAsync(BrandingAssetKind.Background, Ct)).Value.ContentType.ShouldBe("image/jpeg");
    }

    [Fact]
    public async Task SetAsset_InvalidImage_IsRefusedAndNothingChanges()
    {
        var fixture = new Fixture();
        await fixture.Manager.SetAssetAsync(BrandingAssetKind.Logo, Png, Ct);
        fixture.Harness.Cache.Invalidated.Clear();

        (await fixture.Manager.SetAssetAsync(BrandingAssetKind.Logo, "<svg/>"u8.ToArray(), Ct)).Error!.Code.ShouldBe(EventCodes.Configuration.BrandingImageInvalid);
        (await fixture.Manager.SetAssetAsync(BrandingAssetKind.Background, "GIF89a"u8.ToArray(), Ct)).Error!.Code.ShouldBe(EventCodes.Configuration.BrandingImageInvalid);

        fixture.Store.Assets.Single().Value.ContentType.ShouldBe("image/png");
        fixture.Harness.Cache.Invalidated.ShouldBeEmpty();
    }

    [Fact]
    public async Task RemoveAsset_DeletesTheImage_AndMissingIsNotAnError()
    {
        var fixture = new Fixture();
        await fixture.Manager.SetAssetAsync(BrandingAssetKind.Logo, Png, Ct);

        (await fixture.Manager.RemoveAssetAsync(BrandingAssetKind.Logo, Ct)).IsSuccess.ShouldBeTrue();
        (await fixture.Manager.RemoveAssetAsync(BrandingAssetKind.Logo, Ct)).IsSuccess.ShouldBeTrue();

        fixture.Store.Assets.ShouldBeEmpty();
        (await fixture.Query.GetAsync(Ct)).LogoVersion.ShouldBeNull();
        (await fixture.Query.GetAssetAsync(BrandingAssetKind.Logo, Ct)).Error!.Code.ShouldBe(EventCodes.Configuration.BrandingAssetNotFound);
    }

    [Fact]
    public async Task WithoutTenant_WritesAreRefusedAndImagesNotFound()
    {
        var fixture = new Fixture(withTenant: false);

        (await fixture.Manager.SetAssetAsync(BrandingAssetKind.Logo, Png, Ct)).Error!.Code.ShouldBe(EventCodes.Tenancy.TenantRequired);
        (await fixture.Manager.RemoveAssetAsync(BrandingAssetKind.Logo, Ct)).Error!.Code.ShouldBe(EventCodes.Tenancy.TenantRequired);
        (await fixture.Query.GetAssetAsync(BrandingAssetKind.Logo, Ct)).Error!.Code.ShouldBe(EventCodes.Configuration.BrandingAssetNotFound);
        (await fixture.Query.GetAsync(Ct)).LogoVersion.ShouldBeNull();
    }

    [Fact]
    public void Definitions_AreValidated()
    {
        BrandingSettings.All.ShouldAllBe(definition => definition.Module == "Configuration");
        BrandingSettings.PrimaryColor.TryRead("\"#123\"", out _).ShouldBeTrue();
        BrandingSettings.PrimaryColor.TryRead("\"url(javascript:x)\"", out _).ShouldBeFalse();
        BrandingSettings.AppName.TryRead("\"\"", out _).ShouldBeFalse();
        BrandingSettings.AppName.TryRead($"\"{new string('a', 101)}\"", out _).ShouldBeFalse();
        BrandingSettings.BackgroundType.Choices.ShouldBe(["Gradient", "Solid", "Image"]);
        SettingsQueryService.KindOf(BrandingSettings.UseAppName).ShouldBe("boolean");
    }
}

/// <summary>Branding images in memory, one per kind like the unique index.</summary>
internal sealed class InMemoryBrandingAssetStore : IBrandingAssetStore
{
    public Dictionary<BrandingAssetKind, BrandingAsset> Assets { get; } = [];

    public Task<BrandingAsset?> FindAsync(BrandingAssetKind kind, CancellationToken cancellationToken) =>
        Task.FromResult(Assets.GetValueOrDefault(kind));

    public Task<BrandingAsset?> ReadAsync(BrandingAssetKind kind, CancellationToken cancellationToken) => FindAsync(kind, cancellationToken);

    public Task<IReadOnlyDictionary<BrandingAssetKind, string>> VersionsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<BrandingAssetKind, string>>(Assets.ToDictionary(item => item.Key, item => item.Value.Hash));

    public void Add(BrandingAsset asset) => Assets.Add(asset.Kind, asset);

    public void Remove(BrandingAsset asset) => Assets.Remove(asset.Kind);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
