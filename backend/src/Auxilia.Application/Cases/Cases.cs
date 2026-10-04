using System.Globalization;
using System.Text.Json;

using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Cases;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Configuration.Public;
using Auxilia.Application.Directory.Public;
using Auxilia.Application.Identity.Public;
using Auxilia.Application.Messaging.Public;
using Auxilia.Contracts.Cases;
using Auxilia.Contracts.Common;
using Auxilia.Diagnostics;
using Auxilia.Domain.Cases;
using Auxilia.Domain.Messaging;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Application.Cases;

/// <summary>A case as the access rules see it (F10, D-04).</summary>
public sealed record CaseResource(Guid ClientId, Guid? SpecializationId, bool IsPrivate, CaseStatus Status);

/// <summary>
/// Cases of the current tenant (F09, Q02–Q04, D-07): staff open them for a client and a service, move them one status at
/// a time, record payments and complete them with the amount received and the outcome. Every change recomputes the
/// client status when it can change it (Q03).
/// </summary>
public interface ICaseManager
{
    /// <summary>
    /// Price snapshot of the service, specialization of the service unless another is given, number
    /// <c>{year}-{sequence}</c>; a client without an employee goes to the default employee (Q31). Employees open cases
    /// only for services without a specialization or with one they hold (F10).
    /// </summary>
    Task<Result<Guid>> OpenAsync(OpenCaseRequest request, CancellationToken cancellationToken);

    Task<Result> UpdateAsync(Guid id, UpdateCaseRequest request, CancellationToken cancellationToken);

    Task<Result> AdvanceAsync(Guid id, string? note, CancellationToken cancellationToken);

    Task<Result> GoBackAsync(Guid id, string? note, CancellationToken cancellationToken);

    Task<Result> CompleteAsync(Guid id, CompleteCaseRequest request, CancellationToken cancellationToken);

    Task<Result> AddPaymentAsync(Guid id, AddCasePaymentRequest request, CancellationToken cancellationToken);

    /// <summary>Soft delete; employees cannot delete completed cases (F10).</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// F11 (legacy "send expiry e-mail"): e-mails the client the end date of the case (expiry, else due date) with the
    /// template <c>case-expiry-reminder</c> in the client's language.
    /// </summary>
    Task<Result> SendExpiryReminderAsync(Guid id, CancellationToken cancellationToken);
}

public interface ICaseQueryService
{
    /// <summary>The cases the caller may see (F10 in the query), with the legacy filters, toggles and sorts.</summary>
    Task<Result<PagedResponse<CaseListItemResponse>>> ListAsync(CaseListQuery query, CancellationToken cancellationToken);

    /// <summary>The case with its timeline and payments; <c>AUX-14022</c> when the caller cannot see it (Q10).</summary>
    Task<Result<CaseResponse>> GetAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// F10 (D-04, code semantics): Administrators see and manage everything. Employees see a case without a specialization,
/// with a non-private one, or with one they hold; they manage it when it has no specialization or one they hold, and
/// delete it under the same rule unless it is completed. Clients see their own cases.
/// </summary>
internal sealed class CaseAccessPolicy(ICurrentUser currentUser, ICaseDataFactory data) : IResourceAccessPolicy<CaseResource>
{
    private CaseCaller? caller;

    public async Task<bool> CanAccessAsync(CaseResource resource, string permission, CancellationToken cancellationToken) => permission switch
    {
        CasesPermissions.ViewCases => await CanSeeAsync(resource, cancellationToken),
        CasesPermissions.ManageCases => await CanManageAsync(resource, cancellationToken),
        CasesPermissions.DeleteCases => await CanDeleteAsync(resource, cancellationToken),
        _ => false,
    };

