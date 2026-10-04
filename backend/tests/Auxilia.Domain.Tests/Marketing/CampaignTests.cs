using Auxilia.Diagnostics;
using Auxilia.Domain.Marketing;

namespace Auxilia.Domain.Tests.Marketing;

public sealed class CampaignTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Templates_NeedValidLiquid()
    {
        var template = new EmailTemplate(Guid.NewGuid());
        template.Update(" Spring ", "it", " Hello {{ firstName }} ", "<p>Hi</p>", _ => true).IsSuccess.ShouldBeTrue();
        (template.Name, template.Subject).ShouldBe(("Spring", "Hello {{ firstName }}"));

        var invalid = template.Update("", "", "", " ", text => !text.Contains("{%", StringComparison.Ordinal));
        invalid.Error!.Code.ShouldBe(EventCodes.Marketing.TemplateInvalid);
        invalid.Error.ValidationErrors.Keys.ShouldBe(["name", "language", "subject", "body"], ignoreOrder: true);
        template.Update("x", "it", "ok", "{% broken", text => !text.Contains("{%", StringComparison.Ordinal)).Error!.ValidationErrors.Keys.ShouldBe(["body"]);
    }

    [Fact]
    public void Campaigns_AreSentOnce_AndCountTheirRecipients()
    {
        var campaign = new Campaign(Guid.NewGuid());
        campaign.Update("Spring", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).Error!.ValidationErrors.ShouldContainKey("audience");
        campaign.Update(" Spring ", Guid.NewGuid(), null, Guid.NewGuid()).IsSuccess.ShouldBeTrue();

        campaign.Send(null, Now).IsSuccess.ShouldBeTrue();
        campaign.Send(null, Now).Error!.Code.ShouldBe(EventCodes.Marketing.CampaignNotDraft);
        campaign.Update("Other", Guid.NewGuid(), null, Guid.NewGuid()).Error!.Code.ShouldBe(EventCodes.Marketing.CampaignNotDraft);
        campaign.Snapshot(5, 2);
        campaign.RecordSent(2, 1);
        campaign.Complete(Now);
        (campaign.Status, campaign.RecipientCount, campaign.ExcludedCount, campaign.SentCount, campaign.FailedCount, campaign.StartedAt)
            .ShouldBe((CampaignStatus.Sent, 5, 2, 2, 1, (DateTimeOffset?)Now));
        campaign.Cancel(Now).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Campaigns_CanBeCancelledWhileSending_OrFail()
    {
        var sending = new Campaign(Guid.NewGuid());
        sending.Update("A", Guid.NewGuid(), Guid.NewGuid(), null);
        sending.Send(null, Now);
        sending.Cancel(Now).IsSuccess.ShouldBeTrue();
        sending.Complete(Now);
        sending.Status.ShouldBe(CampaignStatus.Cancelled);

        var failed = new Campaign(Guid.NewGuid());
        failed.Fail("AUX-19027", new string('x', 600), Now);
        (failed.Status, failed.ErrorMessage!.Length).ShouldBe((CampaignStatus.Failed, Campaign.ErrorMaxLength));
    }

    [Fact]
    public void RecipientsAndSuppressions_KeepTheirOutcome()
    {
        var pending = new CampaignRecipient(Guid.NewGuid(), Guid.NewGuid(), "a@example.test", null);
        var excluded = new CampaignRecipient(Guid.NewGuid(), Guid.NewGuid(), null, ExclusionReason.NoEmail);
        (pending.Status, excluded.Status, excluded.Exclusion).ShouldBe((CampaignRecipientStatus.Pending, CampaignRecipientStatus.Excluded, (ExclusionReason?)ExclusionReason.NoEmail));
        var message = Guid.NewGuid();
        pending.Sent(message, Now);
        (pending.Status, pending.OutboundMessageId).ShouldBe((CampaignRecipientStatus.Sent, (Guid?)message));
        pending.Failed("AUX-25011", Now);
        pending.Exclude(ExclusionReason.Cancelled, Now);
        (pending.Status, pending.Exclusion, pending.ErrorCode).ShouldBe((CampaignRecipientStatus.Excluded, (ExclusionReason?)ExclusionReason.Cancelled, "AUX-25011"));

        Suppression.Create(Guid.NewGuid(), " a@example.test ", " asked ", Now).Value.ShouldSatisfyAllConditions(
            suppression => suppression.Email.ShouldBe("a@example.test"), suppression => suppression.Reason.ShouldBe("asked"));
        Suppression.Create(Guid.NewGuid(), "nope", new string('r', 201), Now).Error!.ValidationErrors.Keys.ShouldBe(["email", "reason"], ignoreOrder: true);
    }
}
