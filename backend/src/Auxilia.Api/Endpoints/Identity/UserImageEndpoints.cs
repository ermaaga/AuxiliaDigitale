using Auxilia.Api.Authorization;
using Auxilia.Api.Infrastructure;
using Auxilia.Api.Tenancy;
using Auxilia.Application.Identity;

using Microsoft.Net.Http.Headers;

namespace Auxilia.Api.Endpoints.Identity;

/// <summary>
/// Profile pictures (F04) for the header, grids and details: the own one for every user, any user's for staff. Served
/// with the hash as ETag and cached privately (the URL carries <c>?v=</c> the version from the profile or the lists).
/// </summary>
internal sealed class UserImageEndpoints : IApiEndpoints
{
    public void Map(RouteGroupBuilder api)
    {
        api.MapGroup("/users").WithTags("Me").RequireTenant().RequireTenantUser()
            .MapGet("/{id:guid}/image", GetImageAsync)
            .WithName("GetUserImage")
            .WithSummary("The picture of a user (own picture, or any user's for staff) with its hash as ETag")
            .Produces(StatusCodes.Status200OK, contentType: "image/jpeg")
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> GetImageAsync(Guid id, IProfileQueryService profile, HttpContext context, CancellationToken cancellationToken) =>
        (await profile.ImageAsync(id, cancellationToken)).ToHttpResult(image =>
        {
            var etag = $"\"{image.Hash}\"";
            context.Response.Headers.ETag = etag;
            context.Response.Headers.CacheControl = "private, max-age=86400";
            var match = context.Request.Headers.IfNoneMatch.ToString();
            return EntityTagHeaderValue.TryParseList(match.Split(','), out var tags)
                && tags.Any(tag => tag.Equals(EntityTagHeaderValue.Any) || string.Equals(tag.Tag.ToString(), etag, StringComparison.Ordinal))
                ? TypedResults.StatusCode(StatusCodes.Status304NotModified)
                : TypedResults.File(image.Content, image.ContentType);
        });
}
