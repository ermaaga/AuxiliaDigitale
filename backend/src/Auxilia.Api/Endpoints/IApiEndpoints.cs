namespace Auxilia.Api.Endpoints;

/// <summary>
/// Maps a set of endpoints on the <c>/api/v1</c> group. Registered in DI; module descriptors take over this role
/// in task P1-11 (<c>IModuleDescriptor.MapEndpoints</c>).
/// </summary>
public interface IApiEndpoints
{
    void Map(RouteGroupBuilder api);
}
