using Auxilia.Application.Abstractions.Marketing;
using Auxilia.Application.Abstractions.Persistence;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Marketing;
using Auxilia.Persistence.Tenant.Conventions;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Tenant.Marketing;

internal sealed class CampaignDataFactory(ITenantDbContextFactory databases) : ICampaignDataFactory
{
    public async Task<ICampaignData> OpenAsync(CancellationToken cancellationToken) => new CampaignData(await databases.CreateAsync(cancellationToken));
}

/// <inheritdoc cref="ICampaignData"/>
#pragma warning disable CA1304, CA1311 // ToUpper() is translated to SQL upper(): no culture involved.
internal sealed class CampaignData(ITenantDbContext db) : ICampaignData
{
    public async Task<IReadOnlyList<EmailTemplateRow>> TemplatesAsync(CancellationToken cancellationToken)
    {
        var campaigns = db.Set<Campaign>();
        return await db.Set<EmailTemplate>().AsNoTracking()
            .OrderBy(template => template.Name)
            .Select(template => new EmailTemplateRow(
                template.Id, template.Name, template.Language, template.Subject,
                EF.Property<DateTimeOffset?>(template, TenantConventions.UpdatedAt) ?? EF.Property<DateTimeOffset>(template, TenantConventions.CreatedAt),
                campaigns.Count(campaign => campaign.TemplateId == template.Id)))
            .ToListAsync(cancellationToken);
    }

    public Task<EmailTemplate?> FindTemplateAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<EmailTemplate>().SingleOrDefaultAsync(template => template.Id == id, cancellationToken);

    public Task<bool> TemplateNameTakenAsync(string name, Guid? exceptId, CancellationToken cancellationToken) =>
        db.Set<EmailTemplate>().AnyAsync(template => template.Name == name && template.Id != exceptId, cancellationToken);

    public Task<bool> TemplateInUseAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<Campaign>().AnyAsync(campaign => campaign.TemplateId == id, cancellationToken);

