using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Auxilia.Api.IntegrationTests.Host;
using Auxilia.Api.IntegrationTests.Platform;
using Auxilia.Contracts.Common;
using Auxilia.Application.Messaging.Public;
using Auxilia.Contracts.Messaging;
using Auxilia.Domain.Messaging;
using Auxilia.Persistence.Tenant;
using Auxilia.Tests.Common;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Auxilia.Api.IntegrationTests.Messaging;

/// <summary>
/// S-03 over HTTP: sending accounts (secrets in, never out), default and activation, sender rules, test send with its
/// outcome in the outbound log; only the System with a tenant-scoped platform token (D-21).
/// </summary>
public sealed class MessagingEndpointsTests : IClassFixture<PlatformIdentityTests.Factory>, IAsyncLifetime
{
    private const string SmtpPassword = FakeSmtpServer.Password;

    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static bool seeded;

    private readonly PlatformIdentityTests.Factory factory;

    public MessagingEndpointsTests(PlatformIdentityTests.Factory factory) => this.factory = factory;

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
                await using var db = new TenantDbContext(TenantDbContextOptions.Create(dataSource));
                // The system template of the test message (the data-migration that ships it is internal to persistence).
                foreach (var language in new[] { "it", "en" })
                {
                    if (!await db.Set<MessageTemplate>().AnyAsync(template => template.Code == MessageTemplates.AccountTest && template.Language == language))
                    {
                        db.Set<MessageTemplate>().Add(new MessageTemplate(
                            Guid.CreateVersion7(), MessageChannel.Email, MessageTemplates.AccountTest, language, "Test {{ accountName }}", "<p>{{ tenantName }}</p>", isSystem: true));
                    }
                }

