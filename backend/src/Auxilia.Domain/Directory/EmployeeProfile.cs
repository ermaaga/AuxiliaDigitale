using Auxilia.SharedKernel.Domain;

namespace Auxilia.Domain.Directory;

/// <summary>
/// The employee side of a user with the Employee role (<c>directory.employee_profiles</c>, same id as the user, F06).
/// Holds the "default employee" flag (Q31: at most one at a time, it receives the clients nobody was assigned to) and
/// the administrator the employee reports to (Q32). Employees created before B-02 (or by import) have no row until
/// one of these values is set: no row means not default and no administrator.
/// </summary>
public sealed class EmployeeProfile : AggregateRoot<Guid>, IAuditable
{
    private EmployeeProfile(Guid userId)
        : base(userId)
    {
    }

    private EmployeeProfile()
    {
    }

    /// <summary>The default employee of the tenant (unique among the profiles).</summary>
    public bool IsDefault { get; private set; }

    /// <summary>The user (Administrator role) the employee reports to; <c>null</c> when none.</summary>
    public Guid? AdministratorUserId { get; private set; }

    public static EmployeeProfile Create(Guid userId) => new(userId);

    /// <summary>Becomes the default employee; the caller clears the previous one first (Q31).</summary>
    public bool MakeDefault()
    {
        if (IsDefault)
        {
            return false;
        }

        IsDefault = true;
        return true;
    }

    public bool ClearDefault()
    {
        if (!IsDefault)
        {
            return false;
        }

        IsDefault = false;
        return true;
    }

    /// <summary>Q32: reports to <paramref name="administratorUserId"/>, or to nobody with <c>null</c>.</summary>
    public bool SetAdministrator(Guid? administratorUserId)
    {
        if (AdministratorUserId == administratorUserId)
        {
            return false;
        }

        AdministratorUserId = administratorUserId;
        return true;
    }
}
