using System.Text.RegularExpressions;

namespace Auxilia.SharedKernel.Tenancy;

/// <summary>
/// Tenant slug rule (skill auxilia-multitenancy): lowercase letters, digits and inner hyphens, 3–40 characters,
/// immutable after provisioning. Used by the catalog, tenant resolution, log file paths and storage prefixes.
/// </summary>
public static partial class TenantSlug
{
    public const int MinLength = 3;

    public const int MaxLength = 40;

    public static bool IsValid(string? slug) => slug is not null && Pattern().IsMatch(slug);

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{1,38}[a-z0-9])$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
