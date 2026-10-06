using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Domain;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Domain.Cases;

/// <summary>Where a case is (legacy <c>SubscriptionStatus</c>, F09): one step at a time, Completed is terminal.</summary>
public enum CaseStatus
{
    Inserted,
    InProgress,
    Sent,
    Completed,
}

/// <summary>What a case is opened with: the service snapshot is taken from the catalog (Q02: price of the service).</summary>
public sealed record CaseOpening(
    string Number,
    Guid ClientId,
    Guid ServiceId,
    decimal Price,
    string Currency,
    Guid? SpecializationId,
    DateOnly StartedOn,
    DateOnly? DueOn,
    string CustomFields);

/// <summary>How the legacy application left a case (legacy import E-03): applied as it is, without the workflow rules.</summary>
/// <param name="AmountPaid">Legacy <c>AmountPaid</c>: one payment (note <see cref="Case.LegacyPaymentNote"/>) when greater than zero.</param>
public sealed record LegacyCaseState(
    CaseStatus Status, bool IsRejected, bool IsActive, DateOnly? ExpiresOn, DateTimeOffset? CompletedAt, decimal AmountPaid, DateOnly PaidOn);

/// <summary>
/// A case of a client for a service (<c>cases.cases</c>, legacy <c>Subscription</c>, F09). It moves Inserted → InProgress
/// → Sent one step at a time (and back from InProgress or Sent); a sent case is completed with the amount received and
/// the outcome, and then never changes (terminal). The price is a snapshot of the service (Q02); money received is a
/// list of payments. <see cref="ExpiresOn"/> is empty until completion (D-07, Q03/Q04) and <see cref="DueOn"/> is an
/// optional target date. Every status change is kept in <c>case_status_history</c>. Deletes are soft.
/// </summary>
public sealed class Case : AggregateRoot<Guid>, IAuditable, ISoftDeletable
{
    public const int NumberMaxLength = 20;
    public const int NoteMaxLength = 500;

    /// <summary>Note of the payment that carries the legacy <c>AmountPaid</c> (E-03).</summary>
    public const string LegacyPaymentNote = "legacy";

    /// <summary>Oldest start date accepted (typing errors such as 0198).</summary>
    public static readonly DateOnly MinDate = new(1900, 1, 1);

    private readonly List<CaseStatusChange> history = [];
    private readonly List<CasePayment> payments = [];

    private Case(Guid id, CaseOpening opening)
        : base(id)
    {
        Number = opening.Number;
        ClientId = opening.ClientId;
        ServiceId = opening.ServiceId;
        Price = opening.Price;
        Currency = opening.Currency;
        SpecializationId = opening.SpecializationId;
        StartedOn = opening.StartedOn;
        DueOn = opening.DueOn;
        CustomFields = opening.CustomFields;
        Status = CaseStatus.Inserted;
        IsActive = true;
    }

    private Case()
    {
        Number = Currency = CustomFields = string.Empty;
    }

    /// <summary><c>{year}-{sequence}</c>, unique in the tenant.</summary>
    public string Number { get; private set; }

    /// <summary>The client (its person id, Directory).</summary>
    public Guid ClientId { get; private set; }

    public Guid ServiceId { get; private set; }

    /// <summary>An Employee specialization (default: the service's); a private one makes the case private (F10, D-04).</summary>
    public Guid? SpecializationId { get; private set; }

    public CaseStatus Status { get; private set; }

    /// <summary>The outcome of a completed case (legacy <c>IsRejected</c>).</summary>
    public bool IsRejected { get; private set; }

    /// <summary>Legacy <c>IsActive</c>: an expired case becomes inactive (manual job F11).</summary>
    public bool IsActive { get; private set; }

    /// <summary>The price of the service when the case was opened.</summary>
    public decimal Price { get; private set; }

    public string Currency { get; private set; }

    public DateOnly StartedOn { get; private set; }

    public DateOnly? DueOn { get; private set; }

