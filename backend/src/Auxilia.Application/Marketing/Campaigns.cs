using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Channels;
using Auxilia.Application.Abstractions.Marketing;
using Auxilia.Application.Abstractions.Messaging;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Directory.Public;
using Auxilia.Application.Marketing.Public;
using Auxilia.Application.Messaging.Public;
using Auxilia.Contracts.Common;
using Auxilia.Contracts.Marketing;
using Auxilia.Contracts.Messages.V1.Marketing;
using Auxilia.Diagnostics;
using Auxilia.Domain.Marketing;
using Auxilia.Domain.Messaging;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Marketing;

/// <summary>E-mail templates of the campaigns (N01): Liquid subject and body, preview, test send.</summary>
public interface IEmailTemplateManager
{
    Task<Result<Guid>> CreateAsync(SaveEmailTemplateRequest request, CancellationToken cancellationToken);

    Task<Result> UpdateAsync(Guid id, SaveEmailTemplateRequest request, CancellationToken cancellationToken);

    /// <summary>Only a template no campaign uses.</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Queues the template, rendered for a sample person, to any address (purpose Marketing, the caller's account rules).</summary>
    Task<Result> SendTestAsync(Guid id, string? email, CancellationToken cancellationToken);
}

public interface IEmailTemplateQueryService
{
    Task<IReadOnlyList<EmailTemplateListItemResponse>> ListAsync(CancellationToken cancellationToken);

    Task<Result<EmailTemplateResponse>> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The template rendered for a client of the tenant, or a sample person.</summary>
    Task<Result<EmailPreviewResponse>> PreviewAsync(Guid id, Guid? clientId, CancellationToken cancellationToken);
}

/// <summary>
/// E-mail campaigns (N01): a draft with a template and an audience; "send now" queues it once; the Worker snapshots the
/// recipients, leaves out those without e-mail, without the e-mail marketing consent or suppressed, and sends in
/// batches through Messaging (purpose Marketing, account by the rules of the sender's role). Never sent twice.
/// </summary>
public interface ICampaignManager
{
    Task<Result<Guid>> CreateAsync(SaveCampaignRequest request, CancellationToken cancellationToken);

    Task<Result> UpdateAsync(Guid id, SaveCampaignRequest request, CancellationToken cancellationToken);

    /// <summary>A campaign not sending.</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);

    Task<Result> SendAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>A draft, or a campaign still sending (the recipients not sent yet are excluded as cancelled).</summary>
    Task<Result> CancelAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Worker: snapshot and batches; idempotent (a redelivery continues with the pending recipients).</summary>
    Task<Result> ProcessAsync(Guid id, CancellationToken cancellationToken);
}

public interface ICampaignQueryService
{
    Task<PagedResponse<CampaignResponse>> ListAsync(int page, int pageSize, CancellationToken cancellationToken);

    Task<Result<CampaignResponse>> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>How many clients the audience has now (the confirmation of "send now").</summary>
    Task<Result<CampaignAudienceResponse>> AudienceAsync(Guid id, CancellationToken cancellationToken);

    /// <param name="status"><c>Pending</c>, <c>Sent</c>, <c>Failed</c>, <c>Excluded</c> or none.</param>
    Task<Result<PagedResponse<CampaignRecipientResponse>>> RecipientsAsync(Guid id, string? status, int page, int pageSize, CancellationToken cancellationToken);
}

/// <summary>Addresses marketing never writes to (N01; unsubscribe page deferred, D-24).</summary>
public interface ISuppressionManager
{
    Task<Result<Guid>> AddAsync(AddSuppressionRequest request, CancellationToken cancellationToken);

    Task<Result> RemoveAsync(Guid id, CancellationToken cancellationToken);
}

public interface ISuppressionQueryService
{
    Task<IReadOnlyList<SuppressionResponse>> ListAsync(CancellationToken cancellationToken);
}

/// <summary>The Liquid model of a campaign e-mail.</summary>
internal static class CampaignModel
{
    public const string SampleFirstName = "Mario";
    public const string SampleLastName = "Rossi";

