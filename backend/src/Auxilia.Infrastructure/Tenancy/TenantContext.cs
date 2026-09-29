using Auxilia.Application.Abstractions.Tenancy;

namespace Auxilia.Infrastructure.Tenancy;

/// <summary>Scoped holder of the resolved tenant; set once per scope.</summary>
internal sealed class TenantContext : ITenantContext, ITenantContextSetter
{
    public TenantInfo? Current { get; private set; }

    public void Set(TenantInfo tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        if (Current is not null && Current.Id != tenant.Id)
        {
            throw new InvalidOperationException("A scope cannot change tenant; create a new scope instead.");
        }

        Current = tenant;
    }
}
