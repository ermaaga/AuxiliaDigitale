using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Auxilia.Application.Documents.Public;
using Auxilia.Domain.Cases;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Documents;
using Auxilia.Domain.Engagement;
using Auxilia.Domain.Identity;
using Auxilia.Domain.Scheduling;
using Auxilia.MigrationRunner.LegacyImport.Steps;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.MigrationRunner.LegacyImport;

/// <summary>One comparison between the legacy database and the tenant (mapping.md §7).</summary>
internal sealed record ReconciliationCheck(string Name, string Legacy, string Tenant)
{
    public bool Matches => Legacy == Tenant;
}

/// <summary>
/// E-06: the reconciliation every import run ends with, inside its transaction (so a dry run is reconciled too). Each
/// check compares what the legacy holds, net of the declared exclusions and the rows skipped with a reason, with what
/// the tenant holds for the rows the import mapped. Any difference blocks the cutover (exit code 2).
/// </summary>
internal sealed class LegacyReconciliation
{
    /// <summary>Tables imported row by row through the id map: legacy rows = mapped + excluded + skipped.</summary>
    public static readonly IReadOnlyList<string> MappedTables =
    [
        UsersStep.Table, SpecializationsStep.Table, ServiceCatalogStep.CategoriesTable, ServiceCatalogStep.ServicesTable,
        ServiceCatalogStep.FoldersTable, CasesStep.Table, DocumentsStep.Table, AppointmentsStep.Table, RequestsStep.Table, NotificationsStep.Table,
        RegistrationsStep.Table, ImportHistoryStep.TypesTable, ImportHistoryStep.ImportsTable, ImportHistoryStep.JobsTable,
        AccountSecurityStep.AttemptsTable, MessagingAccountStep.Table,
    ];

    private readonly IFileStore files;

    public LegacyReconciliation(IFileStore files) => this.files = files;

    public async Task<IReadOnlyList<ReconciliationCheck>> RunAsync(LegacySource source, LegacyImportContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(context);

        var checks = new List<ReconciliationCheck>();
        await RowsAsync(source, context, checks, cancellationToken);
        await UsersAsync(context, checks, cancellationToken);
        await CasesAsync(context, checks, cancellationToken);
        await ActivityAsync(context, checks, cancellationToken);
        if (!context.DryRun)
        {
            await DocumentsAsync(context, checks, cancellationToken);
        }

        return checks;
    }

    public static string Render(IReadOnlyList<ReconciliationCheck> checks)
    {
        ArgumentNullException.ThrowIfNull(checks);

        var culture = CultureInfo.InvariantCulture;
        var text = new StringBuilder();
        var differences = checks.Count(check => !check.Matches);
        text.AppendLine(culture, $"reconciliation: {(differences == 0 ? "no differences" : $"{differences} difference(s), the cutover is blocked")}");
        foreach (var check in checks)
        {
            text.AppendLine(culture, $"{(check.Matches ? "ok  " : "DIFF")} {check.Name,-48} legacy {check.Legacy,-24} tenant {check.Tenant}");
        }

        return text.ToString();
    }

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Counts<TKey>(IEnumerable<TKey> values)
        where TKey : notnull =>
        string.Join(", ", values.GroupBy(value => value).OrderBy(group => group.Key.ToString(), StringComparer.Ordinal)
            .Select(group => $"{group.Key} {group.Count().ToString(CultureInfo.InvariantCulture)}"));

