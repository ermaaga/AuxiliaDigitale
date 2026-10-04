using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Domain;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Domain.Marketing;

/// <summary>
/// An e-mail template of the campaigns (<c>marketing.email_templates</c>, N01): a name, the language it is written
/// in, the subject and the HTML body with Liquid placeholders (<c>firstName</c>, <c>lastName</c>, <c>fullName</c>, <c>tenantName</c>).
/// </summary>
public sealed class EmailTemplate : AggregateRoot<Guid>, IAuditable
{
    public const int NameMaxLength = 100;
    public const int SubjectMaxLength = 300;
    public const int BodyMaxLength = 100_000;
    public const int LanguageMaxLength = 10;

    public EmailTemplate(Guid id)
        : base(id)
    {
        Name = Language = Subject = Body = string.Empty;
    }

    private EmailTemplate()
    {
        Name = Language = Subject = Body = string.Empty;
    }

    public string Name { get; private set; }

    public string Language { get; private set; }

    public string Subject { get; private set; }

    public string Body { get; private set; }

    /// <param name="isValidTemplate">Whether a text parses as a template (the Liquid renderer).</param>
    public Result Update(string? name, string? language, string? subject, string? body, Func<string, bool> isValidTemplate)
    {
        ArgumentNullException.ThrowIfNull(isValidTemplate);

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var cleanName = name?.Trim() ?? string.Empty;
        if (cleanName.Length is 0 or > NameMaxLength)
        {
            errors["name"] = ["validation.templates.name"];
        }

        var cleanLanguage = language?.Trim() ?? string.Empty;
        if (cleanLanguage.Length is 0 or > LanguageMaxLength)
        {
            errors["language"] = ["validation.templates.language"];
        }

        var cleanSubject = subject?.Trim() ?? string.Empty;
        if (cleanSubject.Length is 0 or > SubjectMaxLength || !isValidTemplate(cleanSubject))
        {
            errors["subject"] = ["validation.templates.subject"];
        }

        var cleanBody = body ?? string.Empty;
        if (string.IsNullOrWhiteSpace(cleanBody) || cleanBody.Length > BodyMaxLength || !isValidTemplate(cleanBody))
        {
            errors["body"] = ["validation.templates.body"];
        }

        if (errors.Count > 0)
        {
            return Errors.Marketing.TemplateInvalid(errors);
        }

        Name = cleanName;
        Language = cleanLanguage;
        Subject = cleanSubject;
        Body = cleanBody;
        return Result.Success();
    }
}

public enum CampaignStatus
{
    Draft,
    Sending,
    Sent,
    Cancelled,
    Failed,
}

/// <summary>
/// An e-mail campaign (<c>marketing.campaigns</c>, N01): a template and an audience (a segment or a static list).
/// Draft → Sending ("send now") → Sent, or Cancelled, or Failed; sent once, never twice. <see cref="ScheduledAt"/> is
/// stored but unused (D-15: no scheduler).
/// </summary>
public sealed class Campaign : AggregateRoot<Guid>, IAuditable
{
    public const int NameMaxLength = 100;
    public const int ErrorMaxLength = 500;

    public Campaign(Guid id)
        : base(id)
    {
        Name = string.Empty;
        Status = CampaignStatus.Draft;
    }

    private Campaign()
    {
        Name = string.Empty;
    }

    public string Name { get; private set; }

    public Guid TemplateId { get; private set; }

    public Guid? SegmentId { get; private set; }

    public Guid? ListId { get; private set; }

    public CampaignStatus Status { get; private set; }

