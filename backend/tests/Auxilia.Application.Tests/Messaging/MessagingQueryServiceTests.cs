using Auxilia.Application.Messaging;
using Auxilia.Diagnostics;
using Auxilia.Domain.Messaging;

namespace Auxilia.Application.Tests.Messaging;

public sealed class MessagingQueryServiceTests
{
    private readonly MessagingHarness harness = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Accounts_AreListedWithoutSecretsByChannelThenName()
    {
        harness.AddAccount("Zeta", isDefault: true);
        harness.AddAccount("Alfa");
        harness.AddAccount("Wa", channel: MessageChannel.WhatsApp, provider: "http-gateway");

        var accounts = await harness.Query.ListAccountsAsync(Ct);

        accounts.Select(account => (account.Name, account.Channel)).ShouldBe([("Alfa", "Email"), ("Zeta", "Email"), ("Wa", "WhatsApp")]);
        accounts[1].IsDefault.ShouldBeTrue();
        accounts.ShouldAllBe(account => account.HasSecret);
        accounts[0].Settings.GetProperty("host").GetString().ShouldBe("smtp.test");
        accounts.Select(account => account.IsAvailable).ShouldBe([true, true, false]);
        accounts.ShouldAllBe(account => !account.Settings.GetRawText().Contains("pw-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rules_AreOrderedWithAnyRoleLast()
    {
        var office = harness.AddAccount("Office", isDefault: true);
        var news = harness.AddAccount("News");
        await harness.Accounts.SetSenderRulesAsync(MessageChannel.Email,
        [
            new SenderRuleInput(MessagePurpose.Marketing, null, news.Id, 1),
            new SenderRuleInput(MessagePurpose.Notification, null, office.Id, 2),
            new SenderRuleInput(MessagePurpose.Notification, "Employee", news.Id, 1),
        ], Ct);

        var rules = await harness.Query.ListRulesAsync(MessageChannel.Email, Ct);

        rules.Select(rule => (rule.Purpose, rule.Role)).ShouldBe([("Notification", "Employee"), ("Notification", null), ("Marketing", null)]);
        (await harness.Query.ListRulesAsync(MessageChannel.WhatsApp, Ct)).ShouldBeEmpty();
        (await harness.Query.ListRulesAsync(null, Ct)).Count.ShouldBe(3);
    }

    [Fact]
    public async Task Outbound_IsPagedNewestFirstAndFiltered()
    {
        var office = harness.AddAccount("Office", isDefault: true);
        var start = DateTimeOffset.UtcNow.AddHours(-1);
        for (var index = 0; index < 5; index++)
        {
            var message = new OutboundMessage(Guid.CreateVersion7(), MessageChannel.Email, MessagePurpose.Notification, office.Id,
                $"user{index}@example.test", "code", "it", "s", "b", start.AddMinutes(index));
            if (index % 2 == 0)
            {
                message.MarkSent(start.AddMinutes(index));
            }

            harness.Data.Outbound.Add(message);
        }

        var page = (await harness.Query.ListOutboundAsync(new OutboundMessageQuery(null, null, null, null, 1, 2), Ct)).Value;
        page.TotalCount.ShouldBe(5);
        page.Items.Select(item => item.Recipient).ShouldBe(["user4@example.test", "user3@example.test"]);
        page.Items[0].CompletedAt.ShouldNotBeNull();
        page.Items[1].CompletedAt.ShouldBeNull();

        var queued = (await harness.Query.ListOutboundAsync(new OutboundMessageQuery("Email", "Queued", "USER1", office.Id, 1, 25), Ct)).Value;
        queued.Items.ShouldHaveSingleItem().Status.ShouldBe("Queued");
    }

    [Fact]
    public async Task Outbound_InvalidQuery_ListsEveryField()
    {
        var result = await harness.Query.ListOutboundAsync(new OutboundMessageQuery("Fax", "Lost", new string('x', 201), null, 0, 101), Ct);

        result.Error!.Code.ShouldBe(EventCodes.Host.ValidationFailed);
        result.Error.ValidationErrors!.Keys.ShouldBe(["page", "pageSize", "channel", "status", "search"], ignoreOrder: true);
    }

    [Fact]
    public void Input_AcceptsOnlyExactEnumNames()
    {
        MessagingInput.TryParseChannel("Email", out var channel).ShouldBeTrue();
        channel.ShouldBe(MessageChannel.Email);
        MessagingInput.TryParseChannel("email", out _).ShouldBeFalse();
        MessagingInput.TryParseChannel("1", out _).ShouldBeFalse();
        MessagingInput.TryParseChannel(null, out _).ShouldBeFalse();
        MessagingInput.TryParsePurpose("Marketing", out var purpose).ShouldBeTrue();
        purpose.ShouldBe(MessagePurpose.Marketing);
        MessagingInput.InvalidField("channel").ValidationErrors!["channel"].ShouldBe(["validation.messaging.channel"]);
    }
}
