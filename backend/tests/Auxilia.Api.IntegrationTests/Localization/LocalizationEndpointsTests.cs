using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Auxilia.Api.IntegrationTests.Host;
using Auxilia.Api.IntegrationTests.Platform;
using Auxilia.Contracts.Common;
using Auxilia.Contracts.Identity;
using Auxilia.Contracts.Localization;
using Auxilia.Contracts.Platform;
using Auxilia.Persistence.Tenant;
using Auxilia.Persistence.Tenant.DataMigrations.Localization;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Auxilia.Api.IntegrationTests.Localization;

/// <summary>
/// F24 over HTTP: anonymous bundles with ETag and fallbacks, the resource editor for the System only (tenant-scoped
/// platform token, D-18/D-21), and edits visible at the next request.
/// </summary>
public sealed class LocalizationEndpointsTests : IClassFixture<PlatformIdentityTests.Factory>, IAsyncLifetime
{
    private const string ConsoleClient = "test-console";
    private const string ConsoleSecret = "integration-tests-console-credential";
    private const string Password = "a long enough platform password";

    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static bool seeded;

    private readonly PlatformIdentityTests.Factory factory;

    public LocalizationEndpointsTests(PlatformIdentityTests.Factory factory) => this.factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await SeedLock.WaitAsync();
        try
        {
            if (!seeded)
            {
                var connectionString = new NpgsqlConnectionStringBuilder(ApiDatabase.Instance.CatalogConnectionString) { Database = "tenant_a" }.ConnectionString;
                await using var dataSource = NpgsqlDataSource.Create(connectionString);
                await using (var languages = new TenantDbContext(TenantDbContextOptions.Create(dataSource)))
                {
                    // ProfileEndpointsTests may add the same languages at the same time, which is fine.
                    try
                    {
                        await TranslationSeed.EnsureLanguagesAsync(languages, CancellationToken.None);
                    }
                    catch (DbUpdateException)
                    {
                    }
                }

                await using var db = new TenantDbContext(TenantDbContextOptions.Create(dataSource));
                await TranslationSeed.UpsertAsync(db, TranslationSeed.LoadAll(), CancellationToken.None);
                seeded = true;
            }
        }
        finally
        {
            SeedLock.Release();
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Bundle_IsAnonymousWithETagAndRevalidation()
    {
        using var client = factory.CreateClient();

        using var languages = await GetAsync(client, "/api/v1/i18n/languages");
        languages.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await languages.Content.ReadFromJsonAsync<LanguageResponse[]>(Ct))!.Select(language => (language.Code, language.IsDefault))
            .ShouldBe([("it", true), ("en", false)]);

        using var bundle = await GetAsync(client, "/api/v1/i18n/it");
        bundle.StatusCode.ShouldBe(HttpStatusCode.OK);
        var values = (await bundle.Content.ReadFromJsonAsync<Dictionary<string, string>>(Ct))!;
        (values["Save"], values["nav.cases"]).ShouldBe(("Salva", "Pratiche"));
        values.Count.ShouldBeGreaterThan(600);
        var etag = bundle.Headers.ETag!;
        bundle.Headers.CacheControl!.NoCache.ShouldBeTrue();

        using var notModified = await GetAsync(client, "/api/v1/i18n/it", etag.Tag);
        notModified.StatusCode.ShouldBe(HttpStatusCode.NotModified);
        using var otherTag = await GetAsync(client, "/api/v1/i18n/it", "\"other\"");
        otherTag.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var english = await GetAsync(client, "/api/v1/i18n/en");
        (await english.Content.ReadFromJsonAsync<Dictionary<string, string>>(Ct))!["Save"].ShouldBe("Save");
        english.Headers.ETag!.Tag.ShouldNotBe(etag.Tag);
    }

    [Fact]
    public async Task Bundle_UnknownLanguageOrMissingTenant()
    {
        using var client = factory.CreateClient();

        using var unknown = await GetAsync(client, "/api/v1/i18n/de");
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ErrorCodeAsync(unknown)).ShouldBe("AUX-21003");

