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

    /// <summary>Send campaigns ("send now", N01: only Administrators by default).</summary>
    public const string SendCampaigns = "marketing.campaigns.send";

    /// <summary>Segments and static lists (N01, M-02).</summary>
    public const string ViewAudiences = "marketing.audiences.view";

    /// <summary>Create and change segments and static lists (N01, M-02).</summary>
    public const string ManageAudiences = "marketing.audiences.manage";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(ViewCampaigns, [TenantRole.Administrator, TenantRole.Employee]),
        new(ManageCampaigns, [TenantRole.Administrator, TenantRole.Employee]),
        new(SendCampaigns, [TenantRole.Administrator]),
        new(ViewAudiences, [TenantRole.Administrator, TenantRole.Employee]),
        new(ManageAudiences, [TenantRole.Administrator, TenantRole.Employee]),
    ];
}
