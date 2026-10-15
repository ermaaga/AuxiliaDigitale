using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;

using Auxilia.Application.Abstractions.Mailbox;
using Auxilia.Diagnostics;
using Auxilia.Domain.Mailbox;

using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;

using Microsoft.Extensions.Logging;

using MailKitAuthenticationException = MailKit.Security.AuthenticationException;

namespace Auxilia.Infrastructure.Adapters.Mail.Imap;

/// <summary>
/// Connection test of a PEC / IMAP account (N05): TCP to the address already checked against SSRF (no second DNS
/// resolution), TLS with the host name for SNI and certificate validation (never disabled), sign-in, inbox opened
/// read-only, disconnect. Nothing is read. Passwords and addresses are never logged.
/// </summary>
internal sealed class ImapConnectionTester(ILogger<ImapConnectionTester> logger) : IImapConnectionTester
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    public async Task<ImapTestOutcome> TestAsync(ImapConnectionTarget target, string userName, string password, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrEmpty(userName);
        ArgumentException.ThrowIfNullOrEmpty(password);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            using var socket = new Socket(target.Address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            await socket.ConnectAsync(new IPEndPoint(target.Address, target.Port), timeout.Token);

            using var client = new ImapClient { Timeout = (int)Timeout.TotalMilliseconds };
            var options = target.Security == ImapSecurity.SslOnConnect ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
            await client.ConnectAsync(socket, target.Host, target.Port, options, timeout.Token);
            await client.AuthenticateAsync(userName, password, timeout.Token);
            await client.Inbox.OpenAsync(FolderAccess.ReadOnly, timeout.Token);
            await client.DisconnectAsync(quit: true, timeout.Token);
            return ImapTestOutcome.Success;
        }
        catch (MailKitAuthenticationException)
        {
            return ImapTestOutcome.AuthenticationFailed;
        }
        catch (Exception exception) when (exception is SocketException or IOException or SslHandshakeException or System.Security.Authentication.AuthenticationException
            or ImapProtocolException or ImapCommandException or ProtocolException or NotSupportedException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            Log.Mailbox.ImapConnectionFailed(logger, exception, target.Port, exception.GetType().Name);
            return ImapTestOutcome.ConnectionFailed;
        }
    }
}
