using System.Text.Json;

using Auxilia.Application.Messaging;
using Auxilia.Application.Messaging.Public;
using Auxilia.Diagnostics;
using Auxilia.Domain.Messaging;

namespace Auxilia.Application.Tests.Messaging;

public sealed class MessagingAccountManagerTests
{
    private static readonly JsonElement Smtp = JsonDocument.Parse("""{"host":"smtp.test","port":587}""").RootElement;
    private static readonly JsonElement NoHost = JsonDocument.Parse("""{"port":587}""").RootElement;

    private readonly MessagingHarness harness = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_FirstAccountOfTheChannelIsDefaultAndTheSecretIsProtected()
    {
        var first = await harness.Accounts.CreateAccountAsync(new CreateMessagingAccount(MessageChannel.Email, "smtp", " Office ", Smtp, "pw"), Ct);
        var second = await harness.Accounts.CreateAccountAsync(new CreateMessagingAccount(MessageChannel.Email, "smtp", "Marketing", Smtp, null), Ct);

        var office = harness.Data.Accounts.Single(account => account.Id == first.Value);
        (office.Name, office.IsDefault, office.SecretProtected).ShouldBe(("Office", true, "enc:pw"));
        harness.Data.Accounts.Single(account => account.Id == second.Value).IsDefault.ShouldBeFalse();
        harness.Cache.Invalidated.ShouldBe(["t:acme:messaging", "t:acme:messaging"]);
    }

    [Fact]
    public async Task Create_InvalidSettingsOrName_AreRefused()
    {
        (await harness.Accounts.CreateAccountAsync(new CreateMessagingAccount(MessageChannel.Email, "smtp", "A", NoHost, null), Ct)).Error!.Code
            .ShouldBe(EventCodes.Messaging.AccountSettingsInvalid);
        (await harness.Accounts.CreateAccountAsync(new CreateMessagingAccount(MessageChannel.WhatsApp, "smtp", "A", Smtp, null), Ct)).Error!.Code
            .ShouldBe(EventCodes.Messaging.AccountSettingsInvalid);
        (await harness.Accounts.CreateAccountAsync(new CreateMessagingAccount(MessageChannel.Email, "smtp", " ", Smtp, null), Ct)).Error!.Code
            .ShouldBe(EventCodes.Messaging.AccountSettingsInvalid);
        harness.Data.Accounts.ShouldBeEmpty();
    }

    [Fact]
    public async Task Create_ProviderWithoutAdapter_IsStoredForLater()
    {
        var gateway = JsonDocument.Parse("""{"endpoint":"https://gw.test","sender":"+39000"}""").RootElement;

        var result = await harness.Accounts.CreateAccountAsync(new CreateMessagingAccount(MessageChannel.WhatsApp, "http-gateway", "WhatsApp", gateway, "token"), Ct);

        result.IsSuccess.ShouldBeTrue();
        harness.Data.Accounts.ShouldHaveSingleItem().IsDefault.ShouldBeTrue();
    }

    [Fact]
    public async Task Update_KeepsTheSecretWhenNotGivenAndValidatesSettings()
    {
        var account = harness.AddAccount("Office", isDefault: true);

        (await harness.Accounts.UpdateAccountAsync(new UpdateMessagingAccount(account.Id, "Front office", Smtp, null), Ct)).IsSuccess.ShouldBeTrue();
        (account.Name, account.SecretProtected).ShouldBe(("Front office", "enc:pw-Office"));

        (await harness.Accounts.UpdateAccountAsync(new UpdateMessagingAccount(account.Id, "X", Smtp, "new"), Ct)).IsSuccess.ShouldBeTrue();
        account.SecretProtected.ShouldBe("enc:new");

        (await harness.Accounts.UpdateAccountAsync(new UpdateMessagingAccount(account.Id, "X", NoHost, null), Ct)).Error!.Code.ShouldBe(EventCodes.Messaging.AccountSettingsInvalid);
        (await harness.Accounts.UpdateAccountAsync(new UpdateMessagingAccount(Guid.CreateVersion7(), "X", Smtp, null), Ct)).Error!.Code.ShouldBe(EventCodes.Messaging.AccountNotFound);
    }

    [Fact]
    public async Task SetDefault_MovesTheDefaultAndTheDefaultCannotBeDeactivated()
    {
        var office = harness.AddAccount("Office", isDefault: true);
        var marketing = harness.AddAccount("Marketing");

        (await harness.Accounts.SetAccountActiveAsync(office.Id, false, Ct)).Error!.Code.ShouldBe(EventCodes.Messaging.DefaultAccountMustBeActive);
        (await harness.Accounts.SetDefaultAccountAsync(marketing.Id, Ct)).IsSuccess.ShouldBeTrue();
        (office.IsDefault, marketing.IsDefault).ShouldBe((false, true));
        (await harness.Accounts.SetAccountActiveAsync(office.Id, false, Ct)).IsSuccess.ShouldBeTrue();
        (await harness.Accounts.SetDefaultAccountAsync(office.Id, Ct)).Error!.Code.ShouldBe(EventCodes.Messaging.DefaultAccountMustBeActive);
    }

