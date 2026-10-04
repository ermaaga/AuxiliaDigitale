using Auxilia.Application.Abstractions.Exports;
using Auxilia.Contracts.Scheduling;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Scheduling;

/// <summary>The appointment grid (F13, F26), as <c>GET /appointments</c>.</summary>
internal sealed class AppointmentExportSource(IAppointmentQueryService appointments) : IExportSource
{
    public string Key => "appointments";

    public string TitleKey => "nav.appointments";

    public string Permission => SchedulingPermissions.ViewAppointments;

    public IReadOnlyList<ExportColumn> Columns { get; } =
    [
        new("startsAt", "DateTime"),
        new("client", "Client"),
        new("employee", "app.appointments.operator"),
        new("duration", "DurationMinutes"),
        new("showInGlobalCalendar", "ShowInGlobalCalendar", Translated: true),
        new("status", "Status", Translated: true),
    ];

    public async Task<Result<ExportPage>> PageAsync(ExportParameters parameters, int page, int pageSize, ExportFormatting formatting, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(formatting);
        var query = new AppointmentListQuery(
            parameters.Id("filter[clientId]"), parameters.Id("filter[employeeUserId]"), parameters.Text("filter[status]"), parameters.Date("from"), parameters.Date("to"),
            parameters.Text("sort"), page, pageSize);
        if (parameters.Errors is { } invalid)
        {
            return invalid;
        }

        var result = await appointments.ListAsync(query, cancellationToken);
        return result.IsFailure
            ? Result.Failure<ExportPage>(result.Error!)
            : new ExportPage(
                result.Value.Items.Select(item => new ExportRow(item.Id, new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["startsAt"] = $"{formatting.Date(item.Date)} {item.Time.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)}",
                    ["client"] = item.Client.FullName,
                    ["employee"] = item.Employee.FullName,
                    ["duration"] = formatting.Number(item.DurationMinutes),
                    ["showInGlobalCalendar"] = item.ShowInGlobalCalendar ? "Yes" : "No",
                    ["status"] = item.Status,
                })).ToArray(),
                (int)result.Value.TotalCount);
    }
}