    /// <summary>Set to the completion day (legacy <c>EndDate</c>).</summary>
    public DateOnly? ExpiresOn { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>The day the client was last told the case is expiring (F11, Q14: once a day).</summary>
    public DateOnly? ExpiryNotifiedOn { get; private set; }

    /// <summary>The date the case ends for the expiry job and reminders: the expiry, else the target date.</summary>
    public DateOnly? EndsOn => ExpiresOn ?? DueOn;

    /// <summary>Custom field values (JSON object, already validated, F20).</summary>
    public string CustomFields { get; private set; }

    /// <summary>The timeline, in the order of the changes.</summary>
    public IReadOnlyList<CaseStatusChange> History => history.OrderBy(change => change.Sequence).ToArray();

    public IReadOnlyList<CasePayment> Payments => payments.OrderBy(payment => payment.PaidOn).ThenBy(payment => payment.RecordedAt).ThenBy(payment => payment.Id).ToArray();

    public decimal AmountPaid => payments.Sum(payment => payment.Amount);

    public static Result<Case> Open(Guid id, CaseOpening opening, Guid? actorUserId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(opening);
        ArgumentException.ThrowIfNullOrWhiteSpace(opening.Number);

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (opening.StartedOn < MinDate)
        {
            errors["startedOn"] = ["validation.cases.startedOn"];
        }

        if (opening.DueOn is { } dueOn && dueOn < opening.StartedOn)
        {
            errors["dueOn"] = ["validation.cases.dueOn"];
        }

        if (errors.Count > 0)
        {
            return Errors.Cases.CaseInvalid(errors);
        }

        var opened = new Case(id, opening);
        opened.history.Add(new CaseStatusChange(Guid.CreateVersion7(), id, 1, null, CaseStatus.Inserted, now, actorUserId, null));
        return opened;
    }

    /// <summary>
    /// Legacy import (E-03): a case in the state the legacy application left it, with one history row for that state and
    /// the legacy amount as one payment. Amounts must have at most two decimals.
    /// </summary>
    public static Result<Case> ImportLegacy(Guid id, CaseOpening opening, LegacyCaseState state, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!Enum.IsDefined(state.Status))
        {
            return Errors.Cases.CaseInvalid("status", "validation.cases.status");
        }

        if (!IsAmount(state.AmountPaid, allowZero: true))
        {
            return Errors.Cases.CaseInvalid("amountPaid", "validation.cases.amount");
        }

        var opened = Open(id, opening, actorUserId: null, at);
        if (opened.IsFailure)
        {
            return opened;
        }

        var imported = opened.Value;
        imported.history.Clear();
        imported.history.Add(new CaseStatusChange(Guid.CreateVersion7(), id, 1, null, state.Status, at, null, null));
        imported.Status = state.Status;
        imported.ApplyLegacyState(state, at);
        return imported;
    }

    /// <summary>
    /// Legacy import (E-03), a later run: the legacy state wins (the legacy application is still the system in use until
    /// the cutover). A different status adds a history row; the legacy payment follows the legacy amount.
    /// </summary>
    /// <returns>Whether something changed.</returns>
    public bool ApplyLegacyState(LegacyCaseState state, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(state);

        var changed = false;
        if (Status != state.Status)
        {
            history.Add(new CaseStatusChange(Guid.CreateVersion7(), Id, history.Count + 1, Status, state.Status, at, null, null));
            Status = state.Status;
            changed = true;
        }

        changed |= IsRejected != state.IsRejected || IsActive != state.IsActive || ExpiresOn != state.ExpiresOn || CompletedAt != state.CompletedAt;
        IsRejected = state.IsRejected;
        IsActive = state.IsActive;
        ExpiresOn = state.ExpiresOn;
        CompletedAt = state.CompletedAt;

        var legacy = payments.Where(payment => payment.Note == LegacyPaymentNote).ToArray();
        if (legacy.Sum(payment => payment.Amount) != state.AmountPaid || (state.AmountPaid > 0 && legacy.Length != 1))
        {
            payments.RemoveAll(payment => payment.Note == LegacyPaymentNote);
            if (state.AmountPaid > 0)
            {
                payments.Add(new CasePayment(Guid.CreateVersion7(), Id, state.AmountPaid, state.PaidOn, LegacyPaymentNote, at, null));
            }

            changed = true;
        }

        return changed;
    }

