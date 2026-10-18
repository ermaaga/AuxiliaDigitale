using System.Globalization;
using System.Net;
using System.Text.Json;

using Auxilia.Application.Abstractions.Mailbox;
using Auxilia.Domain.Mailbox;
using Auxilia.Infrastructure.Adapters.Mailbox;

using MimeKit;
using MimeKit.Utils;

namespace Auxilia.Infrastructure.Adapters.Mail;

/// <summary>Reading of address and date headers shared by the mail adapters (MimeKit parsers, no network).</summary>
internal static class MailHeaders
{
    /// <summary>The first mailbox of an address header: address and display name.</summary>
    public static (string? Address, string? Name) First(string? header)
    {
        if (string.IsNullOrWhiteSpace(header) || !InternetAddressList.TryParse(header, out var list))
        {
            return (null, null);
        }

        var mailbox = list.Mailboxes.FirstOrDefault();
        return mailbox is null ? (null, null) : (mailbox.Address, string.IsNullOrWhiteSpace(mailbox.Name) ? null : mailbox.Name);
    }

    /// <summary>The recipients of an address header as <c>Name &lt;address&gt;</c> (the address alone without a name).</summary>
    public static IReadOnlyList<string> Recipients(string? header) =>
        string.IsNullOrWhiteSpace(header) || !InternetAddressList.TryParse(header, out var list)
            ? []
            : [.. list.Mailboxes.Select(Format)];

    public static string Format(MailboxAddress mailbox) =>
        string.IsNullOrWhiteSpace(mailbox.Name) ? mailbox.Address : $"{mailbox.Name} <{mailbox.Address}>";

    public static string Format(string? name, string? address) =>
        string.IsNullOrWhiteSpace(name) ? address ?? string.Empty : string.IsNullOrWhiteSpace(address) ? name : $"{name} <{address}>";

    public static DateTimeOffset? Date(string? header) =>
        !string.IsNullOrWhiteSpace(header) && DateUtils.TryParse(header, out var date) ? date.ToUniversalTime() : null;
}

/// <summary>
/// Gmail (ADR 0021, plain HTTP): the INBOX and SENT labels. A full sync lists the newest messages received after the
/// window start (<c>messages.list</c> with <c>q=after:</c>, newest first, bounded) after noting the mailbox
/// <c>historyId</c>; then <c>history.list</c> from it per label (404 → full sync again). Each header is read with
/// <c>messages.get</c> asking only headers, labels, size and attachment names (<c>fields</c>): bodies never travel.
/// </summary>
internal sealed class GmailMailProvider(IHttpClientFactory httpClients) : ProviderHttpBase(httpClients), IMailProvider
{
    public const string ApiBase = "https://gmail.googleapis.com/gmail/v1/users/me/";

    /// <summary>The parts of a message read: no body data (the <c>fields</c> filter of the Gmail API).</summary>
    public const string MessageFields =
        "id,threadId,labelIds,sizeEstimate,internalDate,payload(headers,filename,parts(filename,parts(filename,parts(filename))))";

    private const string FullPrefix = "g1";
    private const string HistoryPrefix = "h1";

    private static readonly string[] RateReasons = ["rateLimitExceeded", "userRateLimitExceeded", "quotaExceeded"];

    public ConnectedAccountProvider Provider => ConnectedAccountProvider.Google;

    public Task<IReadOnlyList<MailFolderData>> FoldersAsync(MailboxAccess access, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MailFolderData>>([new("INBOX", "Inbox", MailFolderKind.Inbox), new("SENT", "Sent", MailFolderKind.Sent)]);

    public async Task<MailPage> ReadAsync(MailPageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var token = request.Access.AccessToken ?? throw new ArgumentException("Gmail needs an access token.", nameof(request));
        return request.SyncToken is { Length: > 0 } historyId
            ? await HistoryAsync(request, token, historyId, cancellationToken)
            : await FullAsync(request, token, cancellationToken);
    }

    /// <summary>
    /// <c>messages.get?format=raw</c>: the message in base64url inside a JSON answer (cut at a bound that fits the largest
    /// message read), decoded into a temporary file in blocks.
    /// </summary>
    public async Task<Stream?> OpenRawAsync(MailboxAccess access, MailFolderData folder, string providerMessageId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);
        var token = access.AccessToken ?? throw new ArgumentException("Gmail needs an access token.", nameof(access));
        var uri = new Uri($"{ApiBase}messages/{Uri.EscapeDataString(providerMessageId)}?{Query([new("format", "raw"), new("fields", "raw")])}");
        await using var json = await DownloadAsync(uri, token, (MailRaw.MaxBytes / 3 * 4) + 64 * 1024, cancellationToken);
        if (json is null)
        {
            return null;
        }

        var bytes = new byte[json.Length];
        await json.ReadExactlyAsync(bytes, cancellationToken);
        return await DecodeRawAsync(bytes, cancellationToken);
    }

