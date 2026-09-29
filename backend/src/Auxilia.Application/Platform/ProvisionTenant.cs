namespace Auxilia.Application.Platform;

/// <summary>
/// Input of tenant provisioning. <see cref="ExistingConnectionString"/> is set only when a DBA provides the database
/// (<c>--existing-database</c>); it is stored protected and never logged.
/// </summary>
public sealed record ProvisionTenant(string Slug, string DisplayName, string DefaultLanguage, string TimeZone, string? ExistingConnectionString = null)
{
    public override string ToString() => $"ProvisionTenant {{ Slug = {Slug} }}";
}
