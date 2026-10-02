using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Domain;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Domain.Directory;

/// <summary>Business status of a client (D-05), independent from sign-in.</summary>
public enum ClientStatus
{
    /// <summary>No open case (Q03): a new client starts here.</summary>
    Inactive,

    /// <summary>At least one case still open (Q03).</summary>
    Active,
}

/// <summary>
/// The client side of a <see cref="Person"/> (<c>directory.client_profiles</c>, same id as the person, F05). Holds the
/// business status (D-05, recomputed from the cases, Q03) and the employee in charge, with the history of every
/// assignment (<c>directory.client_assignments</c>). Sign-in (the user account) lives in Identity. Deleting a client
/// soft-deletes the person (Q29); the profile and its history stay and are hidden with it.
/// </summary>
public sealed class ClientProfile : AggregateRoot<Guid>, IAuditable
{
    private readonly List<ClientAssignment> assignments = [];

    private ClientProfile(Guid personId, DateTimeOffset now)
        : base(personId)
    {
        Status = ClientStatus.Inactive;
        StatusChangedAt = now;
    }

    private ClientProfile()
    {
    }

    public ClientStatus Status { get; private set; }

    public DateTimeOffset StatusChangedAt { get; private set; }

    /// <summary>The user (Employee role) currently in charge; <c>null</c> when nobody is (copy of the open assignment, for lists).</summary>
    public Guid? EmployeeUserId { get; private set; }

    /// <summary>Every assignment, the open one included, oldest first.</summary>
    public IReadOnlyList<ClientAssignment> Assignments => assignments.OrderBy(assignment => assignment.AssignedAt).ToArray();

    public static ClientProfile Create(Guid personId, Guid? employeeUserId, DateTimeOffset now)
    {
        var profile = new ClientProfile(personId, now);
        if (employeeUserId is { } employee)
        {
            profile.Assign(employee, now);
        }

        return profile;
    }

    /// <summary>Hands the client to another employee; the previous assignment ends now. Same employee: nothing changes.</summary>
    public bool Assign(Guid employeeUserId, DateTimeOffset now)
    {
        if (EmployeeUserId == employeeUserId)
        {
            return false;
        }

        EndOpenAssignment(now);
        assignments.Add(new ClientAssignment(Guid.CreateVersion7(), Id, employeeUserId, now));
        EmployeeUserId = employeeUserId;
        return true;
    }

    /// <summary>Nobody in charge from now on. Already unassigned: nothing changes.</summary>
    public bool Unassign(DateTimeOffset now)
    {
        if (EmployeeUserId is null)
        {
            return false;
        }

        EndOpenAssignment(now);
        EmployeeUserId = null;
        return true;
    }

    /// <summary>Q60: sign-in can be enabled only for a client with an employee in charge.</summary>
    public Result CanEnableSignIn() => EmployeeUserId is null ? Errors.Directory.ClientEmployeeRequired() : Result.Success();

    /// <summary>Q03: active while at least one case is open; called by the Cases module (B-08).</summary>
    public bool UpdateStatus(bool hasOpenCases, DateTimeOffset now)
    {
        var status = hasOpenCases ? ClientStatus.Active : ClientStatus.Inactive;
        if (status == Status)
        {
            return false;
        }

        Status = status;
        StatusChangedAt = now;
        return true;
    }

    private void EndOpenAssignment(DateTimeOffset now)
    {
        foreach (var open in assignments.Where(assignment => assignment.EndedAt is null))
        {
            open.End(now);
        }
    }
}

/// <summary>A period during which an employee was in charge of a client (<c>directory.client_assignments</c>).</summary>
public sealed class ClientAssignment : Entity<Guid>
{
    internal ClientAssignment(Guid id, Guid clientId, Guid employeeUserId, DateTimeOffset assignedAt)
        : base(id)
    {
        ClientId = clientId;
        EmployeeUserId = employeeUserId;
        AssignedAt = assignedAt;
    }

    private ClientAssignment()
    {
    }

    public Guid ClientId { get; private set; }

    public Guid EmployeeUserId { get; private set; }

    public DateTimeOffset AssignedAt { get; private set; }

    /// <summary><c>null</c> while it is the current assignment.</summary>
    public DateTimeOffset? EndedAt { get; private set; }

    internal void End(DateTimeOffset now) => EndedAt ??= now;
}
