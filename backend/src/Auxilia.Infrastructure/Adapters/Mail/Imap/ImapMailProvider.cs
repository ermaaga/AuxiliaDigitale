using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;

using Auxilia.Application.Abstractions.Mailbox;
using Auxilia.Domain.Mailbox;

using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;

using MailKitAuthenticationException = MailKit.Security.AuthenticationException;

namespace Auxilia.Infrastructure.Adapters.Mail.Imap;

/// <summary>
/// Where an IMAP folder sync stands, in the tokens of <see cref="SyncCursor"/> (all tied to the folder's UIDVALIDITY:
/// another value means the UIDs changed and a full sync starts again).
/// <list type="bullet">
/// <item><c>v1|validity|lastUid</c>: sync token, the next incremental sync reads the UIDs after <c>lastUid</c>;</item>
/// <item><c>f|validity|below|count|top</c>: full sync in progress, newest first: next UIDs below <c>below</c>,
/// <c>count</c> read so far, <c>top</c> the newest UID when it started (the sync token at its end);</item>
/// <item><c>i|validity|after</c>: incremental sync in progress, oldest first after <c>after</c>.</item>
/// </list>
/// </summary>
internal sealed record ImapCursor(char Kind, uint Validity, uint Uid, int Count, uint Top)
{
    public static ImapCursor Synced(uint validity, uint lastUid) => new('v', validity, lastUid, 0, lastUid);

    public static ImapCursor Full(uint validity, uint below, int count, uint top) => new('f', validity, below, count, top);

    public static ImapCursor Incremental(uint validity, uint after) => new('i', validity, after, 0, after);

    public static ImapCursor? Parse(string? token)
    {
        var parts = token?.Split('|');
        if (parts is null || parts.Length < 3 || !uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var validity)
            || !uint.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var uid))
        {
            return null;
        }

        return parts switch
        {
            ["v1", _, _] => Synced(validity, uid),
            ["i", _, _] => Incremental(validity, uid),
            ["f", _, _, var count, var top]
                when int.TryParse(count, NumberStyles.None, CultureInfo.InvariantCulture, out var read)
                && uint.TryParse(top, NumberStyles.None, CultureInfo.InvariantCulture, out var newest) => Full(validity, uid, read, newest),
            _ => null,
        };
    }

    public override string ToString() => Kind switch
    {
        'v' => string.Create(CultureInfo.InvariantCulture, $"v1|{Validity}|{Uid}"),
        'f' => string.Create(CultureInfo.InvariantCulture, $"f|{Validity}|{Uid}|{Count}|{Top}"),
        _ => string.Create(CultureInfo.InvariantCulture, $"i|{Validity}|{Uid}"),
    };

    /// <summary>The id of a message in the folder: unique while UIDVALIDITY stays the same.</summary>
    public static string MessageId(uint validity, uint uid) => string.Create(CultureInfo.InvariantCulture, $"{validity}:{uid}");

    /// <summary>The UIDs of the next page of a full sync: newest first, below <paramref name="below"/> (0 = from the top).</summary>
    public static uint[] FullPage(IEnumerable<uint> uids, uint below, int take) =>
        [.. uids.Where(uid => below == 0 || uid < below).OrderByDescending(uid => uid).Take(Math.Max(take, 0))];

    /// <summary>The UIDs of the next page of an incremental sync: oldest first, after <paramref name="after"/>.</summary>
    public static uint[] IncrementalPage(IEnumerable<uint> uids, uint after, int take) =>
        [.. uids.Where(uid => uid > after).OrderBy(uid => uid).Take(Math.Max(take, 0))];
}