    private static async Task RowsAsync(LegacySource source, LegacyImportContext context, List<ReconciliationCheck> checks, CancellationToken cancellationToken)
    {
        var mapped = context.Ids.Counts();
        foreach (var name in MappedTables)
        {
            var table = LegacyTables.All.Single(item => item.Name == name);
            if (await source.CountAsync(table, cancellationToken) is not { } rows)
            {
                continue;
            }

            var result = context.Report.Tables.GetValueOrDefault(name);
            var skipped = context.Report.Issues
                .Where(issue => issue.Table == name && issue.Kind == LegacyIssueKind.Skipped && context.Ids.Find(name, issue.LegacyId) is null)
                .Select(issue => issue.LegacyId).Distinct().Count();
            var accounted = mapped.GetValueOrDefault(name) + (result?.Excluded ?? 0) + skipped;
            checks.Add(new ReconciliationCheck($"{name}: rows = imported + excluded + skipped", rows.ToString(CultureInfo.InvariantCulture), Count(accounted)));
        }
    }

    private static async Task UsersAsync(LegacyImportContext context, List<ReconciliationCheck> checks, CancellationToken cancellationToken)
    {
        var roleNames = await context.Legacy.Roles.ToDictionaryAsync(role => role.Id, role => role.Name, cancellationToken);
        var legacyRoles = (await context.Legacy.UserRoles.ToListAsync(cancellationToken))
            .Where(row => context.Ids.Find(UsersStep.Table, row.UserId) is not null)
            .Select(row => roleNames.GetValueOrDefault(row.RoleId))
            .Where(name => TenantRoles.TryParse(name, out _))
            .Select(name => name!);
        var users = await context.Tenant.Set<User>().AsNoTracking().ToListAsync(cancellationToken);
        var mappedUsers = users.Where(user => IsMapped(context, UsersStep.Table, user.Id)).ToList();
        checks.Add(new ReconciliationCheck("users per role", Counts(legacyRoles), Counts(mappedUsers.SelectMany(user => user.Roles).Select(role => role.ToString()))));

        // Clients with a migrated employee in charge: the legacy assignment and the open one in the tenant.
        var legacyUsers = await context.Legacy.Users.Select(user => new { user.Id, user.AssignedEmployeeId }).ToListAsync(cancellationToken);
        var employees = users.Where(user => user.Roles.Contains(TenantRole.Employee)).Select(user => user.Id).ToHashSet();
        var clients = await context.Tenant.Set<ClientProfile>().AsNoTracking().ToDictionaryAsync(profile => profile.Id, profile => profile.EmployeeUserId, cancellationToken);
        var people = users.ToDictionary(user => user.Id, user => user.PersonId);
        var expected = 0;
        var found = 0;
        foreach (var row in legacyUsers)
        {
            if (row.AssignedEmployeeId is not { } legacyEmployee || context.Ids.Find(UsersStep.Table, row.Id) is not { } userId
                || !people.TryGetValue(userId, out var personId) || !clients.TryGetValue(personId, out var current)
                || context.Ids.Find(UsersStep.Table, legacyEmployee) is not { } employeeId || !employees.Contains(employeeId))
            {
                continue;
            }

            expected++;
            found += current == employeeId ? 1 : 0;
        }

        checks.Add(new ReconciliationCheck("clients assigned to a migrated employee", Count(expected), Count(found)));
    }

    private static async Task CasesAsync(LegacyImportContext context, List<ReconciliationCheck> checks, CancellationToken cancellationToken)
    {
        var legacy = (await context.Legacy.Subscriptions.ToListAsync(cancellationToken))
            .Select(row => (Row: row, Id: context.Ids.Find(CasesStep.Table, row.Id)))
            .Where(item => item.Id is not null)
            .ToList();
        var ids = legacy.Select(item => item.Id!.Value).ToList();
        var cases = await context.Tenant.Set<Case>().AsNoTracking().Where(@case => ids.Contains(@case.Id)).ToListAsync(cancellationToken);

        checks.Add(new ReconciliationCheck(
            "cases per status",
            Counts(legacy.Select(item => CasesStep.Status(item.Row.Status)?.ToString() ?? "?")),
            Counts(cases.Select(@case => @case.Status.ToString()))));
        checks.Add(new ReconciliationCheck("rejected cases", Count(legacy.Count(item => item.Row.IsRejected)), Count(cases.Count(@case => @case.IsRejected))));
        checks.Add(new ReconciliationCheck(
            "amount paid (legacy) = legacy payments",
            legacy.Sum(item => decimal.Round(item.Row.AmountPaid, 2)).ToString("0.00", CultureInfo.InvariantCulture),
            cases.SelectMany(@case => @case.Payments).Where(payment => payment.Note == Case.LegacyPaymentNote).Sum(payment => payment.Amount)
                .ToString("0.00", CultureInfo.InvariantCulture)));
    }