    /// <summary>A case still counting for the client's status (Q03): active, not completed and not expired.</summary>
    public static bool CountsAsOpen(bool isActive, CaseStatus status, DateOnly? expiresOn, DateOnly today) =>
        isActive && status != CaseStatus.Completed && (expiresOn is null || expiresOn >= today);

    /// <summary>Inserted → InProgress → Sent; from Sent only <see cref="Complete"/> goes on.</summary>
    public Result Advance(Guid? actorUserId, string? note, DateTimeOffset now) => Status switch
    {
        CaseStatus.Inserted => Move(CaseStatus.InProgress, actorUserId, note, now),
        CaseStatus.InProgress => Move(CaseStatus.Sent, actorUserId, note, now),
        CaseStatus.Sent => Errors.Cases.CaseCompletionRequired(),
        _ => Errors.Cases.CaseIsCompleted(),
    };

    /// <summary>InProgress → Inserted, Sent → InProgress.</summary>
    public Result GoBack(Guid? actorUserId, string? note, DateTimeOffset now) => Status switch
    {
        CaseStatus.InProgress => Move(CaseStatus.Inserted, actorUserId, note, now),
        CaseStatus.Sent => Move(CaseStatus.InProgress, actorUserId, note, now),
        CaseStatus.Inserted => Errors.Cases.CaseCannotGoBack(),
        _ => Errors.Cases.CaseIsCompleted(),
    };

    /// <summary>
    /// Sent → Completed: <paramref name="amountReceived"/> (≥ 0, two decimals) is recorded as a payment when not zero,
    /// the case expires today and keeps the outcome.
    /// </summary>
    public Result Complete(decimal amountReceived, bool rejected, Guid? actorUserId, string? note, DateTimeOffset now)
    {
        if (Status == CaseStatus.Completed)
        {
            return Errors.Cases.CaseIsCompleted();
        }

        if (Status != CaseStatus.Sent)
        {
            return Errors.Cases.CaseInvalid("status", "validation.cases.completeFromSent");
        }

        if (!IsAmount(amountReceived, allowZero: true))
        {
            return Errors.Cases.CaseInvalid("amountPaid", "validation.cases.amount");
        }

        if (CheckNote(note) is { IsFailure: true } invalid)
        {
            return invalid;
        }

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        if (amountReceived > 0)
        {
            payments.Add(new CasePayment(Guid.CreateVersion7(), Id, amountReceived, today, Text(note), now, actorUserId));
        }

        IsRejected = rejected;
        ExpiresOn = today;
        CompletedAt = now;
        return Move(CaseStatus.Completed, actorUserId, note, now);
    }

