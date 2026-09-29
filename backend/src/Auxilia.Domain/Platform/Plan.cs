using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Domain;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Domain.Platform;

/// <summary>
/// A commercial plan: which modules are included **for which roles** (catalog <c>plans</c> + <c>plan_modules</c>).
/// Today there is one default plan, <c>standard</c>, with every module; pricing adds plans without code changes.
/// </summary>
public sealed class Plan : AggregateRoot<Guid>
{
    public const int CodeMaxLength = 50;
    public const int NameKeyMaxLength = 150;
    public const string StandardCode = "standard";

    /// <summary>Fixed id of the seeded <c>standard</c> plan.</summary>
    public static readonly Guid StandardId = Guid.Parse("0199a0b2-5d00-7000-8000-000000000001");

    private readonly List<PlanModule> modules = [];

    private Plan(Guid id, string code, string nameKey, bool isDefault)
        : base(id)
    {
        Code = code;
        NameKey = nameKey;
        IsDefault = isDefault;
        IsActive = true;
    }

    private Plan()
    {
        Code = NameKey = string.Empty;
    }

    public string Code { get; private set; }

    public string NameKey { get; private set; }

    /// <summary>Plan assigned to new tenants.</summary>
    public bool IsDefault { get; private set; }

    public bool IsActive { get; private set; }

    public IReadOnlyCollection<PlanModule> Modules => modules.AsReadOnly();

    public static Result<Plan> Create(Guid id, string code, string nameKey, bool isDefault)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > CodeMaxLength || code.Any(char.IsUpper))
        {
            return Errors.Tenancy.CatalogValueInvalid("code", "validation.plan.code");
        }

        if (string.IsNullOrWhiteSpace(nameKey) || nameKey.Length > NameKeyMaxLength)
        {
            return Errors.Tenancy.CatalogValueInvalid("nameKey", "validation.plan.nameKey");
        }

        return new Plan(id, code, nameKey, isDefault);
    }

    /// <summary>Includes the module for the given roles (replacing previous roles); no roles means the module is excluded.</summary>
    public void SetModule(string moduleCode, IReadOnlyCollection<TenantRole> roles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleCode);
        ArgumentNullException.ThrowIfNull(roles);

        modules.RemoveAll(module => module.ModuleCode == moduleCode);
        if (roles.Count > 0)
        {
            modules.Add(new PlanModule(Id, moduleCode, roles));
        }
    }

    public IReadOnlyCollection<TenantRole> RolesFor(string moduleCode) =>
        modules.FirstOrDefault(module => module.ModuleCode == moduleCode)?.Roles ?? [];
}

/// <summary>A module included in a plan for some roles.</summary>
public sealed class PlanModule
{
    internal PlanModule(Guid planId, string moduleCode, IReadOnlyCollection<TenantRole> roles)
    {
        PlanId = planId;
        ModuleCode = moduleCode;
        Roles = roles.Distinct().Order().ToArray();
    }

    private PlanModule()
    {
        ModuleCode = string.Empty;
        Roles = [];
    }

    public Guid PlanId { get; private set; }

    public string ModuleCode { get; private set; }

    public TenantRole[] Roles { get; private set; }
}
