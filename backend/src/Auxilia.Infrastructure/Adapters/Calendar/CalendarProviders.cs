using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Auxilia.Application.Abstractions.Mailbox;
using Auxilia.Domain.Mailbox;

namespace Auxilia.Infrastructure.Adapters.Calendar;

/// <summary>
/// Calendar reads over plain HTTP (ADR 0021, no SDK): the access token of the sync, JSON answers read with
/// <see cref="JsonDocument"/>, failures classified for the sync engine. The HTTP retries (429/5xx with
/// <c>Retry-After</c>) come from the standard resilience handler of the hosts; what is left is
/// <see cref="ProviderFailure.Transient"/>.
/// </summary>
internal abstract class CalendarProviderBase(IHttpClientFactory httpClients) : ICalendarProvider
{
    public const string HttpClientName = "auxilia-mailbox";

    /// <summary>Largest answer read (a page of 250 events is far smaller).</summary>
    private const int MaxBodyBytes = 8 * 1024 * 1024;

    public abstract ConnectedAccountProvider Provider { get; }

    public abstract Task<CalendarPage> ReadAsync(CalendarPageRequest request, CancellationToken cancellationToken);

    /// <summary>GET with the bearer token; non-success answers become a <see cref="MailboxProviderException"/>.</summary>
    protected async Task<JsonDocument> GetAsync(Uri uri, string accessToken, bool withToken, IEnumerable<KeyValuePair<string, string>> headers, CancellationToken cancellationToken)
    {
        using var client = httpClients.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        foreach (var (name, value) in headers)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new MailboxProviderException(ProviderFailure.Transient, "Unreachable", exception);
        }

        using (response)
        {
            if (response.Content.Headers.ContentLength > MaxBodyBytes)
            {
                throw new MailboxProviderException(ProviderFailure.Transient, "AnswerTooLarge");
            }

            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            JsonDocument? json = null;
            try
            {
                json = body.Length is > 0 and <= MaxBodyBytes ? JsonDocument.Parse(body) : null;
            }
            catch (JsonException)
            {
                json = null;
            }

            if (response.IsSuccessStatusCode && json is not null)
            {
                return json;
            }

            using (json)
            {
                throw new MailboxProviderException(Classify(response.StatusCode, json, withToken), StatusReason(response.StatusCode, json));
            }
        }
    }

    /// <summary>What a refused call means for the sync (overridden per provider for their error formats).</summary>
    protected virtual ProviderFailure Classify(HttpStatusCode status, JsonDocument? json, bool withToken) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ProviderFailure.Credentials,
        HttpStatusCode.Gone => ProviderFailure.CursorExpired,
        HttpStatusCode.BadRequest when withToken => ProviderFailure.CursorExpired,
        _ => ProviderFailure.Transient,
    };

    /// <summary>HTTP status and the provider's error code only: never messages, which may echo account data.</summary>
    protected virtual string StatusReason(HttpStatusCode status, JsonDocument? json) =>
        ((int)status).ToString(CultureInfo.InvariantCulture);

    protected static string? String(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    protected static bool Boolean(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    protected static JsonElement Object(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : default;

    protected static string Iso(DateTimeOffset instant) => instant.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    protected static bool IsSafeCode(string? code) =>
        code is { Length: > 0 and <= 64 } && code.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.');
}

/// <summary>
/// Google Calendar (<c>events.list</c> of the primary calendar): full sync of the window with <c>timeMin</c>/<c>timeMax</c>,
/// then incremental with <c>syncToken</c> (410 Gone → full sync again). Recurring events are expanded
/// (<c>singleEvents</c>); cancelled events come back as removed.
/// </summary>
internal sealed class GoogleCalendarProvider(IHttpClientFactory httpClients) : CalendarProviderBase(httpClients)
{
    public static readonly Uri EventsEndpoint = new("https://www.googleapis.com/calendar/v3/calendars/primary/events");

    private static readonly string[] RateReasons = ["rateLimitExceeded", "userRateLimitExceeded", "quotaExceeded"];

    public override ConnectedAccountProvider Provider => ConnectedAccountProvider.Google;

    public override async Task<CalendarPage> ReadAsync(CalendarPageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = new List<KeyValuePair<string, string>>
        {
            new("singleEvents", "true"),
            new("maxResults", request.PageSize.ToString(CultureInfo.InvariantCulture)),
        };
        if (request.SyncToken is { Length: > 0 } syncToken)
        {
            query.Add(new("syncToken", syncToken));
        }
        else
        {
            query.Add(new("timeMin", Iso(request.WindowStart)));
            query.Add(new("timeMax", Iso(request.WindowEnd)));
        }

        if (request.PageToken is { Length: > 0 } pageToken)
        {
            query.Add(new("pageToken", pageToken));
        }

        var uri = new Uri($"{EventsEndpoint}?{string.Join('&', query.Select(pair => $"{pair.Key}={Uri.EscapeDataString(pair.Value)}"))}");
        using var json = await GetAsync(uri, request.AccessToken, request.SyncToken is not null || request.PageToken is not null, [], cancellationToken);
        var root = json.RootElement;
        var (events, removed, malformed) = (new List<CalendarEventData>(), new List<string>(), 0);
        if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                var id = String(item, "id");
                if (string.IsNullOrEmpty(id))
                {
                    malformed++;
                }
                else if (String(item, "status") == "cancelled")
                {
                    removed.Add(id);
                }
                else if (ReadEvent(id, item) is { } read)
                {
                    events.Add(read);
                }
                else
                {
                    malformed++;
                }
            }
        }

        return new CalendarPage(events, removed, String(root, "nextPageToken"), String(root, "nextSyncToken"), malformed);
    }

    protected override ProviderFailure Classify(HttpStatusCode status, JsonDocument? json, bool withToken) =>
        status == HttpStatusCode.Forbidden && RateReasons.Contains(Reason(json)) ? ProviderFailure.Transient : base.Classify(status, json, withToken);

    protected override string StatusReason(HttpStatusCode status, JsonDocument? json) =>
        Reason(json) is { } reason ? $"{(int)status} {reason}" : base.StatusReason(status, json);

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

    private static CalendarEventData? ReadEvent(string id, JsonElement item)
    {
        if (ReadTime(Object(item, "start")) is not { } start || ReadTime(Object(item, "end")) is not { } end || start.AllDay != end.AllDay)
        {
            return null;
        }

        return new CalendarEventData(
            id, String(item, "summary"), String(item, "location"), start.Instant, end.Instant, start.AllDay, String(item, "status") == "tentative", String(item, "htmlLink"));
    }

    /// <summary><c>dateTime</c> (RFC 3339 with offset) or, for all-day events, <c>date</c> at 00:00 UTC.</summary>
    private static (DateTimeOffset Instant, bool AllDay)? ReadTime(JsonElement time)
    {
        if (String(time, "dateTime") is { } dateTime
            && DateTimeOffset.TryParse(dateTime, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var instant))
        {
            return (instant.ToUniversalTime(), false);
        }

        if (String(time, "date") is { } date
            && DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            return (new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), true);
        }

        return null;
    }
}