    public DateTimeOffset? ScheduledAt { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public Guid? SentByUserId { get; private set; }

    public int RecipientCount { get; private set; }

    public int SentCount { get; private set; }

    public int FailedCount { get; private set; }

    public int ExcludedCount { get; private set; }

    public string? ErrorCode { get; private set; }

    public string? ErrorMessage { get; private set; }

    /// <summary>Name, template and audience (exactly one of segment and list); only while a draft.</summary>
    public Result Update(string? name, Guid templateId, Guid? segmentId, Guid? listId)
    {
        if (Status != CampaignStatus.Draft)
        {
            return Errors.Marketing.CampaignNotDraft(Status.ToString());
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var cleanName = name?.Trim() ?? string.Empty;
        if (cleanName.Length is 0 or > NameMaxLength)
        {
            errors["name"] = ["validation.campaigns.name"];
        }

        if ((segmentId is null) == (listId is null))
        {
            errors["audience"] = ["validation.campaigns.audience"];
        }

        if (errors.Count > 0)
        {
            return Errors.Marketing.CampaignInvalid(errors);
        }

        Name = cleanName;
        TemplateId = templateId;
        SegmentId = segmentId;
        ListId = listId;
        return Result.Success();
    }

    /// <summary>"Send now": the Worker takes it from here.</summary>
    public Result Send(Guid? userId, DateTimeOffset now)
    {
        if (Status != CampaignStatus.Draft)
        {
            return Errors.Marketing.CampaignNotDraft(Status.ToString());
        }

        Status = CampaignStatus.Sending;
        SentByUserId = userId;
        StartedAt = now;
        return Result.Success();
    }

    /// <summary>The recipients were snapshotted: how many, and how many were excluded at once.</summary>
    public void Snapshot(int recipients, int excluded)
    {
        RecipientCount = recipients;
        ExcludedCount = excluded;
    }

    public void RecordSent(int sent, int failed)
    {
        SentCount += sent;
        FailedCount += failed;
    }

    public void Complete(DateTimeOffset now)
    {
        if (Status == CampaignStatus.Sending)
        {
            Status = CampaignStatus.Sent;
            CompletedAt = now;
        }
    }

    public void Fail(string errorCode, string message, DateTimeOffset now)
    {
        Status = CampaignStatus.Failed;
        ErrorCode = errorCode;
        ErrorMessage = message.Length > ErrorMaxLength ? message[..ErrorMaxLength] : message;
        CompletedAt = now;
    }

    /// <summary>A draft, or a campaign still sending (the recipients not sent yet are left out).</summary>
    public Result Cancel(DateTimeOffset now)
    {
        if (Status is not (CampaignStatus.Draft or CampaignStatus.Sending))
        {
            return Errors.Marketing.CampaignNotDraft(Status.ToString());
        }

        Status = CampaignStatus.Cancelled;
        CompletedAt = now;
        return Result.Success();
    }
}

public enum CampaignRecipientStatus
{
    Pending,
    Sent,
    Failed,
    Excluded,
}

/// <summary>Why a client of the audience does not receive the campaign.</summary>
public enum ExclusionReason
{
    NoConsent,
    NoEmail,
    Suppressed,
    Cancelled,
}

/// <summary>
/// A recipient of a campaign (<c>marketing.campaign_recipients</c>): the snapshot of the audience at "send now", one
/// row per client (never two e-mails to the same client), then sent, failed or excluded with the reason.
/// </summary>
public sealed class CampaignRecipient
{
    public CampaignRecipient(Guid campaignId, Guid personId, string? email, ExclusionReason? exclusion)
    {
        CampaignId = campaignId;
        PersonId = personId;
        Email = email;
        Status = exclusion is null ? CampaignRecipientStatus.Pending : CampaignRecipientStatus.Excluded;
        Exclusion = exclusion;
    }

    private CampaignRecipient()
    {
    }

    public Guid CampaignId { get; private set; }

    public Guid PersonId { get; private set; }

    public string? Email { get; private set; }

    public CampaignRecipientStatus Status { get; private set; }

    public ExclusionReason? Exclusion { get; private set; }

    public Guid? OutboundMessageId { get; private set; }

    public string? ErrorCode { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public void Sent(Guid outboundMessageId, DateTimeOffset now)
    {
        Status = CampaignRecipientStatus.Sent;
        OutboundMessageId = outboundMessageId;
        ProcessedAt = now;
    }

    public void Failed(string errorCode, DateTimeOffset now)
    {
        Status = CampaignRecipientStatus.Failed;
        ErrorCode = errorCode;
        ProcessedAt = now;
    }

    public void Exclude(ExclusionReason reason, DateTimeOffset now)
    {
        Status = CampaignRecipientStatus.Excluded;
        Exclusion = reason;
        ProcessedAt = now;
    }
}

/// <summary>
/// An address marketing never writes to (<c>marketing.suppressions</c>, N01): added by staff (the unsubscribe page is
/// deferred, D-24); transactional e-mails still go.
/// </summary>
public sealed class Suppression : AggregateRoot<Guid>, IAuditable
{
    public const int EmailMaxLength = 320;
    public const int ReasonMaxLength = 200;

    private Suppression(Guid id, string email, string? reason, DateTimeOffset now)
        : base(id)
    {
        Email = email;
        Reason = reason;
        SuppressedAt = now;
    }

    private Suppression()
    {
        Email = string.Empty;
    }

    public string Email { get; private set; }

    public string? Reason { get; private set; }

    public DateTimeOffset SuppressedAt { get; private set; }

    /// <summary>The address in lower case (compared case-insensitively).</summary>
    public static Result<Suppression> Create(Guid id, string? email, string? reason, DateTimeOffset now)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var address = email?.Trim() ?? string.Empty;
        if (address.Length is 0 or > EmailMaxLength || !address.Contains('@', StringComparison.Ordinal))
        {
            errors["email"] = ["validation.suppressions.email"];
        }

        var text = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (text is { Length: > ReasonMaxLength })
        {
            errors["reason"] = ["validation.suppressions.reason"];
        }

        return errors.Count > 0 ? Errors.Marketing.SuppressionInvalid(errors) : new Suppression(id, address, text, now);
    }
}
