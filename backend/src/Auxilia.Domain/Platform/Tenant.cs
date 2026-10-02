using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Domain;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Domain.Platform;

/// <summary>
/// A customer organisation with its own database (catalog <c>tenants</c>). The slug is immutable; the connection
/// string is stored encrypted (Data Protection) and never logged.
/// </summary>
public sealed class Tenant : AggregateRoot<Guid>
{
    public const int DisplayNameMaxLength = 200;
    public const int LanguageMaxLength = 10;
    public const int TimeZoneMaxLength = 64;
    public const int VersionMaxLength = 150;

    /// <summary>How far ahead debug logging can be enabled (D-28: temporary by definition).</summary>
    public static readonly TimeSpan MaxDebugLogging = TimeSpan.FromHours(24);

    /// <summary>Slugs that name platform hosts or paths and can never be a tenant (skill auxilia-tenant-provisioning).</summary>
    public static readonly IReadOnlySet<string> ReservedSlugs = new HashSet<string>(StringComparer.Ordinal)
    {
        "api", "app", "www", "platform", "admin", "system", "static", "assets", "health", "status", "docs", "mail",
        "auth", "login", "logout", "support", "help", "cdn", "console", "public",
        // Console paths (/platform/tenants/new).
        "new",
    };

    private static readonly Dictionary<TenantStatus, TenantStatus[]> Transitions = new()
    {
        [TenantStatus.Provisioning] = [TenantStatus.Active, TenantStatus.MigrationFailed, TenantStatus.Archived],
        [TenantStatus.Active] = [TenantStatus.Suspended, TenantStatus.MigrationFailed, TenantStatus.Archived],
        [TenantStatus.Suspended] = [TenantStatus.Active, TenantStatus.Archived],
        [TenantStatus.MigrationFailed] = [TenantStatus.Active, TenantStatus.Archived],
        [TenantStatus.Archived] = [],
    };

    private Tenant(Guid id, string slug, string displayName, string defaultLanguage, string timeZone)
        : base(id)
    {
        Slug = slug;
        DisplayName = displayName;
        DefaultLanguage = defaultLanguage;
        TimeZone = timeZone;
        Status = TenantStatus.Provisioning;
    }

    private Tenant()
    {
        Slug = DisplayName = DefaultLanguage = TimeZone = string.Empty;
    }

    public string Slug { get; private set; }

    public string DisplayName { get; private set; }

    public TenantStatus Status { get; private set; }

    /// <summary>Connection string protected with Data Protection (purpose <c>Auxilia.Tenancy.ConnectionString.v1</c>).</summary>
    public string? ConnectionSecret { get; private set; }

    public string? SchemaVersion { get; private set; }

    public string? DataVersion { get; private set; }

    public string DefaultLanguage { get; private set; }

    public string TimeZone { get; private set; }

    public DateTimeOffset? ArchivedAt { get; private set; }

    /// <summary>Until when the tenant's Debug events are written (D-28); past or <c>null</c>: the default level applies.</summary>
    public DateTimeOffset? DebugLoggingUntil { get; private set; }

    /// <summary>The end of the debug logging when it is still running at <paramref name="now"/>.</summary>
    public DateTimeOffset? ActiveDebugLoggingUntil(DateTimeOffset now) => DebugLoggingUntil > now ? DebugLoggingUntil : null;

    public static Result<Tenant> Create(Guid id, string slug, string displayName, string defaultLanguage, string timeZone)
    {
        if (!SharedKernel.Tenancy.TenantSlug.IsValid(slug))
        {
            return Errors.Tenancy.TenantSlugInvalid();
        }

        if (ReservedSlugs.Contains(slug))
        {
            return Errors.Tenancy.TenantSlugReserved();
        }

        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > DisplayNameMaxLength)
        {
            return Errors.Tenancy.CatalogValueInvalid("displayName", "validation.tenant.displayName");
        }

        if (string.IsNullOrWhiteSpace(defaultLanguage) || defaultLanguage.Length > LanguageMaxLength)
        {
            return Errors.Tenancy.CatalogValueInvalid("defaultLanguage", "validation.tenant.defaultLanguage");
        }

        if (string.IsNullOrWhiteSpace(timeZone) || timeZone.Length > TimeZoneMaxLength)
        {
            return Errors.Tenancy.CatalogValueInvalid("timeZone", "validation.tenant.timeZone");
        }

        return new Tenant(id, slug, displayName.Trim(), defaultLanguage, timeZone);
    }

    public void SetConnectionSecret(string protectedConnectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedConnectionString);
        ConnectionSecret = protectedConnectionString;
    }

    public void SetVersions(string? schemaVersion, string? dataVersion)
    {
        SchemaVersion = schemaVersion;
        DataVersion = dataVersion;
    }

    public Result Rename(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > DisplayNameMaxLength)
        {
            return Errors.Tenancy.CatalogValueInvalid("displayName", "validation.tenant.displayName");
        }

        DisplayName = displayName.Trim();
        return Result.Success();
    }

    /// <summary>Changes name and time zone (the slug never changes); an archived tenant is read-only (D-25).</summary>
    public Result Update(string displayName, string timeZone)
    {
        if (Status == TenantStatus.Archived)
        {
            return Errors.Tenancy.TenantArchived();
        }

        if (string.IsNullOrWhiteSpace(timeZone) || timeZone.Length > TimeZoneMaxLength)
        {
            return Errors.Tenancy.CatalogValueInvalid("timeZone", "validation.tenant.timeZone");
        }

        var renamed = Rename(displayName);
        if (renamed.IsSuccess)
        {
            TimeZone = timeZone;
        }

        return renamed;
    }

    /// <summary>
    /// Writes the tenant's Debug events until <paramref name="until"/>, within <see cref="MaxDebugLogging"/>; the level
    /// goes back by itself afterwards. Archived tenants are read-only (D-25).
    /// </summary>
    public Result EnableDebugLogging(DateTimeOffset until, DateTimeOffset now)
    {
        if (Status == TenantStatus.Archived)
        {
            return Errors.Tenancy.TenantArchived();
        }

        if (until <= now || until > now + MaxDebugLogging)
        {
            return Errors.Tenancy.LogLevelUntilInvalid();
        }

        DebugLoggingUntil = until;
        return Result.Success();
    }

    /// <summary>Back to the default level now (allowed when archived too: it only reduces logging).</summary>
    public void DisableDebugLogging() => DebugLoggingUntil = null;

    public Result Activate() => MoveTo(TenantStatus.Active);

    public Result Suspend() => MoveTo(TenantStatus.Suspended);

    public Result MarkMigrationFailed() => MoveTo(TenantStatus.MigrationFailed);

    public Result Archive(DateTimeOffset now)
    {
        var result = MoveTo(TenantStatus.Archived);
        if (result.IsSuccess)
        {
            ArchivedAt = now;
        }

        return result;
    }

    public static bool CanMove(TenantStatus from, TenantStatus to) => Transitions[from].Contains(to);

    private Result MoveTo(TenantStatus target)
    {
        if (!CanMove(Status, target))
        {
            return Errors.Tenancy.TenantTransitionNotAllowed(Status.ToString(), target.ToString());
        }

        Status = target;
        return Result.Success();
    }
}