    /// <summary>The <c>raw</c> property of a Gmail answer decoded (base64url) into a temporary file; a message larger than the bound is cut.</summary>
    internal static async Task<Stream> DecodeRawAsync(byte[] answer, CancellationToken cancellationToken)
    {
        var (start, length) = RawValue(answer);
        var target = TemporaryFiles.Create();
        try
        {
            var buffer = new byte[48 * 1024];
            var position = start;
            var end = start + length;
            while (position < end && target.Length <= MailRaw.MaxBytes)
            {
                var block = Math.Min(64 * 1024, end - position);
                var status = System.Buffers.Text.Base64Url.DecodeFromUtf8(
                    answer.AsSpan(position, block), buffer, out var consumed, out var written, isFinalBlock: position + block >= end);
                if (status is System.Buffers.OperationStatus.InvalidData || consumed == 0)
                {
                    throw new MailboxProviderException(ProviderFailure.Transient, "RawInvalid");
                }

                await target.WriteAsync(buffer.AsMemory(0, written), cancellationToken);
                position += consumed;
            }

            await target.FlushAsync(cancellationToken);
            target.Position = 0;
            return target;
        }
        catch
        {
            await target.DisposeAsync();
            throw;
        }
    }

    private static (int Start, int Length) RawValue(byte[] answer)
    {
        var reader = new Utf8JsonReader(answer);
        while (reader.Read())
        {
            if (reader is { TokenType: JsonTokenType.PropertyName, CurrentDepth: 1 } && reader.ValueTextEquals("raw"u8)
                && reader.Read() && reader is { TokenType: JsonTokenType.String, ValueIsEscaped: false })
            {
                return ((int)reader.TokenStartIndex + 1, reader.ValueSpan.Length);
            }
        }

        throw new MailboxProviderException(ProviderFailure.Transient, "RawMissing");
    }

    protected override ProviderFailure Classify(HttpStatusCode status, JsonDocument? json, bool withToken) => status switch
    {
        HttpStatusCode.Forbidden when RateReasons.Contains(Reason(json)) => ProviderFailure.Transient,
        HttpStatusCode.NotFound when withToken => ProviderFailure.CursorExpired,
        _ => base.Classify(status, json, withToken),
    };

    protected override string StatusReason(HttpStatusCode status, JsonDocument? json) =>
        Reason(json) is { } reason ? $"{(int)status} {reason}" : base.StatusReason(status, json);

    /// <summary>A page of the full sync: page token <c>g1|historyId|count|listPageToken</c>.</summary>
    private async Task<MailPage> FullAsync(MailPageRequest request, string token, CancellationToken cancellationToken)
    {
        string historyId;
        var count = 0;
        string? listToken = null;
        if (request.PageToken is { Length: > 0 } pageToken)
        {
            var parts = pageToken.Split('|');
            if (parts.Length != 4 || parts[0] != FullPrefix || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out count))
            {
                throw new MailboxProviderException(ProviderFailure.CursorExpired, "PageToken");
            }

            (historyId, listToken) = (parts[1], parts[3].Length > 0 ? parts[3] : null);
        }
        else
        {
            using var profile = await GetAsync(new Uri($"{ApiBase}profile"), token, withToken: false, [], cancellationToken);
            historyId = String(profile.RootElement, "historyId") ?? throw new MailboxProviderException(ProviderFailure.Transient, "NoHistoryId");
        }

        var take = Math.Clamp(request.MaxMessages - count, 0, request.PageSize);
        if (take == 0)
        {
            return new MailPage([], [], null, null, historyId, 0);
        }

        var query = new List<KeyValuePair<string, string>>
        {
            new("labelIds", request.Folder.ProviderFolderId),
            new("maxResults", take.ToString(CultureInfo.InvariantCulture)),
            new("q", $"after:{request.Since.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}"),
        };
        if (listToken is not null)
        {
            query.Add(new("pageToken", listToken));
        }

        string[] ids;
        string? next;
        using (var list = await GetAsync(new Uri($"{ApiBase}messages?{Query(query)}"), token, withToken: false, [], cancellationToken))
        {
            ids = list.RootElement.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array
                ? [.. messages.EnumerateArray().Select(item => String(item, "id")).OfType<string>()]
                : [];
            next = String(list.RootElement, "nextPageToken");
        }

