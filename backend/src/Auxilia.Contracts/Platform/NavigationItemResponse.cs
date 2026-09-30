namespace Auxilia.Contracts.Platform;

/// <summary>A menu link of the tenant app (<c>GET /me/navigation</c>); <c>route</c> is relative to <c>/{tenant}</c>.</summary>
public sealed record NavigationItemResponse(string Key, string Module, string LabelKey, string Route, string Icon, int Order);
