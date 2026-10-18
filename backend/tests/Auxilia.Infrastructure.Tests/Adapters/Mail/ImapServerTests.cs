using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

using Auxilia.Application.Abstractions.Mailbox;
using Auxilia.Domain.Mailbox;
using Auxilia.Infrastructure.Adapters.Mail.Imap;

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;

using Microsoft.Extensions.Logging.Abstractions;

using MimeKit;

namespace Auxilia.Infrastructure.Tests.Adapters.Mail;

/// <summary>
/// A GreenMail IMAPS server (Apache-2.0 image) with a certificate made by the test for <see cref="Host"/>: the adapters
/// validate it like a real server's, trusting only that certificate instead of the system store (C-08, N05).
/// </summary>
public sealed class ImapServerFixture : IAsyncLifetime
{
    public const string Host = "imap.example.test";
    public const string Address = "studio@example.test";
    public const string Password = "Imap-test-Passw0rd";

    private const int ImapsPort = 3993;
    private const string KeystorePassword = "greenmail-test";

    private readonly X509Certificate2 certificate = CreateCertificate();
    private readonly IContainer container;

    public ImapServerFixture()
    {
        container = new ContainerBuilder("greenmail/standalone:2.1.5")
            .WithResourceMapping(certificate.Export(X509ContentType.Pkcs12, KeystorePassword), "/home/greenmail/test.p12")
            .WithEnvironment("GREENMAIL_OPTS", string.Join(' ',
                "-Dgreenmail.setup.test.imaps",
                "-Dgreenmail.hostname=0.0.0.0",
                "-Dgreenmail.tls.keystore.file=/home/greenmail/test.p12",
                $"-Dgreenmail.tls.keystore.password={KeystorePassword}",
                $"-Dgreenmail.users={Address.Split('@')[0]}:{Password}@{Address.Split('@')[1]}",
                "-Dgreenmail.users.login=email"))
            .WithPortBinding(ImapsPort, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(ImapsPort))
            .Build();
    }

    /// <summary>Validation of the tests: chain and host name checked, the test certificate as the only root.</summary>
    internal ImapCertificateTrust Trust => new((_, presented, _, errors) =>
    {
        if (presented is null || (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != SslPolicyErrors.None)
        {
            return false;
        }

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(certificate);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        using var leaf = X509CertificateLoader.LoadCertificate(presented.GetRawCertData());
        return chain.Build(leaf);
    });

    public async Task<ImapConnectionTarget> TargetAsync(string host = Host) =>
        new(host, (await Dns.GetHostAddressesAsync(container.Hostname)).First(address => address.AddressFamily == AddressFamily.InterNetwork),
            container.GetMappedPublicPort(ImapsPort), ImapSecurity.SslOnConnect);

    public async Task<MailboxAccess> AccessAsync(string password = Password) =>
        MailboxAccess.Password(new ImapLogin(await TargetAsync(), Address, password));

    /// <summary>Appends a message to a folder of the test mailbox (created when missing) and returns its UID.</summary>
    public async Task<uint> AppendAsync(string folder, string subject, DateTimeOffset date, bool seen = false, string? attachment = null)
    {
        var message = new MimeMessage { Subject = subject, Date = date, MessageId = $"{Guid.NewGuid():N}@example.com" };
        message.From.Add(new MailboxAddress("Mario Rossi", "mario@example.com"));
        message.To.Add(new MailboxAddress("Studio", Address));
        var body = new BodyBuilder { TextBody = "Testo di prova." };
        if (attachment is not null)
        {
            body.Attachments.Add(attachment, "%PDF-1.7\n"u8.ToArray(), new ContentType("application", "pdf"));
        }

        message.Body = body.ToMessageBody();

        using var client = await ConnectAsync();
        var target = string.Equals(folder, "INBOX", StringComparison.Ordinal)
            ? client.Inbox
            : (await client.GetFolder(client.PersonalNamespaces[0]).GetSubfoldersAsync()).FirstOrDefault(item => item.Name == folder)
                ?? await client.GetFolder(client.PersonalNamespaces[0]).CreateAsync(folder, isMessageFolder: true);
        var uid = await target.ShouldNotBeNull().AppendAsync(new AppendRequest(message, seen ? MessageFlags.Seen : MessageFlags.None, date));
        await client.DisconnectAsync(quit: true);
        return uid!.Value.Id;
    }

    public async Task DeleteAsync(uint uid)
    {
        using var client = await ConnectAsync();
        await client.Inbox.OpenAsync(FolderAccess.ReadWrite);
        await client.Inbox.AddFlagsAsync(new UniqueId(client.Inbox.UidValidity, uid), MessageFlags.Deleted, silent: true);
        await client.Inbox.ExpungeAsync();
        await client.DisconnectAsync(quit: true);
    }

    public ValueTask InitializeAsync() => new(container.StartAsync());

    public async ValueTask DisposeAsync()
    {
        await container.DisposeAsync();
        certificate.Dispose();
    }

    private async Task<ImapClient> ConnectAsync()
    {
        var target = await TargetAsync();
        var socket = new Socket(target.Address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(new IPEndPoint(target.Address, target.Port));
        var client = new ImapClient();
        Trust.Apply(client);
        await client.ConnectAsync(socket, Host, target.Port, SecureSocketOptions.SslOnConnect);
        await client.AuthenticateAsync(Address, Password);
        return client;
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={Host}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(Host);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], critical: false));
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: false, hasPathLengthConstraint: false, 0, critical: true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(7));
    }
}

