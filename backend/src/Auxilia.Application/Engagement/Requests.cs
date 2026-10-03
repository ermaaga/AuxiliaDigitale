using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Engagement;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Realtime;
using Auxilia.Application.Directory.Public;
using Auxilia.Contracts.Common;
using Auxilia.Contracts.Engagement;
using Auxilia.Contracts.Realtime;
using Auxilia.Diagnostics;
using Auxilia.Domain.Engagement;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Application.Engagement;

/// <summary>
/// Requests of the current tenant (F15, Q16–Q18): clients ask the employee in charge or the office, employees ask the
/// office; the thread grows with replies of either side and is closed by a participant. Every change is told to the
/// other party (realtime <c>RequestChanged</c>; the persisted notifications come with B-19).
/// </summary>
public interface IRequestManager
{
    Task<Result<Guid>> CreateAsync(CreateRequestRequest request, CancellationToken cancellationToken);

    Task<Result> ReplyAsync(Guid id, ReplyToRequestRequest request, CancellationToken cancellationToken);

    Task<Result> CloseAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Soft delete (Administrators).</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);
}

public interface IRequestQueryService
{
    Task<Result<PagedResponse<RequestListItemResponse>>> ListAsync(RequestListQuery query, CancellationToken cancellationToken);

    /// <summary>The request with its thread; <c>AUX-17006</c> when the caller cannot see it.</summary>
    Task<Result<RequestResponse>> GetAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// F15: participants (sender, employee asked) see a request; Administrators see every request (the office ones by
/// default, Q16, all of them on request). Participants and Administrators reply and close while it is open; only
/// Administrators delete.
/// </summary>
internal sealed class RequestAccessPolicy(ICurrentUser currentUser)
{
    public bool IsAdministrator => Has(TenantRole.Administrator);

    public bool IsEmployee => Has(TenantRole.Employee);

    public bool IsClient => Has(TenantRole.Client);

    public Guid? Me => currentUser.UserId;

    public bool CanSee(Request request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return IsAdministrator || (Me is { } me && (request.SenderUserId == me || request.RecipientUserId == me));
    }

    public bool CanWrite(Request request) => request.Status != RequestStatus.Closed && CanSee(request);

    private bool Has(TenantRole role) => currentUser.ActorType == ActorType.User && currentUser.Roles.Contains(role);
}

internal sealed class RequestManager(
    IOperationRunner operations,
    IRequestDataFactory data,
    IClientDirectory clients,
    IAccessGuard guard,
    RequestAccessPolicy policy,
    IRealtimeNotifier notifier,
    TimeProvider clock) : IRequestManager
{
    public Task<Result<Guid>> CreateAsync(CreateRequestRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync<Guid>(Operations.Requests.CreateRequest, new { request.Type }, async scope =>
        {
            // Legacy: clients and employees write requests; the office answers them.
            var allowed = await guard.EnsureAsync(EngagementPermissions.ManageRequests, cancellationToken);
            if (allowed.IsFailure || policy.Me is not { } me || !(policy.IsClient || policy.IsEmployee))
            {
                return Result.Failure<Guid>(allowed.Error ?? Errors.Identity.PermissionDenied());
            }

            if (!Enum.TryParse<RequestType>(request.Type, ignoreCase: false, out var type) || !Enum.IsDefined(type))
            {
                type = (RequestType)(-1);
            }

            await using var store = await data.OpenAsync(cancellationToken);
            Guid? recipient = null;
            if (policy.IsClient && !policy.IsEmployee && (request.AskMyOperator ?? true)
                && await store.PersonOfUserAsync(me, cancellationToken) is { } person)
            {
                recipient = (await clients.FindAsync(person, cancellationToken))?.EmployeeUserId;
            }

            var opened = Request.Open(Guid.CreateVersion7(), me, recipient, type, request.Subject ?? string.Empty, request.Message ?? string.Empty, clock.GetUtcNow());
            if (opened.IsFailure)
            {
                return Result.Failure<Guid>(opened.Error!);
            }

            store.Add(opened.Value);
            await store.SaveChangesAsync(cancellationToken);
            scope.SetEntity(nameof(Request), opened.Value.Id);
            Tell(scope, opened.Value, "Created", fromSender: true);
            return opened.Value.Id;
        }, cancellationToken);
    }

    public Task<Result> ReplyAsync(Guid id, ReplyToRequestRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ChangeAsync(Operations.Requests.ReplyToRequest, id, EngagementPermissions.ManageRequests, "Replied",
            (loaded, me, now) => loaded.Reply(me, request.Message ?? string.Empty, now), cancellationToken);
    }

    public Task<Result> CloseAsync(Guid id, CancellationToken cancellationToken) =>
        ChangeAsync(Operations.Requests.CloseRequest, id, EngagementPermissions.ManageRequests, "Closed",
            (loaded, me, now) => loaded.Close(me, now), cancellationToken);

    public Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Requests.DeleteRequest, new { RequestId = id }, async _ =>
        {
            var allowed = await guard.EnsureAsync(EngagementPermissions.DeleteRequests, cancellationToken);
            if (allowed.IsFailure)
            {
                return allowed;
            }

            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindAsync(id, readOnly: false, cancellationToken) is not { } loaded || !policy.CanSee(loaded))
            {
                return Errors.Requests.RequestNotFound();
            }

            if (!policy.IsAdministrator)
            {
                return Errors.Identity.PermissionDenied();
            }

            store.Remove(loaded);
            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);

