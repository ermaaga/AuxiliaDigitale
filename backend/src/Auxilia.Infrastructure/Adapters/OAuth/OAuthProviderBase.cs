using System.Globalization;
using System.Net;
using System.Text.Json;

using Auxilia.Application.Abstractions.Mailbox;
using Auxilia.Diagnostics;
using Auxilia.Domain.Mailbox;
using Auxilia.SharedKernel.Results;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auxilia.Infrastructure.Adapters.OAuth;

/// <summary>
/// OAuth applications of connected accounts (section <c>ConnectedAccounts</c>, ADR 0021): deployment configuration from
/// the key vault / user-secrets, never tenant settings. A provider without client id, secret and callback base URL is not
/// offered.
/// </summary>
public sealed class ConnectedAccountsOptions
{
    public const string SectionName = "ConnectedAccounts";

    /// <summary>Public origin of the web app: the callback is <c>{CallbackBaseUrl}/api/connected-accounts/callback/{provider}</c>.</summary>
    public Uri? CallbackBaseUrl { get; set; }

    public OAuthClientOptions Google { get; set; } = new();

    public OAuthClientOptions Microsoft { get; set; } = new();
}

public sealed class OAuthClientOptions
{
    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    /// <summary>Microsoft Entra authority tenant: <c>common</c> = work, school and personal accounts.</summary>
    public string Tenant { get; set; } = "common";
}

/// <summary>
/// Authorization code + PKCE (S256) against a provider's token endpoint over plain HTTP (no SDK, ADR 0021). The ID token
/// comes straight from the token endpoint over TLS, so its claims are read without a signature check (OpenID Connect
/// Core 3.1.3.7); its audience must still be our client. Nothing secret is logged: only the provider's error code.
/// </summary>
internal abstract class OAuthProviderBase : IOAuthProvider
{
    public const string HttpClientName = "auxilia-oauth";

    private readonly IHttpClientFactory httpClients;
    private readonly IOptionsMonitor<ConnectedAccountsOptions> options;
    private readonly TimeProvider clock;
    private readonly ILogger logger;

    protected OAuthProviderBase(IHttpClientFactory httpClients, IOptionsMonitor<ConnectedAccountsOptions> options, TimeProvider clock, ILogger logger)
    {
        this.httpClients = httpClients;
        this.options = options;
        this.clock = clock;
        this.logger = logger;
    }

    public abstract ConnectedAccountProvider Provider { get; }

    public abstract bool SupportsCalendar { get; }

    public bool IsConfigured =>
        Client is { ClientId.Length: > 0, ClientSecret.Length: > 0 } && options.CurrentValue.CallbackBaseUrl is { IsAbsoluteUri: true };

    protected OAuthClientOptions Client => ClientOf(options.CurrentValue);

    protected string RedirectUri =>
        new Uri(options.CurrentValue.CallbackBaseUrl!, $"/api/connected-accounts/callback/{Provider.ToString().ToLowerInvariant()}").ToString();

    protected abstract Uri AuthorizeEndpoint { get; }

    protected abstract Uri TokenEndpoint { get; }