        var (read, removed, malformed) = await ReadMessagesAsync(ids, token, cancellationToken);
        count += ids.Length;
        return next is { Length: > 0 } && count < request.MaxMessages
            ? new MailPage(read, removed, null, $"{FullPrefix}|{historyId}|{count.ToString(CultureInfo.InvariantCulture)}|{next}", null, malformed)
            : new MailPage(read, removed, null, null, historyId, malformed);
    }

    /// <summary>A page of the changes since <paramref name="historyId"/> for the label (page token <c>h1|historyPageToken</c>).</summary>
    private async Task<MailPage> HistoryAsync(MailPageRequest request, string token, string historyId, CancellationToken cancellationToken)
    {
        var label = request.Folder.ProviderFolderId;
        var query = new List<KeyValuePair<string, string>>
        {
            new("startHistoryId", historyId),
            new("labelId", label),
            new("maxResults", request.PageSize.ToString(CultureInfo.InvariantCulture)),
            new("historyTypes", "messageAdded"),
            new("historyTypes", "messageDeleted"),
            new("historyTypes", "labelAdded"),
            new("historyTypes", "labelRemoved"),
        };
        if (request.PageToken is { Length: > 0 } pageToken)
        {
            if (!pageToken.StartsWith(HistoryPrefix + "|", StringComparison.Ordinal))
            {
                throw new MailboxProviderException(ProviderFailure.CursorExpired, "PageToken");
            }

            query.Add(new("pageToken", pageToken[(HistoryPrefix.Length + 1)..]));
        }

        // Last change wins: an id is either read again (added, flags changed) or removed from the label.
        var changes = new Dictionary<string, bool>(StringComparer.Ordinal);
        string? next;
        string? newHistoryId;
        using (var json = await GetAsync(new Uri($"{ApiBase}history?{Query(query)}"), token, withToken: true, [], cancellationToken))
        {
            var root = json.RootElement;
            next = String(root, "nextPageToken");
            newHistoryId = String(root, "historyId");
            if (root.TryGetProperty("history", out var history) && history.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in history.EnumerateArray())
                {
                    Apply(entry, "messagesAdded", changes, (labels, _) => labels.Contains(label) ? true : null);
                    Apply(entry, "messagesDeleted", changes, (_, _) => false);
                    Apply(entry, "labelsAdded", changes, (labels, _) => labels.Contains(label) ? true : null);
                    Apply(entry, "labelsRemoved", changes, (labels, changed) => changed.Contains(label) ? false : labels.Contains(label) ? true : null);
                }
            }
        }

        var (read, removed, malformed) = await ReadMessagesAsync([.. changes.Where(change => change.Value).Select(change => change.Key)], token, cancellationToken);
        removed = [.. removed, .. changes.Where(change => !change.Value).Select(change => change.Key)];
        return next is { Length: > 0 }
            ? new MailPage(read, removed, null, $"{HistoryPrefix}|{next}", null, malformed)
            : new MailPage(read, removed, null, null, newHistoryId ?? historyId, malformed);
    }

    /// <param name="decide">From the labels of the message and the labels the change added/removed: read (true), remove (false), ignore (null).</param>
    private static void Apply(
        JsonElement entry, string property, Dictionary<string, bool> changes, Func<IReadOnlyCollection<string>, IReadOnlyCollection<string>, bool?> decide)
    {
        if (!entry.TryGetProperty(property, out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in items.EnumerateArray())
        {
            var message = Object(item, "message");
            if (String(message, "id") is not { Length: > 0 } id)
            {
                continue;
            }

            if (decide(Labels(message), Labels(item)) is { } decision)
            {
                changes[id] = decision;
            }
        }
    }

    private static string[] Labels(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty("labelIds", out var labels) && labels.ValueKind == JsonValueKind.Array
            ? [.. labels.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString()!)]
            : [];

    private async Task<(List<MailMessageData> Read, List<string> Removed, int Malformed)> ReadMessagesAsync(
        string[] ids, string token, CancellationToken cancellationToken)
    {
        var (read, removed, malformed) = (new List<MailMessageData>(ids.Length), new List<string>(), 0);
        foreach (var id in ids)
        {
            var uri = new Uri($"{ApiBase}messages/{Uri.EscapeDataString(id)}?{Query([new("format", "full"), new("fields", MessageFields)])}");
            using var json = await GetOrNullAsync(uri, token, cancellationToken);
            if (json is null)
            {
                // Deleted between the list and the read.
                removed.Add(id);
            }
            else if (ReadMessage(json.RootElement) is { } message)
            {
                read.Add(message);
            }
            else
            {
                malformed++;
            }
        }

        return (read, removed, malformed);
    }

    private static MailMessageData? ReadMessage(JsonElement root)
    {
        var internalDate = Long(root, "internalDate");
        if (String(root, "id") is not { Length: > 0 } id || internalDate <= 0)
        {
            return null;
        }

        var payload = Object(root, "payload");
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("headers", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var header in list.EnumerateArray())
            {
                if (String(header, "name") is { } name && String(header, "value") is { } value)
                {
                    headers.TryAdd(name, value);
                }
            }
        }

        var attachments = new List<string>();
        CollectFileNames(payload, attachments, depth: 0);
        var labels = Labels(root);
        var (fromAddress, fromName) = MailHeaders.First(headers.GetValueOrDefault("From"));
        return new MailMessageData(
            id,
            headers.GetValueOrDefault("Message-ID") ?? headers.GetValueOrDefault("Message-Id"),
            String(root, "threadId"),
            fromAddress,
            fromName,
            MailHeaders.Recipients(headers.GetValueOrDefault("To")),
            MailHeaders.Recipients(headers.GetValueOrDefault("Cc")),
            headers.GetValueOrDefault("Subject"),
            MailHeaders.Date(headers.GetValueOrDefault("Date")),
            DateTimeOffset.FromUnixTimeMilliseconds(internalDate),
            !labels.Contains("UNREAD"),
            Long(root, "sizeEstimate"),
            attachments.Count > 0,
            attachments);
    }

    private static void CollectFileNames(JsonElement part, List<string> names, int depth)
    {
        if (part.ValueKind != JsonValueKind.Object || depth > 3)
        {
            return;
        }

        if (depth > 0 && String(part, "filename") is { Length: > 0 } name)
        {
            names.Add(name);
        }

        if (part.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in parts.EnumerateArray())
            {
                CollectFileNames(child, names, depth + 1);
            }
        }
    }

    private static string? Reason(JsonDocument? json)
    {
        if (json is null || Object(json.RootElement, "error") is not { ValueKind: JsonValueKind.Object } error
            || !error.TryGetProperty("errors", out var errors) || errors.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var reason = errors.EnumerateArray().Select(item => String(item, "reason")).FirstOrDefault();
        return IsSafeCode(reason) ? reason : null;
    }
}

