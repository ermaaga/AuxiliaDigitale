namespace Auxilia.Api.Endpoints;

/// <summary>
/// Endpoints on the <c>/api/v1</c> group that belong to no tenant module (authentication, <c>/me</c>, platform
/// console). Module endpoints use <see cref="IModuleEndpoints"/>.
/// </summary>
public interface IApiEndpoints
{
    void Map(RouteGroupBuilder api);
}

/// <summary>
/// The HTTP side of a module (<c>Api/Endpoints/&lt;Module&gt;/</c>), paired with the <c>IModuleDescriptor</c> of the same
/// <see cref="ModuleCode"/>. Its endpoints are mapped in a group that needs an active tenant and answers 404 when the
/// module is not visible to the caller's roles in that tenant (ARCHITECTURE §5.2).
/// </summary>
public interface IModuleEndpoints
{
    string ModuleCode { get; }

    void Map(RouteGroupBuilder module);
}