    public async Task<(IReadOnlyList<CampaignRow> Items, int Total)> CampaignsAsync(int skip, int take, CancellationToken cancellationToken)
    {
        var campaigns = db.Set<Campaign>().AsNoTracking();
        var total = await campaigns.CountAsync(cancellationToken);
        var items = await Rows(campaigns.OrderByDescending(campaign => EF.Property<DateTimeOffset>(campaign, TenantConventions.CreatedAt)).ThenBy(campaign => campaign.Id)
            .Skip(skip).Take(take)).ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<CampaignRow?> CampaignAsync(Guid id, CancellationToken cancellationToken) =>
        Rows(db.Set<Campaign>().AsNoTracking().Where(campaign => campaign.Id == id)).SingleOrDefaultAsync(cancellationToken);

    public Task<Campaign?> FindCampaignAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<Campaign>().SingleOrDefaultAsync(campaign => campaign.Id == id, cancellationToken);

    public Task<bool> SegmentExistsAsync(Guid id, CancellationToken cancellationToken) => db.Set<Segment>().AnyAsync(segment => segment.Id == id, cancellationToken);

    public Task<bool> ListExistsAsync(Guid id, CancellationToken cancellationToken) => db.Set<StaticList>().AnyAsync(list => list.Id == id, cancellationToken);

    public Task<bool> HasRecipientsAsync(Guid campaignId, CancellationToken cancellationToken) =>
        db.Set<CampaignRecipient>().AnyAsync(recipient => recipient.CampaignId == campaignId, cancellationToken);

    public async Task<IReadOnlyList<CampaignRecipient>> PendingAsync(Guid campaignId, int take, CancellationToken cancellationToken) =>
        await db.Set<CampaignRecipient>()
            .Where(recipient => recipient.CampaignId == campaignId && recipient.Status == CampaignRecipientStatus.Pending)
            .OrderBy(recipient => recipient.PersonId)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<(IReadOnlyList<CampaignRecipientRow> Items, int Total)> RecipientsAsync(
        Guid campaignId, CampaignRecipientStatus? status, int skip, int take, CancellationToken cancellationToken)
    {
        var recipients = db.Set<CampaignRecipient>().AsNoTracking().Where(recipient => recipient.CampaignId == campaignId);
        if (status is { } wanted)
        {
            recipients = recipients.Where(recipient => recipient.Status == wanted);
        }

        var total = await recipients.CountAsync(cancellationToken);
        var people = db.Set<Person>().IgnoreQueryFilters();
        var items = await recipients
            .OrderBy(recipient => recipient.Email).ThenBy(recipient => recipient.PersonId)
            .Skip(skip).Take(take)
            .Select(recipient => new CampaignRecipientRow(
                recipient.PersonId,
                people.Where(person => person.Id == recipient.PersonId).Select(person => person.FirstName + " " + person.LastName).FirstOrDefault() ?? string.Empty,
                recipient.Email, recipient.Status, recipient.Exclusion, recipient.ErrorCode, recipient.ProcessedAt))
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<int> ExcludePendingAsync(Guid campaignId, ExclusionReason reason, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Set<CampaignRecipient>()
            .Where(recipient => recipient.CampaignId == campaignId && recipient.Status == CampaignRecipientStatus.Pending)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(recipient => recipient.Status, CampaignRecipientStatus.Excluded)
                    .SetProperty(recipient => recipient.Exclusion, reason)
                    .SetProperty(recipient => recipient.ProcessedAt, now),
                cancellationToken);

    public async Task<IReadOnlyList<SuppressionRow>> SuppressionsAsync(CancellationToken cancellationToken) =>
        await db.Set<Suppression>().AsNoTracking()
            .OrderBy(suppression => suppression.Email)
            .Select(suppression => new SuppressionRow(suppression.Id, suppression.Email, suppression.Reason, suppression.SuppressedAt))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlySet<string>> SuppressedAsync(IReadOnlyCollection<string> emails, CancellationToken cancellationToken)
    {
        var wanted = emails.Select(email => email.Trim().ToUpperInvariant()).Distinct(StringComparer.Ordinal).ToArray();
        var found = await db.Set<Suppression>().AsNoTracking()
            .Where(suppression => wanted.Contains(suppression.Email.ToUpper()))
            .Select(suppression => suppression.Email)
            .ToListAsync(cancellationToken);
        return found.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public Task<Suppression?> FindSuppressionAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<Suppression>().SingleOrDefaultAsync(suppression => suppression.Id == id, cancellationToken);

    public void Add(EmailTemplate template) => db.Set<EmailTemplate>().Add(template);

    public void Remove(EmailTemplate template) => db.Set<EmailTemplate>().Remove(template);

    public void Add(Campaign campaign) => db.Set<Campaign>().Add(campaign);

    public void Remove(Campaign campaign) => db.Set<Campaign>().Remove(campaign);

    public void AddRecipients(IEnumerable<CampaignRecipient> recipients) => db.Set<CampaignRecipient>().AddRange(recipients);

    public void Add(Suppression suppression) => db.Set<Suppression>().Add(suppression);

    public void Remove(Suppression suppression) => db.Set<Suppression>().Remove(suppression);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public ValueTask DisposeAsync() => db.DisposeAsync();

    /// <summary>Filter and order before: a projected record cannot be filtered in SQL.</summary>
    private IQueryable<CampaignRow> Rows(IQueryable<Campaign> campaigns)
    {
        var templates = db.Set<EmailTemplate>();
        var segments = db.Set<Segment>();
        var lists = db.Set<StaticList>();
        return campaigns.Select(campaign => new CampaignRow(
            campaign.Id,
            campaign.Name,
            campaign.TemplateId,
            templates.Where(template => template.Id == campaign.TemplateId).Select(template => template.Name).FirstOrDefault() ?? string.Empty,
            campaign.SegmentId,
            campaign.ListId,
            segments.Where(segment => segment.Id == campaign.SegmentId).Select(segment => segment.Name).FirstOrDefault()
                ?? lists.Where(list => list.Id == campaign.ListId).Select(list => list.Name).FirstOrDefault(),
            campaign.Status,
            campaign.RecipientCount,
            campaign.SentCount,
            campaign.FailedCount,
            campaign.ExcludedCount,
            campaign.ErrorCode,
            campaign.ErrorMessage,
            EF.Property<DateTimeOffset>(campaign, TenantConventions.CreatedAt),
            campaign.StartedAt,
            campaign.CompletedAt));
    }
}
#pragma warning restore CA1304, CA1311
