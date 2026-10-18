using Auxilia.Diagnostics;
using Auxilia.Domain.Mailbox;

namespace Auxilia.Domain.Tests.Mailbox;

public sealed class ConnectedAccountTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid User = Guid.CreateVersion7();
    private static readonly ImapServer Aruba = new("imaps.pec.aruba.it", 993, ImapSecurity.SslOnConnect);

    private static OAuthGrantDetails Grant(bool mail = true, bool calendar = false, string? refresh = "protected-1") =>
        new(" Mario.Rossi@Gmail.com ", " Mario Rossi ", mail, calendar, "openid email gmail.readonly", refresh);

    [Fact]
    public void ConnectOAuth_NormalizesTheAddress_AndStoresTheGrant()
    {
        var account = ConnectedAccount.ConnectOAuth(Guid.CreateVersion7(), User, ConnectedAccountProvider.Google, Grant(), "green", Now).Value;

        (account.Address, account.DisplayName, account.Color, account.HasMail, account.HasCalendar).ShouldBe(("mario.rossi@gmail.com", "Mario Rossi", "green", true, false));
        (account.ProtectedSecret, account.Status, account.ConnectedAt, account.ImapHost).ShouldBe(("protected-1", ConnectedAccountStatus.Active, Now, (string?)null));
    }

    [Fact]
    public void ConnectOAuth_RefusesAnInvalidAddressOrColour_AndImap()
    {
        var invalid = ConnectedAccount.ConnectOAuth(Guid.CreateVersion7(), User, ConnectedAccountProvider.Microsoft, Grant() with { Address = "not-an-address" }, "black", Now);

        invalid.Error!.Code.ShouldBe(EventCodes.Mailbox.AccountInvalid);
        invalid.Error.ValidationErrors.Keys.ShouldBe(["address", "color"], ignoreOrder: true);
        Should.Throw<ArgumentOutOfRangeException>(() => ConnectedAccount.ConnectOAuth(Guid.CreateVersion7(), User, ConnectedAccountProvider.Imap, Grant(), "blue", Now));
    }

    [Fact]
    public void Reconnect_AddsTheNewCapabilities_KeepsTheSecretWhenNoneCame_AndWorksAgain()
    {
        var account = ConnectedAccount.ConnectOAuth(Guid.CreateVersion7(), User, ConnectedAccountProvider.Google, Grant(), "blue", Now).Value;
        account.MarkNeedsReconnect("AUX-30011", Now);
        account.Status.ShouldBe(ConnectedAccountStatus.NeedsReconnect);

        account.Reconnect(Grant(mail: false, calendar: true, refresh: null), Now.AddHours(1));

        (account.HasMail, account.HasCalendar, account.ProtectedSecret).ShouldBe((true, true, "protected-1"));
        (account.Status, account.LastErrorCode, account.UpdatedAt).ShouldBe((ConnectedAccountStatus.Active, (string?)null, Now.AddHours(1)));

        account.Reconnect(Grant(refresh: "protected-2"), Now.AddHours(2));
        account.ProtectedSecret.ShouldBe("protected-2");
    }

    [Fact]
    public void ConnectImap_ChecksServerAndUserName_AndIsMailOnly()
    {
        var account = ConnectedAccount.ConnectImap(Guid.CreateVersion7(), User, "Studio@PEC.it", Aruba with { Host = " IMAPS.pec.aruba.it " }, " studio@pec.it ", "protected-pw", "blue", Now).Value;

        (account.Provider, account.Address, account.HasMail, account.HasCalendar).ShouldBe((ConnectedAccountProvider.Imap, "studio@pec.it", true, false));
        (account.ImapHost, account.ImapPort, account.ImapSecurity, account.ImapUserName, account.ProtectedSecret)
            .ShouldBe(("imaps.pec.aruba.it", (int?)993, (ImapSecurity?)ImapSecurity.SslOnConnect, "studio@pec.it", "protected-pw"));

        var invalid = ConnectedAccount.ConnectImap(Guid.CreateVersion7(), User, "studio@pec.it", new ImapServer("10.0.0.5", 0, (ImapSecurity)9), " ", "pw", "blue", Now);
        invalid.Error!.ValidationErrors.Keys.ShouldBe(["host", "port", "userName"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("imap.example.com", true)]
    [InlineData("mail.postecert.it", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("localhost", false)]
    [InlineData("[::1]", false)]
    [InlineData("-bad.example.com", false)]
    [InlineData("example", false)]
    public void ValidateImap_AcceptsDnsNamesOnly(string host, bool valid) =>
        ConnectedAccount.ValidateImap("a@b.it", Aruba with { Host = host }, "user").IsSuccess.ShouldBe(valid);

    [Fact]
    public void ReconnectImap_ReplacesServerAndPassword()
    {
        var account = ConnectedAccount.ConnectImap(Guid.CreateVersion7(), User, "studio@pec.it", Aruba, "studio", "old", "blue", Now).Value;
        account.MarkNeedsReconnect("AUX-30013", Now);

        account.ReconnectImap(Aruba with { Port = 143, Security = ImapSecurity.StartTls }, "studio", "new", Now.AddMinutes(5)).IsSuccess.ShouldBeTrue();

        (account.ImapPort, account.ImapSecurity, account.ProtectedSecret, account.Status).ShouldBe(((int?)143, (ImapSecurity?)ImapSecurity.StartTls, "new", ConnectedAccountStatus.Active));
        account.ReconnectImap(Aruba with { Host = "" }, "studio", "x", Now).Error!.ValidationErrors.Keys.ShouldBe(["host"]);
        Should.Throw<InvalidOperationException>(() => account.Reconnect(Grant(), Now));
    }

    [Fact]
    public void ChangeColor_AcceptsOnlyThePalette()
    {
        var account = ConnectedAccount.ConnectOAuth(Guid.CreateVersion7(), User, ConnectedAccountProvider.Google, Grant(), "blue", Now).Value;

        account.ChangeColor("purple", Now).IsSuccess.ShouldBeTrue();
        account.Color.ShouldBe("purple");
        account.ChangeColor("#ff0000", Now).Error!.ValidationErrors.Keys.ShouldBe(["color"]);
    }

    [Fact]
    public void NextColor_TakesTheFirstUnusedColour()
    {
        ConnectedAccount.NextColor([]).ShouldBe("blue");
        ConnectedAccount.NextColor(["blue", "orange"]).ShouldBe("green");
        ConnectedAccount.NextColor(ConnectedAccount.Colors).ShouldBe("blue");
    }

    [Fact]
    public void MarkNeedsReconnect_KeepsABoundedCode()
    {
        var account = ConnectedAccount.ConnectOAuth(Guid.CreateVersion7(), User, ConnectedAccountProvider.Google, Grant(), "blue", Now).Value;

        account.MarkNeedsReconnect(new string('X', 40), Now);

        account.LastErrorCode!.Length.ShouldBe(ConnectedAccount.ErrorCodeMaxLength);
        account.RotateSecret("rotated", Now);
        account.ProtectedSecret.ShouldBe("rotated");
    }
}

public sealed class OAuthRequestTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid User = Guid.CreateVersion7();

    private static OAuthRequest Request() =>
        OAuthRequest.Create(Guid.CreateVersion7(), new string('a', 64), User, ConnectedAccountProvider.Google, true, false, "verifier", Now);

    [Fact]
    public void Consume_IsSingleUse()
    {
        var request = Request();

        request.Consume(User, ConnectedAccountProvider.Google, Now.AddMinutes(9)).ShouldBe(OAuthRequestRejection.None);
        request.UsedAt.ShouldBe(Now.AddMinutes(9));
        request.Consume(User, ConnectedAccountProvider.Google, Now.AddMinutes(9)).ShouldBe(OAuthRequestRejection.AlreadyUsed);
    }

    [Fact]
    public void Consume_RefusesAnotherUser_WithoutBurningTheRequest()
    {
        var request = Request();

        request.Consume(Guid.CreateVersion7(), ConnectedAccountProvider.Google, Now).ShouldBe(OAuthRequestRejection.OtherUser);
        request.UsedAt.ShouldBeNull();
    }

    [Fact]
    public void Consume_RefusesAnotherProviderOrAnExpiredRequest_AndBurnsIt()
    {
        var wrongProvider = Request();
        wrongProvider.Consume(User, ConnectedAccountProvider.Microsoft, Now).ShouldBe(OAuthRequestRejection.OtherProvider);
        wrongProvider.Consume(User, ConnectedAccountProvider.Google, Now).ShouldBe(OAuthRequestRejection.AlreadyUsed);

        var expired = Request();
        expired.Consume(User, ConnectedAccountProvider.Google, Now + OAuthRequest.Lifetime).ShouldBe(OAuthRequestRejection.Expired);
        expired.UsedAt.ShouldNotBeNull();
    }

    [Fact]
    public void Create_NeedsACapabilityAndAnOAuthProvider()
    {
        Should.Throw<ArgumentException>(() => OAuthRequest.Create(Guid.CreateVersion7(), new string('a', 64), User, ConnectedAccountProvider.Google, false, false, "v", Now));
        Should.Throw<ArgumentOutOfRangeException>(() => OAuthRequest.Create(Guid.CreateVersion7(), new string('a', 64), User, ConnectedAccountProvider.Imap, true, false, "v", Now));
        Should.Throw<ArgumentOutOfRangeException>(() => OAuthRequest.Create(Guid.CreateVersion7(), "short", User, ConnectedAccountProvider.Google, true, false, "v", Now));
    }
}
