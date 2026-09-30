using Auxilia.Application.Abstractions.Channels;
using Auxilia.Application.Messaging;
using Auxilia.Application.Messaging.Public;
using Auxilia.Contracts.Messages.V1.Messaging;
using Auxilia.Diagnostics;
using Auxilia.Domain.Messaging;
using Auxilia.SharedKernel.Tenancy;

using NSubstitute;

namespace Auxilia.Application.Tests.Messaging;

public sealed class MessageDispatchAndDeliveryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Queue_ResolvesTheAccountRendersInTheRecipientLanguageRecordsAndEnqueues()
    {
        var harness = new MessagingHarness(TenantRole.Employee);
        harness.AddAccount("Office", isDefault: true);
        var staff = harness.AddAccount("Staff");
        harness.Data.Rules.Add(new SenderRule(Guid.CreateVersion7(), MessageChannel.Email, MessagePurpose.Notification, "Employee", staff.Id, 1));
        harness.AddTemplate(MessageTemplates.RequestReply, "en", "Reply for {{ name }}");
        harness.AddTemplate(MessageTemplates.RequestReply, "it", "Risposta per {{ name }}", "<p>{{ name }}</p>");

        var result = await harness.Dispatcher.QueueAsync(Request(MessagePurpose.Notification, "it", related: ("Request", Guid.Empty)), Ct);

        var message = harness.Data.Outbound.ShouldHaveSingleItem();
        message.Id.ShouldBe(result.Value);
        (message.AccountId, message.Language, message.Subject, message.Body, message.Status).ShouldBe((staff.Id, "it", "Risposta per D'Angelo", "<p>D&#39;Angelo</p>", OutboundMessageStatus.Queued));
        (message.RelatedEntityType, message.Recipient).ShouldBe(("Request", "anna@example.test"));
        await harness.Outbox.Received(1).EnqueueAsync(
            Arg.Any<Auxilia.Application.Abstractions.Operations.IOperationScope>(), Arg.Is<object>(item => ((DeliverOutboundMessageCommand)item).OutboundMessageId == message.Id), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Queue_LanguageFallsBackToTheTenantThenEnglish()
    {
        var harness = new MessagingHarness();
        harness.AddAccount("Office", isDefault: true);
        harness.AddTemplate(MessageTemplates.RequestReply, "en", "EN");

        await harness.Dispatcher.QueueAsync(Request(MessagePurpose.Transactional, "fr"), Ct);
        harness.Data.Outbound.Single().Language.ShouldBe("en");

        harness.AddTemplate(MessageTemplates.RequestReply, "it", "IT");
        await harness.Dispatcher.QueueAsync(Request(MessagePurpose.Transactional, "fr"), Ct);
        harness.Data.Outbound[1].Subject.ShouldBe("IT");
    }

    [Fact]
    public async Task Queue_Refusals()
    {
        var harness = new MessagingHarness();
        (await harness.Dispatcher.QueueAsync(Request(MessagePurpose.Transactional, "it"), Ct)).Error!.Code.ShouldBe(EventCodes.Messaging.NoAccountForMessage);

        var gatewayOnly = new MessagingHarness();
        gatewayOnly.AddAccount("Wa", isDefault: true, channel: MessageChannel.WhatsApp, provider: "http-gateway");
        (await gatewayOnly.Dispatcher.QueueAsync(Request(MessagePurpose.Transactional, "it") with { Channel = MessageChannel.WhatsApp }, Ct)).Error!.Code
            .ShouldBe(EventCodes.Messaging.ChannelNotAvailable);

        harness.AddAccount("Office", isDefault: true);
        await harness.Snapshots.InvalidateTenantAsync("acme", Ct);
        (await harness.Dispatcher.QueueAsync(Request(MessagePurpose.Transactional, "it") with { Recipient = "nobody" }, Ct)).Error!.Code
            .ShouldBe(EventCodes.Messaging.RecipientInvalid);
        (await harness.Dispatcher.QueueAsync(Request(MessagePurpose.Transactional, "it"), Ct)).Error!.Code.ShouldBe(EventCodes.Messaging.TemplateNotFound);
        harness.AddTemplate(MessageTemplates.RequestReply, "it", "{{ broken");
        (await harness.Dispatcher.QueueAsync(Request(MessagePurpose.Transactional, "it"), Ct)).Error!.Code.ShouldBe(EventCodes.Messaging.TemplateInvalid);
        harness.Data.Outbound.ShouldBeEmpty();
    }

    [Fact]
    public void Resolution_ExactRoleThenAnyRoleThenDefault_ActiveOnly()
    {
        var byRole = Account(); var anyRole = Account(); var fallback = Account(isDefault: true); var inactive = Account(isActive: false); var other = Account(MessageChannel.WhatsApp);
        var snapshot = new MessagingSnapshot(
            [byRole, anyRole, fallback, inactive, other],
            [
                Rule(MessagePurpose.Marketing, "Employee", byRole.Id, 5),
                Rule(MessagePurpose.Marketing, "Employee", inactive.Id, 1),
                Rule(MessagePurpose.Marketing, null, anyRole.Id, 1),
                Rule(MessagePurpose.Marketing, "Administrator", other.Id, 1),
            ]);

        SendingAccountResolution.Resolve(snapshot, MessageChannel.Email, MessagePurpose.Marketing, ["Client", "Employee"]).ShouldBe(byRole);
        SendingAccountResolution.Resolve(snapshot, MessageChannel.Email, MessagePurpose.Marketing, ["Administrator"]).ShouldBe(anyRole);
        SendingAccountResolution.Resolve(snapshot, MessageChannel.Email, MessagePurpose.Transactional, []).ShouldBe(fallback);
        SendingAccountResolution.Resolve(snapshot, MessageChannel.Sms, MessagePurpose.Transactional, []).ShouldBeNull();
    }

    [Fact]
    public async Task Deliver_SendsOnceAndMarksSent()
    {
        var harness = new MessagingHarness();
        var message = Queued(harness);

        (await harness.Delivery.DeliverAsync(message.Id, Ct)).IsSuccess.ShouldBeTrue();
        (await harness.Delivery.DeliverAsync(message.Id, Ct)).IsSuccess.ShouldBeTrue();

        harness.Channel.Sent.ShouldHaveSingleItem().Account.Secret.ShouldBe("pw-Office");
        (message.Status, message.Attempts, message.SentAt is not null).ShouldBe((OutboundMessageStatus.Sent, 1, true));
    }

    [Fact]
    public async Task Deliver_TransientFailureRecordsTheAttemptAndThrows()
    {
        var harness = new MessagingHarness();
        var message = Queued(harness);
        harness.Channel.Failure = new TimeoutException("smtp down");

        await Should.ThrowAsync<TimeoutException>(() => harness.Delivery.DeliverAsync(message.Id, Ct));

        (message.Status, message.Attempts, message.ErrorCode).ShouldBe((OutboundMessageStatus.Queued, 1, "AUX-25018"));
    }

    [Fact]
    public async Task Deliver_PermanentFailuresMarkFailed()
    {
        var harness = new MessagingHarness();
        var refused = Queued(harness);
        harness.Channel.Failure = new ChannelPermanentException(Errors.Messaging.RecipientInvalid());
        (await harness.Delivery.DeliverAsync(refused.Id, Ct)).Error!.Code.ShouldBe(EventCodes.Messaging.RecipientInvalid);
        (refused.Status, refused.ErrorCode).ShouldBe((OutboundMessageStatus.Failed, "AUX-25016"));

        var inactive = Queued(harness);
        harness.Data.Accounts.Single(account => account.Id == inactive.AccountId).SetDefault(false);
        harness.Data.Accounts.Single(account => account.Id == inactive.AccountId).SetActive(false);
        (await harness.Delivery.DeliverAsync(inactive.Id, Ct)).Error!.Code.ShouldBe(EventCodes.Messaging.AccountNotFound);

        var gateway = harness.AddAccount("Wa", channel: MessageChannel.WhatsApp, provider: "http-gateway");
        var noAdapter = new OutboundMessage(Guid.CreateVersion7(), MessageChannel.WhatsApp, MessagePurpose.Transactional, gateway.Id, "+39000", null, "it", "", "hi", DateTimeOffset.UtcNow);
        harness.Data.Outbound.Add(noAdapter);
        (await harness.Delivery.DeliverAsync(noAdapter.Id, Ct)).Error!.Code.ShouldBe(EventCodes.Messaging.ChannelNotAvailable);

        (await harness.Delivery.DeliverAsync(Guid.CreateVersion7(), Ct)).Error!.Code.ShouldBe(EventCodes.Messaging.OutboundMessageNotFound);
    }

    [Fact]
    public async Task Snapshot_IsCachedPerTenantWithoutSecrets()
    {
        var harness = new MessagingHarness();
        var office = harness.AddAccount("Office", isDefault: true);

        var snapshot = await harness.Snapshots.GetAsync(Ct);

        snapshot.Accounts.ShouldHaveSingleItem().ShouldBe(new AccountInfo(office.Id, MessageChannel.Email, "smtp", true, true));
        harness.Cache.Requests.ShouldHaveSingleItem().Key.ShouldBe("t:acme:messaging:accounts:current");
    }

    private static OutboundMessageRequest Request(MessagePurpose purpose, string language, (string Type, Guid Id)? related = null) =>
        new(MessageChannel.Email, purpose, " anna@example.test ", MessageTemplates.RequestReply, language,
            new Dictionary<string, object?> { ["name"] = "D'Angelo" }, related?.Type, related?.Id);

    private static OutboundMessage Queued(MessagingHarness harness)
    {
        var account = harness.Data.Accounts.FirstOrDefault(item => item.Name == "Office") ?? harness.AddAccount("Office", isDefault: true);
        var message = new OutboundMessage(Guid.CreateVersion7(), MessageChannel.Email, MessagePurpose.Transactional, account.Id, "anna@example.test", "x", "it", "s", "b", DateTimeOffset.UtcNow);
        harness.Data.Outbound.Add(message);
        return message;
    }

    private static AccountInfo Account(MessageChannel channel = MessageChannel.Email, bool isDefault = false, bool isActive = true) =>
        new(Guid.CreateVersion7(), channel, "smtp", isDefault, isActive);

    private static RuleInfo Rule(MessagePurpose purpose, string? role, Guid accountId, int priority) =>
        new(MessageChannel.Email, purpose, role, accountId, priority);
}
