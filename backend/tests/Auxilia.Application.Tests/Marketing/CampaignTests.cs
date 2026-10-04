using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Channels;
using Auxilia.Application.Abstractions.Marketing;
using Auxilia.Application.Abstractions.Messaging;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Directory.Public;
using Auxilia.Application.Marketing;
using Auxilia.Application.Marketing.Public;
using Auxilia.Application.Tests.Identity;
using Auxilia.Application.Tests.Platform;
using Auxilia.Contracts.Marketing;
using Auxilia.Contracts.Messages.V1.Marketing;
using Auxilia.Diagnostics;
using Auxilia.Domain.Marketing;
using Auxilia.Domain.Messaging;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

using NSubstitute;

namespace Auxilia.Application.Tests.Marketing;

public sealed class CampaignTests : IAsyncDisposable
{
    private static readonly Guid Admin = Guid.CreateVersion7();
    private static readonly Guid Consenting = Guid.CreateVersion7();
    private static readonly Guid NoConsent = Guid.CreateVersion7();
    private static readonly Guid NoEmail = Guid.CreateVersion7();
    private static readonly Guid Suppressed = Guid.CreateVersion7();

    private readonly InMemoryCampaigns data = new();
    private readonly IAudienceResolver audiences = Substitute.For<IAudienceResolver>();
    private readonly IMarketingContacts contacts = Substitute.For<IMarketingContacts>();
    private readonly RecordingDispatcher messages = new();
    private readonly ITemplateRenderer renderer = Substitute.For<ITemplateRenderer>();
    private readonly IMessageOutbox outbox = Substitute.For<IMessageOutbox>();
    private readonly ICurrentUser caller = Substitute.For<ICurrentUser>();
    private readonly ManualTimeProvider clock = new();
    private readonly EmailTemplateManager templates;
    private readonly EmailTemplateQueryService templateQuery;
    private readonly CampaignManager campaigns;
    private readonly CampaignQueryService campaignQuery;
    private readonly SuppressionManager suppressions;
    private readonly Guid listId = Guid.CreateVersion7();

    public CampaignTests()
    {
        caller.ActorType.Returns(ActorType.User);
        caller.UserId.Returns(Admin);
        caller.Roles.Returns([TenantRole.Administrator]);
        renderer.IsValid(Arg.Any<string>()).Returns(call => !call.Arg<string>().Contains("{%", StringComparison.Ordinal));
        renderer.Render(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<bool>())
            .Returns(call => call.ArgAt<string>(0).Replace("{{ firstName }}", (string)call.ArgAt<IReadOnlyDictionary<string, object?>>(1)["firstName"]!, StringComparison.Ordinal));
        data.Lists.Add(listId);
        audiences.ListAsync(listId, Arg.Any<CancellationToken>()).Returns(Result.Success<IReadOnlyList<Guid>>([Consenting, NoConsent, NoEmail, Suppressed]));
        contacts.FindAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(call => (IReadOnlyList<MarketingContact>)[.. new[]
        {
            new MarketingContact(Consenting, "Anna", "Bianchi", "anna@example.test", "it", true),
            new MarketingContact(NoConsent, "Luca", "Neri", "luca@example.test", "it", false),
            new MarketingContact(NoEmail, "Ugo", "Bo", null, "it", true),
            new MarketingContact(Suppressed, "Eva", "Re", "EVA@example.test", "it", true),
        }.Where(contact => call.Arg<IReadOnlyCollection<Guid>>().Contains(contact.PersonId))]);
        var tenant = SessionSettings.Tenant();
        templates = new EmailTemplateManager(ManagerHarness.Runner(), data, renderer, messages, tenant);
        templateQuery = new EmailTemplateQueryService(data, renderer, contacts, tenant);
        campaigns = new CampaignManager(ManagerHarness.Runner(), data, audiences, contacts, messages, renderer, outbox, caller, tenant, clock);
        campaignQuery = new CampaignQueryService(data, audiences);
        suppressions = new SuppressionManager(ManagerHarness.Runner(), data, clock);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => data.DisposeAsync();

    private async Task<Guid> TemplateAsync() =>
        (await templates.CreateAsync(new SaveEmailTemplateRequest("Spring", "it", "Ciao {{ firstName }}", "<p>Ciao {{ firstName }}</p>"), Ct)).Value;

    private async Task<Guid> CampaignAsync() =>
        (await campaigns.CreateAsync(new SaveCampaignRequest("Spring 2026", await TemplateAsync(), null, listId), Ct)).Value;

    [Fact]
    public async Task Templates_AreValidated_Previewed_AndTested()
    {
        (await templates.CreateAsync(new SaveEmailTemplateRequest("", "", "{% x", "{% y"), Ct)).Error!.ValidationErrors.Keys
            .ShouldBe(["name", "language", "subject", "body"], ignoreOrder: true);
        var id = await TemplateAsync();
        (await templates.CreateAsync(new SaveEmailTemplateRequest("spring", "it", "a", "b"), Ct)).Error!.ValidationErrors["name"].ShouldBe(["validation.templates.nameTaken"]);

        (await templateQuery.PreviewAsync(id, null, Ct)).Value.Subject.ShouldBe("Ciao Mario");
        (await templateQuery.PreviewAsync(id, Consenting, Ct)).Value.Body.ShouldBe("<p>Ciao Anna</p>");

        (await templates.SendTestAsync(id, "test@example.test", Ct)).IsSuccess.ShouldBeTrue();
        messages.Contents.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            content => content.Purpose.ShouldBe(MessagePurpose.Marketing), content => content.Recipient.ShouldBe("test@example.test"),
            content => content.Subject.ShouldBe("Ciao Mario"));
        (await templates.SendTestAsync(id, " ", Ct)).Error!.ValidationErrors.ShouldContainKey("email");
    }

