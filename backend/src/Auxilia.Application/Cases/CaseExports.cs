using Auxilia.Application.Abstractions.Exports;
using Auxilia.Contracts.Cases;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Cases;

/// <summary>The case lists (F09, F26), as <c>GET /cases</c>: F10 applies in the query.</summary>
internal sealed class CaseExportSource(ICaseQueryService cases) : IExportSource
{
    public string Key => "cases";

    public string TitleKey => "nav.cases";

    public string Permission => CasesPermissions.ViewCases;

    public IReadOnlyList<ExportColumn> Columns { get; } =
    [
        new("number", "Number"),
        new("client", "Client"),
        new("service", "app.cases.service"),
        new("startedOn", "StartDate"),
        new("expiresOn", "EndDate"),
        new("amountPaid", "AmountPaid"),
        new("status", "Status", Translated: true),
        new("specialization", "Specialization"),
    ];

    public async Task<Result<ExportPage>> PageAsync(ExportParameters parameters, int page, int pageSize, ExportFormatting formatting, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(formatting);
        var query = new CaseListQuery(
            parameters.Text("filter[clientName]"), parameters.Text("filter[serviceName]"), parameters.Id("filter[clientId]"), parameters.Id("filter[serviceId]"),
            parameters.Text("filter[status]"), parameters.Bool("showAll"), parameters.Bool("showCompleted"), parameters.Text("sort"), page, pageSize);
        if (parameters.Errors is { } invalid)
        {
            return invalid;
        }

        var result = await cases.ListAsync(query, cancellationToken);
        return result.IsFailure
            ? Result.Failure<ExportPage>(result.Error!)
            : new ExportPage(
                result.Value.Items.Select(item => new ExportRow(item.Id, new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["number"] = item.Number,
                    ["client"] = item.Client.FullName,
                    ["service"] = item.Service.Name,
                    ["startedOn"] = formatting.Date(item.StartedOn),
                    ["expiresOn"] = formatting.Date(item.ExpiresOn),
                    ["amountPaid"] = formatting.Money(item.AmountPaid, item.Currency),
                    ["status"] = item.Status == "Completed" && item.IsRejected ? "Rejected" : item.Status,
                    ["specialization"] = item.Specialization?.Name ?? string.Empty,
                })).ToArray(),
                (int)result.Value.TotalCount);
    }
}

/// <summary>The service catalog (F08, F26), as <c>GET /services</c>.</summary>
internal sealed class ServiceExportSource(IServiceCatalogQueryService catalog) : IExportSource
{
    public string Key => "services";

    public string TitleKey => "nav.services";

    public string Permission => CasesPermissions.ViewServices;

    public IReadOnlyList<ExportColumn> Columns { get; } =
    [
        new("name", "Name"),
        new("description", "Description"),
        new("category", "app.services.category"),
        new("specialization", "Specialization"),
        new("price", "Price"),
        new("durationDays", "DurationDays"),
        new("status", "Status", Translated: true),
    ];

    public async Task<Result<ExportPage>> PageAsync(ExportParameters parameters, int page, int pageSize, ExportFormatting formatting, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(formatting);
        var query = new ServiceListQuery(
            parameters.Text("filter[name]"), parameters.Id("filter[categoryId]"), parameters.Id("filter[specializationId]"), parameters.Bool("filter[active]"),
            parameters.Text("sort"), page, pageSize);
        if (parameters.Errors is { } invalid)
        {
            return invalid;
        }

        var result = await catalog.ListAsync(query, cancellationToken);
        return result.IsFailure
            ? Result.Failure<ExportPage>(result.Error!)
            : new ExportPage(
                result.Value.Items.Select(item => new ExportRow(item.Id, new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["name"] = item.Name,
                    ["description"] = item.Description ?? string.Empty,
                    ["category"] = item.Category?.Name ?? string.Empty,
                    ["specialization"] = item.Specialization?.Name ?? string.Empty,
                    ["price"] = formatting.Money(item.Price, item.Currency),
                    ["durationDays"] = formatting.Number(item.DurationDays),
                    ["status"] = item.IsActive ? "Active" : "Inactive",
                })).ToArray(),
                (int)result.Value.TotalCount);
    }
}
