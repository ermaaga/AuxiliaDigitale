using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Configuration;
using Auxilia.Application.Execution;
using Auxilia.Application.Tests.Execution;
using Auxilia.Domain.Platform;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

namespace Auxilia.Application.Tests.Configuration;

/// <summary>Provider and manager over in-memory stores, a recording cache and a switchable tenant/user.</summary>
internal sealed class SettingsHarness
{
    public static readonly TenantInfo Acme = new(Guid.CreateVersion7(), "acme", TenantStatus.Active, "it", "Europe/Rome");

    public static readonly Guid UserId = Guid.CreateVersion7();

    public static readonly SettingDefinition<bool> Enabled = new("test.enabled", "Test", false);

    public static readonly SettingDefinition<int> Days = new("test.days", "Test", 7, isValid: days => days > 0);

    public static readonly SettingDefinition<string> Theme = new("test.theme", "Test", "light", SettingScope.PlatformAndTenant | SettingScope.User);

    public static readonly SettingDefinition<int> PlatformOnly = new("test.platformOnly", "Test", 1, SettingScope.Platform);

    public static readonly SecretSettingDefinition Password = new("test.password", "Test");

    public SettingsHarness(TenantInfo? tenant = null, ActorType actor = ActorType.System)
    {
        TenantContext.Current.Returns(tenant);
        TenantContext.IsResolved.Returns(tenant is not null);
        if (tenant is not null)
        {
            TenantContext.Tenant.Returns(tenant);
        }

        CurrentUser.ActorType.Returns(actor);
        CurrentUser.UserId.Returns(actor == ActorType.User ? UserId : null);

        Snapshots = new SettingsSnapshotCache(Cache, TenantContext, Stores, Stores);
        Provider = new SettingsProvider(Snapshots, TenantContext, CurrentUser, Stores, Secrets, ProviderLogger);
        Manager = new SettingsManager(
            new OperationRunner(NullLogger<OperationRunner>.Instance, CurrentUser, new NoOperationTransactionFactory(), [], TimeProvider.System),
            new SettingDefinitionRegistry([Enabled, Days, Theme, PlatformOnly, Password]),
            Stores, Stores, TenantContext, CurrentUser, Secrets, Snapshots);
    }

    public ITenantContext TenantContext { get; } = Substitute.For<ITenantContext>();

    public ICurrentUser CurrentUser { get; } = Substitute.For<ICurrentUser>();

    public InMemorySettingStores Stores { get; } = new();

    public FakeReferenceDataCache Cache { get; } = new();

    public FakeSecretProtector Secrets { get; } = new();

    public RecordingLogger<SettingsProvider> ProviderLogger { get; } = new();

    public SettingsSnapshotCache Snapshots { get; }

    public SettingsProvider Provider { get; }

    public SettingsManager Manager { get; }
}
