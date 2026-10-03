using System.Text.Json;

using Auxilia.Api.Authorization;
using Auxilia.Api.Infrastructure;
using Auxilia.Application.Documents;
using Auxilia.Contracts.Common;
using Auxilia.Contracts.Documents;
using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Results;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Auxilia.Api.Endpoints.Documents;

/// <summary>
/// The fields of an upload (multipart/form-data, F14): <c>files</c> plus the metadata every file gets. <c>customFields</c>
/// is a JSON object as text (F20).
/// </summary>
internal sealed class UploadDocumentsForm
{
    public Guid ClientId { get; set; }

    public Guid? CaseId { get; set; }

    public Guid? FolderId { get; set; }

    public int? ReferenceYear { get; set; }

    public Guid? AreaId { get; set; }

    public string? Description { get; set; }

    /// <summary>A custom name, only with one file.</summary>
    public string? FileName { get; set; }

    public string? CustomFields { get; set; }

    public IFormFileCollection? Files { get; set; }
}

/// <summary>
/// Documents of the clients (F14, F33) with the F10 rules: lists, detail, upload, metadata, folder, delete, download
/// and case ZIP; areas. A document the caller cannot see is 404. Module <c>documents</c>: 404 when not visible.
/// </summary>
internal sealed class DocumentEndpoints : IModuleEndpoints
{
    /// <summary>The whole request (every file); each file is limited by <c>documents.maxUploadMb</c> while it is read.</summary>
    public const long MaxRequestBytes = 1L << 30;

    /// <summary>Types shown in the browser (preview); every other type is always downloaded.</summary>
    private static readonly string[] InlineTypes = ["application/pdf", "image/jpeg", "image/png", "image/gif", "image/webp"];

    public string ModuleCode => DocumentsModule.ModuleCode;