                await db.SaveChangesAsync();
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
    public async Task Accounts_RulesAndTestSend_EndToEnd()
    {
        var token = await PlatformTenantTokens.IssueAsync(factory, ApiDatabase.TenantA, Ct);
        await using var smtp = new FakeSmtpServer();
        var name = "Office " + Guid.NewGuid().ToString("N")[..6];

        using var invalid = await SendAsync(HttpMethod.Post, "/api/v1/messaging/accounts", token,
            new CreateMessagingAccountRequest("Email", "smtp", name, Json("""{"host":"","port":0}"""), null));
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var badChannel = await SendAsync(HttpMethod.Post, "/api/v1/messaging/accounts", token,
            new CreateMessagingAccountRequest("Fax", "smtp", name, Json("{}"), null));
        badChannel.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var created = await SendAsync(HttpMethod.Post, "/api/v1/messaging/accounts", token,
            new CreateMessagingAccountRequest("Email", "smtp", name, SmtpSettings(smtp.Port), SmtpPassword));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        var id = (await created.Content.ReadFromJsonAsync<CreateMessagingAccountResponse>(Ct))!.Id;

        using var list = await SendAsync(HttpMethod.Get, "/api/v1/messaging/accounts", token);
        var raw = await list.Content.ReadAsStringAsync(Ct);
        raw.ShouldNotContain(SmtpPassword);
        var account = JsonSerializer.Deserialize<MessagingAccountResponse[]>(raw, JsonSerializerOptions.Web)!.Single(item => item.Id == id);
        (account.Channel, account.HasSecret, account.IsActive, account.IsAvailable).ShouldBe(("Email", true, true, true));
        account.Settings.GetProperty("host").GetString().ShouldBe("127.0.0.1");

        // A second account, made default: the first one can then be deactivated.
        using var second = await SendAsync(HttpMethod.Post, "/api/v1/messaging/accounts", token,
            new CreateMessagingAccountRequest("Email", "smtp", name + " bis", SmtpSettings(1), null));
        var secondId = (await second.Content.ReadFromJsonAsync<CreateMessagingAccountResponse>(Ct))!.Id;
        (await SendAsync(HttpMethod.Post, $"/api/v1/messaging/accounts/{secondId}/default", token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var keepDefault = await SendAsync(HttpMethod.Put, $"/api/v1/messaging/accounts/{secondId}/active", token, new SetMessagingAccountActiveRequest(false));
        keepDefault.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(keepDefault)).ShouldBe("AUX-25017");

        using var rules = await SendAsync(HttpMethod.Put, "/api/v1/messaging/rules/Email", token,
            new SetSenderRulesRequest([new SenderRuleRequest("Marketing", null, id, 1), new SenderRuleRequest("Notification", "Employee", secondId, 2)]));
        rules.StatusCode.ShouldBe(HttpStatusCode.OK, await rules.Content.ReadAsStringAsync(Ct));
        (await rules.Content.ReadFromJsonAsync<SenderRuleResponse[]>(Ct))!.Select(rule => (rule.Purpose, rule.Role)).ShouldBe([("Notification", "Employee"), ("Marketing", null)]);
        using var badRule = await SendAsync(HttpMethod.Put, "/api/v1/messaging/rules/Email", token,
            new SetSenderRulesRequest([new SenderRuleRequest("Spam", null, id, 1)]));
        badRule.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(badRule)).ShouldBe("AUX-25020");

        // Test send: delivered by the first account, refused by the second (nothing listens on port 1).
        using var sent = await SendAsync(HttpMethod.Post, $"/api/v1/messaging/accounts/{id}/test", token, new SendTestMessageRequest("anna@example.test", "it"));
        sent.StatusCode.ShouldBe(HttpStatusCode.OK, await sent.Content.ReadAsStringAsync(Ct));
        (await sent.Content.ReadFromJsonAsync<SendTestMessageResponse>(Ct))!.Sent.ShouldBeTrue();
        smtp.Messages.ShouldHaveSingleItem().Recipients.ShouldBe(["anna@example.test"]);

        using var failed = await SendAsync(HttpMethod.Post, $"/api/v1/messaging/accounts/{secondId}/test", token, new SendTestMessageRequest("anna@example.test", "en"));
        var outcome = (await failed.Content.ReadFromJsonAsync<SendTestMessageResponse>(Ct))!;
        (outcome.Sent, outcome.ErrorCode).ShouldBe((false, "AUX-25022"));
        using var badRecipient = await SendAsync(HttpMethod.Post, $"/api/v1/messaging/accounts/{id}/test", token, new SendTestMessageRequest("nobody", "it"));
        badRecipient.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var log = await SendAsync(HttpMethod.Get, $"/api/v1/messaging/outbound-messages?filter[accountId]={secondId}&filter[status]=Failed", token);
        var page = (await log.Content.ReadFromJsonAsync<PagedResponse<OutboundMessageResponse>>(Ct))!;
        page.Items.ShouldHaveSingleItem().ErrorCode.ShouldBe("AUX-25022");
        using var badLog = await SendAsync(HttpMethod.Get, "/api/v1/messaging/outbound-messages?filter[status]=Lost", token);
        badLog.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // An update without a secret keeps the stored one.
        (await SendAsync(HttpMethod.Put, $"/api/v1/messaging/accounts/{id}", token,
            new UpdateMessagingAccountRequest(name + " (renamed)", SmtpSettings(smtp.Port), null))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var again = await SendAsync(HttpMethod.Post, $"/api/v1/messaging/accounts/{id}/test", token, new SendTestMessageRequest("anna@example.test", "it"));
        (await again.Content.ReadFromJsonAsync<SendTestMessageResponse>(Ct))!.Sent.ShouldBeTrue();
    }

    [Fact]
    public async Task Messaging_IsForTheSystemOnly()
    {
        using var client = factory.CreateClient();
        using var anonymous = new HttpRequestMessage(HttpMethod.Get, "/api/v1/messaging/accounts");
        anonymous.Headers.Add("X-Tenant", ApiDatabase.TenantA);
        (await client.SendAsync(anonymous, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/messaging/accounts");
        request.Headers.Add(ApiFactory.TestTenantClaimHeader, ApiDatabase.TenantA);
        request.Headers.Add(ApiFactory.TestRolesHeader, "Administrator");
        using var administrator = await client.SendAsync(request, Ct);
        administrator.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(administrator)).ShouldBe("AUX-12040");
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static JsonElement SmtpSettings(int port) =>
        Json($$"""{"host":"127.0.0.1","port":{{port}},"security":"None","username":"office","fromAddress":"office@example.test","fromName":"Office"}""");

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