    [Fact]
    public async Task SendNow_QueuesOnce_AndTheWorkerSendsOnlyToConsentingNotSuppressedAddresses()
    {
        await suppressions.AddAsync(new AddSuppressionRequest("eva@example.test", "asked"), Ct);
        var id = await CampaignAsync();
        (await campaignQuery.AudienceAsync(id, Ct)).Value.Count.ShouldBe(4);

        (await campaigns.SendAsync(id, Ct)).IsSuccess.ShouldBeTrue();
        (await campaigns.SendAsync(id, Ct)).Error!.Code.ShouldBe(EventCodes.Marketing.CampaignNotDraft);
        await outbox.Received(1).EnqueueAsync(
            Arg.Any<IOperationScope>(), Arg.Is<SendCampaignCommand>(command => command.CampaignId == id && command.Roles.Single() == "Administrator"), Arg.Any<CancellationToken>());

        (await campaigns.ProcessAsync(id, Ct)).IsSuccess.ShouldBeTrue();
        (await campaigns.ProcessAsync(id, Ct)).IsSuccess.ShouldBeTrue();

        var campaign = (await campaignQuery.GetAsync(id, Ct)).Value;
        (campaign.Status, campaign.RecipientCount, campaign.SentCount, campaign.ExcludedCount, campaign.FailedCount).ShouldBe(("Sent", 4, 1, 3, 0));
        messages.Contents.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            content => content.Recipient.ShouldBe("anna@example.test"), content => content.Subject.ShouldBe("Ciao Anna"), content => content.RelatedEntityId.ShouldBe(id));
        var excluded = (await campaignQuery.RecipientsAsync(id, "excluded", 1, 25, Ct)).Value;
        excluded.Items.Select(recipient => (recipient.ClientId, recipient.Exclusion)).ShouldBe(
            [(NoConsent, "NoConsent"), (NoEmail, "NoEmail"), (Suppressed, "Suppressed")], ignoreOrder: true);
    }

    [Fact]
    public async Task AFailingSend_IsRecordedPerRecipient_AndAMissingAudienceFailsTheCampaign()
    {
        messages.Outcome = Errors.Messaging.NoAccountForMessage("Email", "Marketing");
        var id = await CampaignAsync();
        await campaigns.SendAsync(id, Ct);
        await campaigns.ProcessAsync(id, Ct);
        var failed = (await campaignQuery.RecipientsAsync(id, "failed", 1, 25, Ct)).Value.Items;
        failed.Select(recipient => recipient.ClientId).ShouldBe([Consenting, Suppressed], ignoreOrder: true);
        failed.ShouldAllBe(recipient => recipient.ErrorCode == $"AUX-{EventCodes.Messaging.NoAccountForMessage}");
        (await campaignQuery.GetAsync(id, Ct)).Value.ShouldSatisfyAllConditions(
            campaign => campaign.Status.ShouldBe("Sent"), campaign => campaign.FailedCount.ShouldBe(2));

        audiences.ListAsync(listId, Arg.Any<CancellationToken>()).Returns(Result.Failure<IReadOnlyList<Guid>>(Errors.Marketing.ListNotFound()));
        var other = (await campaigns.CreateAsync(new SaveCampaignRequest("Gone", data.Templates.Single().Id, null, listId), Ct)).Value;
        await campaigns.SendAsync(other, Ct);
        await campaigns.ProcessAsync(other, Ct);
        (await campaignQuery.GetAsync(other, Ct)).Value.ShouldSatisfyAllConditions(
            campaign => campaign.Status.ShouldBe("Failed"), campaign => campaign.ErrorCode.ShouldBe($"AUX-{EventCodes.Marketing.CampaignFailed}"));
    }

    [Fact]
    public async Task Campaigns_CheckTheirReferences_AndCanBeCancelledOrDeleted()
    {
        var invalid = await campaigns.CreateAsync(new SaveCampaignRequest("x", Guid.CreateVersion7(), Guid.CreateVersion7(), null), Ct);
        invalid.Error!.ValidationErrors.Keys.ShouldBe(["templateId", "segmentId"], ignoreOrder: true);
        var id = await CampaignAsync();
        (await templates.DeleteAsync(data.Templates.Single().Id, Ct)).Error!.Code.ShouldBe(EventCodes.Marketing.TemplateInUse);

        await campaigns.SendAsync(id, Ct);
        (await campaigns.DeleteAsync(id, Ct)).Error!.Code.ShouldBe(EventCodes.Marketing.CampaignNotDraft);
        (await campaigns.CancelAsync(id, Ct)).IsSuccess.ShouldBeTrue();
        (await campaigns.ProcessAsync(id, Ct)).IsSuccess.ShouldBeTrue();
        messages.Contents.ShouldBeEmpty();
        (await campaigns.DeleteAsync(id, Ct)).IsSuccess.ShouldBeTrue();
        (await campaignQuery.GetAsync(id, Ct)).Error!.Code.ShouldBe(EventCodes.Marketing.CampaignNotFound);
        (await campaignQuery.RecipientsAsync(id, "lost", 1, 25, Ct)).Error!.ValidationErrors.ShouldContainKey("status");
    }

    [Fact]
    public async Task Suppressions_AreUniquePerAddress()
    {
        var id = (await suppressions.AddAsync(new AddSuppressionRequest("Eva@Example.test", null), Ct)).Value;
        (await suppressions.AddAsync(new AddSuppressionRequest("eva@example.test", null), Ct)).Error!.ValidationErrors["email"].ShouldBe(["validation.suppressions.taken"]);
        (await new SuppressionQueryService(data).ListAsync(Ct)).ShouldHaveSingleItem().Email.ShouldBe("Eva@Example.test");
        (await suppressions.RemoveAsync(id, Ct)).IsSuccess.ShouldBeTrue();
        (await suppressions.RemoveAsync(id, Ct)).Error!.Code.ShouldBe(EventCodes.Marketing.SuppressionNotFound);
    }
}

