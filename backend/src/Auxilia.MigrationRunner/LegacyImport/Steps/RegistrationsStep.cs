using Auxilia.Domain.Directory;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.MigrationRunner.LegacyImport.Steps;

/// <summary>
/// E-05 (F02, F03): <c>RegistrationRequests</c> → <c>directory.registration_requests</c>. Not processed → Pending;
/// processed with a legacy user of the same e-mail (the approval created it) → Approved with that client; otherwise
/// Rejected. One pending request per e-mail (B-06).
/// </summary>
internal sealed class RegistrationsStep : ILegacyImportStep
{
    public const string Table = "RegistrationRequests";

    public string Name => "registrations";

    public async Task RunAsync(LegacyImportContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var users = await MigratedUsers.LoadAsync(context, cancellationToken);
        var byEmail = (await context.Legacy.Users.Select(user => new { user.Id, user.Email }).ToListAsync(cancellationToken))
            .Where(user => !string.IsNullOrWhiteSpace(user.Email))
            .GroupBy(user => user.Email.Trim().ToUpperInvariant())
            .ToDictionary(group => group.Key, group => group.Min(user => user.Id));
        var existing = await context.Tenant.Set<RegistrationRequest>().ToDictionaryAsync(request => request.Id, cancellationToken);
        var pending = existing.Values.Where(request => request.Status == RegistrationStatus.Pending).Select(request => request.Email)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = context.Report.For(Table);

        foreach (var legacy in await context.Legacy.RegistrationRequests.OrderBy(row => row.Id).ToListAsync(cancellationToken))
        {
            Guid? clientId = null;
            var status = RegistrationStatus.Pending;
            if (legacy.IsProcessed)
            {
                clientId = byEmail.TryGetValue(legacy.Email.Trim().ToUpperInvariant(), out var legacyUser) ? users.Client(context, legacyUser) : null;
                status = clientId is null ? RegistrationStatus.Rejected : RegistrationStatus.Approved;
            }

            var processedBy = legacy.ProcessedByUserId is { } processor ? users.User(context, processor) : null;
            var processedAt = legacy.ProcessedDate is { } processed ? LegacyImportContext.Instant(processed) : (DateTimeOffset?)null;
            if (context.Ids.Find(Table, legacy.Id) is { } id && existing.TryGetValue(id, out var current))
            {
                // Processed in the legacy after an earlier run.
                if (current.Status == RegistrationStatus.Pending && status != RegistrationStatus.Pending && processedBy is { } by)
                {
                    var outcome = clientId is { } client
                        ? current.Approve(client, by, legacy.Notes, processedAt ?? context.Now)
                        : current.Reject(by, legacy.Notes, processedAt ?? context.Now);
                    if (outcome.IsSuccess)
                    {
                        pending.Remove(current.Email);
                        result.Updated++;
                    }
                }

                continue;
            }

            if (status == RegistrationStatus.Pending && !pending.Add(legacy.Email.Trim()))
            {
                context.Report.Skip(Table, legacy.Id, "another pending request has the same e-mail");
                continue;
            }

            var details = new PersonDetails(legacy.FirstName, legacy.LastName, legacy.Email, context.LocalDate(legacy.DateOfBirth), legacy.Phone, legacy.FiscalCode);
            var imported = RegistrationRequest.ImportLegacy(
                context.Ids.NewId(), details, context.DefaultLanguage, LegacyImportContext.Instant(legacy.RequestDate), status, processedBy, processedAt,
                legacy.Notes, clientId);
            context.Tenant.Add(imported);
            context.Ids.Add(Table, legacy.Id, imported.Id);
            result.Created++;
        }
    }
}
