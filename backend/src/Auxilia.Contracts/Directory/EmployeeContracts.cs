namespace Auxilia.Contracts.Directory;

/// <summary>A specialization of the Employee role held by an employee.</summary>
public sealed record EmployeeSpecializationResponse(Guid Id, string Name);

/// <summary>
/// A row of the employee list (F06). <c>id</c> is the user id (the id clients, specializations and assignments refer
/// to). <c>canSignIn</c> is the legacy Active/Inactive status; <c>isDefault</c> marks the default employee (Q31).
/// </summary>
public sealed record EmployeeListItemResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string? Email,
    string UserName,
    string? Phone,
    bool CanSignIn,
    bool IsDefault,
    int AssignedClients,
    IReadOnlyList<EmployeeSpecializationResponse> Specializations);

/// <summary>An administrator an employee can report to (Q32).</summary>
public sealed record EmployeeAdministratorResponse(Guid UserId, string FullName);

/// <summary>
/// What an employee is working on. <c>assignedClients</c> counts the clients in charge; open cases and appointments of
/// the week are added with their modules (B-08, B-16).
/// </summary>
public sealed record EmployeeWorkloadResponse(int AssignedClients);

/// <summary>
/// The detail of an employee (F06): personal data, account (<c>isActivated</c> once a password is set, D-06), default
/// flag (Q31), administrator (Q32), specializations and workload.
/// </summary>
public sealed record EmployeeDetailResponse(
    Guid Id,
    Guid PersonId,
    string FirstName,
    string LastName,
    string? Email,
    DateOnly? BirthDate,
    string? Phone,
    string? FiscalCode,
    string UserName,
    bool CanSignIn,
    bool IsActivated,
    bool IsDefault,
    DateTimeOffset CreatedAt,
    EmployeeAdministratorResponse? Administrator,
    IReadOnlyList<EmployeeSpecializationResponse> Specializations,
    EmployeeWorkloadResponse Workload);

/// <summary>
/// A new employee (F06, Q55): first and last name, birth date and e-mail (also the user name) are required.
/// <c>canSignIn</c> (legacy "active"): when true the activation e-mail leaves at once (D-06).
/// </summary>
public sealed record CreateEmployeeRequest(
    string FirstName,
    string LastName,
    DateOnly? BirthDate,
    string Email,
    string? Phone,
    string? FiscalCode,
    bool CanSignIn);

/// <summary>The employee exists; <c>invitationSent</c> tells whether the activation e-mail left (otherwise <c>invitationErrorCode</c>).</summary>
public sealed record CreateEmployeeResponse(Guid Id, bool InvitationSent, string? InvitationErrorCode);

/// <summary>Personal data and user name (unique, Q52) of an employee.</summary>
public sealed record UpdateEmployeeRequest(
    string FirstName,
    string LastName,
    DateOnly? BirthDate,
    string Email,
    string? Phone,
    string? FiscalCode,
    string UserName);

public sealed record SetEmployeeSignInRequest(bool CanSignIn);

/// <summary>The whole set of Employee specializations of the employee (F06: many).</summary>
public sealed record SetEmployeeSpecializationsRequest(IReadOnlyList<Guid> SpecializationIds);

public sealed record SetEmployeeAdministratorRequest(Guid AdministratorUserId);

public sealed record EmployeeInvitationResponse(bool Sent, string? ErrorCode);

/// <summary><c>sendLink</c>: e-mail a reset link; otherwise a temporary password is returned once.</summary>
public sealed record ResetEmployeePasswordRequest(bool SendLink);

public sealed record EmployeePasswordResetResponse(string? TemporaryPassword);

/// <summary>
/// The employee list (F06): text filters match anywhere; <c>status</c> <c>active</c> (can sign in) or <c>inactive</c>;
/// <c>sort</c> one of <c>lastName</c> (default, then first name), <c>fullName</c>, <c>email</c>, <c>userName</c>,
/// <c>-</c> for descending.
/// </summary>
public sealed record EmployeeListQuery(
    string? FullName,
    string? LastName,
    string? Email,
    string? UserName,
    string? Phone,
    string? Status,
    string? Sort,
    int Page,
    int PageSize);
