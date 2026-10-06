using Auxilia.Application.Abstractions.Channels;
using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Images;
using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Documents.Public;
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

    public LegacyImporter(LegacyImportServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        steps =
        [
            new LocalizationStep(),
            new SettingsStep(),
            new MessagingAccountStep(services.Secrets),
            new CustomFieldsStep(),
            new SpecializationsStep(),
            new UsersStep(services.Hasher, services.Images),
            new SpecializationMembersStep(),
            new AccountSecurityStep(),
            new ServiceCatalogStep(),
            new CasesStep(),
            new DocumentsStep(services.Files, services.LegacyFiles),
            new AppointmentsStep(),
            new RequestsStep(),
            new NotificationsStep(),
            new RegistrationsStep(),
            new ImportHistoryStep(),
            new AccessStep(services.Modules),
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
        var context = new LegacyImportContext(legacy, tenant, ids, report, clock, zone, defaultLanguage, dryRun);
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

/// <summary>What the steps need from the host: password verification, pictures, the tenant's file store, the legacy files.</summary>
internal sealed record LegacyImportServices(
    IPasswordHasher Hasher, IImageProcessor Images, IFileStore Files, LegacyFiles LegacyFiles, IAccountSecretProtector Secrets, IModuleRegistry Modules);
