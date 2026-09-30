using Auxilia.Domain.Identity;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Domain.Tests.Identity;

public sealed class AccountSecurityDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PasswordHistory_KeepsTheNewestHashesUpToTheMaximum()
    {
        var user = NewUser();
        for (var index = 0; index < User.MaxPasswordHistory + 3; index++)
        {
            user.SetPassword($"hash-{index}", PasswordFormat.Identity, Now.AddMinutes(index));
        }

        user.PasswordHistory.Count.ShouldBe(User.MaxPasswordHistory);
        user.PasswordHistory[0].PasswordHash.ShouldBe($"hash-{User.MaxPasswordHistory + 2}");
        user.PasswordHistory[^1].PasswordHash.ShouldBe("hash-3");
        user.PasswordHistory.ShouldAllBe(entry => entry.UserId == user.Id);
    }

    [Fact]
    public void Expiry_CountsFromTheLastChange()
    {
        var user = NewUser();
        user.IsPasswordExpired(Now, TimeSpan.FromDays(1)).ShouldBeFalse("no password yet");

        user.SetPassword("hash", PasswordFormat.Identity, Now);

        user.IsPasswordExpired(Now.AddDays(1).AddTicks(-1), TimeSpan.FromDays(1)).ShouldBeFalse();
        user.IsPasswordExpired(Now.AddDays(1), TimeSpan.FromDays(1)).ShouldBeTrue();
    }

    [Fact]
    public void LoginAttempt_TruncatesInputsAndDropsTheReasonOnSuccess()
    {
        var failed = new LoginAttempt(Guid.CreateVersion7(), null, " " + new string('u', 300) + " ", "password", Now, false, new string('r', 80), new string('1', 80), " ");
        var succeeded = new LoginAttempt(Guid.CreateVersion7(), Guid.CreateVersion7(), "mario", "email-otp", Now, true, "ignored", "10.0.0.1", "ua");

        (failed.UserName.Length, failed.FailureReason!.Length, failed.IpAddress!.Length, failed.UserAgent).ShouldBe(
            (LoginAttempt.UserNameMaxLength, LoginAttempt.ReasonMaxLength, RefreshSession.IpMaxLength, null));
        (succeeded.FailureReason, succeeded.Succeeded, succeeded.Method).ShouldBe((null, true, "email-otp"));
        Should.Throw<ArgumentException>(() => new LoginAttempt(Guid.CreateVersion7(), null, "x", " ", Now, true, null, null, null));
    }

    [Fact]
    public void Session_CanMoveToANewSecurityStamp()
    {
        var session = new RefreshSession(Guid.CreateVersion7(), Guid.CreateVersion7(), "web", "old", Now, Now.AddHours(1), Now.AddDays(1), null, null);

        session.RenewSecurityStamp("new");

        session.SecurityStamp.ShouldBe("new");
        Should.Throw<ArgumentException>(() => session.RenewSecurityStamp(""));
    }

    private static User NewUser() =>
        User.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "mario.rossi", null, "it", [TenantRole.Client], isActive: true).Value;
}