/// <summary>
/// Microsoft Graph (ADR 0021, plain HTTP): the well-known <c>inbox</c> and <c>sentitems</c> folders through
/// <c>messages/delta</c> (received after the window start, newest first), paging with <c>@odata.nextLink</c> and the
/// <c>@odata.deltaLink</c> for the next incremental sync (410 → full sync again). A full sync stops storing at the
/// maximum but pages to the end for the delta link. Links are followed only on <c>graph.microsoft.com</c>.
/// </summary>
internal sealed class GraphMailProvider(IHttpClientFactory httpClients) : ProviderHttpBase(httpClients), IMailProvider
{
    public const string ApiBase = "https://graph.microsoft.com/v1.0/me/mailFolders/";

    public const string MessagesBase = "https://graph.microsoft.com/v1.0/me/messages/";

    public const string Select =
        "id,internetMessageId,conversationId,from,toRecipients,ccRecipients,subject,sentDateTime,receivedDateTime,isRead,hasAttachments";

    private const string GraphOrigin = "https://graph.microsoft.com/";
    private const string FullPrefix = "m1";

    public ConnectedAccountProvider Provider => ConnectedAccountProvider.Microsoft;

    public Task<IReadOnlyList<MailFolderData>> FoldersAsync(MailboxAccess access, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MailFolderData>>([new("inbox", "Inbox", MailFolderKind.Inbox), new("sentitems", "Sent Items", MailFolderKind.Sent)]);

    public async Task<MailPage> ReadAsync(MailPageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var token = request.Access.AccessToken ?? throw new ArgumentException("Graph needs an access token.", nameof(request));

        // Page token: "m1|count|nextLink" during a full sync (the count bounds what is stored), the next link alone otherwise.
        var full = request.SyncToken is null;
        var count = 0;
        string? link = request.PageToken;
        if (full && link is { Length: > 0 })
        {
            var parts = link.Split('|', 3);
            if (parts.Length != 3 || parts[0] != FullPrefix || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out count))
            {
                throw new MailboxProviderException(ProviderFailure.CursorExpired, "PageToken");
            }

            link = parts[2];
        }

        link = link is { Length: > 0 } ? link : request.SyncToken;
        Uri uri;
        if (link is { Length: > 0 })
        {
            if (!IsGraph(link) || !Uri.TryCreate(link, UriKind.Absolute, out var parsed))
            {
                throw new MailboxProviderException(ProviderFailure.CursorExpired, "ForeignLink");
            }

            uri = parsed;
        }
        else
        {
            uri = new Uri($"{ApiBase}{Uri.EscapeDataString(request.Folder.ProviderFolderId)}/messages/delta?" + Query(
            [
                new("$select", Select),
                new("$filter", $"receivedDateTime ge {Iso(request.Since)}"),
                new("$orderby", "receivedDateTime desc"),
            ]));
        }

        var headers = new KeyValuePair<string, string>[] { new("Prefer", $"odata.maxpagesize={request.PageSize.ToString(CultureInfo.InvariantCulture)}") };
        using var json = await GetAsync(uri, token, link is { Length: > 0 }, headers, cancellationToken);
        var root = json.RootElement;
        var (messages, removed, malformed) = (new List<MailMessageData>(), new List<string>(), 0);
        if (root.TryGetProperty("value", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                var id = String(item, "id");
                if (string.IsNullOrEmpty(id))
                {
                    malformed++;
                }
                else if (item.TryGetProperty("@removed", out _))
                {
                    removed.Add(id);
                }
                else if (full && count >= request.MaxMessages)
                {
                    // Beyond the bound of the first sync: not stored, the paging goes on for the delta link.
                }
                else if (ReadMessage(id, item) is { } message)
                {
                    messages.Add(message);
                    count++;
                }
                else
                {
                    malformed++;
                }
            }
        }

        var next = String(root, "@odata.nextLink");
        var delta = String(root, "@odata.deltaLink");
        var nextToken = IsGraph(next) ? full ? $"{FullPrefix}|{count.ToString(CultureInfo.InvariantCulture)}|{next}" : next : null;
        return new MailPage(messages, removed, null, nextToken, nextToken is null && IsGraph(delta) ? delta : null, malformed);
    }

