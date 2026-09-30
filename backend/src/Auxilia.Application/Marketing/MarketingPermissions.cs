using Auxilia.Application.Abstractions.Modules;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Application.Marketing;

/// <summary>Permissions of the marketing module (<c>identity.role_permissions</c>, F22); default roles reproduce the legacy pages.</summary>
public static class MarketingPermissions
{
    /// <summary>Campaigns and their results (N01).</summary>
    public const string ViewCampaigns = "marketing.campaigns.view";

    /// <summary>Create, schedule and send campaigns (N01).</summary>
    public const string ManageCampaigns = "marketing.campaigns.manage";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(ViewCampaigns, [TenantRole.Administrator, TenantRole.Employee]),
        new(ManageCampaigns, [TenantRole.Administrator, TenantRole.Employee]),
    ];
}
