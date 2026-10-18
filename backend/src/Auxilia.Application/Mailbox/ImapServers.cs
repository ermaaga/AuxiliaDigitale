using Auxilia.Domain.Mailbox;

namespace Auxilia.Application.Mailbox;

/// <summary>IMAP servers of well-known providers, suggested from the domain of the address (the user can change them).</summary>
internal static class ImapServers
{
    private static readonly Dictionary<string, ImapServer> ByDomain = new(StringComparer.OrdinalIgnoreCase)
    {
        // PEC
        ["pec.it"] = new("imaps.pec.aruba.it", 993, ImapSecurity.SslOnConnect),
        ["arubapec.it"] = new("imaps.pec.aruba.it", 993, ImapSecurity.SslOnConnect),
        ["legalmail.it"] = new("mbox.cert.legalmail.it", 993, ImapSecurity.SslOnConnect),
        ["postecert.it"] = new("mail.postecert.it", 993, ImapSecurity.SslOnConnect),

        // Ordinary mailboxes reachable over IMAP with an app password
        ["gmail.com"] = new("imap.gmail.com", 993, ImapSecurity.SslOnConnect),
        ["googlemail.com"] = new("imap.gmail.com", 993, ImapSecurity.SslOnConnect),
        ["outlook.com"] = new("outlook.office365.com", 993, ImapSecurity.SslOnConnect),
        ["hotmail.com"] = new("outlook.office365.com", 993, ImapSecurity.SslOnConnect),
        ["live.com"] = new("outlook.office365.com", 993, ImapSecurity.SslOnConnect),
        ["libero.it"] = new("imapmail.libero.it", 993, ImapSecurity.SslOnConnect),
    };

    public static ImapServer? Suggest(string? address)
    {
        var normalized = ConnectedAccount.NormalizeAddress(address);
        var at = normalized.LastIndexOf('@');
        return at > 0 && ByDomain.TryGetValue(normalized[(at + 1)..], out var server) ? server : null;
    }
}
