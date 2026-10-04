using Auxilia.Application.Abstractions.Imports;
using Auxilia.Contracts.Cases;
using Auxilia.Domain.Cases;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Cases;

/// <summary>
/// Services from a workbook (F19, legacy "Membership" import, Q47): the catalog rules, name unique in the file and in
/// the tenant, category and specialization by name (active ones). Created by <see cref="IServiceCatalogManager"/>.
/// </summary>
internal sealed class ServiceImportTarget(IServiceCatalogManager catalog, IImportLookups lookups) : IImportTarget
{
    public string Entity => "Service";

    public IReadOnlyList<ImportField> Fields { get; } =
    [
        new("name", "Name", Required: true),
        new("description", "Description", Required: false),
        new("price", "Price", Required: true),
        new("durationDays", "DurationDays", Required: true),
        new("category", "app.services.category", Required: false),
        new("specialization", "Specialization", Required: false),
    ];

    public async Task<IReadOnlyList<ImportRowErrors>> ValidateAsync(IReadOnlyList<ImportRow> rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var errors = rows.Select(_ => new ImportRowErrors()).ToArray();
        var taken = await lookups.ServicesAsync(Values(rows, "name"), activeOnly: false, cancellationToken);
        var categories = await lookups.ServiceCategoriesAsync(Values(rows, "category"), cancellationToken);
        var specializations = await lookups.EmployeeSpecializationsAsync(Values(rows, "specialization"), cancellationToken);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var rowErrors = errors[index];
            ImportValues.CheckRequired(row, Fields, rowErrors);
            var price = ImportValues.Amount(row, "price", rowErrors);
            var duration = ImportValues.WholeNumber(row, "durationDays", rowErrors);
            if (price is not null && duration is not null
                && Service.Create(Guid.Empty, new ServiceDetails(row.Value("name"), row.Value("description"), price.Value, duration.Value, null, null, true)) is { IsFailure: true } service)
            {
                rowErrors.Add(service.Error!);
            }

            if (row.Value("name") is { } name && taken.ContainsKey(name))
            {
                rowErrors.Add("name", ImportValues.Taken);
            }

            if (row.Value("category") is { } category && !categories.ContainsKey(category))
            {
                rowErrors.Add("category", ImportValues.NotFound);
            }

            if (row.Value("specialization") is { } specialization && !specializations.ContainsKey(specialization))
            {
                rowErrors.Add("specialization", ImportValues.NotFound);
            }
        }

        ImportValues.CheckDuplicates(rows, errors, "name");
        return errors;
    }

    public async Task<Result<Guid>> ImportAsync(ImportRow row, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(row);

        var errors = new ImportRowErrors();
        Guid? category = null;
        Guid? specialization = null;
        if (row.Value("category") is { } categoryName)
        {
            category = (await lookups.ServiceCategoriesAsync([categoryName], cancellationToken)).TryGetValue(categoryName, out var id) ? id : null;
        }

        if (row.Value("specialization") is { } specializationName)
        {
            specialization = (await lookups.EmployeeSpecializationsAsync([specializationName], cancellationToken)).TryGetValue(specializationName, out var id)
                ? id
                : null;
        }

        return await catalog.CreateServiceAsync(
            new CreateServiceRequest(
                row.Value("name") ?? string.Empty, row.Value("description"), ImportValues.Amount(row, "price", errors) ?? 0,
                ImportValues.WholeNumber(row, "durationDays", errors) ?? 0, category, specialization),
            cancellationToken);
    }

    internal static string[] Values(IReadOnlyList<ImportRow> rows, string key) => [.. rows.Select(row => row.Value(key)).OfType<string>()];
}

/// <summary>
/// Cases from a workbook (F19, legacy "Subscription" import, Q47): the client by fiscal code, the service by name
/// (active), optional start and due dates. Opened by <see cref="CaseManager"/> as the System (no F10 staff rule): price
/// of the service, specialization of the service, the client's employee or the default one (Q31).
/// </summary>
internal sealed class CaseImportTarget(CaseManager cases, IImportLookups lookups) : IImportTarget
{
    public string Entity => "Case";

    public IReadOnlyList<ImportField> Fields { get; } =
    [
        new("clientFiscalCode", "app.imports.field.clientFiscalCode", Required: true),
        new("service", "app.cases.service", Required: true),
        new("startedOn", "StartDate", Required: false),
        new("dueOn", "app.cases.dueOn", Required: false),
    ];

    public async Task<IReadOnlyList<ImportRowErrors>> ValidateAsync(IReadOnlyList<ImportRow> rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var errors = rows.Select(_ => new ImportRowErrors()).ToArray();
        var clients = await lookups.ClientsAsync(ServiceImportTarget.Values(rows, "clientFiscalCode"), cancellationToken);
        var services = await lookups.ServicesAsync(ServiceImportTarget.Values(rows, "service"), activeOnly: true, cancellationToken);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var rowErrors = errors[index];
            ImportValues.CheckRequired(row, Fields, rowErrors);
            var startedOn = ImportValues.Date(row, "startedOn", rowErrors);
            var dueOn = ImportValues.Date(row, "dueOn", rowErrors);
            if (startedOn is not null && dueOn is not null && dueOn < startedOn)
            {
                rowErrors.Add("dueOn", "validation.cases.dueOn");
            }

            if (row.Value("clientFiscalCode") is { } client && !clients.ContainsKey(client))
            {
                rowErrors.Add("clientFiscalCode", ImportValues.NotFound);
            }

            if (row.Value("service") is { } service && !services.ContainsKey(service))
            {
                rowErrors.Add("service", ImportValues.NotFound);
            }
        }

        return errors;
    }

    public async Task<Result<Guid>> ImportAsync(ImportRow row, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(row);

        var errors = new ImportRowErrors();
        var fiscalCode = row.Value("clientFiscalCode") ?? string.Empty;
        var serviceName = row.Value("service") ?? string.Empty;
        var client = (await lookups.ClientsAsync([fiscalCode], cancellationToken)).GetValueOrDefault(fiscalCode);
        var service = (await lookups.ServicesAsync([serviceName], activeOnly: true, cancellationToken)).GetValueOrDefault(serviceName);
        return await cases.ImportAsync(
            new OpenCaseRequest(client, service, ImportValues.Date(row, "startedOn", errors), ImportValues.Date(row, "dueOn", errors), null, null, null),
            cancellationToken);
    }
}
