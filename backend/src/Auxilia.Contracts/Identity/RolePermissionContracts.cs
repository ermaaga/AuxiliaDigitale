namespace Auxilia.Contracts.Identity;

/// <summary>
/// A permission declared by a module (F22) with the tenant roles that hold it (<see cref="Roles"/>) and the roles a new
/// tenant gets (<see cref="DefaultRoles"/>). The description is the translation key
/// <c>permissions.&lt;code&gt;.description</c>.
/// </summary>
public sealed record RolePermissionResponse(string Code, string Module, IReadOnlyList<string> Roles, IReadOnlyList<string> DefaultRoles);

/// <summary>Every permission the role holds afterwards (the others are revoked).</summary>
public sealed record SetRolePermissionsRequest(IReadOnlyList<string> Permissions);