    private Task<Result> ChangeAsync(
        OperationDescriptor operation, Guid id, string permission, string change, Func<Request, Guid, DateTimeOffset, Result> apply, CancellationToken cancellationToken) =>
        operations.RunAsync(operation, new { RequestId = id }, async scope =>
        {
            var allowed = await guard.EnsureAsync(permission, cancellationToken);
            if (allowed.IsFailure || policy.Me is not { } me)
            {
                return allowed.IsFailure ? allowed : Errors.Identity.PermissionDenied();
            }

            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindAsync(id, readOnly: false, cancellationToken) is not { } loaded || !policy.CanSee(loaded))
            {
                return Errors.Requests.RequestNotFound();
            }

            var changed = apply(loaded, me, clock.GetUtcNow());
            if (changed.IsFailure)
            {
                return changed;
            }

            await store.SaveChangesAsync(cancellationToken);
            Tell(scope, loaded, change, fromSender: me == loaded.SenderUserId);
            return Result.Success();
        }, cancellationToken);

    /// <summary>
    /// The other party hears of the change after the commit: from the sender → the employee asked or every
    /// Administrator (office); from anybody else → the sender. Never the actor.
    /// </summary>
    private void Tell(IOperationScope scope, Request request, string change, bool fromSender)
    {
        var pushed = new RequestChangedEvent(request.Id, change, request.Subject);
        if (!fromSender)
        {
            if (request.SenderUserId != policy.Me)
            {
                scope.OnCommitted(ct => notifier.ToUserAsync(request.SenderUserId, RealtimeEvents.RequestChanged, pushed, ct));
            }
        }
        else if (request.RecipientUserId is { } recipient)
        {
            scope.OnCommitted(ct => notifier.ToUserAsync(recipient, RealtimeEvents.RequestChanged, pushed, ct));
        }
        else
        {
            scope.OnCommitted(ct => notifier.ToRoleAsync(TenantRole.Administrator, RealtimeEvents.RequestChanged, pushed, ct));
        }
    }
}

internal sealed class RequestQueryService(IRequestDataFactory data, RequestAccessPolicy policy, IPermissionAccess permissions) : IRequestQueryService
{
    public const int MaxPageSize = 100;

    private static readonly Dictionary<string, RequestSort> Sorts = new(StringComparer.Ordinal)
    {
        ["sentAt"] = RequestSort.SentAt,
        ["lastMessageAt"] = RequestSort.LastMessageAt,
        ["status"] = RequestSort.Status,
    };

