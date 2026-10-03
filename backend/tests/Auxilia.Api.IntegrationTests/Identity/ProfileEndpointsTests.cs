using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Auxilia.Api.IntegrationTests.Host;
using Auxilia.Contracts.Identity;
using Auxilia.Diagnostics;
using Auxilia.Persistence.Tenant;
using Auxilia.Persistence.Tenant.DataMigrations.Localization;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using SkiaSharp;

namespace Auxilia.Api.IntegrationTests.Identity;

/// <summary>
/// B-03 over HTTP (F04, Q35, Q36): the own profile, language, theme, picture (resized, visible to its owner and to
/// staff) and sessions of the signed-in user, never another user's.
/// </summary>
public sealed class ProfileEndpointsTests(ProfileEndpointsTests.Factory factory) : IClassFixture<ProfileEndpointsTests.Factory>, IAsyncLifetime
{
    private const string Password = "A long Passw0rd for tests!";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        // The languages of tenant A (it, en), as the localization data-migration creates them; another class may seed them
        // at the same time, which is fine.
        var connectionString = new NpgsqlConnectionStringBuilder(ApiDatabase.Instance.CatalogConnectionString) { Database = "tenant_a" }.ConnectionString;
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var db = new TenantDbContext(TenantDbContextOptions.Create(dataSource));
        try
        {
            await TranslationSeed.EnsureLanguagesAsync(db, CancellationToken.None);
        }
        catch (DbUpdateException)
        {
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Profile_UpdateLanguageAndTheme()
    {
        var (token, userName) = await SignedInAsync(TenantRole.Client);

        var profile = await GetProfileAsync(token);
        (profile.UserName, profile.Theme, profile.ImageVersion).ShouldBe((userName, "System", null));

        using var updated = await SendAsync(HttpMethod.Put, "/api/v1/me/profile", token, new UpdateProfileRequest("Maria", "Bianchi", "maria@example.test", "333 1234567"));
        updated.StatusCode.ShouldBe(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync(Ct));
        (await updated.Content.ReadFromJsonAsync<ProfileResponse>(Ct))!.ShouldBe(profile with
        {
            FirstName = "Maria", LastName = "Bianchi", Email = "maria@example.test", Phone = "333 1234567",
        });

        using var invalid = await SendAsync(HttpMethod.Put, "/api/v1/me/profile", token, new UpdateProfileRequest("", "Bianchi", null, "12"));
        (await ErrorFieldsAsync(invalid, HttpStatusCode.BadRequest)).ShouldBe(["firstName", "email", "phone"], ignoreOrder: true);

        using var english = await SendAsync(HttpMethod.Put, "/api/v1/me/language", token, new ChangeLanguageRequest("en"));
        english.StatusCode.ShouldBe(HttpStatusCode.OK, await english.Content.ReadAsStringAsync(Ct));
        (await english.Content.ReadFromJsonAsync<ProfileResponse>(Ct))!.LanguageCode.ShouldBe("en");
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Put, "/api/v1/me/language", token, new ChangeLanguageRequest("xx")),
            HttpStatusCode.BadRequest, EventCodes.Identity.LanguageNotAvailable);