    public static IReadOnlyDictionary<string, object?> Of(string firstName, string lastName, string tenantName) =>
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["firstName"] = firstName,
            ["lastName"] = lastName,
            ["fullName"] = $"{firstName} {lastName}",
            ["tenantName"] = tenantName,
        };

    public static Result<EmailPreviewResponse> Render(ITemplateRenderer renderer, EmailTemplate template, IReadOnlyDictionary<string, object?> model)
    {
        var subject = renderer.Render(template.Subject, model, html: false);
        var body = renderer.Render(template.Body, model, html: true);
        return subject is null || body is null
            ? Errors.Marketing.TemplateInvalid("body", "validation.templates.body")
            : new EmailPreviewResponse(subject.Trim(), body);
    }
}

internal sealed class EmailTemplateManager(
    IOperationRunner operations, ICampaignDataFactory data, ITemplateRenderer renderer, IMessageDispatcher messages, ITenantContext tenant) : IEmailTemplateManager
{
    public Task<Result<Guid>> CreateAsync(SaveEmailTemplateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync<Guid>(Operations.Marketing.CreateTemplate, null, async scope =>
        {
            var template = new EmailTemplate(Guid.CreateVersion7());
            await using var store = await data.OpenAsync(cancellationToken);
            var applied = await ApplyAsync(store, template, request, cancellationToken);
            if (applied.IsFailure)
            {
                return Result.Failure<Guid>(applied.Error!);
            }

            store.Add(template);
            await store.SaveChangesAsync(cancellationToken);
            scope.SetEntity(nameof(EmailTemplate), template.Id);
            return template.Id;
        }, cancellationToken);
    }

    public Task<Result> UpdateAsync(Guid id, SaveEmailTemplateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync(Operations.Marketing.UpdateTemplate, new { TemplateId = id }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindTemplateAsync(id, cancellationToken) is not { } template)
            {
                return Errors.Marketing.TemplateNotFound();
            }

            var applied = await ApplyAsync(store, template, request, cancellationToken);
            if (applied.IsFailure)
            {
                return applied;
            }

            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);
    }

    public Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Marketing.DeleteTemplate, new { TemplateId = id }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindTemplateAsync(id, cancellationToken) is not { } template)
            {
                return Errors.Marketing.TemplateNotFound();
            }

            if (await store.TemplateInUseAsync(id, cancellationToken))
            {
                return Errors.Marketing.TemplateInUse();
            }

            store.Remove(template);
            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);

    public Task<Result> SendTestAsync(Guid id, string? email, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Marketing.SendTemplateTest, new { TemplateId = id }, async _ =>
        {
            if (string.IsNullOrWhiteSpace(email) || email.Length > Suppression.EmailMaxLength)
            {
                return Errors.Marketing.TemplateInvalid("email", "validation.templates.testEmail");
            }

            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindTemplateAsync(id, cancellationToken) is not { } template)
            {
                return Errors.Marketing.TemplateNotFound();
            }

            var content = CampaignModel.Render(renderer, template, CampaignModel.Of(CampaignModel.SampleFirstName, CampaignModel.SampleLastName, tenant.Tenant.Slug));
            if (content.IsFailure)
            {
                return Result.Failure(content.Error!);
            }

            var queued = await messages.QueueContentAsync(
                new OutboundContentRequest(MessageChannel.Email, MessagePurpose.Marketing, email.Trim(), template.Language, content.Value.Subject, content.Value.Body, nameof(EmailTemplate), template.Id),
                cancellationToken);
            return queued.IsFailure ? Result.Failure(queued.Error!) : Result.Success();
        }, cancellationToken);

    private async Task<Result> ApplyAsync(ICampaignData store, EmailTemplate template, SaveEmailTemplateRequest request, CancellationToken cancellationToken)
    {
        var updated = template.Update(request.Name, request.Language, request.Subject, request.Body, renderer.IsValid);
        if (updated.IsFailure)
        {
            return updated;
        }

        return await store.TemplateNameTakenAsync(template.Name, template.Id, cancellationToken)
            ? Errors.Marketing.TemplateInvalid("name", "validation.templates.nameTaken")
            : Result.Success();
    }
}

