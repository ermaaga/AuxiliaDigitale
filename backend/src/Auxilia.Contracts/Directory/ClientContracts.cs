using System.Text.Json;

namespace Auxilia.Contracts.Directory;

/// <summary>The employee in charge of a client.</summary>
public sealed record ClientEmployeeResponse(Guid UserId, string FullName);

/// <summary>
/// A row of the client lists (F05). <c>status</c> is the business status (<c>Active</c> while a case is open, Q03);
/// <c>canSignIn</c> is the account access (D-05). <c>customFields</c> is a JSON object (F20). <c>userId</c> is the
/// client's account; <c>imageVersion</c> the profile picture hash of the client's account (null without a picture), for <c>GET /users/{id}/image?v=</c> (F04).
/// </summary>
public sealed record ClientListItemResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string? Email,
    string UserName,
    string? Phone,
    string? FiscalCode,
    string Status,
    bool CanSignIn,
    ClientEmployeeResponse? Employee,
    JsonElement CustomFields,
    Guid UserId,
    string? ImageVersion);

/// <summary>The sign-in account of a client: <c>isActivated</c> once the client has set a password (D-06).</summary>
public sealed record ClientAccountResponse(Guid UserId, string UserName, bool CanSignIn, bool IsActivated);

/// <summary>A period during which an employee was in charge; <c>endedAt</c> is null for the current one.</summary>
public sealed record ClientAssignmentResponse(Guid EmployeeUserId, string? EmployeeName, DateTimeOffset AssignedAt, DateTimeOffset? EndedAt);

public sealed record ClientSpecializationResponse(Guid Id, string Name);

/// <summary>
/// The detail of a client (F05): personal data, account, employee in charge with history, specializations and the
/// profile picture version of the account (F04).
/// </summary>
public sealed record ClientDetailResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string? Email,
    DateOnly? BirthDate,
    string? Phone,
    string? FiscalCode,
    string Status,
    DateTimeOffset StatusChangedAt,
    JsonElement CustomFields,
    ClientAccountResponse? Account,
    ClientEmployeeResponse? Employee,
    IReadOnlyList<ClientAssignmentResponse> Assignments,
    IReadOnlyList<ClientSpecializationResponse> Specializations,
    string? ImageVersion);

/// <summary>
/// A new client (F05). The e-mail is also the user name. Created by an Administrator: can sign in, assigned to
/// <c>employeeUserId</c> when given. Created by an Employee: cannot sign in yet and is assigned to that employee.
/// </summary>
public sealed record CreateClientRequest(
    string FirstName,
    string LastName,
    DateOnly? BirthDate,
    string Email,
    string? Phone,
    string FiscalCode,
    JsonElement? CustomFields,
    Guid? EmployeeUserId);

/// <summary>The client exists; <c>invitationSent</c> tells whether the activation e-mail left (otherwise <c>invitationErrorCode</c>).</summary>
public sealed record CreateClientResponse(Guid Id, bool InvitationSent, string? InvitationErrorCode);

/// <summary>Personal data, user name (unique, Q52) and custom fields of a client.</summary>
public sealed record UpdateClientRequest(
    string FirstName,
    string LastName,
    DateOnly? BirthDate,
    string Email,
    string? Phone,
    string FiscalCode,
    string UserName,
    JsonElement? CustomFields);

/// <summary>Enables or disables the client's sign-in; enabling needs an employee in charge (Q60).</summary>
public sealed record SetClientSignInRequest(bool CanSignIn);

public sealed record AssignClientEmployeeRequest(Guid EmployeeUserId);

/// <summary>The whole set of Client specializations of the client (Q30: many).</summary>
public sealed record SetClientSpecializationsRequest(IReadOnlyList<Guid> SpecializationIds);

public sealed record ClientInvitationResponse(bool Sent, string? ErrorCode);

/// <summary><c>sendLink</c>: e-mail a reset link; otherwise a temporary password is returned once.</summary>
public sealed record ResetClientPasswordRequest(bool SendLink);

public sealed record ClientPasswordResetResponse(string? TemporaryPassword);

/// <summary>
/// The client lists (F05): <c>view</c> <c>all</c> (default) or <c>mine</c> (clients of the calling employee); text
/// filters match anywhere; <c>sort</c> one of <c>lastName</c> (default, then first name), <c>fullName</c>, <c>email</c>,
/// <c>userName</c>, <c>-</c> for descending. <c>employeeUserId</c> restricts the <c>all</c> view to the clients of
/// one employee (the employee detail, F06).
/// </summary>
public sealed record ClientListQuery(
    string? View,
    string? FullName,
    string? LastName,
    string? Email,
    string? UserName,
    string? Phone,
    string? Status,
    string? Sort,
    int Page,
    int PageSize,
    Guid? EmployeeUserId = null,
    Guid? TagId = null);