        using var noTenant = await client.GetAsync("/api/v1/i18n/it", Ct);
        noTenant.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Editor_IsForTheSystemOnly()
    {
        using var client = factory.CreateClient();
        using var anonymous = await GetAsync(client, "/api/v1/localization/keys");
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/localization/keys");
        request.Headers.Add(ApiFactory.TestTenantClaimHeader, ApiDatabase.TenantA);
        request.Headers.Add(ApiFactory.TestRolesHeader, "Administrator");
        using var administrator = await client.SendAsync(request, Ct);
        administrator.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(administrator)).ShouldBe("AUX-12040");
    }

    [Fact]
    public async Task Edits_AreVisibleAtTheNextRequestWithFallbacks()
    {
        var token = await TenantTokenAsync();
        var key = "app.test." + Guid.NewGuid().ToString("N")[..8];
        using var client = factory.CreateClient();
        using var before = await GetAsync(client, "/api/v1/i18n/it");
        var etag = before.Headers.ETag!.Tag;

        // English only: Italian falls back to English.
        using var created = await SendAsync(HttpMethod.Post, "/api/v1/localization/keys", token,
            new CreateResourceKeyRequest(key, "app", "Test key", new Dictionary<string, string> { ["en"] = "Hello" }));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        var id = (await created.Content.ReadFromJsonAsync<CreateResourceKeyResponse>(Ct))!.Id;
        created.Headers.Location!.ToString().ShouldBe($"/api/v1/localization/keys/{id}");

        using var afterCreate = await GetAsync(client, "/api/v1/i18n/it", etag);
        afterCreate.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await afterCreate.Content.ReadFromJsonAsync<Dictionary<string, string>>(Ct))![key].ShouldBe("Hello");

        (await SendAsync(HttpMethod.Put, $"/api/v1/localization/keys/{id}/translations/it", token, new SetTranslationRequest("Ciao"))).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);
        using var afterEdit = await GetAsync(client, "/api/v1/i18n/it");
        (await afterEdit.Content.ReadFromJsonAsync<Dictionary<string, string>>(Ct))![key].ShouldBe("Ciao");

        using var missing = await SendAsync(HttpMethod.Get, $"/api/v1/localization/keys?search={key}&filter[missingLanguage]=en", token);
        (await missing.Content.ReadFromJsonAsync<PagedResponse<ResourceKeyResponse>>(Ct))!.TotalCount.ShouldBe(0);

        (await SendAsync(HttpMethod.Delete, $"/api/v1/localization/keys/{id}/translations/en", token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SendAsync(HttpMethod.Delete, $"/api/v1/localization/keys/{id}/translations/it", token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var keyOnly = await GetAsync(client, "/api/v1/i18n/en");
        (await keyOnly.Content.ReadFromJsonAsync<Dictionary<string, string>>(Ct))![key].ShouldBe(key);

        using var detail = await SendAsync(HttpMethod.Get, $"/api/v1/localization/keys/{id}", token);
        (await detail.Content.ReadFromJsonAsync<ResourceKeyResponse>(Ct))!.MissingLanguages.ShouldBe(["en", "it"]);

        (await SendAsync(HttpMethod.Put, $"/api/v1/localization/keys/{id}", token, new UpdateResourceKeyRequest("common", null))).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);
        (await SendAsync(HttpMethod.Delete, $"/api/v1/localization/keys/{id}", token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SendAsync(HttpMethod.Get, $"/api/v1/localization/keys/{id}", token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Editor_ListsLanguagesCategoriesAndRefusesInvalidInput()
    {
        var token = await TenantTokenAsync();

        using var languages = await SendAsync(HttpMethod.Get, "/api/v1/localization/languages", token);
        var stats = (await languages.Content.ReadFromJsonAsync<LanguageStatsResponse[]>(Ct))!;
        stats.Select(language => language.Code).ShouldBe(["en", "it"]);
        stats.ShouldAllBe(language => language.TranslatedCount > 600);

        using var categories = await SendAsync(HttpMethod.Get, "/api/v1/localization/categories", token);
        (await categories.Content.ReadFromJsonAsync<ResourceCategoryResponse[]>(Ct))!.Select(category => category.Category).ShouldContain("validation");

        using var duplicate = await SendAsync(HttpMethod.Post, "/api/v1/localization/keys", token, new CreateResourceKeyRequest("Save", "common", null, null));
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(duplicate)).ShouldBe("AUX-21002");

        using var invalid = await SendAsync(HttpMethod.Post, "/api/v1/localization/keys", token, new CreateResourceKeyRequest("bad key", "common", null, null));
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(invalid)).ShouldBe("AUX-21004");

        using var badPage = await SendAsync(HttpMethod.Get, "/api/v1/localization/keys?pageSize=500&sort=value", token);
        badPage.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private async Task<string> TenantTokenAsync()
    {
        var email = "i18n-" + Guid.NewGuid().ToString("N")[..8] + "@example.test";
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