internal sealed class EmailTemplateQueryService(ICampaignDataFactory data, ITemplateRenderer renderer, IMarketingContacts contacts, ITenantContext tenant)
    : IEmailTemplateQueryService
{
    public async Task<IReadOnlyList<EmailTemplateListItemResponse>> ListAsync(CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        return [.. (await store.TemplatesAsync(cancellationToken)).Select(row =>
            new EmailTemplateListItemResponse(row.Id, row.Name, row.Language, row.Subject, row.UpdatedAt, row.CampaignCount))];
    }

    public async Task<Result<EmailTemplateResponse>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        return await store.FindTemplateAsync(id, cancellationToken) is { } template
            ? new EmailTemplateResponse(template.Id, template.Name, template.Language, template.Subject, template.Body)
            : Errors.Marketing.TemplateNotFound();
    }

    public async Task<Result<EmailPreviewResponse>> PreviewAsync(Guid id, Guid? clientId, CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        if (await store.FindTemplateAsync(id, cancellationToken) is not { } template)
        {
            return Errors.Marketing.TemplateNotFound();
        }

        var (first, last) = (CampaignModel.SampleFirstName, CampaignModel.SampleLastName);
        if (clientId is { } client)
        {
            var found = await contacts.FindAsync([client], cancellationToken);
            if (found.Count == 0)
            {
                return Errors.Directory.ClientNotFound();
            }

            var contact = found[0];

            (first, last) = (contact.FirstName, contact.LastName);
        }

        return CampaignModel.Render(renderer, template, CampaignModel.Of(first, last, tenant.Tenant.Slug));
    }
}

