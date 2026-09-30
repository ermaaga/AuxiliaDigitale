namespace Auxilia.Contracts.Identity;

/// <summary>
/// <c>GET /me</c>: the signed-in tenant user, the tenant, every role and the effective permissions (the UI hides what
/// the API would deny anyway).
/// </summary>
public sealed record MeResponse(
    Guid Id,
    string UserName,
    string? Email,
    string Language,
    string Tenant,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);
