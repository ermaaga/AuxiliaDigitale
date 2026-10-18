using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Auxilia.Application.Abstractions.Mailbox;

namespace Auxilia.Infrastructure.Adapters.Mailbox;

/// <summary>
/// Provider reads over plain HTTP (calendars and mail) (ADR 0021, no SDK): the access token of the sync, JSON answers read with
/// <see cref="JsonDocument"/>, failures classified for the sync engine. The HTTP retries (429/5xx with
/// <c>Retry-After</c>) come from the standard resilience handler of the hosts; what is left is
/// <see cref="ProviderFailure.Transient"/>.
/// </summary>
internal abstract class ProviderHttpBase(IHttpClientFactory httpClients)
{
    public const string HttpClientName = "auxilia-mailbox";

    /// <summary>Largest answer read (a page of 250 events or 100 headers is far smaller).</summary>
    private const int MaxBodyBytes = 8 * 1024 * 1024;

    /// <summary>GET with the bearer token; non-success answers become a <see cref="MailboxProviderException"/>.</summary>
    protected async Task<JsonDocument> GetAsync(Uri uri, string accessToken, bool withToken, IEnumerable<KeyValuePair<string, string>> headers, CancellationToken cancellationToken) =>
        await SendAsync(uri, accessToken, withToken, headers, notFoundIsNull: false, cancellationToken)
        ?? throw new MailboxProviderException(ProviderFailure.Transient, "404");

    /// <summary>Like <see cref="GetAsync"/>, but null when the item is gone (404): it was deleted meanwhile.</summary>
    protected Task<JsonDocument?> GetOrNullAsync(Uri uri, string accessToken, CancellationToken cancellationToken) =>
        SendAsync(uri, accessToken, withToken: false, [], notFoundIsNull: true, cancellationToken);

    /// <summary>
    /// GET of a large answer (an original message) into a temporary file, cut at <paramref name="maxBytes"/> + 1 bytes;
    /// null when the item is gone (404). Failures are classified like <see cref="GetAsync"/>.
    /// </summary>
    protected async Task<Stream?> DownloadAsync(Uri uri, string accessToken, long maxBytes, CancellationToken cancellationToken)
    {
        using var client = httpClients.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

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
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new MailboxProviderException(Classify(response.StatusCode, null, withToken: false), StatusReason(response.StatusCode, null));
            }

            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            try
            {
                return await Mail.TemporaryFiles.CopyBoundedAsync(body, maxBytes, cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                throw new MailboxProviderException(ProviderFailure.Transient, "Interrupted", exception);
            }
        }
    }

    private async Task<JsonDocument?> SendAsync(
        Uri uri, string accessToken, bool withToken, IEnumerable<KeyValuePair<string, string>> headers, bool notFoundIsNull, CancellationToken cancellationToken)
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

            if (notFoundIsNull && response.StatusCode == HttpStatusCode.NotFound)
            {
                json?.Dispose();
                return null;
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

    protected static long Long(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value))
        {
            return 0;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var number) => number,
            JsonValueKind.String when long.TryParse(value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0,
        };
    }

    protected static string Query(IEnumerable<KeyValuePair<string, string>> pairs) =>
        string.Join('&', pairs.Select(pair => $"{pair.Key}={Uri.EscapeDataString(pair.Value)}"));

    protected static string Iso(DateTimeOffset instant) => instant.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    protected static bool IsSafeCode(string? code) =>
        code is { Length: > 0 and <= 64 } && code.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.');
}
