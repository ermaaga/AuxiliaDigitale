using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Images;
using Auxilia.MigrationRunner.LegacyImport.Steps;
using Auxilia.Persistence.Tenant;

namespace Auxilia.MigrationRunner.LegacyImport;

/// <summary>
/// Runs the import steps in the order of docs/migration/mapping.md §4, all in one transaction of the tenant database:
/// committed at the end, or rolled back with <c>--dry-run</c> (the report is the same). Writes go through the
/// persistence model, never the Managers (ADR 0016).
/// </summary>
internal sealed class LegacyImporter
{
    private readonly IReadOnlyList<ILegacyImportStep> steps;

    public LegacyImporter(IPasswordHasher hasher, IImageProcessor images)
    {
        steps =
        [
            new SpecializationsStep(),
            new UsersStep(hasher, images),
            new SpecializationMembersStep(),
            new AccountSecurityStep(),
        ];
    }

    public IReadOnlyList<string> StepNames => steps.Select(step => step.Name).ToArray();

    public async Task<LegacyImportReport> RunAsync(
        LegacySource source, TenantDbContext tenant, TimeProvider clock, TimeZoneInfo zone, string defaultLanguage, bool dryRun,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(tenant);

        var report = new LegacyImportReport();
        await using var transaction = await tenant.Database.BeginTransactionAsync(cancellationToken);
        await using var legacy = source.CreateContext();
        var ids = await LegacyIdMap.LoadAsync(tenant, clock, cancellationToken);
        var context = new LegacyImportContext(legacy, tenant, ids, report, clock, zone, defaultLanguage);
        foreach (var step in steps)
        {
            await step.RunAsync(context, cancellationToken);
            await tenant.SaveChangesAsync(cancellationToken);
        }

        if (dryRun)
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        else
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return report;
    }
}