    /// <summary>Money received before completion (&gt; 0, two decimals, not in the future).</summary>
    public Result AddPayment(decimal amount, DateOnly paidOn, string? note, Guid? actorUserId, DateTimeOffset now)
    {
        if (Status == CaseStatus.Completed)
        {
            return Errors.Cases.CaseIsCompleted();
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (!IsAmount(amount, allowZero: false))
        {
            errors["amount"] = ["validation.cases.amount"];
        }

        if (paidOn < MinDate || paidOn > DateOnly.FromDateTime(now.UtcDateTime))
        {
            errors["paidOn"] = ["validation.cases.paidOn"];
        }

        if (note?.Trim().Length > NoteMaxLength)
        {
            errors["note"] = ["validation.cases.note"];
        }

        if (errors.Count > 0)
        {
            return Errors.Cases.CaseInvalid(errors);
        }

        payments.Add(new CasePayment(Guid.CreateVersion7(), Id, amount, paidOn, Text(note), now, actorUserId));
        return Result.Success();
    }

    /// <summary>The expiry job (F11): an active case whose expiry date has passed becomes inactive.</summary>
    /// <returns>Whether it changed.</returns>
    public bool ExpireIfDue(DateOnly today)
    {
        if (!IsActive || ExpiresOn is not { } expires || expires > today)
        {
            return false;
        }

        IsActive = false;
        return true;
    }

    /// <summary>Records that the client was told today; false when it already was (one "expiring" notice a day).</summary>
    public bool MarkExpiryNotified(DateOnly today)
    {
        if (ExpiryNotifiedOn == today)
        {
            return false;
        }

        ExpiryNotifiedOn = today;
        return true;
    }

    /// <summary>Due date and custom fields of a case not completed yet.</summary>
    public Result Update(DateOnly? dueOn, string customFields)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customFields);

        if (Status == CaseStatus.Completed)
        {
            return Errors.Cases.CaseIsCompleted();
        }

        if (dueOn is { } due && due < StartedOn)
        {
            return Errors.Cases.CaseInvalid("dueOn", "validation.cases.dueOn");
        }

        DueOn = dueOn;
        CustomFields = customFields;
        return Result.Success();
    }

    private static bool IsAmount(decimal amount, bool allowZero) =>
        (allowZero ? amount >= 0 : amount > 0) && amount <= Service.MaxPrice && decimal.Round(amount, 2) == amount;

    private static Result CheckNote(string? note) =>
        note?.Trim().Length > NoteMaxLength ? Errors.Cases.CaseInvalid("note", "validation.cases.note") : Result.Success();

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private Result Move(CaseStatus to, Guid? actorUserId, string? note, DateTimeOffset now)
    {
        if (CheckNote(note) is { IsFailure: true } invalid)
        {
            return invalid;
        }

        history.Add(new CaseStatusChange(Guid.CreateVersion7(), Id, history.Count + 1, Status, to, now, actorUserId, Text(note)));
        Status = to;
        return Result.Success();
    }
}

/// <summary>A status change of a case (<c>cases.case_status_history</c>): the case timeline.</summary>
public sealed class CaseStatusChange : Entity<Guid>
{
    internal CaseStatusChange(Guid id, Guid caseId, int sequence, CaseStatus? fromStatus, CaseStatus toStatus, DateTimeOffset changedAt, Guid? changedByUserId, string? note)
        : base(id)
    {
        CaseId = caseId;
        Sequence = sequence;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        ChangedAt = changedAt;
        ChangedByUserId = changedByUserId;
        Note = note;
    }

    private CaseStatusChange()
    {
    }

    public Guid CaseId { get; private set; }

    /// <summary>1 for the opening, then one more for each change (unique per case).</summary>
    public int Sequence { get; private set; }

    /// <summary><c>null</c> for the opening.</summary>
    public CaseStatus? FromStatus { get; private set; }

    public CaseStatus ToStatus { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }

    public Guid? ChangedByUserId { get; private set; }

    public string? Note { get; private set; }
}

/// <summary>Money received for a case (<c>cases.case_payments</c>, Q02).</summary>
public sealed class CasePayment : Entity<Guid>
{
    internal CasePayment(Guid id, Guid caseId, decimal amount, DateOnly paidOn, string? note, DateTimeOffset recordedAt, Guid? recordedByUserId)
        : base(id)
    {
        CaseId = caseId;
        Amount = amount;
        PaidOn = paidOn;
        Note = note;
        RecordedAt = recordedAt;
        RecordedByUserId = recordedByUserId;
    }

    private CasePayment()
    {
    }

    public Guid CaseId { get; private set; }

    public decimal Amount { get; private set; }

    public DateOnly PaidOn { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public Guid? RecordedByUserId { get; private set; }
}