/// <summary>Campaign data in memory.</summary>
internal sealed class InMemoryCampaigns : ICampaignDataFactory, ICampaignData
{
    public List<EmailTemplate> Templates { get; } = [];

    public List<Campaign> Campaigns { get; } = [];

    public List<CampaignRecipient> Recipients { get; } = [];

    public List<Suppression> Suppressions { get; } = [];

    public List<Guid> Lists { get; } = [];

    public Task<ICampaignData> OpenAsync(CancellationToken cancellationToken) => Task.FromResult<ICampaignData>(this);

    public Task<IReadOnlyList<EmailTemplateRow>> TemplatesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EmailTemplateRow>>([.. Templates.Select(template => new EmailTemplateRow(
            template.Id, template.Name, template.Language, template.Subject, DateTimeOffset.UnixEpoch, Campaigns.Count(campaign => campaign.TemplateId == template.Id)))]);

    public Task<EmailTemplate?> FindTemplateAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Templates.SingleOrDefault(template => template.Id == id));

    public Task<bool> TemplateNameTakenAsync(string name, Guid? exceptId, CancellationToken cancellationToken) =>
        Task.FromResult(Templates.Any(template => string.Equals(template.Name, name, StringComparison.OrdinalIgnoreCase) && template.Id != exceptId));

    public Task<bool> TemplateInUseAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Campaigns.Any(campaign => campaign.TemplateId == id));

    public Task<(IReadOnlyList<CampaignRow> Items, int Total)> CampaignsAsync(int skip, int take, CancellationToken cancellationToken) =>
        Task.FromResult<(IReadOnlyList<CampaignRow>, int)>(([.. Campaigns.Skip(skip).Take(take).Select(Row)], Campaigns.Count));

    public Task<CampaignRow?> CampaignAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Campaigns.Where(campaign => campaign.Id == id).Select(Row).SingleOrDefault());

    public Task<Campaign?> FindCampaignAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Campaigns.SingleOrDefault(campaign => campaign.Id == id));

    public Task<bool> SegmentExistsAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(false);

    public Task<bool> ListExistsAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Lists.Contains(id));

    public Task<bool> HasRecipientsAsync(Guid campaignId, CancellationToken cancellationToken) => Task.FromResult(Recipients.Any(recipient => recipient.CampaignId == campaignId));

    public Task<IReadOnlyList<CampaignRecipient>> PendingAsync(Guid campaignId, int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CampaignRecipient>>([.. Recipients.Where(recipient => recipient.CampaignId == campaignId && recipient.Status == CampaignRecipientStatus.Pending).Take(take)]);

    public Task<(IReadOnlyList<CampaignRecipientRow> Items, int Total)> RecipientsAsync(
        Guid campaignId, CampaignRecipientStatus? status, int skip, int take, CancellationToken cancellationToken)
    {
        var rows = Recipients.Where(recipient => recipient.CampaignId == campaignId && (status is null || recipient.Status == status)).ToArray();
        return Task.FromResult<(IReadOnlyList<CampaignRecipientRow>, int)>(([.. rows.Skip(skip).Take(take).Select(recipient => new CampaignRecipientRow(
            recipient.PersonId, "Name", recipient.Email, recipient.Status, recipient.Exclusion, recipient.ErrorCode, recipient.ProcessedAt))], rows.Length));
    }

    public Task<int> ExcludePendingAsync(Guid campaignId, ExclusionReason reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var pending = Recipients.Where(recipient => recipient.CampaignId == campaignId && recipient.Status == CampaignRecipientStatus.Pending).ToArray();
        foreach (var recipient in pending)
        {
            recipient.Exclude(reason, now);
        }

        return Task.FromResult(pending.Length);
    }

    public Task<IReadOnlyList<SuppressionRow>> SuppressionsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SuppressionRow>>([.. Suppressions.Select(item => new SuppressionRow(item.Id, item.Email, item.Reason, item.SuppressedAt))]);

    public Task<IReadOnlySet<string>> SuppressedAsync(IReadOnlyCollection<string> emails, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<string>>(Suppressions.Select(item => item.Email).Where(email => emails.Contains(email, StringComparer.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase));

    public Task<Suppression?> FindSuppressionAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Suppressions.SingleOrDefault(item => item.Id == id));

    public void Add(EmailTemplate template) => Templates.Add(template);

    public void Remove(EmailTemplate template) => Templates.Remove(template);

    public void Add(Campaign campaign) => Campaigns.Add(campaign);

    public void Remove(Campaign campaign)
    {
        Campaigns.Remove(campaign);
        Recipients.RemoveAll(recipient => recipient.CampaignId == campaign.Id);
    }

    public void AddRecipients(IEnumerable<CampaignRecipient> recipients) => Recipients.AddRange(recipients);

    public void Add(Suppression suppression) => Suppressions.Add(suppression);

    public void Remove(Suppression suppression) => Suppressions.Remove(suppression);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private CampaignRow Row(Campaign campaign) =>
        new(
            campaign.Id, campaign.Name, campaign.TemplateId, "Spring", campaign.SegmentId, campaign.ListId, "List", campaign.Status, campaign.RecipientCount,
            campaign.SentCount, campaign.FailedCount, campaign.ExcludedCount, campaign.ErrorCode, campaign.ErrorMessage, DateTimeOffset.UnixEpoch, campaign.StartedAt,
            campaign.CompletedAt);
}
