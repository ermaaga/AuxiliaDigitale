using Auxilia.Domain.Platform;

namespace Auxilia.Domain.Tests.Platform;

public sealed class PlatformUserTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Enrollment_SetsPasswordAndSecret_AndChangesTheStamp()
    {
        var user = new PlatformUser(Guid.CreateVersion7(), " ops@example.test ", " Ops ");
        var stamp = user.SecurityStamp;
        (user.Email, user.DisplayName, user.IsEnrolled, user.IsActive).ShouldBe(("ops@example.test", "Ops", false, true));
        user.Roles.ShouldHaveSingleItem().Role.ShouldBe(PlatformUser.SystemRole);
        Should.Throw<InvalidOperationException>(() => user.CompleteEnrollment("hash", 1));

        user.BeginEnrollment("protected");
        user.CompleteEnrollment("hash", 100);

        (user.IsEnrolled, user.TwoFactorSecret, user.PendingTwoFactorSecret, user.LastTotpStep).ShouldBe((true, "protected", null, 100L));
        user.SecurityStamp.ShouldNotBe(stamp);
    }

    [Fact]
    public void TotpSteps_AreUsedOnce()
    {
        var user = new PlatformUser(Guid.CreateVersion7(), "ops@example.test", "Ops");

        user.TryUseTotpStep(10).ShouldBeTrue();
        user.TryUseTotpStep(10).ShouldBeFalse();
        user.TryUseTotpStep(9).ShouldBeFalse();
        user.TryUseTotpStep(11).ShouldBeTrue();
    }

    [Fact]
    public void Lockout_IsProgressive_AndSuccessClearsIt()
    {
        var user = new PlatformUser(Guid.CreateVersion7(), "ops@example.test", "Ops");

        user.RecordFailedSignIn(Now, 2, TimeSpan.FromMinutes(15)).ShouldBeNull();
        user.RecordFailedSignIn(Now, 2, TimeSpan.FromMinutes(15)).ShouldBe(Now.AddMinutes(15));
        user.IsLockedOut(Now.AddMinutes(14)).ShouldBeTrue();
        user.RecordFailedSignIn(Now, 2, TimeSpan.FromMinutes(15));
        user.RecordFailedSignIn(Now, 2, TimeSpan.FromMinutes(15)).ShouldBe(Now.AddMinutes(30));

        user.RecordSuccessfulSignIn(Now.AddHours(1));
        (user.IsLockedOut(Now), user.LastLoginAt, user.LockoutCount).ShouldBe((false, Now.AddHours(1), 0));
    }

    [Fact]
    public void ResetAndDeactivation_InvalidateSessions()
    {
        var user = new PlatformUser(Guid.CreateVersion7(), "ops@example.test", "Ops");
        user.BeginEnrollment("protected");
        user.CompleteEnrollment("hash", 1);
        var stamp = user.SecurityStamp;

        user.ResetCredentials();
        (user.IsEnrolled, user.LastTotpStep).ShouldBe((false, null));
        user.SecurityStamp.ShouldNotBe(stamp);

        stamp = user.SecurityStamp;
        user.Deactivate();
        (user.IsActive, user.SecurityStamp == stamp).ShouldBe((false, false));
        user.Activate();
        user.IsActive.ShouldBeTrue();
    }
}
