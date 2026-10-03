using Auxilia.Api.Authorization;
using Auxilia.Api.Infrastructure;
using Auxilia.Application.Cases;
using Auxilia.Contracts.Cases;
using Auxilia.Contracts.Common;
using Auxilia.SharedKernel.Results;

using Microsoft.AspNetCore.Mvc;

namespace Auxilia.Api.Endpoints.Cases;

/// <summary>
/// The service catalog (F08, Q26–Q28): services and their categories. Staff read it (to open cases), Administrators
/// manage it. Module <c>cases</c>: 404 when not visible to the caller's role.
/// </summary>
internal sealed class ServiceEndpoints : IModuleEndpoints
{
    public string ModuleCode => CasesModule.ModuleCode;

    public void Map(RouteGroupBuilder module)
    {
        var categories = module.MapGroup("/service-categories").WithTags("Services");

        categories.MapGet("/", CategoriesAsync)
            .RequirePermission(CasesPermissions.ViewServices)
            .WithName("ListServiceCategories")
            .WithSummary("Every service category by name, inactive ones included, with the number of services using it")
            .Produces<IReadOnlyList<ServiceCategoryResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        categories.MapPost("/", CreateCategoryAsync)
            .RequirePermission(CasesPermissions.ManageServices)
            .WithName("CreateServiceCategory")
            .WithSummary("Creates a service category (name unique among the active ones)")
            .Produces<ServiceCategoryResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        categories.MapPut("/{id:guid}", UpdateCategoryAsync)
            .RequirePermission(CasesPermissions.ManageServices)
            .WithName("UpdateServiceCategory")
            .WithSummary("Changes name, description and active state of a category")
            .Produces<ServiceCategoryResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        categories.MapDelete("/{id:guid}", DeleteCategoryAsync)
            .RequirePermission(CasesPermissions.ManageServices)
            .WithName("DeleteServiceCategory")
            .WithSummary("Deletes a category no service uses; otherwise 409 (deactivate it instead)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        var services = module.MapGroup("/services").WithTags("Services");

        services.MapGet("/", ListAsync)
            .RequirePermission(CasesPermissions.ViewServices)
            .WithName("ListServices")
            .WithSummary("Services filtered by name, category, specialization and active state, sorted by name, price or duration")
            .Produces<PagedResponse<ServiceResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        services.MapGet("/{id:guid}", GetAsync)
            .RequirePermission(CasesPermissions.ViewServices)
            .WithName("GetService")
            .WithSummary("A service with its category and specialization")
            .Produces<ServiceResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        services.MapPost("/", CreateAsync)
            .RequirePermission(CasesPermissions.ManageServices)
            .WithName("CreateService")
            .WithSummary("Creates an active service (price in euro, duration in days, optional category and Employee specialization)")
            .Produces<ServiceResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        services.MapPut("/{id:guid}", UpdateAsync)
            .RequirePermission(CasesPermissions.ManageServices)
            .WithName("UpdateService")
            .WithSummary("Changes a service, including its active state, category and specialization")
            .Produces<ServiceResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        services.MapDelete("/{id:guid}", DeleteAsync)
            .RequirePermission(CasesPermissions.ManageServices)
            .WithName("DeleteService")
            .WithSummary("Deletes a service (soft delete); a service in use is deactivated instead")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> CategoriesAsync(IServiceCatalogQueryService catalog, CancellationToken cancellationToken) =>
        TypedResults.Ok(await catalog.CategoriesAsync(cancellationToken));

    private static async Task<IResult> CreateCategoryAsync(
        CreateServiceCategoryRequest request, IServiceCatalogManager manager, IServiceCatalogQueryService catalog, CancellationToken cancellationToken)
    {
        var created = await manager.CreateCategoryAsync(request, cancellationToken);
        return created.IsFailure
            ? created.Error!.ToProblem()
            : TypedResults.Created($"/api/v1/service-categories/{created.Value}", await CategoryAsync(catalog, created.Value, cancellationToken));
    }

    private static async Task<IResult> UpdateCategoryAsync(
        Guid id, UpdateServiceCategoryRequest request, IServiceCatalogManager manager, IServiceCatalogQueryService catalog, CancellationToken cancellationToken)
    {
        var updated = await manager.UpdateCategoryAsync(id, request, cancellationToken);
        return updated.IsFailure ? updated.Error!.ToProblem() : TypedResults.Ok(await CategoryAsync(catalog, id, cancellationToken));
    }

    private static async Task<IResult> DeleteCategoryAsync(Guid id, IServiceCatalogManager manager, CancellationToken cancellationToken) =>
        (await manager.DeleteCategoryAsync(id, cancellationToken)).ToHttpResult(TypedResults.NoContent);

    private static async Task<IResult> ListAsync(
        IServiceCatalogQueryService catalog,
        [FromQuery(Name = "filter[name]")] string? name,
        [FromQuery(Name = "filter[categoryId]")] Guid? categoryId,
        [FromQuery(Name = "filter[specializationId]")] Guid? specializationId,
        [FromQuery(Name = "filter[active]")] bool? active,
        string? sort,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken) =>
        (await catalog.ListAsync(new ServiceListQuery(name, categoryId, specializationId, active, sort, page ?? 1, pageSize ?? 25), cancellationToken))
            .ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> GetAsync(Guid id, IServiceCatalogQueryService catalog, CancellationToken cancellationToken) =>
        (await catalog.GetAsync(id, cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> CreateAsync(
        CreateServiceRequest request, IServiceCatalogManager manager, IServiceCatalogQueryService catalog, CancellationToken cancellationToken)
    {
        var created = await manager.CreateServiceAsync(request, cancellationToken);
        return created.IsFailure
            ? created.Error!.ToProblem()
            : (await catalog.GetAsync(created.Value, cancellationToken)).ToHttpResult(service => TypedResults.Created($"/api/v1/services/{service.Id}", service));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, UpdateServiceRequest request, IServiceCatalogManager manager, IServiceCatalogQueryService catalog, CancellationToken cancellationToken) =>
        await DetailAfterAsync(await manager.UpdateServiceAsync(id, request, cancellationToken), id, catalog, cancellationToken);

    private static async Task<IResult> DeleteAsync(Guid id, IServiceCatalogManager manager, CancellationToken cancellationToken) =>
        (await manager.DeleteServiceAsync(id, cancellationToken)).ToHttpResult(TypedResults.NoContent);

    private static async Task<ServiceCategoryResponse?> CategoryAsync(IServiceCatalogQueryService catalog, Guid id, CancellationToken cancellationToken) =>
        (await catalog.CategoriesAsync(cancellationToken)).SingleOrDefault(category => category.Id == id);

    /// <summary>A change answers with the service as it is now.</summary>
    private static async Task<IResult> DetailAfterAsync(Result change, Guid id, IServiceCatalogQueryService catalog, CancellationToken cancellationToken) =>
        change.IsFailure ? change.Error!.ToProblem() : (await catalog.GetAsync(id, cancellationToken)).ToHttpResult(TypedResults.Ok);
}
