using System.Text.Json;

namespace Auxilia.Contracts.Cases;

/// <summary>
/// Opens a case (F09): <c>startedOn</c> defaults to today, <c>dueOn</c> is an optional target (D-07),
/// <c>specializationId</c> defaults to the service's (an active Employee specialization). <c>employeeUserId</c> (only
/// for Administrators) hands the client to that employee; otherwise a client without an employee goes to the default
/// one (Q31). <c>customFields</c> is a JSON object (F20, entity <c>case</c>).
/// </summary>
public sealed record OpenCaseRequest(
    Guid ClientId,
    Guid ServiceId,
    DateOnly? StartedOn,
    DateOnly? DueOn,
    Guid? SpecializationId,
    Guid? EmployeeUserId,
    JsonElement? CustomFields);

/// <summary>Due date and custom fields of a case not completed yet.</summary>
public sealed record UpdateCaseRequest(DateOnly? DueOn, JsonElement? CustomFields);

/// <summary>An optional note (at most 500 characters) written in the status history.</summary>
public sealed record ChangeCaseStatusRequest(string? Note);

/// <summary>
/// Completes a sent case: <c>amountPaid</c> is the money received now (default: price minus what was already paid,
/// never below zero), recorded as a payment; <c>rejected</c> is the outcome.
/// </summary>
public sealed record CompleteCaseRequest(decimal? AmountPaid, bool Rejected, string? Note);

/// <summary>Money received (&gt; 0, two decimals); <c>paidOn</c> defaults to today.</summary>
public sealed record AddCasePaymentRequest(decimal Amount, DateOnly? PaidOn, string? Note);

public sealed record CaseClientResponse(Guid Id, string FullName);

public sealed record CaseServiceResponse(Guid Id, string Name, string? Description, int DurationDays);

public sealed record CaseSpecializationResponse(Guid Id, string Name, bool IsPrivate);

/// <summary>A user of the timeline or of a payment (name empty when unknown).</summary>
public sealed record CaseUserResponse(Guid UserId, string FullName);

/// <summary>A step of the timeline; <c>fromStatus</c> is null for the opening.</summary>
public sealed record CaseStatusChangeResponse(string? FromStatus, string ToStatus, DateTimeOffset ChangedAt, CaseUserResponse? ChangedBy, string? Note);

public sealed record CasePaymentResponse(Guid Id, decimal Amount, DateOnly PaidOn, string? Note, DateTimeOffset RecordedAt, CaseUserResponse? RecordedBy);

/// <summary>
/// A case (F09): <c>status</c> <c>Inserted</c>, <c>InProgress</c>, <c>Sent</c> or <c>Completed</c>; <c>price</c> is
/// the service price when opened (Q02), <c>amountPaid</c> the sum of the payments; <c>expiresOn</c> is set at
/// completion (D-07). <c>canManage</c> / <c>canDelete</c> say what the caller may do (F10).
/// </summary>
public sealed record CaseResponse(
    Guid Id,
    string Number,
    CaseClientResponse Client,
    CaseServiceResponse Service,
    CaseSpecializationResponse? Specialization,
    string Status,
    bool IsRejected,
    bool IsActive,
    decimal Price,
    string Currency,
    decimal AmountPaid,
    DateOnly StartedOn,
    DateOnly? DueOn,
    DateOnly? ExpiresOn,
    DateTimeOffset? CompletedAt,
    JsonElement CustomFields,
    IReadOnlyList<CaseStatusChangeResponse> History,
    IReadOnlyList<CasePaymentResponse> Payments,
    bool CanManage,
    bool CanDelete);

/// <summary>
/// A row of the case lists (F09): <c>validity</c> is what clients see — <c>Active</c>, <c>Expired</c> (expiry date
/// passed) or <c>Inactive</c>; <c>customFields</c> is a JSON object (F20).
/// </summary>
public sealed record CaseListItemResponse(
    Guid Id,
    string Number,
    CaseClientResponse Client,
    CaseServiceRefResponse Service,
    CaseSpecializationResponse? Specialization,
    string Status,
    bool IsRejected,
    string Validity,
    DateOnly StartedOn,
    DateOnly? DueOn,
    DateOnly? ExpiresOn,
    decimal Price,
    string Currency,
    decimal AmountPaid,
    JsonElement CustomFields);

public sealed record CaseServiceRefResponse(Guid Id, string Name);

/// <summary>
/// The case lists (F09, F10). Every caller sees only what F10 allows (Administrators everything, employees D-04,
/// clients their own). <c>clientName</c> matches name or surname, <c>serviceName</c> the service; <c>clientId</c> /
/// <c>serviceId</c> give the cases of one client (360°) or one service (service detail); <c>status</c> one status.
/// <c>showAll</c> off restricts an employee to cases without a specialization or with one held; <c>showCompleted</c>
/// off hides completed cases (both default off for employees, on for everyone else). <c>sort</c> one of
/// <c>startedOn</c> (default, descending), <c>client</c>, <c>service</c>, <c>expiresOn</c>, <c>amountPaid</c>,
/// <c>number</c>; <c>-</c> for descending.
/// </summary>
public sealed record CaseListQuery(
    string? ClientName,
    string? ServiceName,
    Guid? ClientId,
    Guid? ServiceId,
    string? Status,
    bool? ShowAll,
    bool? ShowCompleted,
    string? Sort,
    int Page,
    int PageSize);