/// <summary>
/// PEC / IMAP headers with MailKit (N05): TCP to the address checked against SSRF by the sync service (no second DNS
/// resolution), TLS with the host name for SNI and certificate validation (never disabled), folders opened read-only.
/// A full sync reads the newest messages delivered since the window start in pages of envelopes, flags, size and body
/// structure (never the bodies); incremental syncs read the UIDs after the last one, and their last page lists every
/// UID of the folder so deleted messages are removed. Read flags of messages already copied are not refreshed (v1).
/// </summary>
internal sealed class ImapMailProvider : IMailProvider
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>Names of the sent folder on servers without SPECIAL-USE (Italian PEC providers included).</summary>
    private static readonly string[] SentNames = ["Sent", "Sent Items", "Sent Messages", "Posta inviata", "Inviata", "Inviati", "Posta Inviata"];

    private const MessageSummaryItems Summary =
        MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.Flags | MessageSummaryItems.Size
        | MessageSummaryItems.InternalDate | MessageSummaryItems.BodyStructure;

    public ConnectedAccountProvider Provider => ConnectedAccountProvider.Imap;

    public Task<IReadOnlyList<MailFolderData>> FoldersAsync(MailboxAccess access, CancellationToken cancellationToken) =>
        WithClientAsync<IReadOnlyList<MailFolderData>>(access, async (client, token) =>
        {
            var folders = new List<MailFolderData> { new(client.Inbox.FullName, "Inbox", MailFolderKind.Inbox) };
            if (await FindSentAsync(client, token) is { } sent)
            {
                folders.Add(new MailFolderData(sent.FullName, sent.Name, MailFolderKind.Sent));
            }

            return folders;
        }, cancellationToken);

    public Task<MailPage> ReadAsync(MailPageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return WithClientAsync(request.Access, async (client, token) =>
        {
            var folder = request.Folder.Kind == MailFolderKind.Inbox && string.Equals(request.Folder.ProviderFolderId, client.Inbox.FullName, StringComparison.Ordinal)
                ? client.Inbox
                : await client.GetFolderAsync(request.Folder.ProviderFolderId, token);
            await folder.OpenAsync(FolderAccess.ReadOnly, token);
            var validity = folder.UidValidity;
            var cursor = ImapCursor.Parse(request.PageToken) ?? ImapCursor.Parse(request.SyncToken);
            if ((request.PageToken is { Length: > 0 } || request.SyncToken is { Length: > 0 }) && (cursor is null || cursor.Validity != validity))
            {
                throw new MailboxProviderException(ProviderFailure.CursorExpired, "UidValidity");
            }

            return cursor is null or { Kind: 'f' }
                ? await FullAsync(folder, validity, cursor, request, token)
                : await IncrementalAsync(folder, validity, cursor, request, token);
        }, cancellationToken);
    }

    private static async Task<MailPage> FullAsync(IMailFolder folder, uint validity, ImapCursor? cursor, MailPageRequest request, CancellationToken token)
    {
        var count = cursor?.Count ?? 0;
        var uids = (await folder.SearchAsync(SearchQuery.DeliveredAfter(request.Since.UtcDateTime.Date.AddDays(-1)), token)).Select(uid => uid.Id).ToArray();
        var top = cursor?.Top ?? (uids.Length > 0 ? uids.Max() : (folder.UidNext?.Id ?? 1) - 1);
        var page = ImapCursor.FullPage(uids.Where(uid => uid <= top), cursor?.Uid ?? 0, Math.Min(request.PageSize, request.MaxMessages - count));
        var (messages, malformed) = await FetchAsync(folder, validity, page, token);
        count += page.Length;
        var more = page.Length > 0 && count < request.MaxMessages && uids.Any(uid => uid < page[^1]);
        return more
            ? new MailPage(messages, [], null, ImapCursor.Full(validity, page[^1], count, top).ToString(), null, malformed)
            : new MailPage(messages, [], null, null, ImapCursor.Synced(validity, top).ToString(), malformed);
    }

    private static async Task<MailPage> IncrementalAsync(IMailFolder folder, uint validity, ImapCursor cursor, MailPageRequest request, CancellationToken token)
    {
        var all = (await folder.SearchAsync(SearchQuery.All, token)).Select(uid => uid.Id).ToArray();
        var page = ImapCursor.IncrementalPage(all, cursor.Uid, request.PageSize);
        var (messages, malformed) = await FetchAsync(folder, validity, page, token);
        if (page.Length > 0 && all.Any(uid => uid > page[^1]))
        {
            return new MailPage(messages, [], null, ImapCursor.Incremental(validity, page[^1]).ToString(), null, malformed);
        }

        var last = page.Length > 0 ? page[^1] : cursor.Uid;
        return new MailPage(messages, [], [.. all.Select(uid => ImapCursor.MessageId(validity, uid))], null, ImapCursor.Synced(validity, last).ToString(), malformed);
    }

    private static async Task<(List<MailMessageData> Messages, int Malformed)> FetchAsync(IMailFolder folder, uint validity, uint[] uids, CancellationToken token)
    {
        var (messages, malformed) = (new List<MailMessageData>(uids.Length), 0);
        if (uids.Length == 0)
        {
            return (messages, malformed);
        }

        var summaries = await folder.FetchAsync([.. uids.Select(uid => new UniqueId(validity, uid))], Summary, token);
        foreach (var summary in summaries)
        {
            if (Read(validity, summary) is { } message)
            {
                messages.Add(message);
            }
            else
            {
                malformed++;
            }
        }

        // A UID asked and not answered was expunged meanwhile: the next sync's UID list removes it.
        return (messages, malformed);
    }

    internal static MailMessageData? Read(uint validity, IMessageSummary summary)
    {
        if (!summary.UniqueId.IsValid || summary.Envelope is not { } envelope)
        {
            return null;
        }

        var from = envelope.From.Mailboxes.FirstOrDefault();
        var attachments = summary.Attachments?.Select(part => part.FileName).OfType<string>().Where(name => name.Length > 0).ToArray() ?? [];
        var sentAt = envelope.Date?.ToUniversalTime();
        return new MailMessageData(
            ImapCursor.MessageId(validity, summary.UniqueId.Id),
            envelope.MessageId,
            null,
            from?.Address,
            string.IsNullOrWhiteSpace(from?.Name) ? null : from.Name,
            [.. envelope.To.Mailboxes.Select(MailHeaders.Format)],
            [.. envelope.Cc.Mailboxes.Select(MailHeaders.Format)],
            envelope.Subject,
            sentAt,
            summary.InternalDate?.ToUniversalTime() ?? sentAt ?? DateTimeOffset.UnixEpoch,
            summary.Flags?.HasFlag(MessageFlags.Seen) ?? false,
            (long)(summary.Size ?? 0),
            attachments.Length > 0,
            attachments);
    }

    private static async Task<IMailFolder?> FindSentAsync(ImapClient client, CancellationToken token)
    {
        if ((client.Capabilities & (ImapCapabilities.SpecialUse | ImapCapabilities.XList)) != 0)
        {
            try
            {
                if (client.GetFolder(SpecialFolder.Sent) is { } special)
                {
                    return special;
                }
            }
            catch (NotSupportedException)
            {
                // Advertised but not usable: fall back to the usual names.
            }
        }

        if (client.PersonalNamespaces.Count == 0)
        {
            return null;
        }

        var root = client.GetFolder(client.PersonalNamespaces[0]);
        var candidates = await root.GetSubfoldersAsync(false, token);
        var inboxChildren = client.Inbox.Attributes.HasFlag(FolderAttributes.HasNoChildren) ? [] : await client.Inbox.GetSubfoldersAsync(false, token);
        return SentNames
            .Select(name => candidates.Concat(inboxChildren).FirstOrDefault(folder => string.Equals(folder.Name, name, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(folder => folder is not null);
    }

    /// <summary>Signs in for one call and classifies the failures for the sync (credentials, transient).</summary>
    private static async Task<T> WithClientAsync<T>(MailboxAccess access, Func<ImapClient, CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);
        var login = access.Imap ?? throw new ArgumentException("IMAP needs a sign-in.", nameof(access));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            using var socket = new Socket(login.Target.Address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            await socket.ConnectAsync(new IPEndPoint(login.Target.Address, login.Target.Port), timeout.Token);
            using var client = new ImapClient { Timeout = (int)Timeout.TotalMilliseconds };
            var options = login.Target.Security == ImapSecurity.SslOnConnect ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
            await client.ConnectAsync(socket, login.Target.Host, login.Target.Port, options, timeout.Token);
            await client.AuthenticateAsync(login.UserName, login.Password, timeout.Token);
            var result = await work(client, timeout.Token);
            await client.DisconnectAsync(quit: true, timeout.Token);
            return result;
        }
        catch (MailKitAuthenticationException exception)
        {
            throw new MailboxProviderException(ProviderFailure.Credentials, "AuthenticationFailed", exception);
        }
        catch (FolderNotFoundException exception)
        {
            throw new MailboxProviderException(ProviderFailure.CursorExpired, "FolderNotFound", exception);
        }
        catch (Exception exception) when (exception is SocketException or IOException or SslHandshakeException or System.Security.Authentication.AuthenticationException
            or ImapProtocolException or ImapCommandException or ProtocolException or ServiceNotConnectedException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new MailboxProviderException(ProviderFailure.Transient, exception.GetType().Name, exception);
        }
    }
}
