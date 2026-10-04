using Auxilia.Application.Abstractions.Imports;
using Auxilia.Application.Abstractions.Marketing;
using Auxilia.Diagnostics;
using Auxilia.Domain.Marketing;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Marketing;

/// <summary>
/// Members of static lists from a workbook (N01, M-02: "lists from import"): the list by name, the client by fiscal
/// code; a client already in the list counts as imported.
/// </summary>
internal sealed class ListMemberImportTarget(IAudienceDataFactory data, IImportLookups lookups, TimeProvider clock) : IImportTarget
{
    public string Entity => "ListMember";

    public IReadOnlyList<ImportField> Fields { get; } =
    [
        new("list", "app.imports.field.list", Required: true),
        new("clientFiscalCode", "app.imports.field.clientFiscalCode", Required: true),
    ];

    public async Task<IReadOnlyList<ImportRowErrors>> ValidateAsync(IReadOnlyList<ImportRow> rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var errors = rows.Select(_ => new ImportRowErrors()).ToArray();
        var lists = await ListsAsync(cancellationToken);
        var clients = await lookups.ClientsAsync([.. rows.Select(row => row.Value("clientFiscalCode")).OfType<string>()], cancellationToken);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            ImportValues.CheckRequired(row, Fields, errors[index]);
            if (row.Value("list") is { } list && !lists.ContainsKey(list))
            {
                errors[index].Add("list", ImportValues.NotFound);
            }

            if (row.Value("clientFiscalCode") is { } code && !clients.ContainsKey(code))
            {
                errors[index].Add("clientFiscalCode", ImportValues.NotFound);
            }

            if (row.Value("list") is { } name && row.Value("clientFiscalCode") is { } fiscalCode && !seen.Add($"{name}\n{fiscalCode}"))
            {
                errors[index].Add("clientFiscalCode", ImportValues.Duplicate);
            }
        }

        return errors;
    }

    public async Task<Result<Guid>> ImportAsync(ImportRow row, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(row);

        var lists = await ListsAsync(cancellationToken);
        var fiscalCode = row.Value("clientFiscalCode") ?? string.Empty;
        if (!lists.TryGetValue(row.Value("list") ?? string.Empty, out var listId)
            || !(await lookups.ClientsAsync([fiscalCode], cancellationToken)).TryGetValue(fiscalCode, out var clientId))
        {
            return Errors.Marketing.ListInvalid("list", ImportValues.NotFound);
        }

        await using var store = await data.OpenAsync(cancellationToken);
        if ((await store.MembersAmongAsync(listId, [clientId], cancellationToken)).Count == 0)
        {
            store.AddMembers([new StaticListMember(listId, clientId, null, clock.GetUtcNow())]);
            await store.SaveChangesAsync(cancellationToken);
        }

        return clientId;
    }

    private async Task<Dictionary<string, Guid>> ListsAsync(CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        return (await store.ListsAsync(AudienceScope.Everyone, cancellationToken)).ToDictionary(list => list.Name, list => list.Id, StringComparer.OrdinalIgnoreCase);
    }
}
