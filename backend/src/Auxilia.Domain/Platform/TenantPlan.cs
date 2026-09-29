using Auxilia.SharedKernel.Domain;

namespace Auxilia.Domain.Platform;

/// <summary>Plan assigned to a tenant for a period; the current one is the latest with <c>ValidFrom &lt;= now</c> and no ended <c>ValidTo</c>.</summary>
public sealed class TenantPlan : Entity<Guid>
{
    public TenantPlan(Guid id, Guid tenantId, Guid planId, DateTimeOffset validFrom, DateTimeOffset? validTo = null)
        : base(id)
    {
        if (validTo is { } end && end <= validFrom)
        {
            throw new ArgumentOutOfRangeException(nameof(validTo), "ValidTo must be after ValidFrom.");
        }

        TenantId = tenantId;
        PlanId = planId;
        ValidFrom = validFrom;
        ValidTo = validTo;
    }

    private TenantPlan()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid PlanId { get; private set; }

    public DateTimeOffset ValidFrom { get; private set; }

    public DateTimeOffset? ValidTo { get; private set; }

    public bool IsValidAt(DateTimeOffset instant) => ValidFrom <= instant && (ValidTo is null || instant < ValidTo);

    public void End(DateTimeOffset validTo)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(validTo, ValidFrom);
        ValidTo = validTo;
    }
}