/// <summary>
/// Microsoft Graph (<c>/me/calendarView/delta</c> of the window): pages through <c>@odata.nextLink</c>, then the
/// <c>@odata.deltaLink</c> for the next incremental sync (410 Gone → full sync again). Times asked in UTC; removed and
/// cancelled events come back as removed. Links of the provider are followed only on <c>graph.microsoft.com</c>.
/// </summary>
internal sealed class MicrosoftCalendarProvider(IHttpClientFactory httpClients) : CalendarProviderBase(httpClients)
{
    public static readonly Uri DeltaEndpoint = new("https://graph.microsoft.com/v1.0/me/calendarView/delta");

    private const string GraphOrigin = "https://graph.microsoft.com/";

    public override ConnectedAccountProvider Provider => ConnectedAccountProvider.Microsoft;

    public override async Task<CalendarPage> ReadAsync(CalendarPageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var link = request.PageToken is { Length: > 0 } ? request.PageToken : request.SyncToken;
        Uri uri;
        if (link is { Length: > 0 })
        {
            if (!link.StartsWith(GraphOrigin, StringComparison.Ordinal) || !Uri.TryCreate(link, UriKind.Absolute, out var parsed))
            {
                throw new MailboxProviderException(ProviderFailure.CursorExpired, "ForeignLink");
            }

            uri = parsed;
        }
        else
        {
            uri = new Uri($"{DeltaEndpoint}?startDateTime={Uri.EscapeDataString(Iso(request.WindowStart))}&endDateTime={Uri.EscapeDataString(Iso(request.WindowEnd))}");
        }

        var headers = new KeyValuePair<string, string>[]
        {
            new("Prefer", $"odata.maxpagesize={request.PageSize.ToString(CultureInfo.InvariantCulture)}"),
            new("Prefer", "outlook.timezone=\"UTC\""),
        };
        using var json = await GetAsync(uri, request.AccessToken, link is { Length: > 0 }, headers, cancellationToken);
        var root = json.RootElement;
        var (events, removed, malformed) = (new List<CalendarEventData>(), new List<string>(), 0);
        if (root.TryGetProperty("value", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                var id = String(item, "id");
                if (string.IsNullOrEmpty(id))
                {
                    malformed++;
                }
                else if (item.TryGetProperty("@removed", out _) || Boolean(item, "isCancelled"))
                {
                    removed.Add(id);
                }
                else if (ReadEvent(id, item) is { } read)
                {
                    events.Add(read);
                }
                else
                {
                    malformed++;
                }
            }
        }

        var next = String(root, "@odata.nextLink");
        var delta = String(root, "@odata.deltaLink");
        return new CalendarPage(
            events, removed, IsGraph(next) ? next : null, IsGraph(delta) ? delta : null, malformed);
    }

    protected override string StatusReason(HttpStatusCode status, JsonDocument? json)
    {
        var code = json is null ? null : String(Object(json.RootElement, "error"), "code");
        return IsSafeCode(code) ? $"{(int)status} {code}" : base.StatusReason(status, json);
    }

    private static bool IsGraph(string? link) => link is { Length: > 0 } && link.StartsWith(GraphOrigin, StringComparison.Ordinal);

    private static CalendarEventData? ReadEvent(string id, JsonElement item)
    {
        var allDay = Boolean(item, "isAllDay");
        if (ReadTime(Object(item, "start"), allDay) is not { } start || ReadTime(Object(item, "end"), allDay) is not { } end)
        {
            return null;
        }

        return new CalendarEventData(
            id, String(item, "subject"), String(Object(item, "location"), "displayName"), start, end, allDay,
            string.Equals(String(item, "showAs"), "tentative", StringComparison.OrdinalIgnoreCase), String(item, "webLink"));
    }

    /// <summary><c>dateTime</c> without offset in the <c>timeZone</c> asked (UTC); all-day events keep their date at 00:00 UTC.</summary>
    private static DateTimeOffset? ReadTime(JsonElement time, bool allDay)
    {
        if (String(time, "dateTime") is not { } dateTime
            || !DateTime.TryParse(dateTime, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return null;
        }

        var zone = String(time, "timeZone");
        if (!allDay && zone is not null && !string.Equals(zone, "UTC", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var value = allDay ? parsed.Date : parsed;
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeSpan.Zero);
    }
}