    public void Map(RouteGroupBuilder module)
    {
        var documents = module.MapGroup("/documents").WithTags("Documents");

        documents.MapGet("/", ListAsync)
            .RequirePermission(DocumentsPermissions.ViewDocuments)
            .WithName("ListDocuments")
            .WithSummary("Documents the caller may see (F10), filtered by client, case, folder, name, year, area, description and uploader")
            .Produces<PagedResponse<DocumentListItemResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        documents.MapGet("/zip", ZipAsync)
            .RequirePermission(DocumentsPermissions.ViewDocuments)
            .WithName("DownloadCaseDocumentsZip")
            .WithSummary("A ZIP of the documents of a case, or of one folder subtree (folder paths kept)")
            .Produces(StatusCodes.Status200OK, contentType: "application/zip")
            .ProducesProblem(StatusCodes.Status404NotFound);

        documents.MapGet("/{id:guid}", GetAsync)
            .RequirePermission(DocumentsPermissions.ViewDocuments)
            .WithName("GetDocument")
            .WithSummary("A document with its folder path and what the caller may do")
            .Produces<DocumentResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        documents.MapGet("/{id:guid}/content", ContentAsync)
            .RequirePermission(DocumentsPermissions.ViewDocuments)
            .WithName("DownloadDocument")
            .WithSummary("The file; inline=true shows PDF and images in the browser, any other type is downloaded")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .ProducesProblem(StatusCodes.Status404NotFound);

        documents.MapPost("/", UploadAsync)
            .RequirePermission(DocumentsPermissions.ManageDocuments)
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .WithFormOptions(multipartBodyLengthLimit: MaxRequestBytes)
            .Accepts<UploadDocumentsForm>("multipart/form-data")
            .WithName("UploadDocuments")
            .WithSummary("Uploads files for a client, optionally a case and a folder of its service, with the same metadata")
            .Produces<UploadDocumentsResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        documents.MapPut("/{id:guid}", UpdateAsync)
            .RequirePermission(DocumentsPermissions.ManageDocuments)
            .WithName("UpdateDocument")
            .WithSummary("Changes name (extension kept), reference year, area, description and custom fields")
            .Produces<DocumentResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        documents.MapPut("/{id:guid}/folder", MoveAsync)
            .RequirePermission(DocumentsPermissions.ManageDocuments)
            .WithName("MoveDocument")
            .WithSummary("Moves a case document to another folder of the case's service, or to none")
            .Produces<DocumentResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        documents.MapDelete("/{id:guid}", DeleteAsync)
            .RequirePermission(DocumentsPermissions.ManageDocuments)
            .WithName("DeleteDocument")
            .WithSummary("Deletes a document and its file")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        var areas = module.MapGroup("/document-areas").WithTags("Documents");

        areas.MapGet("/", AreasAsync)
            .RequirePermission(DocumentsPermissions.ViewDocuments)
            .WithName("ListDocumentAreas")
            .WithSummary("Every document area by name, inactive ones included, with the number of documents")
            .Produces<IReadOnlyList<DocumentAreaResponse>>();

        areas.MapPost("/", CreateAreaAsync)
            .RequirePermission(DocumentsPermissions.ManageAreas)
            .WithName("CreateDocumentArea")
            .WithSummary("Creates a document area (name unique among the active ones)")
            .Produces<IReadOnlyList<DocumentAreaResponse>>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        areas.MapPut("/{id:guid}", UpdateAreaAsync)
            .RequirePermission(DocumentsPermissions.ManageAreas)
            .WithName("UpdateDocumentArea")
            .WithSummary("Renames or (de)activates a document area")
            .Produces<IReadOnlyList<DocumentAreaResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> ListAsync(
        IDocumentQueryService documents,
        [FromQuery(Name = "filter[clientId]")] Guid? clientId,
        [FromQuery(Name = "filter[caseId]")] Guid? caseId,
        [FromQuery(Name = "filter[folderId]")] Guid? folderId,
        [FromQuery(Name = "filter[clientName]")] string? clientName,
        [FromQuery(Name = "filter[fileName]")] string? fileName,
        [FromQuery(Name = "filter[description]")] string? description,
        [FromQuery(Name = "filter[uploadedBy]")] string? uploadedBy,
        [FromQuery(Name = "filter[referenceYear]")] int? referenceYear,
        [FromQuery(Name = "filter[areaId]")] Guid? areaId,
        string? sort,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken) =>
        (await documents.ListAsync(
            new DocumentListQuery(clientId, caseId, folderId, clientName, fileName, description, uploadedBy, referenceYear, areaId, sort, page ?? 1, pageSize ?? 25),
            cancellationToken))
            .ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> GetAsync(Guid id, IDocumentQueryService documents, CancellationToken cancellationToken) =>
        (await documents.GetAsync(id, cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> ContentAsync(Guid id, bool? inline, IDocumentQueryService documents, HttpContext context, CancellationToken cancellationToken) =>
        (await documents.OpenAsync(id, cancellationToken)).ToHttpResult(content => File(context, content, inline == true && InlineTypes.Contains(content.ContentType)));

    private static async Task<IResult> ZipAsync(Guid caseId, Guid? folderId, IDocumentQueryService documents, HttpContext context, CancellationToken cancellationToken) =>
        (await documents.ZipAsync(caseId, folderId, cancellationToken)).ToHttpResult(content => File(context, content, inline: false));

    private static async Task<IResult> UploadAsync(
        [FromForm] UploadDocumentsForm form, IDocumentManager manager, CancellationToken cancellationToken)
    {
        JsonElement? customFields = null;
        if (!string.IsNullOrWhiteSpace(form.CustomFields))
        {
            try
            {
                using var parsed = JsonDocument.Parse(form.CustomFields);
                customFields = parsed.RootElement.Clone();
            }
            catch (JsonException)
            {
                return Errors.Documents.DocumentInvalid("customFields", "validation.documents.customFields").ToProblem();
            }
        }

        var streams = (form.Files ?? new FormFileCollection()).Select(file => (file.FileName, Content: file.OpenReadStream())).ToArray();
        try
        {
            var request = new UploadDocuments(
                form.ClientId, form.CaseId, form.FolderId, form.ReferenceYear, form.AreaId, form.Description, form.FileName, customFields,
                streams.Select(file => new UploadedFile(file.Content, file.FileName)).ToArray());
            return (await manager.UploadAsync(request, cancellationToken))
                .ToHttpResult(uploaded => TypedResults.Created("/api/v1/documents", uploaded));
        }
        finally
        {
            foreach (var (_, content) in streams)
            {
                await content.DisposeAsync();
            }
        }
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, UpdateDocumentRequest request, IDocumentManager manager, IDocumentQueryService documents, CancellationToken cancellationToken) =>
        await DetailAfterAsync(await manager.UpdateAsync(id, request, cancellationToken), id, documents, cancellationToken);

    private static async Task<IResult> MoveAsync(
        Guid id, MoveDocumentRequest request, IDocumentManager manager, IDocumentQueryService documents, CancellationToken cancellationToken) =>
        await DetailAfterAsync(await manager.MoveAsync(id, request.FolderId, cancellationToken), id, documents, cancellationToken);

    private static async Task<IResult> DeleteAsync(Guid id, IDocumentManager manager, CancellationToken cancellationToken) =>
        (await manager.DeleteAsync(id, cancellationToken)).ToHttpResult(TypedResults.NoContent);

    private static async Task<IResult> AreasAsync(IDocumentQueryService documents, CancellationToken cancellationToken) =>
        TypedResults.Ok(await documents.AreasAsync(cancellationToken));

    private static async Task<IResult> CreateAreaAsync(
        CreateDocumentAreaRequest request, IDocumentManager manager, IDocumentQueryService documents, CancellationToken cancellationToken)
    {
        var created = await manager.CreateAreaAsync(request, cancellationToken);
        return created.IsFailure ? created.Error!.ToProblem() : TypedResults.Created("/api/v1/document-areas", await documents.AreasAsync(cancellationToken));
    }

    private static async Task<IResult> UpdateAreaAsync(
        Guid id, UpdateDocumentAreaRequest request, IDocumentManager manager, IDocumentQueryService documents, CancellationToken cancellationToken)
    {
        var updated = await manager.UpdateAreaAsync(id, request, cancellationToken);
        return updated.IsFailure ? updated.Error!.ToProblem() : TypedResults.Ok(await documents.AreasAsync(cancellationToken));
    }

    /// <summary>Never cached; attachment unless an allowlisted type is previewed (skill auxilia-security, uploads).</summary>
    private static Microsoft.AspNetCore.Http.HttpResults.FileStreamHttpResult File(HttpContext context, DocumentContent content, bool inline)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        var disposition = new ContentDispositionHeaderValue(inline ? "inline" : "attachment");
        disposition.SetHttpFileName(content.FileName);
        context.Response.Headers.ContentDisposition = disposition.ToString();
        return TypedResults.Stream(content.Content, content.ContentType);
    }

    /// <summary>A change answers with the document as it is now.</summary>
    private static async Task<IResult> DetailAfterAsync(Result change, Guid id, IDocumentQueryService documents, CancellationToken cancellationToken) =>
        change.IsFailure ? change.Error!.ToProblem() : (await documents.GetAsync(id, cancellationToken)).ToHttpResult(TypedResults.Ok);
}
