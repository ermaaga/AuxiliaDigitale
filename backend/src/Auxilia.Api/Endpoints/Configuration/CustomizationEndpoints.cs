using Auxilia.Api.Authorization;
using Auxilia.Api.Infrastructure;
using Auxilia.Api.Tenancy;
using Auxilia.Application.Configuration;
using Auxilia.Contracts.Configuration;

using Microsoft.AspNetCore.Mvc;

namespace Auxilia.Api.Endpoints.Configuration;

/// <summary>
/// Custom fields (F20) and grid layouts (F21), D-18. <c>/custom-fields</c> and <c>/grids</c>: edited by the System with a
/// platform token scoped to the tenant (D-21). <c>/me/custom-fields/{entity}</c> and <c>/me/grids/{key}</c>: what the
/// signed-in tenant user's forms and grids need.
/// </summary>
internal sealed class CustomizationEndpoints : IApiEndpoints
{
    public void Map(RouteGroupBuilder api)
    {
        var fields = api.MapGroup("/custom-fields").WithTags("Configuration").RequirePlatformTenant();

        fields.MapGet("/entities", ListEntities)
            .WithName("ListCustomFieldEntities")
            .WithSummary("Entities whose records carry custom fields")
            .Produces<IReadOnlyList<CustomFieldEntityResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        fields.MapGet(string.Empty, ListCustomFieldsAsync)
            .WithName("ListCustomFields")
            .WithSummary("Custom field definitions, optionally of one entity, by entity then order")
            .Produces<IReadOnlyList<CustomFieldDefinitionResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        fields.MapPost(string.Empty, CreateCustomFieldAsync)
            .WithName("CreateCustomField")
            .WithSummary("Adds a custom field to an entity")
            .Produces<CreateCustomFieldResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        fields.MapPut("/{id:guid}", UpdateCustomFieldAsync)
            .WithName("UpdateCustomField")
            .WithSummary("Changes a custom field (entity, key and type stay)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        fields.MapDelete("/{id:guid}", DeleteCustomFieldAsync)
            .WithName("DeleteCustomField")
            .WithSummary("Removes a custom field (stored values are no longer shown)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        var grids = api.MapGroup("/grids").WithTags("Configuration").RequirePlatformTenant();

        grids.MapGet(string.Empty, ListGridsAsync)
            .WithName("ListGrids")
            .WithSummary("Grids of the tenant app with their columns and the layout of every role")
            .Produces<IReadOnlyList<GridResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        grids.MapPut("/{key}/layouts/{role}", SetLayoutAsync)
            .WithName("SetGridLayout")
            .WithSummary("Sets the columns a role sees, in their order")
            .Produces<GridRoleLayoutResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        grids.MapDelete("/{key}/layouts/{role}", ResetLayoutAsync)
            .WithName("ResetGridLayout")
            .WithSummary("Removes the layout of a role: the default columns apply again")
            .Produces<GridRoleLayoutResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        var mine = api.MapGroup("/me").WithTags("Me").RequireTenant().RequireAuthorization();

        mine.MapGet("/grids/{key}", GetMyLayoutAsync)
            .WithName("GetMyGridLayout")
            .WithSummary("Columns of a grid for the signed-in user (layout of the first of the user's roles that sees it)")
            .Produces<MyGridLayoutResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        mine.MapGet("/custom-fields/{entityType}", GetMyCustomFieldsAsync)
            .WithName("GetMyCustomFields")
            .WithSummary("Custom field definitions of an entity, for forms, details and grids")
            .Produces<IReadOnlyList<CustomFieldDefinitionResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static Microsoft.AspNetCore.Http.HttpResults.Ok<IReadOnlyList<CustomFieldEntityResponse>> ListEntities(ICustomFieldQueryService query) =>
        TypedResults.Ok(query.ListEntities());

    private static async Task<IResult> ListCustomFieldsAsync(
        ICustomFieldQueryService query, [FromQuery(Name = "filter[entityType]")] string? entityType, CancellationToken cancellationToken) =>
        (await query.ListAsync(entityType, cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> CreateCustomFieldAsync(CreateCustomFieldRequest request, ICustomFieldManager fields, CancellationToken cancellationToken) =>
        (await fields.CreateAsync(request, cancellationToken))
            .ToHttpResult(id => TypedResults.Created($"/api/v1/custom-fields/{id}", new CreateCustomFieldResponse(id)));

    private static async Task<IResult> UpdateCustomFieldAsync(Guid id, UpdateCustomFieldRequest request, ICustomFieldManager fields, CancellationToken cancellationToken) =>
        (await fields.UpdateAsync(id, request, cancellationToken)).ToHttpResult(TypedResults.NoContent);

    private static async Task<IResult> DeleteCustomFieldAsync(Guid id, ICustomFieldManager fields, CancellationToken cancellationToken) =>
        (await fields.DeleteAsync(id, cancellationToken)).ToHttpResult(TypedResults.NoContent);

    private static async Task<IResult> ListGridsAsync(IGridQueryService query, CancellationToken cancellationToken) =>
        TypedResults.Ok(await query.ListAsync(cancellationToken));

    private static async Task<IResult> SetLayoutAsync(string key, string role, SetGridLayoutRequest request, IGridLayoutManager layouts, CancellationToken cancellationToken) =>
        (await layouts.SetAsync(key, role, request.Columns, cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> ResetLayoutAsync(string key, string role, IGridLayoutManager layouts, CancellationToken cancellationToken) =>
        (await layouts.ResetAsync(key, role, cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> GetMyLayoutAsync(string key, IGridQueryService query, CancellationToken cancellationToken) =>
        (await query.GetMineAsync(key, cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> GetMyCustomFieldsAsync(
        string entityType, Application.Abstractions.Authorization.ICurrentUser currentUser, ICustomFieldQueryService query, CancellationToken cancellationToken)
    {
        if (currentUser.ActorType != Application.Abstractions.Authorization.ActorType.User)
        {
            return Diagnostics.Errors.Configuration.CustomFieldNotFound().ToProblem();
        }

        var list = await query.ListAsync(entityType, cancellationToken);
        return list.IsFailure ? Diagnostics.Errors.Configuration.CustomFieldNotFound().ToProblem() : TypedResults.Ok(list.Value);
    }
}
