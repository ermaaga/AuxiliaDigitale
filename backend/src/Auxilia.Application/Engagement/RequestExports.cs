using Auxilia.Application.Abstractions.Exports;
using Auxilia.Contracts.Engagement;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Engagement;

/// <summary>The request inbox (F15, F26), as <c>GET /requests</c> with the same <c>box</c>.</summary>
internal sealed class RequestExportSource(IRequestQueryService requests) : IExportSource
{
    public string Key => "requests";

    public string TitleKey => "nav.requests";

    public string Permission => EngagementPermissions.ViewRequests;

    public IReadOnlyList<ExportColumn> Columns { get; } =
    [
        new("sentAt", "CreatedAt"),
        new("subject", "Subject"),
        new("sender", "Name"),
        new("recipient", "app.requests.recipient"),
        new("type", "Type", Translated: true),
        new("status", "Status", Translated: true),
    ];

    public async Task<Result<ExportPage>> PageAsync(ExportParameters parameters, int page, int pageSize, ExportFormatting formatting, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(formatting);
        var result = await requests.ListAsync(
            new RequestListQuery(parameters.Text("box"), parameters.Text("filter[status]"), parameters.Text("filter[type]"), parameters.Text("sort"), page, pageSize),
            cancellationToken);
        return result.IsFailure
            ? Result.Failure<ExportPage>(result.Error!)
            : new ExportPage(
                result.Value.Items.Select(item => new ExportRow(item.Id, new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["sentAt"] = formatting.DateTime(item.SentAt),
                    ["subject"] = item.Subject,
                    ["sender"] = item.Sender.FullName,
                    ["recipient"] = item.Recipient?.FullName ?? string.Empty,
                    ["type"] = $"app.requests.type.{item.Type}",
                    ["status"] = $"app.requests.status.{item.Status}",
                })).ToArray(),
                (int)result.Value.TotalCount);
    }
}