    public Uri AuthorizationUrl(OAuthAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        EnsureConfigured();

        var query = new List<KeyValuePair<string, string>>
        {
            new("response_type", "code"),
            new("client_id", Client.ClientId!),
            new("redirect_uri", RedirectUri),
            new("scope", string.Join(' ', Scopes(authorization.Mail, authorization.Calendar))),
            new("state", authorization.State),
            new("code_challenge", authorization.CodeChallenge),
            new("code_challenge_method", "S256"),
        };
        query.AddRange(ExtraAuthorizationParameters());
        if (!string.IsNullOrWhiteSpace(authorization.LoginHint))
        {
            query.Add(new("login_hint", authorization.LoginHint));
        }

        return new Uri($"{AuthorizeEndpoint}?{string.Join('&', query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"))}");
    }

    public async Task<Result<OAuthTokens>> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        ArgumentException.ThrowIfNullOrEmpty(codeVerifier);
        EnsureConfigured();

        var form = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "authorization_code"),
            new("code", code),
            new("redirect_uri", RedirectUri),
            new("code_verifier", codeVerifier),
            new("client_id", Client.ClientId!),
            new("client_secret", Client.ClientSecret!),
        };

        try
        {
            var (status, json) = await PostAsync(TokenEndpoint, form, cancellationToken);
            if (status != HttpStatusCode.OK || json is null)
            {
                var reason = ErrorCodeOf(json);
                Log.Mailbox.OAuthExchangeFailed(logger, null, Provider.ToString(), reason);
                return Errors.Mailbox.OAuthExchangeFailed();
            }

            var tokens = ReadTokens(json.RootElement, withIdentity: true);
            if (tokens is null)
            {
                Log.Mailbox.OAuthExchangeFailed(logger, null, Provider.ToString(), "UnusableAnswer");
                return Errors.Mailbox.OAuthExchangeFailed();
            }

            return tokens;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException
            || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            Log.Mailbox.OAuthExchangeFailed(logger, exception, Provider.ToString(), "Unreachable");
            return Errors.Mailbox.OAuthExchangeFailed();
        }
    }

    /// <summary>A refused grant (4xx: revoked, expired) is a failure; network and 5xx errors throw so the caller can retry.</summary>
    public async Task<Result<OAuthTokens>> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(refreshToken);
        EnsureConfigured();

        var form = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "refresh_token"),
            new("refresh_token", refreshToken),
            new("client_id", Client.ClientId!),
            new("client_secret", Client.ClientSecret!),
        };
        var (status, json) = await PostAsync(TokenEndpoint, form, cancellationToken);
        if ((int)status >= 500)
        {
            throw new HttpRequestException($"The {Provider} token endpoint answered {(int)status}.", null, status);
        }

        if (status != HttpStatusCode.OK || json is null || ReadTokens(json.RootElement, withIdentity: false) is not { } tokens)
        {
            Log.Mailbox.OAuthExchangeFailed(logger, null, Provider.ToString(), ErrorCodeOf(json));
            return Errors.Mailbox.OAuthExchangeFailed();
        }

        return tokens;
    }

    public virtual Task<OAuthRevocation> RevokeAsync(string refreshToken, CancellationToken cancellationToken) =>
        Task.FromResult(OAuthRevocation.NotSupported);

    protected abstract OAuthClientOptions ClientOf(ConnectedAccountsOptions configured);

    protected abstract IEnumerable<string> Scopes(bool mail, bool calendar);

    protected abstract IEnumerable<KeyValuePair<string, string>> ExtraAuthorizationParameters();

    /// <summary>Mail and calendar access among the granted scopes.</summary>
    protected abstract (bool Mail, bool Calendar) Capabilities(IReadOnlyCollection<string> scopes);

    protected async Task<(HttpStatusCode Status, JsonDocument? Json)> PostAsync(Uri endpoint, IEnumerable<KeyValuePair<string, string>> form, CancellationToken cancellationToken)
    {
        using var client = httpClients.CreateClient(HttpClientName);
        using var content = new FormUrlEncodedContent(form);
        using var response = await client.PostAsync(endpoint, content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonDocument? json = null;
        if (body.Length is > 0 and < 64 * 1024 && body.TrimStart().StartsWith('{'))
        {
            json = JsonDocument.Parse(body);
        }

        return (response.StatusCode, json);
    }

    private OAuthTokens? ReadTokens(JsonElement root, bool withIdentity)
    {
        if (!TryString(root, "access_token", out var accessToken))
        {
            return null;
        }

        TryString(root, "refresh_token", out var refreshToken);
        TryString(root, "scope", out var scope);
        var expiresIn = root.TryGetProperty("expires_in", out var expires) && expires.ValueKind == JsonValueKind.Number && expires.TryGetInt32(out var seconds)
            ? seconds
            : 3600;
        var scopes = (scope ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var (mail, calendar) = Capabilities(scopes);

        string? address = null;
        string? name = null;
        if (withIdentity)
        {
            if (!TryString(root, "id_token", out var idToken) || ReadIdToken(idToken!) is not { } identity)
            {
                return null;
            }

            (address, name) = identity;
        }

        return new OAuthTokens(
            accessToken!, refreshToken, clock.GetUtcNow().AddSeconds(Math.Clamp(expiresIn, 60, 86_400)), scope, mail, calendar, address, name);
    }

    /// <summary>E-mail and name of the account; null when the token is malformed or not issued to our client.</summary>
    private (string Address, string? Name)? ReadIdToken(string idToken)
    {
        var parts = idToken.Split('.');
        if (parts.Length != 3)
        {
            return null;
        }

        try
        {
            using var payload = JsonDocument.Parse(FromBase64Url(parts[1]));
            var root = payload.RootElement;
            var audience = root.TryGetProperty("aud", out var aud) ? aud : default;
            var forUs = audience.ValueKind switch
            {
                JsonValueKind.String => audience.GetString() == Client.ClientId,
                JsonValueKind.Array => audience.EnumerateArray().Any(item => item.GetString() == Client.ClientId),
                _ => false,
            };
            if (!forUs)
            {
                return null;
            }

            var address = TryString(root, "email", out var email) ? email : TryString(root, "preferred_username", out var username) ? username : null;
            TryString(root, "name", out var name);
            return string.IsNullOrWhiteSpace(address) ? null : (address!, name);
        }
        catch (Exception exception) when (exception is JsonException or FormatException)
        {
            return null;
        }
    }

    private static string ErrorCodeOf(JsonDocument? json)
    {
        // Only the OAuth error code (e.g. invalid_grant): never the description, which may echo request values.
        if (json is not null && TryString(json.RootElement, "error", out var error) && error!.Length <= 64 && error.All(character => char.IsAsciiLetterOrDigit(character) || character == '_'))
        {
            return error;
        }

        return "HttpError";
    }

    private static bool TryString(JsonElement element, string property, out string? value)
    {
        value = element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var found) && found.ValueKind == JsonValueKind.String
            ? found.GetString()
            : null;
        return !string.IsNullOrEmpty(value);
    }

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            _ => string.Empty,
        };
        return Convert.FromBase64String(padded);
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"{ConnectedAccountsOptions.SectionName}:{Provider} is not configured."));
        }
    }

    protected static bool HasScope(IReadOnlyCollection<string> scopes, string scope) =>
        scopes.Any(item => string.Equals(item, scope, StringComparison.OrdinalIgnoreCase)
            || item.EndsWith("/" + scope, StringComparison.OrdinalIgnoreCase));
}