    /// <summary>The same visibility as <see cref="CanSeeAsync"/>, as a query filter for the lists (never post-filtering).</summary>
    public async Task<CaseScope> ScopeAsync(CancellationToken cancellationToken)
    {
        if (Has(TenantRole.Administrator))
        {
            return new CaseScope(true, null, null);
        }

        if (Has(TenantRole.Employee))
        {
            return new CaseScope(false, currentUser.UserId, null);
        }

        return Has(TenantRole.Client) && (await CallerAsync(cancellationToken)).PersonId is { } personId
            ? new CaseScope(false, null, personId)
            : CaseScope.None;
    }

    /// <summary>Employees only (not Administrators): the "show all" / "show completed" toggles start off (F09).</summary>
    public bool IsEmployeeOnly => Has(TenantRole.Employee) && !Has(TenantRole.Administrator);

    public async Task<bool> CanSeeAsync(CaseResource resource, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (Has(TenantRole.Administrator))
        {
            return true;
        }

        var me = await CallerAsync(cancellationToken);
        return (Has(TenantRole.Employee) && (resource.SpecializationId is not { } specialization || !resource.IsPrivate || me.SpecializationIds.Contains(specialization)))
            || (Has(TenantRole.Client) && me.PersonId == resource.ClientId);
    }

    public async Task<bool> CanManageAsync(CaseResource resource, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (Has(TenantRole.Administrator))
        {
            return true;
        }

        return Has(TenantRole.Employee)
            && (resource.SpecializationId is not { } specialization || (await CallerAsync(cancellationToken)).SpecializationIds.Contains(specialization));
    }

    public async Task<bool> CanDeleteAsync(CaseResource resource, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return Has(TenantRole.Administrator) || (resource.Status != CaseStatus.Completed && await CanManageAsync(resource, cancellationToken));
    }

    private bool Has(TenantRole role) => currentUser.ActorType == ActorType.User && currentUser.Roles.Contains(role);

