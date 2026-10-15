using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;

using Auxilia.Application.Abstractions.Mailbox;
using Auxilia.Diagnostics;
using Auxilia.Infrastructure.Adapters.OAuth;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NSubstitute;

namespace Auxilia.Infrastructure.Tests.Adapters.OAuth;

public sealed class OAuthProviderTests : IDisposable
{
    private const string GoogleClient = "google-client.apps.googleusercontent.com";
    private const string MicrosoftClient = "6f1d2c3b-0000-4000-8000-000000000001";

    private readonly StubHandler handler = new();
    private readonly ConnectedAccountsOptions configured = new()
    {
        CallbackBaseUrl = new Uri("https://app.example.com"),
        Google = new OAuthClientOptions { ClientId = GoogleClient, ClientSecret = "google-secret" },
        Microsoft = new OAuthClientOptions { ClientId = MicrosoftClient, ClientSecret = "microsoft-secret" },
    };

    private readonly GoogleOAuthProvider google;
    private readonly MicrosoftOAuthProvider microsoft;

    public OAuthProviderTests()
    {
        var options = Substitute.For<IOptionsMonitor<ConnectedAccountsOptions>>();
        options.CurrentValue.Returns(_ => configured);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(OAuthProviderBase.HttpClientName).Returns(_ => new HttpClient(handler, disposeHandler: false));
        google = new GoogleOAuthProvider(factory, options, TimeProvider.System, NullLogger<GoogleOAuthProvider>.Instance);
        microsoft = new MicrosoftOAuthProvider(factory, options, TimeProvider.System, NullLogger<MicrosoftOAuthProvider>.Instance);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => handler.Dispose();

    [Fact]
    public void AuthorizationUrl_Google_AsksOfflineIncrementalReadOnlyScopesWithPkce()
    {
        var url = google.AuthorizationUrl(new OAuthAuthorization("state-1", "challenge-1", Mail: false, Calendar: true, LoginHint: "mario@gmail.com"));
        var query = HttpUtility.ParseQueryString(url.Query);

        url.GetLeftPart(UriPartial.Path).ShouldBe("https://accounts.google.com/o/oauth2/v2/auth");
        (query["response_type"], query["client_id"], query["state"], query["code_challenge"], query["code_challenge_method"])
            .ShouldBe(("code", GoogleClient, "state-1", "challenge-1", "S256"));
        query["redirect_uri"].ShouldBe("https://app.example.com/api/connected-accounts/callback/google");
        query["scope"]!.Split(' ').ShouldBe(["openid", "email", "profile", GoogleOAuthProvider.CalendarScope]);
        (query["access_type"], query["include_granted_scopes"], query["prompt"], query["login_hint"]).ShouldBe(("offline", "true", "consent", "mario@gmail.com"));
    }

    [Fact]
    public void AuthorizationUrl_Microsoft_UsesTheCommonAuthorityAndOfflineAccess()
    {
        var url = microsoft.AuthorizationUrl(new OAuthAuthorization("s", "c", Mail: true, Calendar: false, LoginHint: null));
        var query = HttpUtility.ParseQueryString(url.Query);

        url.GetLeftPart(UriPartial.Path).ShouldBe("https://login.microsoftonline.com/common/oauth2/v2.0/authorize");
        query["scope"]!.Split(' ').ShouldBe(["openid", "email", "profile", "offline_access", "User.Read", "Mail.Read"]);
        (query["redirect_uri"], query["response_mode"], query["login_hint"]).ShouldBe(("https://app.example.com/api/connected-accounts/callback/microsoft", "query", null));
    }

    [Fact]
    public async Task ExchangeCode_SendsVerifierAndSecret_AndReadsTheIdentityAndCapabilities()
    {
        handler.Answer(HttpStatusCode.OK, Json(new
        {
            access_token = "access-1",
            refresh_token = "refresh-1",
            expires_in = 3599,
            scope = $"openid {GoogleOAuthProvider.MailScope} https://www.googleapis.com/auth/userinfo.email",
            id_token = IdToken(new { aud = GoogleClient, email = "Mario@Gmail.com", name = "Mario Rossi" }),
        }));

        var tokens = (await google.ExchangeCodeAsync("code-1", "verifier-1", Ct)).Value;

        (tokens.AccessToken, tokens.RefreshToken, tokens.HasMail, tokens.HasCalendar, tokens.Address, tokens.DisplayName)
            .ShouldBe(("access-1", "refresh-1", true, false, "Mario@Gmail.com", "Mario Rossi"));
        var form = HttpUtility.ParseQueryString(handler.LastBody!);
        handler.LastRequest!.RequestUri.ShouldBe(new Uri("https://oauth2.googleapis.com/token"));
        (form["grant_type"], form["code"], form["code_verifier"], form["client_secret"], form["redirect_uri"])
            .ShouldBe(("authorization_code", "code-1", "verifier-1", "google-secret", "https://app.example.com/api/connected-accounts/callback/google"));
    }

    [Fact]
    public async Task ExchangeCode_Microsoft_UsesPreferredUsername_AndGraphScopes()
    {
        handler.Answer(HttpStatusCode.OK, Json(new
        {
            access_token = "a",
            refresh_token = "r",
            scope = "https://graph.microsoft.com/Calendars.Read https://graph.microsoft.com/Mail.Read https://graph.microsoft.com/User.Read openid",
            id_token = IdToken(new { aud = MicrosoftClient, preferred_username = "studio@contoso.it" }),
        }));

        var tokens = (await microsoft.ExchangeCodeAsync("code", "verifier", Ct)).Value;

        (tokens.Address, tokens.HasMail, tokens.HasCalendar).ShouldBe(("studio@contoso.it", true, true));
    }

    [Fact]
    public async Task ExchangeCode_RefusesAnIdTokenForAnotherClient_AndProviderErrors()
    {
        handler.Answer(HttpStatusCode.OK, Json(new { access_token = "a", refresh_token = "r", id_token = IdToken(new { aud = "someone-else", email = "x@y.it" }) }));
        (await google.ExchangeCodeAsync("code", "verifier", Ct)).Error!.Code.ShouldBe(EventCodes.Mailbox.OAuthExchangeFailed);

        handler.Answer(HttpStatusCode.BadRequest, Json(new { error = "invalid_grant", error_description = "Bad Request" }));
        (await google.ExchangeCodeAsync("code", "verifier", Ct)).Error!.Code.ShouldBe(EventCodes.Mailbox.OAuthExchangeFailed);

        handler.Answer(HttpStatusCode.OK, Json(new { access_token = "a" }));
        (await google.ExchangeCodeAsync("code", "verifier", Ct)).Error!.Code.ShouldBe(EventCodes.Mailbox.OAuthExchangeFailed);

        handler.Throw = new HttpRequestException("down");
        (await google.ExchangeCodeAsync("code", "verifier", Ct)).Error!.Code.ShouldBe(EventCodes.Mailbox.OAuthExchangeFailed);
    }

    [Fact]
    public async Task Refresh_RefusedGrantIsAFailure_ServerErrorsThrow()
    {
        handler.Answer(HttpStatusCode.OK, Json(new { access_token = "a2", refresh_token = "r2", scope = "Mail.Read" }));
        var tokens = (await microsoft.RefreshAsync("r1", Ct)).Value;
        (tokens.AccessToken, tokens.RefreshToken, tokens.HasMail).ShouldBe(("a2", "r2", true));
        HttpUtility.ParseQueryString(handler.LastBody!)["grant_type"].ShouldBe("refresh_token");

        handler.Answer(HttpStatusCode.BadRequest, Json(new { error = "invalid_grant" }));
        (await microsoft.RefreshAsync("r1", Ct)).Error!.Code.ShouldBe(EventCodes.Mailbox.OAuthExchangeFailed);

        handler.Answer(HttpStatusCode.ServiceUnavailable, "");
        await Should.ThrowAsync<HttpRequestException>(() => microsoft.RefreshAsync("r1", Ct));
    }

    [Fact]
    public async Task Revoke_GoogleCallsTheRevokeEndpoint_MicrosoftHasNone()
    {
        handler.Answer(HttpStatusCode.OK, "");
        (await google.RevokeAsync("refresh-1", Ct)).ShouldBe(OAuthRevocation.Revoked);
        handler.LastRequest!.RequestUri.ShouldBe(new Uri("https://oauth2.googleapis.com/revoke"));
        HttpUtility.ParseQueryString(handler.LastBody!)["token"].ShouldBe("refresh-1");

        handler.Answer(HttpStatusCode.InternalServerError, "");
        (await google.RevokeAsync("refresh-1", Ct)).ShouldBe(OAuthRevocation.Failed);

        (await microsoft.RevokeAsync("refresh-1", Ct)).ShouldBe(OAuthRevocation.NotSupported);
    }

    [Fact]
    public void IsConfigured_NeedsClientSecretAndCallback()
    {
        google.IsConfigured.ShouldBeTrue();
        configured.Google.ClientSecret = null;
        google.IsConfigured.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => google.AuthorizationUrl(new OAuthAuthorization("s", "c", true, false, null)));

        configured.CallbackBaseUrl = null;
        microsoft.IsConfigured.ShouldBeFalse();
    }

    private static string Json(object value) => JsonSerializer.Serialize(value);

    private static string IdToken(object claims) =>
        $"{Base64Url("{\"alg\":\"RS256\"}")}.{Base64Url(Json(claims))}.signature";

    private static string Base64Url(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class StubHandler : HttpMessageHandler
    {
        private HttpStatusCode status = HttpStatusCode.OK;
        private string body = "{}";

        public HttpRequestMessage? LastRequest { get; private set; }

        public string? LastBody { get; private set; }

        public Exception? Throw { get; set; }

        public void Answer(HttpStatusCode answerStatus, string answerBody)
        {
            (status, body, Throw) = (answerStatus, answerBody, null);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            if (Throw is not null)
            {
                throw Throw;
            }

            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
