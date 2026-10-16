using System.Net.Security;

using MailKit.Net.Imap;

namespace Auxilia.Infrastructure.Adapters.Mail.Imap;

/// <summary>
/// How the IMAP adapters validate the server certificate (N05: TLS always, validation never disabled). The application
/// registers <see cref="System"/>, i.e. MailKit's default validation against the operating system trust store with the
/// host name of the account. The integration tests pass a callback that still validates chain and name, against the
/// root of the test server instead of the system store.
/// </summary>
internal sealed class ImapCertificateTrust(RemoteCertificateValidationCallback? validation)
{
    public static ImapCertificateTrust System { get; } = new(null);

    public void Apply(ImapClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (validation is not null)
        {
            client.ServerCertificateValidationCallback = validation;
        }
    }
}