    private async Task<CaseCaller> CallerAsync(CancellationToken cancellationToken)
    {
        if (caller is null)
        {
            if (currentUser.UserId is not { } userId)
            {
                caller = new CaseCaller(null, new HashSet<Guid>());
            }
            else
            {
                await using var store = await data.OpenAsync(cancellationToken);
                caller = await store.CallerAsync(userId, cancellationToken);
            }
        }

        return caller;
    }
}

internal static class CaseResources
{
    public static async Task<CaseResource> OfAsync(ICaseData store, Case @case, CancellationToken cancellationToken) =>
        new(
            @case.ClientId,
            @case.SpecializationId,
            @case.SpecializationId is { } specialization && await store.IsPrivateSpecializationAsync(specialization, cancellationToken),
            @case.Status);
}

internal sealed class CaseManager(
    IOperationRunner operations,
    ICaseDataFactory data,
    IClientDirectory clients,
    ICustomFieldValidator customFields,
    IAccessGuard guard,
    CaseAccessPolicy policy,
    ICurrentUser currentUser,
    IUserAccounts accounts,
    IMessageDispatcher messages,
    TimeProvider clock) : ICaseManager
{
    public const string CustomFieldEntity = "case";

    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    private bool CallerIsAdministrator => currentUser.Roles.Contains(TenantRole.Administrator);

    public Task<Result<Guid>> OpenAsync(OpenCaseRequest request, CancellationToken cancellationToken) => OpenAsync(request, checkAccess: true, cancellationToken);

    /// <summary>A case from an import (F19, D-18): the System opens it, the F10 rules of staff do not apply.</summary>
    internal Task<Result<Guid>> ImportAsync(OpenCaseRequest request, CancellationToken cancellationToken) => OpenAsync(request, checkAccess: false, cancellationToken);

    private Task<Result<Guid>> OpenAsync(OpenCaseRequest request, bool checkAccess, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync<Guid>(Operations.Cases.OpenCase, new { request.ClientId, request.ServiceId }, async scope =>
        {
            var fields = await customFields.ValidateAsync(CustomFieldEntity, request.CustomFields, cancellationToken);
            if (fields.IsFailure)
            {
                return Result.Failure<Guid>(fields.Error!);
            }

            await using var store = await data.OpenAsync(cancellationToken);
            var client = await clients.FindAsync(request.ClientId, cancellationToken);
            var service = await store.ServiceAsync(request.ServiceId, cancellationToken);
            var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
            if (client is null)
            {
                errors["clientId"] = ["validation.cases.client"];
            }

            if (service is not { IsActive: true })
            {
                errors["serviceId"] = ["validation.cases.service"];
            }

            // Legacy: the specialization given, else the service's. A different one must be an active Employee specialization.
            var specialization = request.SpecializationId ?? service?.SpecializationId;
            var isPrivate = false;
            if (specialization is { } specializationId)
            {
                if (specializationId == service?.SpecializationId)
                {
                    isPrivate = await store.IsPrivateSpecializationAsync(specializationId, cancellationToken);
                }
                else if (await store.EmployeeSpecializationPrivacyAsync(specializationId, cancellationToken) is { } privacy)
                {
                    isPrivate = privacy;
                }
                else
                {
                    errors["specializationId"] = ["validation.cases.specialization"];
                }
            }

            if (errors.Count > 0)
            {
                return Errors.Cases.CaseInvalid(errors);
            }

            // F10: an employee opens cases only for specializations held (or none).
            var allowed = checkAccess
                ? await guard.EnsureAsync(CasesPermissions.ManageCases, new CaseResource(client!.Id, specialization, isPrivate, CaseStatus.Inserted), cancellationToken)
                : Result.Success();
            if (allowed.IsFailure)
            {
                return Result.Failure<Guid>(allowed.Error!);
            }

            // Only an Administrator chooses who is in charge (legacy admin panel); otherwise the default employee when nobody is.
            var assigned = await clients.EnsureEmployeeAsync(client!.Id, CallerIsAdministrator ? request.EmployeeUserId : null, cancellationToken);
            if (assigned.IsFailure)
            {
                return Result.Failure<Guid>(assigned.Error!);
            }

            var now = clock.GetUtcNow();
            var year = now.UtcDateTime.Year;
            var number = $"{year}-{await store.NextNumberAsync(year, cancellationToken):D5}";
            var opened = Case.Open(
                Guid.CreateVersion7(),
                new CaseOpening(number, client.Id, service!.Id, service.Price, service.Currency, specialization, request.StartedOn ?? Today, request.DueOn, fields.Value),
                currentUser.UserId,
                now);
            if (opened.IsFailure)
            {
                return Result.Failure<Guid>(opened.Error!);
            }

            store.Add(opened.Value);
            await store.SaveChangesAsync(cancellationToken);
            scope.SetEntity(nameof(Case), opened.Value.Id);

            var status = await RecomputeClientStatusAsync(store, client.Id, cancellationToken);
            return status.IsFailure ? Result.Failure<Guid>(status.Error!) : Result.Success(opened.Value.Id);
        }, cancellationToken);
    }

    public Task<Result> UpdateAsync(Guid id, UpdateCaseRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ChangeAsync(Operations.Cases.UpdateCase, id, CasesPermissions.ManageCases, recomputeClient: false, async (@case, _) =>
        {
            var fields = await customFields.ValidateAsync(CustomFieldEntity, request.CustomFields, cancellationToken);
            return fields.IsFailure ? Result.Failure(fields.Error!) : @case.Update(request.DueOn, fields.Value);
        }, cancellationToken);
    }

    public Task<Result> AdvanceAsync(Guid id, string? note, CancellationToken cancellationToken) =>
        ChangeAsync(Operations.Cases.AdvanceCase, id, CasesPermissions.ManageCases, recomputeClient: false,
            (@case, now) => Task.FromResult(@case.Advance(currentUser.UserId, note, now)), cancellationToken);

    public Task<Result> GoBackAsync(Guid id, string? note, CancellationToken cancellationToken) =>
        ChangeAsync(Operations.Cases.MoveCaseBack, id, CasesPermissions.ManageCases, recomputeClient: false,
            (@case, now) => Task.FromResult(@case.GoBack(currentUser.UserId, note, now)), cancellationToken);

    public Task<Result> CompleteAsync(Guid id, CompleteCaseRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ChangeAsync(Operations.Cases.CompleteCase, id, CasesPermissions.ManageCases, recomputeClient: true, (@case, now) =>
        {
            // Legacy: the amount field starts from the service price; with payments, from what is still due.
            var amount = request.AmountPaid ?? Math.Max(0, @case.Price - @case.AmountPaid);
            return Task.FromResult(@case.Complete(amount, request.Rejected, currentUser.UserId, request.Note, now));
        }, cancellationToken);
    }

    public Task<Result> AddPaymentAsync(Guid id, AddCasePaymentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ChangeAsync(Operations.Cases.RecordCasePayment, id, CasesPermissions.ManageCases, recomputeClient: false,
            (@case, now) => Task.FromResult(@case.AddPayment(request.Amount, request.PaidOn ?? Today, request.Note, currentUser.UserId, now)), cancellationToken);
    }

    public Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Cases.DeleteCase, new { CaseId = id }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            var loaded = await LoadAsync(store, id, CasesPermissions.DeleteCases, cancellationToken);
            if (loaded.IsFailure)
            {
                return Result.Failure(loaded.Error!);
            }

            store.Remove(loaded.Value);
            await store.SaveChangesAsync(cancellationToken);
            return await RecomputeClientStatusAsync(store, loaded.Value.ClientId, cancellationToken);
        }, cancellationToken);

    public Task<Result> SendExpiryReminderAsync(Guid id, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Cases.SendCaseExpiryReminder, new { CaseId = id }, async scope =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            var loaded = await LoadAsync(store, id, CasesPermissions.ManageCases, cancellationToken);
            if (loaded.IsFailure)
            {
                return Result.Failure(loaded.Error!);
            }

            var @case = loaded.Value;
            if (@case.EndsOn is not { } endsOn)
            {
                return Errors.Cases.CaseHasNoEndDate();
            }

            var client = await clients.FindAsync(@case.ClientId, cancellationToken);
            if (client is null || await accounts.FindByPersonAsync(@case.ClientId, cancellationToken) is not { Email: { Length: > 0 } email } account)
            {
                return Errors.Cases.CaseClientHasNoEmail();
            }

            var service = await store.ServiceAsync(@case.ServiceId, cancellationToken);
            var queued = await messages.QueueAsync(
                new OutboundMessageRequest(
                    MessageChannel.Email, MessagePurpose.Transactional, email, MessageTemplates.CaseExpiryReminder, account.LanguageCode,
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["name"] = client.FullName,
                        ["serviceName"] = service?.Name ?? string.Empty,
                        ["endDate"] = endsOn.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                    },
                    nameof(Case),
                    @case.Id),
                cancellationToken);
            scope.SetEntity(nameof(Case), @case.Id);
            return queued.IsFailure ? Result.Failure(queued.Error!) : Result.Success();
        }, cancellationToken);

    private Task<Result> ChangeAsync(
        OperationDescriptor operation, Guid id, string permission, bool recomputeClient, Func<Case, DateTimeOffset, Task<Result>> change, CancellationToken cancellationToken) =>
        operations.RunAsync(operation, new { CaseId = id }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            var loaded = await LoadAsync(store, id, permission, cancellationToken);
            if (loaded.IsFailure)
            {
                return Result.Failure(loaded.Error!);
            }

            var changed = await change(loaded.Value, clock.GetUtcNow());
            if (changed.IsFailure)
            {
                return changed;
            }

            await store.SaveChangesAsync(cancellationToken);
            return recomputeClient ? await RecomputeClientStatusAsync(store, loaded.Value.ClientId, cancellationToken) : Result.Success();
        }, cancellationToken);

    /// <summary>The case (tracked): 404 when the caller cannot see it (Q10), 403 when it can but may not do this (F10).</summary>
    private async Task<Result<Case>> LoadAsync(ICaseData store, Guid id, string permission, CancellationToken cancellationToken)
    {
        if (await store.FindAsync(id, readOnly: false, cancellationToken) is not { } @case)
        {
            return Errors.Cases.CaseNotFound();
        }

        var resource = await CaseResources.OfAsync(store, @case, cancellationToken);
        if (!await policy.CanSeeAsync(resource, cancellationToken))
        {
            return Errors.Cases.CaseNotFound();
        }

        var allowed = await guard.EnsureAsync(permission, resource, cancellationToken);
        return allowed.IsFailure ? Result.Failure<Case>(allowed.Error!) : @case;
    }

    private async Task<Result> RecomputeClientStatusAsync(ICaseData store, Guid clientId, CancellationToken cancellationToken) =>
        await clients.UpdateStatusAsync(clientId, await store.HasOpenCasesAsync(clientId, Today, cancellationToken), cancellationToken);
}

