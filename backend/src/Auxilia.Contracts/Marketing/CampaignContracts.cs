namespace Auxilia.Contracts.Marketing;

/// <summary>An e-mail template (N01): Liquid placeholders <c>firstName</c>, <c>lastName</c>, <c>fullName</c>, <c>tenantName</c>.</summary>
public sealed record SaveEmailTemplateRequest(string Name, string Language, string Subject, string Body);

public sealed record EmailTemplateListItemResponse(Guid Id, string Name, string Language, string Subject, DateTimeOffset UpdatedAt, int CampaignCount);

public sealed record EmailTemplateResponse(Guid Id, string Name, string Language, string Subject, string Body);

/// <summary>Renders a template for a client of the tenant, or for a sample person when none is given.</summary>
public sealed record PreviewEmailTemplateRequest(Guid? ClientId);

public sealed record EmailPreviewResponse(string Subject, string Body);

/// <summary>Sends the template, rendered for a sample person, to any address (purpose Marketing).</summary>
public sealed record TestEmailTemplateRequest(string Email);

/// <summary>A campaign: exactly one of <c>segmentId</c> and <c>listId</c>.</summary>
public sealed record SaveCampaignRequest(string Name, Guid TemplateId, Guid? SegmentId, Guid? ListId);

/// <param name="Status"><c>Draft</c>, <c>Sending</c>, <c>Sent</c>, <c>Cancelled</c> or <c>Failed</c>.</param>
public sealed record CampaignResponse(
    Guid Id,
    string Name,
    Guid TemplateId,
    string TemplateName,
    Guid? SegmentId,
    Guid? ListId,
    string? AudienceName,
    string Status,
    int RecipientCount,
    int SentCount,
    int FailedCount,
    int ExcludedCount,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);

/// <summary>Before "send now": the clients of the audience now (the exclusions are applied when sending).</summary>
public sealed record CampaignAudienceResponse(int Count);

/// <param name="Status"><c>Pending</c>, <c>Sent</c>, <c>Failed</c> or <c>Excluded</c>.</param>
/// <param name="Exclusion"><c>NoConsent</c>, <c>NoEmail</c>, <c>Suppressed</c> or <c>Cancelled</c>.</param>
public sealed record CampaignRecipientResponse(Guid ClientId, string FullName, string? Email, string Status, string? Exclusion, string? ErrorCode, DateTimeOffset? ProcessedAt);

public sealed record AddSuppressionRequest(string Email, string? Reason);

public sealed record SuppressionResponse(Guid Id, string Email, string? Reason, DateTimeOffset SuppressedAt);
