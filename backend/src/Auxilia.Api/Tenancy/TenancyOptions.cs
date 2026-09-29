namespace Auxilia.Api.Tenancy;

/// <summary>Host-based tenant resolution (section <c>Tenancy</c>).</summary>
public sealed class TenancyOptions
{
    public const string SectionName = "Tenancy";

    /// <summary>Domains whose first label is the tenant slug, e.g. <c>auxilia.app</c> → <c>acme.auxilia.app</c>.</summary>
    public string[] BaseDomains { get; set; } = [];

    /// <summary>Hosts that never identify a tenant (the API host itself, localhost).</summary>
    public string[] IgnoredHosts { get; set; } = ["localhost", "127.0.0.1", "[::1]"];
}