internal sealed class CaseQueryService(ICaseDataFactory data, CaseAccessPolicy policy, IPermissionAccess permissions, TimeProvider clock) : ICaseQueryService
{
    public const int MaxPageSize = 100;
    public const int MaxFilterLength = 200;

    private static readonly Dictionary<string, CaseSort> Sorts = new(StringComparer.Ordinal)
    {
        ["startedOn"] = CaseSort.StartedOn,
        ["client"] = CaseSort.Client,
        ["service"] = CaseSort.Service,
        ["expiresOn"] = CaseSort.ExpiresOn,
        ["amountPaid"] = CaseSort.AmountPaid,
        ["number"] = CaseSort.Number,
    };

    public async Task<Result<PagedResponse<CaseListItemResponse>>> ListAsync(CaseListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (query.Page < 1)
        {
            errors["page"] = ["validation.paging.page"];
        }

        if (query.PageSize is < 1 or > MaxPageSize)
        {
            errors["pageSize"] = ["validation.paging.pageSize"];
        }

        var sortField = query.Sort?.TrimStart('-');
        var sort = CaseSort.StartedOn;
        if (!string.IsNullOrEmpty(sortField) && !Sorts.TryGetValue(sortField, out sort))
        {
            errors["sort"] = ["validation.paging.sort"];
        }

        CaseStatus? status = null;
        if (!string.IsNullOrEmpty(query.Status))
        {
            if (Enum.TryParse<CaseStatus>(query.Status, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed))
            {
                status = parsed;
            }
            else
            {
                errors["status"] = ["validation.cases.status"];
            }
        }

        if (query.ClientName is { Length: > MaxFilterLength } || query.ServiceName is { Length: > MaxFilterLength })
        {
            errors["search"] = ["validation.paging.search"];
        }

        if (errors.Count > 0)
        {
            return Errors.Host.ValidationFailed(errors);
        }

        // Legacy: the newest first unless a sort is chosen.
        var descending = string.IsNullOrEmpty(query.Sort) || query.Sort.StartsWith('-');
        var employeeOnly = policy.IsEmployeeOnly;
        var scope = await policy.ScopeAsync(cancellationToken);
        await using var store = await data.OpenAsync(cancellationToken);
        var (items, total) = await store.PageAsync(
            new CaseFilter(
                scope,
                Text(query.ClientName),
                Text(query.ServiceName),
                query.ClientId,
                query.ServiceId,
                status,
                OnlyHeldOrUnspecialized: employeeOnly && !(query.ShowAll ?? false),
                IncludeCompleted: query.ShowCompleted ?? !employeeOnly,
                sort,
                descending,
                (query.Page - 1) * query.PageSize,
                query.PageSize),
            cancellationToken);

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        return new PagedResponse<CaseListItemResponse>(items.Select(row => ToResponse(row, today)).ToArray(), query.Page, query.PageSize, total);
    }