    public async Task<Result<PagedResponse<RequestListItemResponse>>> ListAsync(RequestListQuery query, CancellationToken cancellationToken)
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
        var sort = RequestSort.SentAt;
        if (!string.IsNullOrEmpty(sortField) && !Sorts.TryGetValue(sortField, out sort))
        {
            errors["sort"] = ["validation.paging.sort"];
        }

        var status = Parse<RequestStatus>(query.Status, "status", errors);
        var type = Parse<RequestType>(query.Type, "type", errors);
        var box = query.Box ?? (policy.IsClient && !policy.IsAdministrator && !policy.IsEmployee ? "sent" : "received");
        if (box is not ("received" or "sent" or "all"))
        {
            errors["box"] = ["validation.requests.box"];
        }

        if (errors.Count > 0)
        {
            return Errors.Host.ValidationFailed(errors);
        }

        if (box == "all" && !policy.IsAdministrator)
        {
            return Errors.Identity.PermissionDenied();
        }

        var me = policy.Me;
        var scope = me is null
            ? RequestBox.None
            : box switch
            {
                "sent" => new RequestBox(null, false, me, false),
                "all" => new RequestBox(null, false, null, true),
                _ => new RequestBox(me, policy.IsAdministrator, null, false),
            };

        // Legacy: the newest first unless a sort is chosen.
        var descending = string.IsNullOrEmpty(query.Sort) || query.Sort.StartsWith('-');
        await using var store = await data.OpenAsync(cancellationToken);
        var (items, total) = await store.PageAsync(
            new RequestFilter(scope, status, type, sort, descending, (query.Page - 1) * query.PageSize, query.PageSize), cancellationToken);
        return new PagedResponse<RequestListItemResponse>(
            items.Select(row => new RequestListItemResponse(
                row.Id,
                row.Type.ToString(),
                row.Subject,
                row.Status.ToString(),
                new RequestUserResponse(row.SenderUserId, row.SenderName),
                row.RecipientUserId is { } recipient ? new RequestUserResponse(recipient, row.RecipientName ?? string.Empty) : null,
                row.SentAt,
                row.LastMessageAt,
                row.MessageCount)).ToArray(),
            query.Page,
            query.PageSize,
            total);
    }

    public async Task<Result<RequestResponse>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        if (await store.FindAsync(id, readOnly: true, cancellationToken) is not { } request || !policy.CanSee(request))
        {
            return Errors.Requests.RequestNotFound();
        }

        var messages = request.Messages;
        var names = await store.UserNamesAsync(
            messages.Select(message => message.AuthorUserId).Append(request.SenderUserId).Concat(request.RecipientUserId is { } r ? [r] : []).Distinct().ToArray(),
            cancellationToken);
        RequestUserResponse User(Guid userId) => new(userId, names.GetValueOrDefault(userId, string.Empty));

        var canWrite = policy.CanWrite(request) && await permissions.HasAsync(EngagementPermissions.ManageRequests, cancellationToken);
        var canDelete = policy.IsAdministrator && await permissions.HasAsync(EngagementPermissions.DeleteRequests, cancellationToken);
        return new RequestResponse(
            request.Id,
            request.Type.ToString(),
            request.Subject,
            request.Status.ToString(),
            User(request.SenderUserId),
            request.RecipientUserId is { } recipient ? User(recipient) : null,
            request.SentAt,
            request.LastMessageAt,
            request.ClosedAt,
            messages.Select(message => new RequestMessageResponse(message.Id, User(message.AuthorUserId), message.Body, message.SentAt, message.AuthorUserId == policy.Me)).ToArray(),
            canWrite,
            canWrite,
            canDelete);
    }

    private static TEnum? Parse<TEnum>(string? value, string field, Dictionary<string, string[]> errors)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (Enum.TryParse<TEnum>(value, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        errors[field] = [$"validation.requests.{field}"];
        return null;
    }
}
