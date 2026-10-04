using Auxilia.Application.Abstractions.Exports;
using Auxilia.Contracts.Directory;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Directory;

/// <summary>The client list (F05, F07 overview, F26): the same query as <c>GET /clients</c>, filters and <c>view</c> included.</summary>
internal sealed class ClientExportSource(IClientQueryService clients) : IExportSource
{
    public string Key => "clients";

    public string TitleKey => "nav.clients";

    public string Permission => DirectoryPermissions.ViewClients;

    public IReadOnlyList<ExportColumn> Columns { get; } =
    [
        new("lastName", "Surname"),
        new("firstName", "Name"),
        new("email", "Email"),
        new("userName", "Username"),
        new("phone", "Phone"),
        new("fiscalCode", "app.clients.fiscalCode"),
        new("employee", "app.clients.employee"),
        new("status", "Status", Translated: true),
        new("canSignIn", "app.clients.canSignIn", Translated: true),
    ];

    public async Task<Result<ExportPage>> PageAsync(ExportParameters parameters, int page, int pageSize, ExportFormatting formatting, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(formatting);
        var query = new ClientListQuery(
            parameters.Text("view"), parameters.Text("filter[fullName]"), parameters.Text("filter[lastName]"), parameters.Text("filter[email]"),
            parameters.Text("filter[userName]"), parameters.Text("filter[phone]"), parameters.Text("filter[status]"), parameters.Text("sort"),
            page, pageSize, parameters.Id("filter[employeeUserId]"));
        if (parameters.Errors is { } invalid)
        {
            return invalid;
        }

        var result = await clients.ListAsync(query, cancellationToken);
        return result.IsFailure
            ? Result.Failure<ExportPage>(result.Error!)
            : new ExportPage(
                result.Value.Items.Select(item => new ExportRow(item.Id, new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["lastName"] = item.LastName,
                    ["firstName"] = item.FirstName,
                    ["email"] = item.Email ?? string.Empty,
                    ["userName"] = item.UserName,
                    ["phone"] = item.Phone ?? string.Empty,
                    ["fiscalCode"] = item.FiscalCode ?? string.Empty,
                    ["employee"] = item.Employee?.FullName ?? string.Empty,
                    ["status"] = item.Status,
                    ["canSignIn"] = item.CanSignIn ? "Yes" : "No",
                })).ToArray(),
                (int)result.Value.TotalCount);
    }
}

/// <summary>The employee list (F06, F26), as <c>GET /employees</c>.</summary>
internal sealed class EmployeeExportSource(IEmployeeQueryService employees) : IExportSource
{
    public string Key => "employees";

    public string TitleKey => "nav.employees";

    public string Permission => DirectoryPermissions.ViewEmployees;

    public IReadOnlyList<ExportColumn> Columns { get; } =
    [
        new("lastName", "Surname"),
        new("firstName", "Name"),
        new("userName", "Username"),
        new("email", "Email"),
        new("phone", "Phone"),
        new("status", "Status", Translated: true),
        new("specializations", "app.employees.specializations"),
        new("assignedClients", "app.employees.assignedClients"),
    ];

    public async Task<Result<ExportPage>> PageAsync(ExportParameters parameters, int page, int pageSize, ExportFormatting formatting, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(formatting);
        var result = await employees.ListAsync(
            new EmployeeListQuery(
                parameters.Text("filter[fullName]"), parameters.Text("filter[lastName]"), parameters.Text("filter[email]"), parameters.Text("filter[userName]"),
                parameters.Text("filter[phone]"), parameters.Text("filter[status]"), parameters.Text("sort"), page, pageSize),
            cancellationToken);
        return result.IsFailure
            ? Result.Failure<ExportPage>(result.Error!)
            : new ExportPage(
                result.Value.Items.Select(item => new ExportRow(item.Id, new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["lastName"] = item.LastName,
                    ["firstName"] = item.FirstName,
                    ["userName"] = item.UserName,
                    ["email"] = item.Email ?? string.Empty,
                    ["phone"] = item.Phone ?? string.Empty,
                    ["status"] = item.CanSignIn ? "Active" : "Inactive",
                    ["specializations"] = string.Join(", ", item.Specializations.Select(specialization => specialization.Name)),
                    ["assignedClients"] = formatting.Number(item.AssignedClients),
                })).ToArray(),
                (int)result.Value.TotalCount);
    }
}

/// <summary>The registration requests (F02, F26), as <c>GET /registrations</c>.</summary>
internal sealed class RegistrationExportSource(IRegistrationQueryService registrations) : IExportSource
{
    public string Key => "registrations";

    public string TitleKey => "app.exports.registrations";

    public string Permission => DirectoryPermissions.ReviewRegistrations;

    public IReadOnlyList<ExportColumn> Columns { get; } =
    [
        new("requestedAt", "Date"),
        new("lastName", "Surname"),
        new("firstName", "Name"),
        new("email", "Email"),
        new("phone", "Phone"),
        new("fiscalCode", "app.clients.fiscalCode"),
        new("status", "Status", Translated: true),
        new("processedAt", "app.exports.processedAt"),
    ];

    public async Task<Result<ExportPage>> PageAsync(ExportParameters parameters, int page, int pageSize, ExportFormatting formatting, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(formatting);
        var query = new RegistrationListQuery(
            parameters.Text("filter[status]"), parameters.Date("filter[from]"), parameters.Date("filter[to]"), parameters.Text("search"), parameters.Text("sort"), page, pageSize);
        if (parameters.Errors is { } invalid)
        {
            return invalid;
        }

        var result = await registrations.ListAsync(query, cancellationToken);
        return result.IsFailure
            ? Result.Failure<ExportPage>(result.Error!)
            : new ExportPage(
                result.Value.Items.Select(item => new ExportRow(item.Id, new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["requestedAt"] = formatting.DateTime(item.RequestedAt),
                    ["lastName"] = item.LastName,
                    ["firstName"] = item.FirstName,
                    ["email"] = item.Email,
                    ["phone"] = item.Phone,
                    ["fiscalCode"] = item.FiscalCode,
                    ["status"] = item.Status,
                    ["processedAt"] = formatting.DateTime(item.ProcessedAt),
                })).ToArray(),
                (int)result.Value.TotalCount);
    }
}