internal sealed class CampaignManager(
    IOperationRunner operations,
    ICampaignDataFactory data,
    IAudienceResolver audiences,
    IMarketingContacts contacts,
    IMessageDispatcher messages,
    ITemplateRenderer renderer,
    IMessageOutbox outbox,
    ICurrentUser currentUser,
    ITenantContext tenant,
    TimeProvider clock) : ICampaignManager
{
    /// <summary>Recipients per batch: each batch is one transaction (queued messages and recipient statuses together).</summary>
    public const int BatchSize = 50;

    public Task<Result<Guid>> CreateAsync(SaveCampaignRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync<Guid>(Operations.Marketing.CreateCampaign, null, async scope =>
        {
            var campaign = new Campaign(Guid.CreateVersion7());
            await using var store = await data.OpenAsync(cancellationToken);
            var applied = await ApplyAsync(store, campaign, request, cancellationToken);
            if (applied.IsFailure)
            {
                return Result.Failure<Guid>(applied.Error!);
            }

            store.Add(campaign);
            await store.SaveChangesAsync(cancellationToken);
            scope.SetEntity(nameof(Campaign), campaign.Id);
            return campaign.Id;
        }, cancellationToken);
    }

    public Task<Result> UpdateAsync(Guid id, SaveCampaignRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync(Operations.Marketing.UpdateCampaign, new { CampaignId = id }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindCampaignAsync(id, cancellationToken) is not { } campaign)
            {
                return Errors.Marketing.CampaignNotFound();
            }

            var applied = await ApplyAsync(store, campaign, request, cancellationToken);
            if (applied.IsFailure)
            {
                return applied;
            }

            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);
    }

    public Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Marketing.DeleteCampaign, new { CampaignId = id }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindCampaignAsync(id, cancellationToken) is not { } campaign)
            {
                return Errors.Marketing.CampaignNotFound();
            }

            if (campaign.Status == CampaignStatus.Sending)
            {
                return Errors.Marketing.CampaignNotDraft(campaign.Status.ToString());
            }

            store.Remove(campaign);
            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);

    public Task<Result> SendAsync(Guid id, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Marketing.QueueCampaign, new { CampaignId = id }, async scope =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindCampaignAsync(id, cancellationToken) is not { } campaign)
            {
                return Errors.Marketing.CampaignNotFound();
            }

            if (campaign.SegmentId is null && campaign.ListId is null)
            {
                return Errors.Marketing.CampaignInvalid("audience", "validation.campaigns.audience");
            }

            var sent = campaign.Send(currentUser.UserId, clock.GetUtcNow());
            if (sent.IsFailure)
            {
                return sent;
            }

            await store.SaveChangesAsync(cancellationToken);
            await outbox.EnqueueAsync(scope, new SendCampaignCommand(campaign.Id, [.. currentUser.Roles.Select(role => role.ToString())]), cancellationToken);
            return Result.Success();
        }, cancellationToken);

    public Task<Result> CancelAsync(Guid id, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Marketing.CancelCampaign, new { CampaignId = id }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindCampaignAsync(id, cancellationToken) is not { } campaign)
            {
                return Errors.Marketing.CampaignNotFound();
            }

            var now = clock.GetUtcNow();
            var cancelled = campaign.Cancel(now);
            if (cancelled.IsFailure)
            {
                return cancelled;
            }

            await store.SaveChangesAsync(cancellationToken);
            await store.ExcludePendingAsync(id, ExclusionReason.Cancelled, now, cancellationToken);
            return Result.Success();
        }, cancellationToken);

    public Task<Result> ProcessAsync(Guid id, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Marketing.SendCampaign, new { CampaignId = id }, async _ =>
        {
            Campaign? campaign;
            EmailTemplate? template;
            bool snapshotted;
            await using (var store = await data.OpenAsync(cancellationToken))
            {
                campaign = await store.FindCampaignAsync(id, cancellationToken);
                if (campaign is null)
                {
                    return Errors.Marketing.CampaignNotFound();
                }

                if (campaign.Status != CampaignStatus.Sending)
                {
                    return Result.Success();
                }

                template = await store.FindTemplateAsync(campaign.TemplateId, cancellationToken);
                snapshotted = await store.HasRecipientsAsync(id, cancellationToken);
            }

            if (template is null)
            {
                return await FailAsync(id, "the template was deleted", cancellationToken);
            }

            if (!snapshotted)
            {
                var snapshot = await SnapshotAsync(campaign, cancellationToken);
                if (snapshot.IsFailure)
                {
                    return await FailAsync(id, snapshot.Error!.Description, cancellationToken);
                }
            }

            while (true)
            {
                var processed = await operations.RunAsync<int>(Operations.Marketing.SendCampaignBatch, new { CampaignId = id }, async _ =>
                {
                    await using var store = await data.OpenAsync(cancellationToken);
                    var current = await store.FindCampaignAsync(id, cancellationToken);
                    if (current is not { Status: CampaignStatus.Sending })
                    {
                        return 0;
                    }

                    var batch = await store.PendingAsync(id, BatchSize, cancellationToken);
                    var people = (await contacts.FindAsync([.. batch.Select(recipient => recipient.PersonId)], cancellationToken)).ToDictionary(contact => contact.PersonId);
                    var (sent, failed) = (0, 0);
                    var now = clock.GetUtcNow();
                    foreach (var recipient in batch)
                    {
                        if (!people.TryGetValue(recipient.PersonId, out var contact))
                        {
                            recipient.Exclude(ExclusionReason.NoEmail, now);
                            continue;
                        }

                        var content = CampaignModel.Render(renderer, template, CampaignModel.Of(contact.FirstName, contact.LastName, tenant.Tenant.Slug));
                        var queued = content.IsFailure
                            ? Result.Failure<Guid>(content.Error!)
                            : await messages.QueueContentAsync(
                                new OutboundContentRequest(
                                    MessageChannel.Email, MessagePurpose.Marketing, recipient.Email!, template.Language, content.Value.Subject, content.Value.Body,
                                    nameof(Campaign), id),
                                cancellationToken);
                        if (queued.IsSuccess)
                        {
                            recipient.Sent(queued.Value, now);
                            sent++;
                        }
                        else
                        {
                            recipient.Failed(queued.Error!.DisplayCode, now);
                            failed++;
                        }
                    }

                    current.RecordSent(sent, failed);
                    await store.SaveChangesAsync(cancellationToken);
                    return batch.Count;
                }, cancellationToken);
                if (processed.IsFailure)
                {
                    return Result.Failure(processed.Error!);
                }

                if (processed.Value < BatchSize)
                {
                    break;
                }
            }

            await ChangeAsync(id, campaign => campaign.Complete(clock.GetUtcNow()), cancellationToken);
            return Result.Success();
        }, cancellationToken);

    /// <summary>One row per client of the audience now, excluded at once when there is no e-mail, no consent or a suppression.</summary>
    private async Task<Result> SnapshotAsync(Campaign campaign, CancellationToken cancellationToken)
    {
        var members = campaign.SegmentId is { } segment
            ? await audiences.SegmentAsync(segment, cancellationToken)
            : await audiences.ListAsync(campaign.ListId!.Value, cancellationToken);
        if (members.IsFailure)
        {
            return Result.Failure(members.Error!);
        }

        var people = await contacts.FindAsync(members.Value, cancellationToken);
        return await operations.RunAsync(Operations.Marketing.SendCampaignBatch, new { CampaignId = campaign.Id }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            var suppressed = await store.SuppressedAsync([.. people.Select(person => person.Email).OfType<string>()], cancellationToken);
            var recipients = people.Select(person => new CampaignRecipient(
                campaign.Id,
                person.PersonId,
                person.Email,
                string.IsNullOrWhiteSpace(person.Email) ? ExclusionReason.NoEmail
                : !person.EmailMarketingConsent ? ExclusionReason.NoConsent
                : suppressed.Contains(person.Email) ? ExclusionReason.Suppressed
                : null)).ToArray();
            store.AddRecipients(recipients);
            var tracked = await store.FindCampaignAsync(campaign.Id, cancellationToken);
            tracked!.Snapshot(recipients.Length, recipients.Count(recipient => recipient.Status == CampaignRecipientStatus.Excluded));
            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);
    }

    private async Task<Result> FailAsync(Guid id, string reason, CancellationToken cancellationToken)
    {
        var error = Errors.Marketing.CampaignFailed(reason);
        await ChangeAsync(id, campaign => campaign.Fail(error.DisplayCode, error.Description, clock.GetUtcNow()), cancellationToken);
        return Result.Success();
    }

    private Task<Result> ChangeAsync(Guid id, Action<Campaign> change, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Marketing.SendCampaignBatch, new { CampaignId = id }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            change((await store.FindCampaignAsync(id, cancellationToken))!);
            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);

    private static async Task<Result> ApplyAsync(ICampaignData store, Campaign campaign, SaveCampaignRequest request, CancellationToken cancellationToken)
    {
        var updated = campaign.Update(request.Name, request.TemplateId, request.SegmentId, request.ListId);
        if (updated.IsFailure)
        {
            return updated;
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (await store.FindTemplateAsync(request.TemplateId, cancellationToken) is null)
        {
            errors["templateId"] = ["validation.campaigns.template"];
        }

        if (request.SegmentId is { } segment && !await store.SegmentExistsAsync(segment, cancellationToken))
        {
            errors["segmentId"] = ["validation.campaigns.segment"];
        }

        if (request.ListId is { } list && !await store.ListExistsAsync(list, cancellationToken))
        {
            errors["listId"] = ["validation.campaigns.list"];
        }

        return errors.Count > 0 ? Errors.Marketing.CampaignInvalid(errors) : Result.Success();
    }
}

