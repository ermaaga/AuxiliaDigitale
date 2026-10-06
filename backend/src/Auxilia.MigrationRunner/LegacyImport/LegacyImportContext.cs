using Auxilia.Persistence.Tenant;

namespace Auxilia.MigrationRunner.LegacyImport;

/// <summary>Everything a step of the import works with: both databases, the id map, the report and the tenant's clock.</summary>
internal sealed class LegacyImportContext
{
    public LegacyImportContext(
        LegacyDbContext legacy, TenantDbContext tenant, LegacyIdMap ids, LegacyImportReport report, TimeProvider clock, TimeZoneInfo zone,
        string defaultLanguage, bool dryRun = false)
    {
        DryRun = dryRun;
        Legacy = legacy;
        Tenant = tenant;
        Ids = ids;
        Report = report;
        Zone = zone;
        DefaultLanguage = defaultLanguage;
        Now = clock.GetUtcNow();
        Today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Now, zone).DateTime);
    }

    public LegacyDbContext Legacy { get; }

    public TenantDbContext Tenant { get; }

    public LegacyIdMap Ids { get; }

    public LegacyImportReport Report { get; }

    /// <summary>Time zone of the tenant: calendar dates the legacy stored as instants are read in it.</summary>
    public TimeZoneInfo Zone { get; }

    public string DefaultLanguage { get; }

    /// <summary>Nothing is kept: the transaction is rolled back and files are not committed to the storage.</summary>
    public bool DryRun { get; }

    public DateTimeOffset Now { get; }

    public DateOnly Today { get; }

    /// <summary>A legacy <c>timestamp with time zone</c> as an instant.</summary>
    public static DateTimeOffset Instant(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    /// <summary>
    /// A calendar date the legacy stored as an instant (midnight local time saved in UTC, e.g. a birth date): the date in
    /// the tenant time zone. <c>-infinity</c> (the default of columns added later) and <c>infinity</c> → <c>null</c>.
    /// </summary>
    public DateOnly? LocalDate(DateTime value) =>
        value == DateTime.MinValue || value == DateTime.MaxValue
            ? null
            : DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Instant(value), Zone).DateTime);
}

/// <summary>One part of the import (e.g. users); steps run in order inside one transaction, each followed by a save.</summary>
internal interface ILegacyImportStep
{
    string Name { get; }

    Task RunAsync(LegacyImportContext context, CancellationToken cancellationToken);
}
