using Auxilia.Domain.Engagement;

namespace Auxilia.Application.Abstractions.Engagement;

/// <summary>Which requests an inbox lists (already decided by the role): addressed to a user, to the office, sent by a user, or all.</summary>
public sealed record RequestBox(Guid? RecipientUserId, bool Office, Guid? SenderUserId, bool Everything)
{
    public static readonly RequestBox None = new(null, false, null, false);
}

/// <summary>Sortable columns of the inbox (legacy: created, status; default created descending).</summary>
public enum RequestSort
{
    SentAt,
    LastMessageAt,
    Status,
}

public sealed record RequestFilter(RequestBox Box, RequestStatus? Status, RequestType? Type, RequestSort Sort, bool Descending, int Skip, int Take);

public sealed record RequestRow(
    Guid Id,
    RequestType Type,
    string Subject,
    RequestStatus Status,
    Guid SenderUserId,
    string SenderName,
    Guid? RecipientUserId,
    string? RecipientName,
    DateTimeOffset SentAt,
    DateTimeOffset LastMessageAt,
    int MessageCount);

/// <summary>
/// Requests of the current tenant (F15), one unit of work (inside a write operation it joins the operation's
/// transaction). Deleted requests are never returned.
/// </summary>
public interface IRequestData : IAsyncDisposable
{
    Task<(IReadOnlyList<RequestRow> Items, int Total)> PageAsync(RequestFilter filter, CancellationToken cancellationToken);

    /// <summary>The request with its thread; tracked unless <paramref name="readOnly"/>.</summary>
    Task<Request?> FindAsync(Guid id, bool readOnly, CancellationToken cancellationToken);

    /// <summary>Names of the users (person first and last name), deleted people included.</summary>
    Task<IReadOnlyDictionary<Guid, string>> UserNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);

    /// <summary>The person of a user (a client's employee in charge).</summary>
    Task<Guid?> PersonOfUserAsync(Guid userId, CancellationToken cancellationToken);

    void Add(Request request);

    /// <summary>Soft delete.</summary>
    void Remove(Request request);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IRequestDataFactory
{
    Task<IRequestData> OpenAsync(CancellationToken cancellationToken);
}