internal sealed class CampaignQueryService(ICampaignDataFactory data, IAudienceResolver audiences) : ICampaignQueryService
{
    public const int MaxPageSize = 100;

    public async Task<PagedResponse<CampaignResponse>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        (page, pageSize) = (Math.Max(1, page), Math.Clamp(pageSize, 1, MaxPageSize));
        await using var store = await data.OpenAsync(cancellationToken);
        var (items, total) = await store.CampaignsAsync((page - 1) * pageSize, pageSize, cancellationToken);
        return new PagedResponse<CampaignResponse>([.. items.Select(ToResponse)], page, pageSize, total);
    }

    public async Task<Result<CampaignResponse>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        return await store.CampaignAsync(id, cancellationToken) is { } row ? ToResponse(row) : Errors.Marketing.CampaignNotFound();
    }

    public async Task<Result<CampaignAudienceResponse>> AudienceAsync(Guid id, CancellationToken cancellationToken)
    {
        CampaignRow? row;
        await using (var store = await data.OpenAsync(cancellationToken))
        {
            row = await store.CampaignAsync(id, cancellationToken);
        }

        if (row is null)
        {
            return Errors.Marketing.CampaignNotFound();
        }

        var members = row.SegmentId is { } segment
            ? await audiences.SegmentAsync(segment, cancellationToken)
            : row.ListId is { } list ? await audiences.ListAsync(list, cancellationToken) : Result.Success<IReadOnlyList<Guid>>([]);
        return members.IsFailure ? Result.Failure<CampaignAudienceResponse>(members.Error!) : new CampaignAudienceResponse(members.Value.Count);
    }

    public async Task<Result<PagedResponse<CampaignRecipientResponse>>> RecipientsAsync(Guid id, string? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        CampaignRecipientStatus? wanted = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<CampaignRecipientStatus>(status, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            {
                return Errors.Marketing.CampaignInvalid("status", "validation.campaigns.recipientStatus");
            }

            wanted = parsed;
        }

        (page, pageSize) = (Math.Max(1, page), Math.Clamp(pageSize, 1, MaxPageSize));
        await using var store = await data.OpenAsync(cancellationToken);
        if (await store.CampaignAsync(id, cancellationToken) is null)
        {
            return Errors.Marketing.CampaignNotFound();
        }

        var (items, total) = await store.RecipientsAsync(id, wanted, (page - 1) * pageSize, pageSize, cancellationToken);
        return new PagedResponse<CampaignRecipientResponse>(
            [.. items.Select(row => new CampaignRecipientResponse(row.PersonId, row.FullName, row.Email, row.Status.ToString(), row.Exclusion?.ToString(), row.ErrorCode, row.ProcessedAt))],
            page,
            pageSize,
            total);
    }

    private static CampaignResponse ToResponse(CampaignRow row) =>
        new(
            row.Id, row.Name, row.TemplateId, row.TemplateName, row.SegmentId, row.ListId, row.AudienceName, row.Status.ToString(), row.RecipientCount, row.SentCount,
            row.FailedCount, row.ExcludedCount, row.ErrorCode, row.ErrorMessage, row.CreatedAt, row.StartedAt, row.CompletedAt);
}

