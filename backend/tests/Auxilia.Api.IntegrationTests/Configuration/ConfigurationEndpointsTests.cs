using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Auxilia.Api.IntegrationTests.Host;
using Auxilia.Api.IntegrationTests.Platform;
using Auxilia.Contracts.Configuration;
using Auxilia.Contracts.Identity;
using Auxilia.Contracts.Platform;

namespace Auxilia.Api.IntegrationTests.Configuration;

/// <summary>
/// F23 over HTTP: the settings editor and the branding uploads for the System only (tenant-scoped platform token,
/// D-21), the public branding for every visitor, images with their hash as ETag.
/// </summary>
public sealed class ConfigurationEndpointsTests : IClassFixture<PlatformIdentityTests.Factory>
{
    private const string ConsoleClient = "test-console";
    private const string ConsoleSecret = "integration-tests-console-credential";
    private const string Password = "a long enough platform password";

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01];

    private readonly PlatformIdentityTests.Factory factory;

    public ConfigurationEndpointsTests(PlatformIdentityTests.Factory factory) => this.factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Settings_AreListedSetAndReset()
    {
        var token = await TenantTokenAsync();

        using var list = await SendAsync(HttpMethod.Get, "/api/v1/settings", token);
        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        var settings = (await list.Content.ReadFromJsonAsync<SettingResponse[]>(Ct))!.ToDictionary(setting => setting.Key);
        settings["cases.expiry.expiringDays"].Kind.ShouldBe("integer");
        settings["branding.background.kind"].Choices.ShouldBe(["Gradient", "Solid", "Image"]);
        settings.Values.ShouldAllBe(setting => setting.Kind != "secret" || setting.EffectiveValue == null);

        using var set = await SendAsync(HttpMethod.Put, "/api/v1/settings/cases.expiry.expiringDays", token, new SetSettingRequest(Json("21")));
        set.StatusCode.ShouldBe(HttpStatusCode.OK, await set.Content.ReadAsStringAsync(Ct));
        var changed = (await set.Content.ReadFromJsonAsync<SettingResponse>(Ct))!;
        changed.EffectiveValue!.Value.GetInt32().ShouldBe(21);
        changed.Source.ShouldBe("Tenant");

        using var invalid = await SendAsync(HttpMethod.Put, "/api/v1/settings/cases.expiry.expiringDays", token, new SetSettingRequest(Json("\"many\"")));
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(invalid)).ShouldBe("AUX-20003");

        using var reset = await SendAsync(HttpMethod.Delete, "/api/v1/settings/cases.expiry.expiringDays", token);
        reset.StatusCode.ShouldBe(HttpStatusCode.OK);
        var restored = (await reset.Content.ReadFromJsonAsync<SettingResponse>(Ct))!;
        restored.Source.ShouldBe("Default");
        restored.HasTenantValue.ShouldBeFalse();

        using var unknown = await SendAsync(HttpMethod.Put, "/api/v1/settings/no.such.key", token, new SetSettingRequest(Json("1")));
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var unknownReset = await SendAsync(HttpMethod.Delete, "/api/v1/settings/no.such.key", token);
        unknownReset.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Branding_IsPublicAndFollowsTheSettings()
    {
        var token = await TenantTokenAsync();
        using var client = factory.CreateClient();

        using var noTenant = await client.GetAsync("/api/v1/branding", Ct);
        noTenant.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await SendAsync(HttpMethod.Put, "/api/v1/settings/branding.appName", token, new SetSettingRequest(Json("\"Studio Alfa\"")))).StatusCode
            .ShouldBe(HttpStatusCode.OK);
        using var branding = await GetAsync(client, "/api/v1/branding");
        branding.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = (await branding.Content.ReadFromJsonAsync<BrandingResponse>(Ct))!;
        body.AppName.ShouldBe("Studio Alfa");
        body.PrimaryColor.ShouldBe("#667eea");

        using var badColour = await SendAsync(HttpMethod.Put, "/api/v1/settings/branding.theme.primaryColor", token, new SetSettingRequest(Json("\"red\"")));
        badColour.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await SendAsync(HttpMethod.Delete, "/api/v1/settings/branding.appName", token)).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var restored = await GetAsync(client, "/api/v1/branding");
        (await restored.Content.ReadFromJsonAsync<BrandingResponse>(Ct))!.AppName.ShouldBe("Auxilia Digitale");
    }

    [Fact]
    public async Task Images_AreUploadedServedWithETagAndRemoved()
    {
        var token = await TenantTokenAsync();
        using var client = factory.CreateClient();

        using var uploaded = await UploadAsync("logo", token, Png, "logo.png");
        uploaded.StatusCode.ShouldBe(HttpStatusCode.OK, await uploaded.Content.ReadAsStringAsync(Ct));
        var version = (await uploaded.Content.ReadFromJsonAsync<BrandingResponse>(Ct))!.LogoVersion!;
        version.Length.ShouldBe(16);

        using var image = await GetAsync(client, $"/api/v1/branding/logo?v={version}");
        image.StatusCode.ShouldBe(HttpStatusCode.OK);
        image.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
        (await image.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(Png);
        image.Headers.ETag!.Tag.ShouldBe($"\"{version}\"");
        image.Headers.CacheControl!.ToString().ShouldContain("immutable");

        using var revalidated = await GetAsync(client, "/api/v1/branding/logo", $"\"{version}\"");
        revalidated.StatusCode.ShouldBe(HttpStatusCode.NotModified);
        using var stale = await GetAsync(client, "/api/v1/branding/logo?v=old");
        stale.Headers.CacheControl!.NoCache.ShouldBeTrue();

        using var svg = await UploadAsync("logo", token, "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray(), "logo.svg");
        svg.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(svg)).ShouldBe("AUX-20007");

        using var tooLarge = await UploadAsync("logo", token, [.. Png, .. new byte[600 * 1024]], "big.png");
        tooLarge.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(tooLarge)).ShouldBe("AUX-20008");

        using var unknownAsset = await UploadAsync("favicon", token, Png, "x.png");
        unknownAsset.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var removed = await SendAsync(HttpMethod.Delete, "/api/v1/branding/logo", token);
        removed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await removed.Content.ReadFromJsonAsync<BrandingResponse>(Ct))!.LogoVersion.ShouldBeNull();
        using var gone = await GetAsync(client, "/api/v1/branding/logo");
        gone.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ErrorCodeAsync(gone)).ShouldBe("AUX-20009");
    }

    [Fact]
    public async Task Editing_IsForTheSystemOnly()
    {
        using var client = factory.CreateClient();
        using var anonymous = await GetAsync(client, "/api/v1/settings");
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        foreach (var (method, path) in new[] { (HttpMethod.Get, "/api/v1/settings"), (HttpMethod.Delete, "/api/v1/branding/logo") })
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Add(ApiFactory.TestTenantClaimHeader, ApiDatabase.TenantA);
            request.Headers.Add(ApiFactory.TestRolesHeader, "Administrator");
            using var administrator = await client.SendAsync(request, Ct);
            administrator.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await ErrorCodeAsync(administrator)).ShouldBe("AUX-12040");
        }
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private async Task<HttpResponseMessage> UploadAsync(string asset, string token, byte[] content, string fileName)
    {
        using var client = factory.CreateClient();
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/branding/{asset}") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request, Ct);
    }

    private async Task<string> TenantTokenAsync()
    {
        var email = "settings-" + Guid.NewGuid().ToString("N")[..8] + "@example.test";
        var activation = await factory.AddPlatformUserAsync(email);
        using var client = factory.CreateClient();

        using var enrollment = await client.PostAsJsonAsync("/api/v1/platform/auth/enrollment", new PlatformEnrollmentRequest(activation), Ct);
        var secret = (await enrollment.Content.ReadFromJsonAsync<PlatformEnrollmentResponse>(Ct))!.Secret;
        using var activated = await client.PostAsJsonAsync(
            "/api/v1/platform/auth/activate", new PlatformActivateRequest(activation, Password, PlatformIdentityTests.Code(secret, 0)), Ct);
        activated.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var signIn = new HttpRequestMessage(HttpMethod.Post, "/api/v1/platform/auth/token")
        {
            Content = JsonContent.Create(new PlatformTokenRequest("password", email, Password, PlatformIdentityTests.Code(secret, 1), null)),
        };
        signIn.Headers.Add("X-Client-Id", ConsoleClient);
        signIn.Headers.Add("X-Client-Secret", ConsoleSecret);
        using var signedIn = await client.SendAsync(signIn, Ct);
        var console = (await signedIn.Content.ReadFromJsonAsync<TokenResponse>(Ct))!.AccessToken;

        using var opened = await SendAsync(HttpMethod.Post, $"/api/v1/platform/tenants/{ApiDatabase.TenantA}/token", console);
        return (await opened.Content.ReadFromJsonAsync<PlatformTenantTokenResponse>(Ct))!.AccessToken;
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string path, string? ifNoneMatch = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Tenant", ApiDatabase.TenantA);
        if (ifNoneMatch is not null)
        {
            request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(ifNoneMatch));
        }

        return await client.SendAsync(request, Ct);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string token, object? body = null)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body, body.GetType()) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request, Ct);
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errorCode").GetString();
}
