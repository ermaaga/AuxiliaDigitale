using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Domain;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Domain.Engagement;

/// <summary>What a request is about (Q18: the legacy UI types plus <c>Appointment</c> from the entity).</summary>
public enum RequestType
{
    Information,
    General,
    Support,
    Appointment,
}

/// <summary>
/// Where a request is (F15): Pending (waiting for an answer), Responded (the other party answered) and Closed
/// (terminal). A follow-up of the sender makes it Pending again.
/// </summary>
public enum RequestStatus
{
    Pending,
    Responded,
    Closed,
}

/// <summary>
/// A request with its thread (<c>engagement.requests</c>, legacy <c>Request</c>, F15): a client asks the employee in
/// charge or the office, an employee asks the office (<see cref="RecipientUserId"/> <c>null</c> = the Administrators).
/// The first message is the original text; replies are appended (legacy <c>Response</c> becomes message #2). Deletes
/// are soft.
/// </summary>
public sealed class Request : AggregateRoot<Guid>, IAuditable, ISoftDeletable
{
    public const int SubjectMaxLength = 200;
    public const int BodyMaxLength = 4000;

    private readonly List<RequestMessage> messages = [];

    private Request(Guid id, Guid senderUserId, Guid? recipientUserId, RequestType type, string subject, DateTimeOffset now)
        : base(id)
    {
        SenderUserId = senderUserId;
        RecipientUserId = recipientUserId;
        Type = type;
        Subject = subject;
        Status = RequestStatus.Pending;
        SentAt = now;
        LastMessageAt = now;
    }

    private Request()
    {
        Subject = string.Empty;
    }

    public Guid SenderUserId { get; private set; }

    /// <summary>The employee asked; <c>null</c> for the office (every Administrator).</summary>
    public Guid? RecipientUserId { get; private set; }

    public RequestType Type { get; private set; }

    public string Subject { get; private set; }

    public RequestStatus Status { get; private set; }

    public DateTimeOffset SentAt { get; private set; }

    /// <summary>When the thread last moved (sort of the inbox).</summary>
    public DateTimeOffset LastMessageAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public Guid? ClosedByUserId { get; private set; }

    /// <summary>The thread, oldest first.</summary>
    public IReadOnlyList<RequestMessage> Messages => messages.OrderBy(message => message.Sequence).ToArray();

    public static Result<Request> Open(Guid id, Guid senderUserId, Guid? recipientUserId, RequestType type, string subject, string body, DateTimeOffset now)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (!Enum.IsDefined(type))
        {
            errors["type"] = ["validation.requests.type"];
        }

        if (string.IsNullOrWhiteSpace(subject) || subject.Trim().Length > SubjectMaxLength)
        {
            errors["subject"] = ["validation.requests.subject"];
        }

        if (!IsBody(body))
        {
            errors["message"] = ["validation.requests.message"];
        }

        if (errors.Count > 0)
        {
            return Errors.Requests.RequestInvalid(errors);
        }

        var request = new Request(id, senderUserId, recipientUserId, type, subject.Trim(), now);
        request.messages.Add(new RequestMessage(Guid.CreateVersion7(), id, 1, senderUserId, body.Trim(), now));
        return request;
    }

    /// <summary>
    /// Appends a message: from the other party the request becomes Responded, from the sender (a follow-up) Pending
    /// again. A closed request takes no more messages.
    /// </summary>
    public Result Reply(Guid authorUserId, string body, DateTimeOffset now)
    {
        if (Status == RequestStatus.Closed)
        {
            return Errors.Requests.RequestIsClosed();
        }

        if (!IsBody(body))
        {
            return Errors.Requests.RequestInvalid(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["message"] = ["validation.requests.message"] });
        }

        messages.Add(new RequestMessage(Guid.CreateVersion7(), Id, messages.Count + 1, authorUserId, body.Trim(), now));
        Status = authorUserId == SenderUserId ? RequestStatus.Pending : RequestStatus.Responded;
        LastMessageAt = now;
        return Result.Success();
    }

    /// <summary>Closes the thread (terminal).</summary>
    public Result Close(Guid? actorUserId, DateTimeOffset now)
    {
        if (Status == RequestStatus.Closed)
        {
            return Errors.Requests.RequestIsClosed();
        }

        Status = RequestStatus.Closed;
        ClosedAt = now;
        ClosedByUserId = actorUserId;
        return Result.Success();
    }

    private static bool IsBody(string? body) => !string.IsNullOrWhiteSpace(body) && body.Trim().Length <= BodyMaxLength;
}

/// <summary>A message of a request thread (<c>engagement.request_messages</c>).</summary>
public sealed class RequestMessage : Entity<Guid>
{
    internal RequestMessage(Guid id, Guid requestId, int sequence, Guid authorUserId, string body, DateTimeOffset sentAt)
        : base(id)
    {
        RequestId = requestId;
        Sequence = sequence;
        AuthorUserId = authorUserId;
        Body = body;
        SentAt = sentAt;
    }

    private RequestMessage()
    {
        Body = string.Empty;
    }

    public Guid RequestId { get; private set; }

    /// <summary>1 for the original text, then one more for each reply (unique per request).</summary>
    public int Sequence { get; private set; }

    public Guid AuthorUserId { get; private set; }

    public string Body { get; private set; }

    public DateTimeOffset SentAt { get; private set; }
}