    [Fact]
    public async Task SetSenderRules_ReplacesTheRulesOfTheChannelAfterValidation()
    {
        var office = harness.AddAccount("Office", isDefault: true);
        var inactive = harness.AddAccount("Old", isActive: false);
        var whatsApp = harness.AddAccount("Wa", channel: MessageChannel.WhatsApp, provider: "http-gateway");
        harness.Data.Rules.Add(new SenderRule(Guid.CreateVersion7(), MessageChannel.Email, MessagePurpose.Marketing, null, office.Id, 1));
        harness.Data.Rules.Add(new SenderRule(Guid.CreateVersion7(), MessageChannel.WhatsApp, MessagePurpose.Marketing, null, whatsApp.Id, 1));

        (await harness.Accounts.SetSenderRulesAsync(MessageChannel.Email, [new(MessagePurpose.Notification, "Employee", office.Id, 1)], Ct)).IsSuccess.ShouldBeTrue();

        harness.Data.Rules.Where(rule => rule.Channel == MessageChannel.Email).ShouldHaveSingleItem().Role.ShouldBe("Employee");
        harness.Data.Rules.Count(rule => rule.Channel == MessageChannel.WhatsApp).ShouldBe(1);

        async Task<int> CodeOf(SenderRuleInput rule) => (await harness.Accounts.SetSenderRulesAsync(MessageChannel.Email, [rule], Ct)).Error!.Code;
        (await CodeOf(new(MessagePurpose.Notification, "SystemConfigurator", office.Id, 1))).ShouldBe(EventCodes.Messaging.SenderRuleInvalid);
        (await CodeOf(new(MessagePurpose.Notification, null, inactive.Id, 1))).ShouldBe(EventCodes.Messaging.SenderRuleInvalid);
        (await CodeOf(new(MessagePurpose.Notification, null, whatsApp.Id, 1))).ShouldBe(EventCodes.Messaging.SenderRuleInvalid);
        (await CodeOf(new(MessagePurpose.Notification, null, Guid.CreateVersion7(), 1))).ShouldBe(EventCodes.Messaging.SenderRuleInvalid);
    }

    [Fact]
    public async Task SendTest_SendsNowWithTheDecryptedSecretAndRecordsTheOutcome()
    {
        var office = harness.AddAccount("Office", isDefault: true);
        harness.AddTemplate(MessageTemplates.AccountTest, "en", "Test from {{ accountName }}", "<p>{{ tenantName }}</p>");

        var sent = (await harness.Accounts.SendTestAsync(office.Id, "anna@example.test", "de", Ct)).Value;
        (sent.Sent, sent.ErrorCode).ShouldBe((true, null));

        var (message, account) = harness.Channel.Sent.ShouldHaveSingleItem();
        (message.Recipient, message.Subject, message.Body, account.Secret).ShouldBe(("anna@example.test", "Test from Office", "<p>acme</p>", "pw-Office"));
        var logged = harness.Data.Outbound.ShouldHaveSingleItem();
        (logged.Status, logged.Language, logged.Purpose).ShouldBe((OutboundMessageStatus.Sent, "en", MessagePurpose.Transactional));

        harness.Channel.Failure = new Auxilia.Application.Abstractions.Channels.ChannelPermanentException(Errors.Messaging.AccountAuthenticationFailed());
        var refused = (await harness.Accounts.SendTestAsync(office.Id, "anna@example.test", "en", Ct)).Value;
        (refused.Sent, refused.ErrorCode).ShouldBe((false, "AUX-25021"));
        harness.Data.Outbound[1].Status.ShouldBe(OutboundMessageStatus.Failed);
        refused.MessageId.ShouldBe(harness.Data.Outbound[1].Id);
    }

    [Fact]
    public async Task SendTest_ServerNotReachable_IsAFailedOutcomeNotAnError()
    {
        var office = harness.AddAccount("Office", isDefault: true);
        harness.AddTemplate(MessageTemplates.AccountTest, "it", "Test", "<p>x</p>");
        harness.Channel.Failure = new System.Net.Sockets.SocketException(111);

        var outcome = (await harness.Accounts.SendTestAsync(office.Id, " anna@example.test ", "", Ct)).Value;

        (outcome.Sent, outcome.ErrorCode).ShouldBe((false, "AUX-25022"));
        var logged = harness.Data.Outbound.ShouldHaveSingleItem();
        (logged.Status, logged.Recipient, logged.Language, logged.ErrorCode).ShouldBe((OutboundMessageStatus.Failed, "anna@example.test", "it", "AUX-25022"));
        harness.Logger.Entries.ShouldContain(entry => entry.EventId.Id == EventCodes.Messaging.TestDeliveryFailed);
    }

    [Fact]
    public async Task SendTest_Refusals()
    {
        var office = harness.AddAccount("Office", isDefault: true);
        var gateway = harness.AddAccount("Wa", channel: MessageChannel.WhatsApp, provider: "http-gateway");

        (await harness.Accounts.SendTestAsync(Guid.CreateVersion7(), "a@b.test", "en", Ct)).Error!.Code.ShouldBe(EventCodes.Messaging.AccountNotFound);
        (await harness.Accounts.SendTestAsync(gateway.Id, "+39000", "en", Ct)).Error!.Code.ShouldBe(EventCodes.Messaging.ChannelNotAvailable);
        (await harness.Accounts.SendTestAsync(office.Id, "nobody", "en", Ct)).Error!.Code.ShouldBe(EventCodes.Messaging.RecipientInvalid);
        (await harness.Accounts.SendTestAsync(office.Id, "  ", "en", Ct)).Error!.Code.ShouldBe(EventCodes.Messaging.RecipientInvalid);
        (await harness.Accounts.SendTestAsync(office.Id, "a@b.test", "en", Ct)).Error!.Code.ShouldBe(EventCodes.Messaging.TemplateNotFound);
        harness.Data.Outbound.ShouldBeEmpty();
    }
}
