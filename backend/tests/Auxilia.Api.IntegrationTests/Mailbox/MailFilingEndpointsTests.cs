using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using Auxilia.Api.IntegrationTests.Cases;
using Auxilia.Api.IntegrationTests.Identity;
using Auxilia.Application.Abstractions.Mailbox;
using Auxilia.Contracts.Cases;
using Auxilia.Contracts.Common;
using Auxilia.Contracts.Documents;
using Auxilia.Contracts.Mailbox;
using Auxilia.Diagnostics;
using Auxilia.Domain.Mailbox;
using Auxilia.Persistence.Tenant;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Npgsql;

namespace Auxilia.Api.IntegrationTests.Mailbox;

/// <summary>
/// C-06b over HTTP: a message read live from a fake Gmail, its .eml and attachment downloaded, filed into a case through
/// the Documents module (local storage) and recognised the second time; only the owner reaches the message.
/// </summary>
public sealed class MailFilingEndpointsTests(MailFilingEndpointsTests.Factory factory) : IClassFixture<MailFilingEndpointsTests.Factory>
{
    private const string Password = "A long Passw0rd for tests!";

    private static readonly string Original = """
        From: "Mario Rossi" <mario@example.com>
        To: studio@example.com
        Subject: Documenti pratica
        Message-ID: <filing-test@example.com>
        MIME-Version: 1.0
        Content-Type: multipart/mixed; boundary="b1"

        --b1
        Content-Type: text/plain; charset=utf-8

        In allegato la fattura.
        --b1
        Content-Type: application/pdf; name="fattura.pdf"
        Content-Disposition: attachment; filename="fattura.pdf"
        Content-Transfer-Encoding: base64

        JVBERi0xLjcK
        --b1--
        """.ReplaceLineEndings("\r\n");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Owner_ReadsDownloadsAndFilesAMessage_TheSecondFilingNeedsForce()
    {
        var token = await SignInAsync(TenantRole.Administrator);
        var other = await SignInAsync(TenantRole.Employee);
        var account = await ConnectGoogleAsync(token);
        var messageId = await StoreMessageAsync(account.Id, "msg-1");
        factory.Mail.Raw["msg-1"] = Encoding.UTF8.GetBytes(Original);
        var (clientId, _) = await CaseEndpointsTests.Factory.AddClientAsync();
        var caseId = await OpenCaseAsync(token, clientId);

        var detail = await GetAsync<MailMessageDetailResponse>(token, $"/api/v1/me/connected-accounts/mail-messages/{messageId}");
        detail.Text!.Trim().ShouldBe("In allegato la fattura.");
        detail.Attachments.ShouldHaveSingleItem().ShouldBe(new MailAttachmentResponse(0, "fattura.pdf", "application/pdf", 9));
        detail.FiledIn.ShouldBeEmpty();

        using (var eml = await SendAsync(HttpMethod.Get, $"/api/v1/me/connected-accounts/mail-messages/{messageId}/eml", token))
        {
            eml.StatusCode.ShouldBe(HttpStatusCode.OK);
            eml.Content.Headers.ContentType!.MediaType.ShouldBe("message/rfc822");
            eml.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
            eml.Headers.CacheControl!.NoStore.ShouldBeTrue();
            Encoding.UTF8.GetString(await eml.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(Original);
        }

        using (var attachment = await SendAsync(HttpMethod.Get, $"/api/v1/me/connected-accounts/mail-messages/{messageId}/attachments/0", token))
        {
            attachment.Content.Headers.ContentType!.MediaType.ShouldBe("application/octet-stream");
            (await attachment.Content.ReadAsByteArrayAsync(Ct)).ShouldBe("%PDF-1.7\n"u8.ToArray());
        }

        var filed = await FileAsync(token, messageId, new FileMailRequest(caseId, null, [0], "Dalla posta", false));
        filed.AttachmentDocumentIds.Count.ShouldBe(1);
        var documents = await GetAsync<PagedResponse<DocumentListItemResponse>>(token, $"/api/v1/documents?filter[caseId]={caseId}&sort=fileName");
        documents.Items.Select(item => item.FileName).ShouldBe(["2026-10-16_Documenti_pratica.eml", "fattura.pdf"]);

        using (var again = await SendAsync(HttpMethod.Post, $"/api/v1/me/connected-accounts/mail-messages/{messageId}/file", token, new FileMailRequest(caseId, null, null, null, false)))
        {
            again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await ErrorCodeAsync(again)).ShouldBe($"AUX-{EventCodes.Mailbox.MailAlreadyFiled}");
        }

        await FileAsync(token, messageId, new FileMailRequest(caseId, null, null, null, true));
        (await GetAsync<PagedResponse<DocumentListItemResponse>>(token, $"/api/v1/documents?filter[caseId]={caseId}")).TotalCount.ShouldBe(3);
        (await GetAsync<MailMessageDetailResponse>(token, $"/api/v1/me/connected-accounts/mail-messages/{messageId}")).FiledIn
            .ShouldHaveSingleItem().CaseId.ShouldBe(caseId);

        // Another user does not reach the message; a message gone at the provider is 404 too.
        (await SendAsync(HttpMethod.Get, $"/api/v1/me/connected-accounts/mail-messages/{messageId}", other)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Post, $"/api/v1/me/connected-accounts/mail-messages/{messageId}/file", other, new FileMailRequest(caseId, null, null, null, false)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var goneId = await StoreMessageAsync(account.Id, "gone");
        using var gone = await SendAsync(HttpMethod.Get, $"/api/v1/me/connected-accounts/mail-messages/{goneId}", token);
        (await ErrorCodeAsync(gone)).ShouldBe($"AUX-{EventCodes.Mailbox.MailMessageNotFound}");
        (await SendAsync(HttpMethod.Get, $"/api/v1/me/connected-accounts/mail-messages/{messageId}/attachments/7", token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Clients_CannotReachMessages()
    {
        var (_, userName) = await factory.AddUserAsync([TenantRole.Client], Password);
        var client = await factory.SignInAsync(userName, Password, Ct);

        (await SendAsync(HttpMethod.Get, $"/api/v1/me/connected-accounts/mail-messages/{Guid.CreateVersion7()}", client)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Post, $"/api/v1/me/connected-accounts/mail-messages/{Guid.CreateVersion7()}/file", client,
            new FileMailRequest(Guid.CreateVersion7(), null, null, null, false))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task<string> SignInAsync(TenantRole role)
    {
        var (_, userName) = await factory.AddUserAsync([role], Password);
        return await factory.SignInAsync(userName, Password, Ct);
    }

    private async Task<ConnectedAccountResponse> ConnectGoogleAsync(string token)
    {
        using var start = await SendAsync(HttpMethod.Post, "/api/v1/me/connected-accounts/oauth/google/authorize", token, new StartOAuthRequest(true, false, null));
        var started = (await start.Content.ReadFromJsonAsync<StartOAuthResponse>(Ct))!;
        var state = Uri.UnescapeDataString(started.AuthorizationUrl.Query.Split("state=")[1]);
        using var callback = await SendAsync(HttpMethod.Post, "/api/v1/me/connected-accounts/oauth/google/callback", token, new CompleteOAuthRequest(state, "code", null));
        callback.StatusCode.ShouldBe(HttpStatusCode.OK, await callback.Content.ReadAsStringAsync(Ct));
        return (await callback.Content.ReadFromJsonAsync<ConnectedAccountResponse>(Ct))!;
    }

    private async Task<Guid> OpenCaseAsync(string token, Guid clientId)
    {
        using var service = await SendAsync(HttpMethod.Post, "/api/v1/services", token,
            new CreateServiceRequest("Mail service " + Guid.NewGuid().ToString("N")[..8], null, 10m, 30, null, null));
        service.StatusCode.ShouldBe(HttpStatusCode.Created, await service.Content.ReadAsStringAsync(Ct));
        var serviceId = (await service.Content.ReadFromJsonAsync<ServiceResponse>(Ct))!.Id;
        using var opened = await SendAsync(HttpMethod.Post, "/api/v1/cases", token, new OpenCaseRequest(clientId, serviceId, null, null, null, null, null));
        opened.StatusCode.ShouldBe(HttpStatusCode.Created, await opened.Content.ReadAsStringAsync(Ct));
        return (await opened.Content.ReadFromJsonAsync<CaseResponse>(Ct))!.Id;
    }

    private static async Task<Guid> StoreMessageAsync(Guid accountId, string providerId)
    {
        await using var dataSource = NpgsqlDataSource.Create(
            new NpgsqlConnectionStringBuilder(ApiDatabase.Instance.CatalogConnectionString) { Database = "tenant_a" }.ConnectionString);
        await using var db = new TenantDbContext(TenantDbContextOptions.Create(dataSource));
        var folder = db.Set<MailFolder>().SingleOrDefault(item => item.AccountId == accountId);
        if (folder is null)
        {
            folder = MailFolder.Create(Guid.CreateVersion7(), accountId, new MailFolderData("INBOX", "Inbox", MailFolderKind.Inbox), DateTimeOffset.UtcNow);
            db.Add(folder);
        }

        var received = new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);
        var message = MailMessage.Create(Guid.CreateVersion7(), accountId, folder.Id,
            new MailMessageData(providerId, "<filing-test@example.com>", null, "mario@example.com", "Mario Rossi", ["studio@example.com"], [],
                "Documenti pratica", received, received, false, 512, true, ["fattura.pdf"]),
            DateTimeOffset.UtcNow);
        db.Add(message);
        await db.SaveChangesAsync(Ct);
        return message.Id;
    }

    private async Task<FileMailResponse> FileAsync(string token, Guid messageId, FileMailRequest request)
    {
        using var response = await SendAsync(HttpMethod.Post, $"/api/v1/me/connected-accounts/mail-messages/{messageId}/file", token, request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<FileMailResponse>(Ct))!;
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errorCode").GetString();

    private async Task<T> GetAsync<T>(string token, string path)
    {
        using var response = await SendAsync(HttpMethod.Get, path, token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<T>(Ct))!;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string token, object? body = null)
    {
        using var http = factory.CreateClient();
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType());
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await http.SendAsync(request, Ct);
    }

    public sealed class Factory : AuthEndpointsTests.Factory
    {
        private readonly string storage = Path.Combine(Path.GetTempPath(), "auxilia-mail-filing-" + Guid.NewGuid().ToString("N"));

        public FakeGmail Mail { get; } = new();

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            if (Directory.Exists(storage))
            {
                Directory.Delete(storage, recursive: true);
            }
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Storage:Local:RootPath", storage);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IOAuthProvider>();
                services.AddSingleton<IOAuthProvider, RefreshingGoogle>();
                services.RemoveAll<IMailProvider>();
                services.AddSingleton<IMailProvider>(Mail);
            });
        }
    }

    /// <summary>Gmail serving the originals put in <see cref="Raw"/> (the sync is not used here).</summary>
    public sealed class FakeGmail : IMailProvider
    {
        public Dictionary<string, byte[]> Raw { get; } = new(StringComparer.Ordinal);

        public ConnectedAccountProvider Provider => ConnectedAccountProvider.Google;

        public Task<IReadOnlyList<MailFolderData>> FoldersAsync(MailboxAccess access, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MailFolderData>>([new("INBOX", "Inbox", MailFolderKind.Inbox)]);

        public Task<MailPage> ReadAsync(MailPageRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new MailPage([], [], null, null, "h-1", 0));

        public Task<Stream?> OpenRawAsync(MailboxAccess access, MailFolderData folder, string providerMessageId, CancellationToken cancellationToken)
        {
            access.AccessToken.ShouldBe("access");
            return Task.FromResult<Stream?>(Raw.TryGetValue(providerMessageId, out var bytes) ? new MemoryStream(bytes) : null);
        }
    }

    private sealed class RefreshingGoogle : IOAuthProvider
    {
        public ConnectedAccountProvider Provider => ConnectedAccountProvider.Google;

        public bool IsConfigured => true;

        public bool SupportsCalendar => true;

        public Uri AuthorizationUrl(OAuthAuthorization authorization) => new($"https://accounts.example/consent?state={authorization.State}");

        public Task<Result<OAuthTokens>> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(new OAuthTokens(
                "access", "refresh", DateTimeOffset.UtcNow.AddHours(1), "openid gmail", true, false, $"{Guid.NewGuid():N}@gmail.com", null)));

        public Task<Result<OAuthTokens>> RefreshAsync(string refreshToken, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(new OAuthTokens("access", null, DateTimeOffset.UtcNow.AddHours(1), null, true, false, null, null)));

        public Task<OAuthRevocation> RevokeAsync(string refreshToken, CancellationToken cancellationToken) => Task.FromResult(OAuthRevocation.Revoked);
    }
}
