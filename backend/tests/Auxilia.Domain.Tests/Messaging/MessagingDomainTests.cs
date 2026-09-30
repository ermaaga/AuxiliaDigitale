using Auxilia.Diagnostics;
using Auxilia.Domain.Messaging;

namespace Auxilia.Domain.Tests.Messaging;

public sealed class MessagingDomainTests
{
    [Fact]
    public void Account_ValidatesAndKeepsTheDefaultActive()
    {
        MessagingAccount.Create(Guid.CreateVersion7(), MessageChannel.Email, "", "A", "{}", null).Error!.Code.ShouldBe(EventCodes.Messaging.AccountSettingsInvalid);
        MessagingAccount.Create(Guid.CreateVersion7(), MessageChannel.Email, "smtp", new string('a', 101), "{}", null).IsFailure.ShouldBeTrue();
        MessagingAccount.Create(Guid.CreateVersion7(), MessageChannel.Email, "smtp", "A", " ", null).IsFailure.ShouldBeTrue();

        var account = MessagingAccount.Create(Guid.CreateVersion7(), MessageChannel.Email, "smtp", "A", "{}", "s").Value;
        account.IsActive.ShouldBeTrue();
        account.SetDefault(true).IsSuccess.ShouldBeTrue();
        account.SetActive(false).Error!.Code.ShouldBe(EventCodes.Messaging.DefaultAccountMustBeActive);
        account.SetDefault(false);
        account.SetActive(false).IsSuccess.ShouldBeTrue();
        account.SetDefault(true).Error!.Code.ShouldBe(EventCodes.Messaging.DefaultAccountMustBeActive);
        account.Update("B", "{}", null).IsSuccess.ShouldBeTrue();
        account.SecretProtected.ShouldBe("s");
        account.Update("", "{}", null).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Rule_KeepsItsValuesAndLimitsTheRole()
    {
        var accountId = Guid.CreateVersion7();
        var rule = new SenderRule(Guid.CreateVersion7(), MessageChannel.Email, MessagePurpose.Marketing, "Employee", accountId, 3);

        (rule.Channel, rule.Purpose, rule.Role, rule.AccountId, rule.Priority).ShouldBe((MessageChannel.Email, MessagePurpose.Marketing, "Employee", accountId, 3));
        Should.Throw<ArgumentOutOfRangeException>(() => new SenderRule(Guid.CreateVersion7(), MessageChannel.Email, MessagePurpose.Marketing, new string('r', 31), accountId, 1));
    }

    [Fact]
    public void Template_CustomisedContentIsNeverUpgraded()
    {
        var template = new MessageTemplate(Guid.CreateVersion7(), MessageChannel.Email, "password-reset", "it", "S", "B", isSystem: true);

        template.UpgradeSystemContent("S2", "B2").ShouldBeTrue();
        template.Customize("Mine", "Body");
        template.UpgradeSystemContent("S3", "B3").ShouldBeFalse();

        (template.Subject, template.Body, template.IsCustomized, template.IsSystem).ShouldBe(("Mine", "Body", true, true));
        Should.Throw<ArgumentException>(() => template.Customize("x", " "));
        Should.Throw<ArgumentOutOfRangeException>(() => new MessageTemplate(Guid.CreateVersion7(), MessageChannel.Email, "c", "it", new string('s', 301), "b", false));
    }

    [Fact]
    public void Outbound_GoesFromQueuedToSentOrFailedOnce()
    {
        var sent = Queued();
        sent.RecordFailedAttempt("AUX-25018");
        sent.MarkSent(DateTimeOffset.UtcNow);
        (sent.Status, sent.Attempts, sent.ErrorCode, sent.IsFinal).ShouldBe((OutboundMessageStatus.Sent, 2, null, true));
        Should.Throw<InvalidOperationException>(() => sent.MarkFailed("AUX-1", DateTimeOffset.UtcNow));

        var failed = Queued();
        failed.MarkFailed("AUX-25016", DateTimeOffset.UtcNow);
        (failed.Status, failed.ErrorCode, failed.FailedAt is not null).ShouldBe((OutboundMessageStatus.Failed, "AUX-25016", true));
        Should.Throw<InvalidOperationException>(() => failed.RecordFailedAttempt("AUX-1"));
        Should.Throw<ArgumentException>(() => new OutboundMessage(Guid.CreateVersion7(), MessageChannel.Email, MessagePurpose.Marketing, Guid.CreateVersion7(), " ", null, "it", "s", "b", DateTimeOffset.UtcNow));
    }

    private static OutboundMessage Queued() =>
        new(Guid.CreateVersion7(), MessageChannel.Email, MessagePurpose.Transactional, Guid.CreateVersion7(), "a@b.test", "t", "it", "s", "b", DateTimeOffset.UtcNow, "Case", Guid.CreateVersion7());
}