/// <summary>
/// The IMAP adapters against a real server (N05 quality criterion): connection test with TLS validation and credentials,
/// folders, full sync newest first in pages, incremental sync with deletions, original message read live.
/// </summary>
public sealed class ImapServerTests(ImapServerFixture server) : IClassFixture<ImapServerFixture>
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ConnectionTest_SignsInOverValidatedTls_AndClassifiesFailures()
    {
        var tester = new ImapConnectionTester(server.Trust, NullLogger<ImapConnectionTester>.Instance);
        var target = await server.TargetAsync();

        (await tester.TestAsync(target, ImapServerFixture.Address, ImapServerFixture.Password, Ct)).ShouldBe(ImapTestOutcome.Success);
        (await tester.TestAsync(target, ImapServerFixture.Address, "wrong password", Ct)).ShouldBe(ImapTestOutcome.AuthenticationFailed);

        // Validation is never off: the system store does not know the test certificate, another host name does not match it.
        var system = new ImapConnectionTester(ImapCertificateTrust.System, NullLogger<ImapConnectionTester>.Instance);
        (await system.TestAsync(target, ImapServerFixture.Address, ImapServerFixture.Password, Ct)).ShouldBe(ImapTestOutcome.ConnectionFailed);
        (await tester.TestAsync(await server.TargetAsync("other.example.test"), ImapServerFixture.Address, ImapServerFixture.Password, Ct))
            .ShouldBe(ImapTestOutcome.ConnectionFailed);
    }

    [Fact]
    public async Task Sync_ReadsHeadersInPages_ThenOnlyWhatChanged_AndOpensTheOriginal()
    {
        var provider = new ImapMailProvider(server.Trust);
        var access = await server.AccessAsync();
        await server.AppendAsync("INBOX", "Vecchio", Now.AddDays(-200));
        var first = await server.AppendAsync("INBOX", "Primo", Now.AddDays(-3), seen: true);
        await server.AppendAsync("INBOX", "Secondo", Now.AddDays(-2), attachment: "fattura.pdf");
        await server.AppendAsync("INBOX", "Terzo", Now.AddDays(-1));
        await server.AppendAsync("Sent", "Risposta", Now.AddDays(-1), seen: true);

        var folders = await provider.FoldersAsync(access, Ct);
        folders.Select(folder => folder.Kind).ShouldBe([MailFolderKind.Inbox, MailFolderKind.Sent]);
        var inbox = folders[0];

        // Full sync: the newest first, pages of 2, only the window (90 days), headers only.
        var page1 = await provider.ReadAsync(new MailPageRequest(access, inbox, null, null, Now.AddDays(-90), 2000, 2), Ct);
        // The two newest UIDs (the server answers them in UID order).
        page1.Messages.Select(message => message.Subject).ShouldBe(["Terzo", "Secondo"], ignoreOrder: true);
        page1.NextPageToken.ShouldNotBeNull();
        var secondo = page1.Messages.Single(message => message.Subject == "Secondo");
        (secondo.FromAddress, secondo.FromName, secondo.IsRead, secondo.HasAttachments).ShouldBe(("mario@example.com", "Mario Rossi", false, true));
        secondo.AttachmentNames.ShouldBe(["fattura.pdf"]);
        secondo.To.ShouldHaveSingleItem().ShouldContain(ImapServerFixture.Address);

        var page2 = await provider.ReadAsync(new MailPageRequest(access, inbox, page1.NextPageToken, null, Now.AddDays(-90), 2000, 2), Ct);
        var primo = page2.Messages.ShouldHaveSingleItem();
        (primo.Subject, primo.IsRead).ShouldBe(("Primo", true));
        page2.NextPageToken.ShouldBeNull();
        page2.SyncToken.ShouldNotBeNull();

        // Incremental: only the new message, and the ids still present (the deleted one is gone).
        await server.AppendAsync("INBOX", "Quarto", Now);
        await server.DeleteAsync(first);
        var changes = await provider.ReadAsync(new MailPageRequest(access, inbox, null, page2.SyncToken, Now.AddDays(-90), 2000, 100), Ct);
        changes.Messages.ShouldHaveSingleItem().Subject.ShouldBe("Quarto");
        changes.Present.ShouldNotBeNull().ShouldNotContain(primo.ProviderMessageId);
        changes.Present.ShouldContain(secondo.ProviderMessageId);

        // The original, read live; a message no longer there is null.
        await using (var raw = await provider.OpenRawAsync(access, inbox, secondo.ProviderMessageId, Ct))
        {
            var original = await MimeMessage.LoadAsync(raw.ShouldNotBeNull(), Ct);
            (original.Subject, original.Attachments.Single().ContentDisposition?.FileName).ShouldBe(("Secondo", "fattura.pdf"));
        }

        (await provider.OpenRawAsync(access, inbox, primo.ProviderMessageId, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task WrongPassword_IsACredentialsFailure_ForTheSync()
    {
        var provider = new ImapMailProvider(server.Trust);

        var access = await server.AccessAsync("wrong password");

        var failure = await Should.ThrowAsync<MailboxProviderException>(() => provider.FoldersAsync(access, Ct));

        failure.Failure.ShouldBe(ProviderFailure.Credentials);
    }
}
