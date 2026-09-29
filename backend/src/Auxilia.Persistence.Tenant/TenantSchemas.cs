namespace Auxilia.Persistence.Tenant;

/// <summary>One PostgreSQL schema per module in every tenant database (ARCHITECTURE §10.3, no <c>training</c>, D-09).</summary>
public static class TenantSchemas
{
    public const string Identity = "identity";
    public const string Directory = "directory";
    public const string Cases = "cases";
    public const string Scheduling = "scheduling";
    public const string Documents = "documents";
    public const string Engagement = "engagement";
    public const string Messaging = "messaging";
    public const string Marketing = "marketing";
    public const string Imports = "imports";
    public const string Configuration = "configuration";
    public const string Localization = "localization";
    public const string Audit = "audit";
    public const string Ops = "ops";
}
