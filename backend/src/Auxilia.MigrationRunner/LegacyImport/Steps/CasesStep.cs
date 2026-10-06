using System.Globalization;
using System.Text.Json;

using Auxilia.Domain.Cases;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Identity;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.MigrationRunner.LegacyImport.Steps;

/// <summary>
/// E-03 (F09, F10): <c>Subscriptions</c> → <c>cases.cases</c> with one history row for the legacy status and the legacy
/// <c>AmountPaid</c> as one payment, numbers <c>{year}-{sequence}</c> from <c>cases.case_numbers</c> by the year of the
/// start date; then the business status of every client with migrated cases (Q03). Rules: mapping.md §5.2.
/// </summary>
internal sealed class CasesStep : ILegacyImportStep
{
    public const string Table = "Subscriptions";

    public string Name => "cases";

    public async Task RunAsync(LegacyImportContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var db = context.Tenant;
        var clients = await (from user in db.Set<User>().AsNoTracking()
                             join profile in db.Set<ClientProfile>().AsNoTracking() on user.PersonId equals profile.Id
                             select new { user.Id, user.PersonId }).ToDictionaryAsync(row => row.Id, row => row.PersonId, cancellationToken);
        var services = await db.Set<Service>().AsNoTracking().ToDictionaryAsync(service => service.Id, cancellationToken);
        var employeeSpecializations = (await db.Set<Specialization>().AsNoTracking().Where(specialization => specialization.Role == TenantRole.Employee)
            .Select(specialization => specialization.Id).ToListAsync(cancellationToken)).ToHashSet();
        var prices = await context.Legacy.Memberships.ToDictionaryAsync(membership => membership.Id, membership => membership.Price, cancellationToken);
        var existing = await db.Set<Case>().ToDictionaryAsync(@case => @case.Id, cancellationToken);
        var result = context.Report.For(Table);

        foreach (var legacy in await context.Legacy.Subscriptions.OrderBy(row => row.Id).ToListAsync(cancellationToken))
        {
            if (Status(legacy.Status) is not { } status)
            {
                context.Report.Skip(Table, legacy.Id, $"unknown status {legacy.Status.ToString(CultureInfo.InvariantCulture)}");
                continue;
            }

            if (context.Ids.Find(UsersStep.Table, legacy.UserId) is not { } userId || !clients.TryGetValue(userId, out var clientId))
            {
                context.Report.Skip(Table, legacy.Id, "client not migrated or not a client");
                continue;
            }

            if (context.Ids.Find(ServiceCatalogStep.ServicesTable, legacy.MembershipId) is not { } serviceId || !services.TryGetValue(serviceId, out var service))
            {
                context.Report.Skip(Table, legacy.Id, "service not migrated");
                continue;
            }

            if (context.LocalDate(legacy.StartDate) is not { } startedOn)
            {
                context.Report.Skip(Table, legacy.Id, "start date missing");
                continue;
            }

            var amount = decimal.Round(legacy.AmountPaid, 2);
            if (amount != legacy.AmountPaid)
            {
                context.Report.Warn(Table, legacy.Id, "amount paid with more than two decimals: rounded");
            }

            var expiresOn = legacy.EndDate is { } end ? context.LocalDate(end) : null;
            var completedAt = status == CaseStatus.Completed ? (legacy.EndDate is { } completed ? LegacyImportContext.Instant(completed) : context.Now) : (DateTimeOffset?)null;
            var state = new LegacyCaseState(status, legacy.IsRejected, legacy.IsActive, expiresOn, completedAt, amount, startedOn);

            if (context.Ids.Find(Table, legacy.Id) is { } id && existing.TryGetValue(id, out var current))
            {
                if (current.ApplyLegacyState(state, context.Now))
                {
                    result.Updated++;
                }

                continue;
            }

            Guid? specialization = null;
            if ((legacy.RoleSpecializationId is { } legacySpecialization ? context.Ids.Find(SpecializationsStep.Table, legacySpecialization) : service.SpecializationId) is { } mapped)
            {
                if (employeeSpecializations.Contains(mapped))
                {
                    specialization = mapped;
                }
                else
                {
                    context.Report.Warn(Table, legacy.Id, "specialization not migrated or not of the Employee role: left out");
                }
            }
            else if (legacy.RoleSpecializationId is not null)
            {
                context.Report.Warn(Table, legacy.Id, "specialization not migrated or not of the Employee role: left out");
            }

            var customFields = CustomFields(legacy.CustomFields);
            if (customFields is null)
            {
                context.Report.Warn(Table, legacy.Id, "custom fields not a JSON object: left out");
            }

            var number = await NextNumberAsync(context, startedOn.Year, cancellationToken);
            var price = decimal.Round(prices.GetValueOrDefault(legacy.MembershipId, service.Price), 2);
            var opening = new CaseOpening(number, clientId, serviceId, price, Service.DefaultCurrency, specialization, startedOn, DueOn: null, customFields ?? "{}");
            var newId = context.Ids.NewId();
            var imported = Case.ImportLegacy(newId, opening, state, context.Now);
            if (imported.IsFailure)
            {
                context.Report.Skip(Table, legacy.Id, $"the case is not valid ({string.Join(", ", imported.Error!.ValidationErrors?.Keys ?? [])})");
                continue;
            }

            db.Add(imported.Value);
            existing[newId] = imported.Value;
            context.Ids.Add(Table, legacy.Id, newId);
            result.Created++;
        }

        await ClientStatusAsync(context, existing.Values, cancellationToken);
    }

    /// <summary>Legacy <c>SubscriptionStatus</c> (0…3) → <see cref="CaseStatus"/>.</summary>
    public static CaseStatus? Status(int legacy) => legacy switch
    {
        0 => CaseStatus.Inserted,
        1 => CaseStatus.InProgress,
        2 => CaseStatus.Sent,
        3 => CaseStatus.Completed,
        _ => null,
    };

    /// <summary>The legacy jsonb as a JSON object; empty → <c>{}</c>, anything else → <c>null</c>.</summary>
    public static string? CustomFields(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return "{}";
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object ? json : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Same sequence the case manager uses: the year's row is locked until the import transaction ends.</summary>
    private static async Task<string> NextNumberAsync(LegacyImportContext context, int year, CancellationToken cancellationToken)
    {
        var next = (await context.Tenant.Database.SqlQuery<int>($"""
            INSERT INTO cases.case_numbers (year, last_number) VALUES ({year}, 1)
            ON CONFLICT (year) DO UPDATE SET last_number = cases.case_numbers.last_number + 1
            RETURNING last_number AS "Value"
            """).ToListAsync(cancellationToken)).Single();
        return string.Create(CultureInfo.InvariantCulture, $"{year}-{next:D5}");
    }

    /// <summary>Q03: a client is active while at least one of its cases counts as open.</summary>
    private static async Task ClientStatusAsync(LegacyImportContext context, IEnumerable<Case> cases, CancellationToken cancellationToken)
    {
        var open = cases.GroupBy(@case => @case.ClientId)
            .ToDictionary(group => group.Key, group => group.Any(@case => Case.CountsAsOpen(@case.IsActive, @case.Status, @case.ExpiresOn, context.Today)));
        var ids = open.Keys.ToList();
        var profiles = await context.Tenant.Set<ClientProfile>().Where(profile => ids.Contains(profile.Id)).ToListAsync(cancellationToken);
        foreach (var profile in profiles)
        {
            profile.UpdateStatus(open[profile.Id], context.Now);
        }
    }
}
