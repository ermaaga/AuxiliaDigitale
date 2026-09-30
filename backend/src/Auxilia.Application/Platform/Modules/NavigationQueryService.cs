using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Modules;
using Auxilia.Contracts.Platform;

namespace Auxilia.Application.Platform.Modules;

/// <summary>The tenant app menu of the current user (served by <c>GET /me/navigation</c> from P2-03).</summary>
public interface INavigationQueryService
{
    /// <summary>
    /// Entries of the modules visible to at least one role of the user, for those roles (union), ordered. Permission
    /// checks are added with <c>role_permissions</c> (P2-03).
    /// </summary>
    Task<IReadOnlyList<NavigationItemResponse>> GetAsync(CancellationToken cancellationToken);
}

internal sealed class NavigationQueryService : INavigationQueryService
{
    private readonly IModuleRegistry registry;
    private readonly IModuleAccess access;
    private readonly ICurrentUser currentUser;

    public NavigationQueryService(IModuleRegistry registry, IModuleAccess access, ICurrentUser currentUser)
    {
        this.registry = registry;
        this.access = access;
        this.currentUser = currentUser;
    }

    public async Task<IReadOnlyList<NavigationItemResponse>> GetAsync(CancellationToken cancellationToken)
    {
        if (currentUser.ActorType != ActorType.User || currentUser.Roles.Count == 0)
        {
            return [];
        }

        var modules = await access.GetAsync(cancellationToken);
        var roles = currentUser.Roles;

        return registry.All
            .Where(module => modules.Modules.ContainsKey(module.Code))
            .SelectMany(module => module.Navigation
                .Where(entry => roles.Any(role => entry.Roles.Contains(role) && modules.Modules[module.Code].Contains(role)))
                .Select(entry => new NavigationItemResponse(entry.Key, module.Code, entry.LabelKey, entry.Route, entry.Icon, entry.Order)))
            .DistinctBy(item => item.Key)
            .OrderBy(item => item.Order)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .ToArray();
    }
}
