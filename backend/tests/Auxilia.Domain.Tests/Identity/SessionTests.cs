using Auxilia.Domain.Identity;
using Auxilia.Domain.Platform;

namespace Auxilia.Domain.Tests.Identity;

public sealed class SessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Session_IdleExpirySlidesButNeverPassesTheAbsoluteOne()
    {
        var session = new RefreshSession(Guid.CreateVersion7(), Guid.CreateVersion7(), "web", "stamp", Now, Now.AddHours(2), Now.AddHours(3), " ", new string('a', 600));

        (session.IpAddress, session.UserAgent!.Length, session.LastUsedAt).ShouldBe((null, RefreshSession.UserAgentMaxLength, Now));
        session.IsActiveAt(Now.AddHours(1)).ShouldBeTrue();
        session.IsActiveAt(Now.AddHours(2)).ShouldBeFalse();

        session.Touch(Now.AddHours(1.5), TimeSpan.FromHours(2));
        session.IdleExpiresAt.ShouldBe(Now.AddHours(3));
        session.IsActiveAt(Now.AddHours(2.5)).ShouldBeTrue();
        session.IsActiveAt(Now.AddHours(3)).ShouldBeFalse();

        new RefreshSession(Guid.CreateVersion7(), Guid.CreateVersion7(), "web", "stamp", Now, Now.AddDays(2), Now.AddDays(1), null, null)
            .IdleExpiresAt.ShouldBe(Now.AddDays(1));
    }

    [Fact]
    public void Session_EndKeepsTheFirstReason()
    {
        var session = new RefreshSession(Guid.CreateVersion7(), Guid.CreateVersion7(), "web", "stamp", Now, Now.AddHours(2), Now.AddDays(1), "10.0.0.1", null);

        session.End(Now.AddMinutes(1), SessionEndReason.Logout);
        session.End(Now.AddMinutes(2), SessionEndReason.Revoked);

        (session.EndedAt, session.EndReason).ShouldBe((Now.AddMinutes(1), SessionEndReason.Logout));
        session.IsActiveAt(Now.AddMinutes(1)).ShouldBeFalse();
    }

    [Fact]
    public void Tokens_AreUsedOnce()
    {
        var refresh = new RefreshToken(new string('a', 64), Guid.CreateVersion7(), Now);
        refresh.Consume(Now.AddMinutes(1));
        refresh.Consume(Now.AddMinutes(2));
        (refresh.IsConsumed, refresh.ConsumedAt).ShouldBe((true, Now.AddMinutes(1)));

        var link = new UserToken(Guid.CreateVersion7(), Guid.CreateVersion7(), UserTokenPurpose.PasswordReset, new string('b', 64), Now, Now.AddHours(1));
        link.IsUsableAt(Now.AddMinutes(59)).ShouldBeTrue();
        link.IsUsableAt(Now.AddHours(1)).ShouldBeFalse();
        link.Use(Now.AddMinutes(5));
        link.IsUsableAt(Now.AddMinutes(6)).ShouldBeFalse();
    }

    [Fact]
    public void SigningKey_StaysPublishedForTheGraceAfterRetirement()
    {
        var key = new SigningKey(new string('k', 32), "{}", "protected", Now);
        key.IsActive.ShouldBeTrue();
        key.IsPublishedAt(Now.AddYears(1)).ShouldBeTrue();

        key.Retire(Now.AddHours(1), TimeSpan.FromHours(2));
        key.Retire(Now.AddHours(5), TimeSpan.FromHours(2));

        key.IsActive.ShouldBeFalse();
        key.PublishedUntil.ShouldBe(Now.AddHours(3));
        key.IsPublishedAt(Now.AddHours(2.9)).ShouldBeTrue();
        key.IsPublishedAt(Now.AddHours(3)).ShouldBeFalse();
        Should.Throw<ArgumentOutOfRangeException>(() => new SigningKey(new string('k', 33), "{}", "protected", Now));
    }
}