    public async Task<Result<CaseResponse>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        if (await store.FindAsync(id, readOnly: true, cancellationToken) is not { } @case)
        {
            return Errors.Cases.CaseNotFound();
        }

        var resource = await CaseResources.OfAsync(store, @case, cancellationToken);
        if (!await policy.CanSeeAsync(resource, cancellationToken))
        {
            return Errors.Cases.CaseNotFound();
        }

        var names = await store.NamesAsync(@case, cancellationToken);
        var history = @case.History;
        var payments = @case.Payments;
        var users = await store.UserNamesAsync(
            history.Select(change => change.ChangedByUserId).Concat(payments.Select(payment => payment.RecordedByUserId)).OfType<Guid>().Distinct().ToArray(),
            cancellationToken);
        CaseUserResponse? User(Guid? userId) => userId is { } known ? new CaseUserResponse(known, users.GetValueOrDefault(known, string.Empty)) : null;

        var canManage = await permissions.HasAsync(CasesPermissions.ManageCases, cancellationToken) && await policy.CanManageAsync(resource, cancellationToken);
        var canDelete = await permissions.HasAsync(CasesPermissions.DeleteCases, cancellationToken) && await policy.CanDeleteAsync(resource, cancellationToken);
        using var customFields = JsonDocument.Parse(@case.CustomFields);
        return new CaseResponse(
            @case.Id,
            @case.Number,
            new CaseClientResponse(@case.ClientId, names.ClientName),
            new CaseServiceResponse(@case.ServiceId, names.ServiceName, names.ServiceDescription, names.ServiceDurationDays),
            @case.SpecializationId is { } specialization ? new CaseSpecializationResponse(specialization, names.SpecializationName ?? string.Empty, names.SpecializationPrivate) : null,
            @case.Status.ToString(),
            @case.IsRejected,
            @case.IsActive,
            @case.Price,
            @case.Currency,
            @case.AmountPaid,
            @case.StartedOn,
            @case.DueOn,
            @case.ExpiresOn,
            @case.CompletedAt,
            customFields.RootElement.Clone(),
            history.Select(change => new CaseStatusChangeResponse(change.FromStatus?.ToString(), change.ToStatus.ToString(), change.ChangedAt, User(change.ChangedByUserId), change.Note)).ToArray(),
            payments.Select(payment => new CasePaymentResponse(payment.Id, payment.Amount, payment.PaidOn, payment.Note, payment.RecordedAt, User(payment.RecordedByUserId))).ToArray(),
            canManage,
            canDelete);
    }

    /// <summary>Legacy client badge: inactive, expired (expiry date passed) or active.</summary>
    public static string Validity(bool isActive, DateOnly? expiresOn, DateOnly today) =>
        !isActive ? "Inactive" : expiresOn is { } expires && expires < today ? "Expired" : "Active";

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static CaseListItemResponse ToResponse(CaseRow row, DateOnly today)
    {
        using var customFields = JsonDocument.Parse(row.CustomFields);
        return new CaseListItemResponse(
            row.Id,
            row.Number,
            new CaseClientResponse(row.ClientId, row.ClientName),
            new CaseServiceRefResponse(row.ServiceId, row.ServiceName),
            row.SpecializationId is { } specialization ? new CaseSpecializationResponse(specialization, row.SpecializationName ?? string.Empty, row.SpecializationPrivate) : null,
            row.Status.ToString(),
            row.IsRejected,
            Validity(row.IsActive, row.ExpiresOn, today),
            row.StartedOn,
            row.DueOn,
            row.ExpiresOn,
            row.Price,
            row.Currency,
            row.AmountPaid,
            customFields.RootElement.Clone());
    }
}
