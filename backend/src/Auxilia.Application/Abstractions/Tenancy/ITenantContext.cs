namespace Auxilia.Application.Abstractions.Tenancy;

/// <summary>
/// The tenant of the current request, message or job (scoped; skill auxilia-multitenancy). Resolved once by the
/// host (API middleware, Worker pipeline, auxctl); never taken from request bodies or query strings.
/// </summary>
public interface ITenantContext
{
    TenantInfo? Current { get; }

    bool IsResolved => Current is not null;

    /// <summary>The current tenant; throws when the scope has none (a programming error).</summary>
    TenantInfo Tenant => Current ?? throw new InvalidOperationException("No tenant is resolved for this scope.");
}

/// <summary>Sets the tenant of a scope; used only by the host code that resolves it. A scope never changes tenant.</summary>
public interface ITenantContextSetter
{
    void Set(TenantInfo tenant);
}