    private static async Task ActivityAsync(LegacyImportContext context, List<ReconciliationCheck> checks, CancellationToken cancellationToken)
    {
        var appointments = (await context.Legacy.Appointments.ToListAsync(cancellationToken))
            .Where(row => context.Ids.Find(AppointmentsStep.Table, row.Id) is not null)
            .Select(row => AppointmentsStep.Status(row.Status)?.ToString() ?? "?");
        var tenantAppointments = (await context.Tenant.Set<Appointment>().AsNoTracking().ToListAsync(cancellationToken))
            .Where(appointment => IsMapped(context, AppointmentsStep.Table, appointment.Id))
            .Select(appointment => appointment.Status.ToString());
        checks.Add(new ReconciliationCheck("appointments per status", Counts(appointments), Counts(tenantAppointments)));

        var answered = (await context.Legacy.Requests.ToListAsync(cancellationToken))
            .Count(row => context.Ids.Find(RequestsStep.Table, row.Id) is not null && !string.IsNullOrWhiteSpace(row.Response));
        var replied = (await context.Tenant.Set<Request>().AsNoTracking().ToListAsync(cancellationToken))
            .Count(request => IsMapped(context, RequestsStep.Table, request.Id) && request.Messages.Count >= 2);
        checks.Add(new ReconciliationCheck("requests with a response", Count(answered), Count(replied)));

        var registrations = (await context.Tenant.Set<RegistrationRequest>().AsNoTracking().ToListAsync(cancellationToken))
            .Where(request => IsMapped(context, RegistrationsStep.Table, request.Id)).ToList();
        var processed = (await context.Legacy.RegistrationRequests.ToListAsync(cancellationToken))
            .Count(row => context.Ids.Find(RegistrationsStep.Table, row.Id) is not null && row.IsProcessed);
        checks.Add(new ReconciliationCheck("processed registrations", Count(processed), Count(registrations.Count(request => request.Status != RegistrationStatus.Pending))));
    }

    /// <summary>Every migrated document is read back from the storage: same size and SHA-256 as recorded.</summary>
    private async Task DocumentsAsync(LegacyImportContext context, List<ReconciliationCheck> checks, CancellationToken cancellationToken)
    {
        // --since: documents imported (and verified) before the instant are not read back again.
        var documents = (await context.Tenant.Set<Document>().AsNoTracking().ToListAsync(cancellationToken))
            .Where(document => context.Ids.ImportedAt(DocumentsStep.Table, document.Id) is { } at && (context.Since is not { } since || at >= since))
            .ToList();
        var intact = 0;
        foreach (var document in documents)
        {
            var opened = await files.OpenReadAsync(document.StorageKey, cancellationToken);
            if (opened.IsFailure || opened.Value is not { } stream)
            {
                continue;
            }

            await using (stream)
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                long size = 0;
                int read;
                while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                    size += read;
                }

                intact += size == document.Size && Convert.ToHexStringLower(hash.GetHashAndReset()) == document.Sha256 ? 1 : 0;
            }
        }

        var name = context.Since is { } from ? $"documents imported since {from:yyyy-MM-dd HH:mm}Z, SHA-256" : "documents readable with their SHA-256";
        checks.Add(new ReconciliationCheck(name, Count(documents.Count), Count(intact)));
    }

    private static bool IsMapped(LegacyImportContext context, string table, Guid id) => context.Ids.IsMapped(table, id);
}
