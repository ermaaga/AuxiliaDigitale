using System.Text.Json;

using Auxilia.Application.Abstractions.Channels;
using Auxilia.Diagnostics;
using Auxilia.Infrastructure.Adapters.Channels.Smtp;
using Auxilia.Tests.Common;

namespace Auxilia.Infrastructure.Tests.Adapters;

public sealed class SmtpEmailChannelTests : IAsyncDisposable
{
    private readonly FakeSmtpServer server = new();
    private readonly SmtpEmailChannel channel = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Send_AuthenticatesAndDeliversHtmlWithSenderAndSubject()
    {
        await channel.SendAsync(new ChannelMessage("anna@example.test", "Hello Anna", "<p>Ciao</p>"), Account(username: "sender"), Ct);

        var mail = server.Messages.ShouldHaveSingleItem();
        mail.From.ShouldBe("noreply@studio.test");
        mail.Recipients.ShouldBe(["anna@example.test"]);
        mail.Data.ShouldContain("Subject: Hello Anna");
        mail.Data.ShouldContain("From: Studio <noreply@studio.test>");
        mail.Data.ShouldContain("text/html");
    }

    [Fact]
    public async Task RefusedRecipient_IsPermanent()
    {
        var exception = await Should.ThrowAsync<ChannelPermanentException>(() =>
            channel.SendAsync(new ChannelMessage("reject@example.test", "s", "b"), Account(), Ct));

        exception.Error.Code.ShouldBe(EventCodes.Messaging.RecipientInvalid);
    }

    [Fact]
    public async Task WrongPassword_IsPermanent()
    {
        var exception = await Should.ThrowAsync<ChannelPermanentException>(() =>
            channel.SendAsync(new ChannelMessage("anna@example.test", "s", "b"), Account(username: "sender", password: "wrong"), Ct));

        exception.Error.Code.ShouldBe(EventCodes.Messaging.AccountAuthenticationFailed);
    }

    [Fact]
    public async Task TemporaryRefusalOrUnreachableServer_IsTransient()
    {
        var busy = await Should.ThrowAsync<Exception>(() => channel.SendAsync(new ChannelMessage("busy@example.test", "s", "b"), Account(), Ct));
        busy.ShouldNotBeOfType<ChannelPermanentException>();

        var unreachable = Settings(port: 1);
        var down = await Should.ThrowAsync<Exception>(() => channel.SendAsync(new ChannelMessage("anna@example.test", "s", "b"), new ChannelAccount(unreachable, null), Ct));
        down.ShouldNotBeOfType<ChannelPermanentException>();
    }

    [Fact]
    public async Task InvalidRecipientOrSettings_ArePermanent()
    {
        (await Should.ThrowAsync<ChannelPermanentException>(() => channel.SendAsync(new ChannelMessage("not-an-address", "s", "b"), Account(), Ct)))
            .Error.Code.ShouldBe(EventCodes.Messaging.RecipientInvalid);
        (await Should.ThrowAsync<ChannelPermanentException>(() => channel.SendAsync(new ChannelMessage("anna@example.test", "s", "b"), new ChannelAccount("{", null), Ct)))
            .Error.Code.ShouldBe(EventCodes.Messaging.AccountSettingsInvalid);
    }

    [Theory]
    [InlineData("""{"host":"smtp.example.test","port":587,"security":"StartTls","fromAddress":"a@b.test"}""", true)]
    [InlineData("""{"host":"smtp.example.test","port":587,"security":"StartTls","username":"u","fromAddress":"a@b.test","fromName":"A"}""", true)]
    [InlineData("""{"host":"","port":587,"security":"None","fromAddress":"a@b.test"}""", false)]
    [InlineData("""{"host":"h","port":70000,"security":"None","fromAddress":"a@b.test"}""", false)]
    [InlineData("""{"host":"h","port":25,"security":"Magic","fromAddress":"a@b.test"}""", false)]
    [InlineData("""{"host":"h","port":25,"security":"None","fromAddress":"nobody"}""", false)]
    [InlineData("not json", false)]
    public void Settings_AreValidated(string json, bool valid) => channel.AreSettingsValid(json).ShouldBe(valid);

    [Fact]
    public void Identity_IsSmtpEmail()
    {
        channel.Provider.ShouldBe("smtp");
        channel.Channel.ShouldBe(Domain.Messaging.MessageChannel.Email);
        channel.IsValidRecipient(" ").ShouldBeFalse();
    }

    public ValueTask DisposeAsync() => server.DisposeAsync();

    private ChannelAccount Account(string? username = null, string password = FakeSmtpServer.Password) =>
        new(Settings(server.Port, username), username is null ? null : password);

    private static string Settings(int port, string? username = null) => JsonSerializer.Serialize(
        new SmtpSettings("127.0.0.1", port, SmtpSecurity.None, username, "noreply@studio.test", "Studio"), SmtpSettings.Json);
}