internal sealed class SuppressionManager(IOperationRunner operations, ICampaignDataFactory data, TimeProvider clock) : ISuppressionManager
{
    public Task<Result<Guid>> AddAsync(AddSuppressionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync<Guid>(Operations.Marketing.AddSuppression, null, async scope =>
        {
            var created = Suppression.Create(Guid.CreateVersion7(), request.Email, request.Reason, clock.GetUtcNow());
            if (created.IsFailure)
            {
                return Result.Failure<Guid>(created.Error!);
            }

            await using var store = await data.OpenAsync(cancellationToken);
            if ((await store.SuppressedAsync([created.Value.Email], cancellationToken)).Count > 0)
            {
                return Errors.Marketing.SuppressionInvalid(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["email"] = ["validation.suppressions.taken"] });
            }

            store.Add(created.Value);
            await store.SaveChangesAsync(cancellationToken);
            scope.SetEntity(nameof(Suppression), created.Value.Id);
            return created.Value.Id;
        }, cancellationToken);
    }

    public Task<Result> RemoveAsync(Guid id, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Marketing.RemoveSuppression, new { SuppressionId = id }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindSuppressionAsync(id, cancellationToken) is not { } suppression)
            {
                return Errors.Marketing.SuppressionNotFound();
            }

            store.Remove(suppression);
            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);
}

internal sealed class SuppressionQueryService(ICampaignDataFactory data) : ISuppressionQueryService
{
    public async Task<IReadOnlyList<SuppressionResponse>> ListAsync(CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        return [.. (await store.SuppressionsAsync(cancellationToken)).Select(row => new SuppressionResponse(row.Id, row.Email, row.Reason, row.SuppressedAt))];
    }
}