    /// <summary><c>/me/messages/{id}/$value</c>: the MIME content of the message, into a temporary file.</summary>
    public Task<Stream?> OpenRawAsync(MailboxAccess access, MailFolderData folder, string providerMessageId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);
        var token = access.AccessToken ?? throw new ArgumentException("Graph needs an access token.", nameof(access));
        return DownloadAsync(new Uri($"{MessagesBase}{Uri.EscapeDataString(providerMessageId)}/$value"), token, MailRaw.MaxBytes, cancellationToken);
    }

    protected override string StatusReason(HttpStatusCode status, JsonDocument? json)
    {
        var code = json is null ? null : String(Object(json.RootElement, "error"), "code");
        return IsSafeCode(code) ? $"{(int)status} {code}" : base.StatusReason(status, json);
    }

    private static bool IsGraph(string? link) => link is { Length: > 0 } && link.StartsWith(GraphOrigin, StringComparison.Ordinal);

    private static MailMessageData? ReadMessage(string id, JsonElement item)
    {
        if (String(item, "receivedDateTime") is not { } received
            || !DateTimeOffset.TryParse(received, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var receivedAt))
        {
            return null;
        }

        var sentAt = String(item, "sentDateTime") is { } sent && DateTimeOffset.TryParse(sent, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : (DateTimeOffset?)null;
        var from = Object(Object(item, "from"), "emailAddress");
        return new MailMessageData(
            id,
            String(item, "internetMessageId"),
            String(item, "conversationId"),
            String(from, "address"),
            String(from, "name"),
            Recipients(item, "toRecipients"),
            Recipients(item, "ccRecipients"),
            String(item, "subject"),
            sentAt,
            receivedAt.ToUniversalTime(),
            Boolean(item, "isRead"),
            0,
            Boolean(item, "hasAttachments"),
            []);
    }

    private static string[] Recipients(JsonElement item, string property) =>
        item.TryGetProperty(property, out var list) && list.ValueKind == JsonValueKind.Array
            ? [.. list.EnumerateArray()
                .Select(recipient => Object(recipient, "emailAddress"))
                .Select(address => MailHeaders.Format(String(address, "name"), String(address, "address")))
                .Where(text => text.Length > 0)]
            : [];
}
