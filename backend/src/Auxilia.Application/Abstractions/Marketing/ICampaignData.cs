using Auxilia.Domain.Marketing;

namespace Auxilia.Application.Abstractions.Marketing;

public sealed record EmailTemplateRow(Guid Id, string Name, string Language, string Subject, DateTimeOffset UpdatedAt, int CampaignCount);

public sealed record CampaignRow(
    Guid Id, string Name, Guid TemplateId, string TemplateName, Guid? SegmentId, Guid? ListId, string? AudienceName, CampaignStatus Status,
    int RecipientCount, int SentCount, int FailedCount, int ExcludedCount, string? ErrorCode, string? ErrorMessage,
    DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt);

public sealed record CampaignRecipientRow(
    Guid PersonId, string FullName, string? Email, CampaignRecipientStatus Status, ExclusionReason? Exclusion, string? ErrorCode, DateTimeOffset? ProcessedAt);

public sealed record SuppressionRow(Guid Id, string Email, string? Reason, DateTimeOffset SuppressedAt);

/// <summary>E-mail templates, campaigns, recipients and suppressions of the current tenant (N01, M-03), one unit of work.</summary>
public interface ICampaignData : IAsyncDisposable
{
    Task<IReadOnlyList<EmailTemplateRow>> TemplatesAsync(CancellationToken cancellationToken);

    /// <summary>Tracked.</summary>
    Task<EmailTemplate?> FindTemplateAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> TemplateNameTakenAsync(string name, Guid? exceptId, CancellationToken cancellationToken);

    Task<bool> TemplateInUseAsync(Guid id, CancellationToken cancellationToken);

    Task<(IReadOnlyList<CampaignRow> Items, int Total)> CampaignsAsync(int skip, int take, CancellationToken cancellationToken);

    Task<CampaignRow?> CampaignAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Tracked.</summary>
    Task<Campaign?> FindCampaignAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> SegmentExistsAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> ListExistsAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> HasRecipientsAsync(Guid campaignId, CancellationToken cancellationToken);

    /// <summary>Tracked pending recipients, at most <paramref name="take"/>.</summary>
    Task<IReadOnlyList<CampaignRecipient>> PendingAsync(Guid campaignId, int take, CancellationToken cancellationToken);

    Task<(IReadOnlyList<CampaignRecipientRow> Items, int Total)> RecipientsAsync(
        Guid campaignId, CampaignRecipientStatus? status, int skip, int take, CancellationToken cancellationToken);

    /// <summary>Marks every pending recipient excluded (cancelled campaign).</summary>
    Task<int> ExcludePendingAsync(Guid campaignId, ExclusionReason reason, DateTimeOffset now, CancellationToken cancellationToken);

    Task<IReadOnlyList<SuppressionRow>> SuppressionsAsync(CancellationToken cancellationToken);

    /// <summary>The suppressed addresses among <paramref name="emails"/> (case-insensitive).</summary>
    Task<IReadOnlySet<string>> SuppressedAsync(IReadOnlyCollection<string> emails, CancellationToken cancellationToken);

    /// <summary>Tracked.</summary>
    Task<Suppression?> FindSuppressionAsync(Guid id, CancellationToken cancellationToken);

    void Add(EmailTemplate template);

    void Remove(EmailTemplate template);

    void Add(Campaign campaign);

    void Remove(Campaign campaign);

    void AddRecipients(IEnumerable<CampaignRecipient> recipients);

    void Add(Suppression suppression);

    void Remove(Suppression suppression);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface ICampaignDataFactory
{
    Task<ICampaignData> OpenAsync(CancellationToken cancellationToken);
}
