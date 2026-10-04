using Auxilia.Application.Abstractions.Exports;
using Auxilia.Contracts.Documents;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Documents;

/// <summary>The document list (F14, F26), as <c>GET /documents</c>: F10 applies in the query.</summary>
internal sealed class DocumentExportSource(IDocumentQueryService documents) : IExportSource
{
    public string Key => "documents";

    public string TitleKey => "app.documents.title";

    public string Permission => DocumentsPermissions.ViewDocuments;

    public IReadOnlyList<ExportColumn> Columns { get; } =
    [
        new("fileName", "FileName"),
        new("client", "Client"),
        new("case", "app.documents.case"),
        new("referenceYear", "ReferenceYear"),
        new("area", "Area"),
        new("description", "Description"),
        new("uploadedBy", "UploadedBy"),
        new("uploadedAt", "UploadDate"),
        new("size", "app.documents.size"),
    ];

    public async Task<Result<ExportPage>> PageAsync(ExportParameters parameters, int page, int pageSize, ExportFormatting formatting, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(formatting);
        var query = new DocumentListQuery(
            parameters.Id("filter[clientId]"), parameters.Id("filter[caseId]"), parameters.Id("filter[folderId]"), parameters.Text("filter[clientName]"),
            parameters.Text("filter[fileName]"), parameters.Text("filter[description]"), parameters.Text("filter[uploadedBy]"), parameters.Number("filter[referenceYear]"),
            parameters.Id("filter[areaId]"), parameters.Text("sort"), page, pageSize);
        if (parameters.Errors is { } invalid)
        {
            return invalid;
        }

        var result = await documents.ListAsync(query, cancellationToken);
        return result.IsFailure
            ? Result.Failure<ExportPage>(result.Error!)
            : new ExportPage(
                result.Value.Items.Select(item => new ExportRow(item.Id, new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["fileName"] = item.FileName,
                    ["client"] = item.Client.FullName,
                    ["case"] = item.Case is { } linked ? $"{linked.Number} {linked.ServiceName}" : string.Empty,
                    ["referenceYear"] = item.ReferenceYear.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["area"] = item.Area?.Name ?? string.Empty,
                    ["description"] = item.Description ?? string.Empty,
                    ["uploadedBy"] = item.UploadedBy?.FullName ?? string.Empty,
                    ["uploadedAt"] = formatting.DateTime(item.UploadedAt),
                    ["size"] = $"{formatting.Number((item.Size + 1023) / 1024)} KB",
                })).ToArray(),
                (int)result.Value.TotalCount);
    }
}
