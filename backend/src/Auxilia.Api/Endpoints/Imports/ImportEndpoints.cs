using Auxilia.Api.Authorization;
using Auxilia.Api.Infrastructure;
using Auxilia.Api.Tenancy;
using Auxilia.Application.Imports;
using Auxilia.Contracts.Common;
using Auxilia.Contracts.Imports;
using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Results;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Auxilia.Api.Endpoints.Imports;

/// <summary>The fields of an import upload (multipart/form-data, F19).</summary>
internal sealed class StartImportForm
{
    public string? Name { get; set; }

    public Guid ImportTypeId { get; set; }

    public IFormFile? File { get; set; }
}

/// <summary>
/// Imports (F19, D-18): import types with their template, then upload → validation by the Worker → preview of the rows
/// → confirm (the Worker imports) or cancel; delete when finished. Technical: the System with a platform token scoped
/// to the tenant (D-21).
/// </summary>
internal sealed class ImportEndpoints : IApiEndpoints
{
    /// <summary>The file limit plus room for the other fields.</summary>
    private const long MaxRequestBytes = ImportLimits.MaxFileBytes + (64 * 1024);

    public void Map(RouteGroupBuilder api)
    {
        var imports = api.MapGroup("/imports").WithTags("Imports").RequirePlatformTenant();

        imports.MapGet("/entities", ListEntities)
            .WithName("ListImportEntities")
            .WithSummary("The entities that can be imported with their columns (required ones flagged)")
            .Produces<IReadOnlyList<ImportEntityResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        imports.MapGet("/types", async (IImportQueryService query, CancellationToken cancellationToken) => TypedResults.Ok(await query.TypesAsync(cancellationToken)))
            .WithName("ListImportTypes")
            .WithSummary("The import types of the tenant, by name")
            .Produces<IReadOnlyList<ImportTypeResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        imports.MapPost("/types", CreateTypeAsync)
            .WithName("CreateImportType")
            .WithSummary("Adds an import type for an entity (the name defaults to the entity)")
            .Produces<CreateImportTypeResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        imports.MapDelete("/types/{id:guid}", DeleteTypeAsync)
            .WithName("DeleteImportType")
            .WithSummary("Deletes an import type without imports")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        imports.MapGet("/types/{id:guid}/template", TemplateAsync)
            .WithName("GetImportTemplate")
            .WithSummary("The Excel template of an import type: one header row of column keys, required columns red")
            .Produces(StatusCodes.Status200OK, contentType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        imports.MapGet(string.Empty, ListAsync)
            .WithName("ListImports")
            .WithSummary("The imports of the tenant, newest first, with status and progress")
            .Produces<PagedResponse<ImportJobResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // A bearer token, not a cookie, authorises the upload: there is no form to forge (the BFF checks its own header).
        imports.MapPost(string.Empty, StartAsync)
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .WithFormOptions(multipartBodyLengthLimit: MaxRequestBytes)
            .Accepts<StartImportForm>("multipart/form-data")
            .WithName("StartImport")
            .WithSummary("Uploads an Excel file (.xlsx, at most 10 MB) for an import type: the Worker validates its rows")
            .Produces<StartImportResponse>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        imports.MapGet("/{id:guid}", GetAsync)
            .WithName("GetImport")
            .WithSummary("An import with its status, counters and progress")
            .Produces<ImportJobResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        imports.MapGet("/{id:guid}/rows", RowsAsync)
            .WithName("ListImportRows")
            .WithSummary("The rows of an import in order, optionally of one status, with their errors (preview)")
            .Produces<PagedResponse<ImportRowResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        imports.MapPost("/{id:guid}/confirm", ConfirmAsync)
            .WithName("ConfirmImport")
            .WithSummary("Imports the valid rows of an import waiting for confirmation (the Worker does it)")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        imports.MapPost("/{id:guid}/cancel", CancelAsync)
            .WithName("CancelImport")
            .WithSummary("Cancels an import waiting for confirmation: nothing is imported, its rows are discarded")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        imports.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteImport")
            .WithSummary("Deletes a finished import (completed, failed or cancelled)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static Ok<IReadOnlyList<ImportEntityResponse>> ListEntities(IImportQueryService query) => TypedResults.Ok(query.Entities());

    private static async Task<IResult> CreateTypeAsync(CreateImportTypeRequest request, IImportTypeManager manager, CancellationToken cancellationToken) =>
        (await manager.CreateAsync(request, cancellationToken))
            .ToHttpResult(id => TypedResults.Created($"/api/v1/imports/types/{id}", new CreateImportTypeResponse(id)));

    private static async Task<IResult> DeleteTypeAsync(Guid id, IImportTypeManager manager, CancellationToken cancellationToken) =>
        (await manager.DeleteAsync(id, cancellationToken)).ToHttpResult(TypedResults.NoContent);

    private static async Task<IResult> TemplateAsync(Guid id, IImportQueryService query, CancellationToken cancellationToken)
    {
        var template = await query.TemplateAsync(id, cancellationToken);
        return template.IsFailure
            ? template.Error!.ToProblem()
            : TypedResults.File(template.Value.Content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", template.Value.FileName);
    }

    private static async Task<IResult> ListAsync(IImportQueryService query, int? page, int? pageSize, CancellationToken cancellationToken) =>
        TypedResults.Ok(await query.ListAsync(page ?? 1, pageSize ?? 25, cancellationToken));

    private static async Task<IResult> StartAsync([FromForm] StartImportForm form, IImportManager manager, CancellationToken cancellationToken)
    {
        if (form.File is not { } file)
        {
            return Result.Failure(Errors.Imports.ImportInvalid("file", "validation.imports.file")).ToHttpResult(TypedResults.NoContent);
        }

        if (file.Length > ImportLimits.MaxFileBytes)
        {
            return Result.Failure(Errors.Imports.ImportInvalid("file", "validation.imports.fileSize")).ToHttpResult(TypedResults.NoContent);
        }

        byte[] content;
        await using (var stream = file.OpenReadStream())
        using (var buffer = new MemoryStream((int)file.Length))
        {
            await stream.CopyToAsync(buffer, cancellationToken);
            content = buffer.ToArray();
        }

        return (await manager.StartAsync(new StartImportRequest(form.Name, form.ImportTypeId, file.FileName, content), cancellationToken))
            .ToHttpResult(id => TypedResults.Accepted($"/api/v1/imports/{id}", new StartImportResponse(id)));
    }

    private static async Task<IResult> GetAsync(Guid id, IImportQueryService query, CancellationToken cancellationToken) =>
        (await query.GetAsync(id, cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> RowsAsync(Guid id, IImportQueryService query, string? status, int? page, int? pageSize, CancellationToken cancellationToken) =>
        (await query.RowsAsync(id, status, page ?? 1, pageSize ?? 25, cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> ConfirmAsync(Guid id, IImportManager manager, CancellationToken cancellationToken) =>
        (await manager.ConfirmAsync(id, cancellationToken)).ToHttpResult(() => TypedResults.Accepted($"/api/v1/imports/{id}"));

    private static async Task<IResult> CancelAsync(Guid id, IImportManager manager, CancellationToken cancellationToken) =>
        (await manager.CancelAsync(id, cancellationToken)).ToHttpResult(TypedResults.NoContent);

    private static async Task<IResult> DeleteAsync(Guid id, IImportManager manager, CancellationToken cancellationToken) =>
        (await manager.DeleteAsync(id, cancellationToken)).ToHttpResult(TypedResults.NoContent);
}