        using var dark = await SendAsync(HttpMethod.Put, "/api/v1/me/preferences", token, new UpdatePreferencesRequest("Dark"));
        (await dark.Content.ReadFromJsonAsync<ProfileResponse>(Ct))!.Theme.ShouldBe("Dark");
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Put, "/api/v1/me/preferences", token, new UpdatePreferencesRequest("Pink")),
            HttpStatusCode.BadRequest, EventCodes.Identity.UserValueInvalid);

        (await GetProfileAsync(token)).ShouldBe(profile with
        {
            FirstName = "Maria", LastName = "Bianchi", Email = "maria@example.test", Phone = "333 1234567", LanguageCode = "en", Theme = "Dark",
        });
    }

    [Fact]
    public async Task Picture_IsResized_ServedWithETag_ToItsOwnerAndStaffOnly()
    {
        var (client, _) = await SignedInAsync(TenantRole.Client);
        var clientId = (await GetProfileAsync(client)).UserId;
        var (otherClient, _) = await SignedInAsync(TenantRole.Client);
        var (employee, _) = await SignedInAsync(TenantRole.Employee);

        using var uploaded = await UploadAsync(client, Png(1000, 500), "picture.png");
        uploaded.StatusCode.ShouldBe(HttpStatusCode.OK, await uploaded.Content.ReadAsStringAsync(Ct));
        var version = (await uploaded.Content.ReadFromJsonAsync<ProfileResponse>(Ct))!.ImageVersion;
        version.ShouldNotBeNull();

        using var own = await SendAsync(HttpMethod.Get, $"/api/v1/users/{clientId}/image?v={version}", client);
        own.StatusCode.ShouldBe(HttpStatusCode.OK);
        own.Content.Headers.ContentType!.MediaType.ShouldBe("image/jpeg");
        own.Headers.ETag!.Tag.ShouldBe($"\"{version}\"");
        using (var picture = SKBitmap.Decode(await own.Content.ReadAsByteArrayAsync(Ct)))
        {
            (picture.Width, picture.Height).ShouldBe((400, 200));
        }

        using var cached = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/users/{clientId}/image");
        cached.Headers.Authorization = new AuthenticationHeaderValue("Bearer", employee);
        cached.Headers.IfNoneMatch.Add(new EntityTagHeaderValue($"\"{version}\""));
        using var http = factory.CreateClient();
        (await http.SendAsync(cached, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotModified);

        (await SendAsync(HttpMethod.Get, $"/api/v1/users/{clientId}/image", otherClient)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await ShouldHaveCodeAsync(await UploadAsync(client, "not an image"u8.ToArray(), "notes.txt"),
            HttpStatusCode.BadRequest, EventCodes.Identity.ProfileImageInvalid);
        (await SendAsync(HttpMethod.Delete, "/api/v1/me/image", client)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetProfileAsync(client)).ImageVersion.ShouldBeNull();
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Delete, "/api/v1/me/image", client),
            HttpStatusCode.NotFound, EventCodes.Identity.ProfileImageNotFound);
    }

    [Fact]
    public async Task Sessions_ListsTheOwnOpenOnes_AndEndsOnlyThose()
    {
        var (_, userName) = await factory.AddUserAsync([TenantRole.Employee], Password);
        var first = await factory.SignInAsync(userName, Password, Ct);
        var second = await factory.SignInAsync(userName, Password, Ct);
        var (stranger, _) = await SignedInAsync(TenantRole.Employee);

        var sessions = await SessionsAsync(second);
        sessions.Length.ShouldBe(2);
        var current = sessions.Where(session => session.IsCurrent).ShouldHaveSingleItem();
        var other = sessions.Single(session => !session.IsCurrent);
        (other.ClientId, current.ClientId).ShouldBe((AuthEndpointsTests.ClientId, AuthEndpointsTests.ClientId));

        // Another user's session is not found, never ended.
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Delete, $"/api/v1/me/sessions/{other.Id}", stranger),
            HttpStatusCode.NotFound, EventCodes.Identity.SessionNotFound);

        (await SendAsync(HttpMethod.Delete, $"/api/v1/me/sessions/{other.Id}", second)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SessionsAsync(second)).ShouldHaveSingleItem().Id.ShouldBe(current.Id);
        (await SendAsync(HttpMethod.Get, "/api/v1/me/profile", first)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Delete, $"/api/v1/me/sessions/{other.Id}", second),
            HttpStatusCode.NotFound, EventCodes.Identity.SessionNotFound);
    }

    [Fact]
    public async Task Profile_NeedsASignedInTenantUser()
    {
        (await SendAsync(HttpMethod.Get, "/api/v1/me/profile", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await SendAsync(HttpMethod.Get, "/api/v1/me/sessions", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await SendAsync(HttpMethod.Get, $"/api/v1/users/{Guid.NewGuid()}/image", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.SeaGreen);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private async Task<(string Token, string UserName)> SignedInAsync(TenantRole role)
    {
        var (_, userName) = await factory.AddUserAsync([role], Password);
        return (await factory.SignInAsync(userName, Password, Ct), userName);
    }

    private async Task<ProfileResponse> GetProfileAsync(string token)
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/v1/me/profile", token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<ProfileResponse>(Ct))!;
    }

    private async Task<MySessionResponse[]> SessionsAsync(string token)
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/v1/me/sessions", token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<MySessionResponse[]>(Ct))!;
    }

    private async Task<HttpResponseMessage> UploadAsync(string token, byte[] content, string fileName)
    {
        using var http = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/me/image");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        request.Content = new MultipartFormDataContent { { file, "file", fileName } };
        return await http.SendAsync(request, Ct);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? token, object? body = null)
    {
        using var http = factory.CreateClient();
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType());
        }

        if (token is null)
        {
            request.Headers.Add("X-Tenant", ApiDatabase.TenantA);
        }
        else
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await http.SendAsync(request, Ct);
    }

    private static async Task<string[]> ErrorFieldsAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        response.StatusCode.ShouldBe(status, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors").EnumerateObject().Select(field => field.Name).ToArray();
    }

    private static async Task ShouldHaveCodeAsync(HttpResponseMessage response, HttpStatusCode status, int code)
    {
        using (response)
        {
            response.StatusCode.ShouldBe(status, await response.Content.ReadAsStringAsync(Ct));
            (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errorCode").GetString().ShouldBe($"AUX-{code}");
        }
    }

    public sealed class Factory : AuthEndpointsTests.Factory;
}
