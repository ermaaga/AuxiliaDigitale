using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Auxilia.Api.IntegrationTests.Identity;
using Auxilia.Application.Directory;
using Auxilia.Contracts.Common;
using Auxilia.Contracts.Directory;
using Auxilia.Diagnostics;
using Auxilia.Domain.Platform;
using Auxilia.Persistence.Catalog;
using Auxilia.SharedKernel.Tenancy;

using Ixnas.AltchaNet;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Api.IntegrationTests.Registrations;

/// <summary>
/// B-06 over HTTP (F02/F03, API only D-14): an external client application sends registrations (ALTCHA for a public
/// client, none for a confidential one), duplicates are refused, and staff list, approve (the client is created) and
/// reject them, with the permissions of each role.
/// </summary>
public sealed class RegistrationEndpointsTests(RegistrationEndpointsTests.Factory factory) : IClassFixture<RegistrationEndpointsTests.Factory>
{
    private const string Password = "A long Passw0rd for tests!";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Closed_ByDefault_TheCaptchaAndTheSubmissionAreForbidden()
    {
        await factory.SetTenantSettingAsync(DirectorySettings.RegistrationEnabled.Key, false);

        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Get, "/api/v1/registrations/captcha", Factory.PublicClient), HttpStatusCode.Forbidden,
            EventCodes.Directory.RegistrationDisabled);
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Post, "/api/v1/registrations", Factory.PublicClient, NewRequest(NewEmail(), "x")),
            HttpStatusCode.Forbidden, EventCodes.Directory.RegistrationDisabled);
    }

    [Fact]
    public async Task PublicClient_SolvesTheAltcha_OncePerRequest()
    {
        await factory.SetTenantSettingAsync(DirectorySettings.RegistrationEnabled.Key, true);
        var email = NewEmail();

        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Post, "/api/v1/registrations", Factory.PublicClient, NewRequest(email, null)),
            HttpStatusCode.BadRequest, EventCodes.Directory.RegistrationCaptchaInvalid);

        var solution = await SolveAsync();
        using (var submitted = await SendAsync(HttpMethod.Post, "/api/v1/registrations", Factory.PublicClient, NewRequest(email, solution)))
        {
            submitted.StatusCode.ShouldBe(HttpStatusCode.Accepted, await submitted.Content.ReadAsStringAsync(Ct));
            (await submitted.Content.ReadFromJsonAsync<RegistrationSubmittedResponse>(Ct))!.Id.ShouldNotBe(Guid.Empty);
        }

        // A solution is accepted once; a new one meets the duplicate check (Q05: one pending request per e-mail).
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Post, "/api/v1/registrations", Factory.PublicClient, NewRequest(NewEmail(), solution)),
            HttpStatusCode.BadRequest, EventCodes.Directory.RegistrationCaptchaInvalid);
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Post, "/api/v1/registrations", Factory.PublicClient, NewRequest(email.ToUpperInvariant(), await SolveAsync())),
            HttpStatusCode.Conflict, EventCodes.Directory.RegistrationPending);
    }

    [Fact]
    public async Task ConfidentialClient_NeedsNoCaptcha_ButItsSecret()
    {
        await factory.SetTenantSettingAsync(DirectorySettings.RegistrationEnabled.Key, true);

        using (var captcha = await SendAsync(HttpMethod.Get, "/api/v1/registrations/captcha", Factory.Confidential(factory)))
        {
            var challenge = (await captcha.Content.ReadFromJsonAsync<CaptchaChallengeResponse>(Ct))!;
            (challenge.Provider, challenge.Altcha).ShouldBe(("none", null));
        }

        (await SendAsync(HttpMethod.Post, "/api/v1/registrations", Factory.Confidential(factory), NewRequest(NewEmail(), null))).StatusCode
            .ShouldBe(HttpStatusCode.Accepted);
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Post, "/api/v1/registrations", (AuthEndpointsTests.ClientId, "wrong"), NewRequest(NewEmail(), null)),
            HttpStatusCode.Unauthorized, EventCodes.Identity.ClientInvalid);
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Post, "/api/v1/registrations", ("nobody", null), NewRequest(NewEmail(), null)),
            HttpStatusCode.Unauthorized, EventCodes.Identity.ClientInvalid);
    }

    [Fact]
    public async Task InvalidRequests_AndRegisteredE_mails_AreRefused()
    {
        await factory.SetTenantSettingAsync(DirectorySettings.RegistrationEnabled.Key, true);
        var client = Factory.Confidential(factory);

        using var invalid = await SendAsync(HttpMethod.Post, "/api/v1/registrations", client,
            new SubmitRegistrationRequest("", "Rossi", "nope", "12", DateOnly.FromDateTime(DateTime.UtcNow), "XYZ", false, "", null, null));
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await invalid.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("errorCode").GetString().ShouldBe($"AUX-{EventCodes.Directory.RegistrationInvalid}");
        problem.GetProperty("errors").EnumerateObject().Select(field => field.Name)
            .ShouldBe(["firstName", "email", "phone", "fiscalCode", "birthDate", "privacyConsent", "privacyVersion"], ignoreOrder: true);

        var (_, userName) = await factory.AddUserAsync([TenantRole.Client], null);
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Post, "/api/v1/registrations", client, NewRequest(userName + "@EXAMPLE.test", null)),
            HttpStatusCode.Conflict, EventCodes.Directory.RegistrationEmailRegistered);
    }

    [Fact]
    public async Task Staff_ListApproveAndReject_ClientsCannot()
    {
        await factory.SetTenantSettingAsync(DirectorySettings.RegistrationEnabled.Key, true);
        var (employeeId, employeeName) = await factory.AddUserAsync([TenantRole.Employee], Password);
        var employee = await factory.SignInAsync(employeeName, Password, Ct);
        var (_, clientName) = await factory.AddUserAsync([TenantRole.Client], Password);
        var client = await factory.SignInAsync(clientName, Password, Ct);
        var approvedEmail = NewEmail();
        var approvedId = await SubmitAsync(approvedEmail);
        var rejectedId = await SubmitAsync(NewEmail());

        var pending = await ListAsync(employee, $"filter[status]=Pending&search={Uri.EscapeDataString(approvedEmail)}");
        var item = pending.Items.ShouldHaveSingleItem();
        (item.Id, item.Status, item.ClientApplication, item.PrivacyVersion).ShouldBe((approvedId, "Pending", AuthEndpointsTests.ClientId, "2026-01"));

        using var approved = await SendAsync(HttpMethod.Post, $"/api/v1/registrations/{approvedId}/approve", employee, new ProcessRegistrationRequest("welcome"));
        approved.StatusCode.ShouldBe(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync(Ct));
        var created = (await approved.Content.ReadFromJsonAsync<ApproveRegistrationResponse>(Ct))!;

        using (var detail = await SendAsync(HttpMethod.Get, $"/api/v1/clients/{created.ClientId}", employee))
        {
            detail.StatusCode.ShouldBe(HttpStatusCode.OK, await detail.Content.ReadAsStringAsync(Ct));
            var body = (await detail.Content.ReadFromJsonAsync<ClientDetailResponse>(Ct))!;
            (body.Account!.UserName, body.Account.IsActivated, body.Status).ShouldBe((approvedEmail, false, "Inactive"));
            (body.Account.CanSignIn, created.InvitationSent).ShouldBe((body.Employee is not null, body.Employee is not null));
        }

        using (var processed = await SendAsync(HttpMethod.Get, $"/api/v1/registrations/{approvedId}", employee))
        {
            var body = (await processed.Content.ReadFromJsonAsync<RegistrationResponse>(Ct))!;
            (body.Status, body.ClientId, body.ProcessedBy!.UserId, body.Notes).ShouldBe(("Approved", created.ClientId, employeeId, "welcome"));
        }

        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Post, $"/api/v1/registrations/{approvedId}/approve", employee, new ProcessRegistrationRequest(null)),
            HttpStatusCode.Conflict, EventCodes.Directory.RegistrationProcessed);

        using (var rejected = await SendAsync(HttpMethod.Post, $"/api/v1/registrations/{rejectedId}/reject", employee, null))
        {
            rejected.StatusCode.ShouldBe(HttpStatusCode.OK, await rejected.Content.ReadAsStringAsync(Ct));
            (await rejected.Content.ReadFromJsonAsync<RegistrationResponse>(Ct))!.Status.ShouldBe("Rejected");
        }

        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Post, $"/api/v1/registrations/{Guid.NewGuid()}/reject", employee, null),
            HttpStatusCode.NotFound, EventCodes.Directory.RegistrationNotFound);
        (await ListAsync(employee, "filter[status]=Rejected&pageSize=100")).Items.ShouldContain(row => row.Id == rejectedId);

        // A client has neither the module nor the permission.
        using var forbidden = await SendAsync(HttpMethod.Get, "/api/v1/registrations", client);
        forbidden.StatusCode.ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
    }

    private static string NewEmail() => $"applicant-{Guid.NewGuid():N}@example.test";

    /// <summary>A valid fiscal code nobody else has.</summary>
    private static string NewFiscalCode()
    {
        static char Letter() => (char)('A' + Random.Shared.Next(26));
        static char Digit() => (char)('0' + Random.Shared.Next(10));
        return new string([Letter(), Letter(), Letter(), Letter(), Letter(), Letter(), Digit(), Digit(), 'A', Digit(), Digit(), Letter(), Digit(), Digit(), Digit(), Letter()]);
    }

    private static SubmitRegistrationRequest NewRequest(string email, string? captcha) =>
        new("Mario", "Rossi", email, "333 1234567", new DateOnly(1980, 1, 1), NewFiscalCode(), true, "2026-01", "en", captcha);

    private async Task<Guid> SubmitAsync(string email)
    {
        using var submitted = await SendAsync(HttpMethod.Post, "/api/v1/registrations", Factory.Confidential(factory), NewRequest(email, null));
        submitted.StatusCode.ShouldBe(HttpStatusCode.Accepted, await submitted.Content.ReadAsStringAsync(Ct));
        return (await submitted.Content.ReadFromJsonAsync<RegistrationSubmittedResponse>(Ct))!.Id;
    }

    /// <summary>What the external client application does: fetch the challenge and solve it (proof of work).</summary>
    private async Task<string> SolveAsync()
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/v1/registrations/captcha", Factory.PublicClient);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var challenge = (await response.Content.ReadFromJsonAsync<CaptchaChallengeResponse>(Ct))!;
        challenge.Provider.ShouldBe("altcha");
        var solved = Altcha.CreateSolver().Solve(new AltchaChallenge
        {
            Algorithm = challenge.Altcha!.Algorithm,
            Challenge = challenge.Altcha.Challenge,
            Salt = challenge.Altcha.Salt,
            Signature = challenge.Altcha.Signature,
            Maxnumber = (int)challenge.Altcha.Maxnumber,
        });
        solved.Success.ShouldBeTrue();
        return solved.Altcha;
    }

    private async Task<PagedResponse<RegistrationResponse>> ListAsync(string token, string query)
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/v1/registrations?" + query, token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<PagedResponse<RegistrationResponse>>(Ct))!;
    }

    /// <summary>As an external client application (anonymous, tenant from <c>X-Tenant</c>).</summary>
    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, (string Id, string? Secret) client, object? body = null) =>
        SendAsync(method, path, request =>
        {
            request.Headers.Add("X-Tenant", ApiDatabase.TenantA);
            request.Headers.Add("X-Client-Id", client.Id);
            if (client.Secret is not null)
            {
                request.Headers.Add("X-Client-Secret", client.Secret);
            }
        }, body);

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string token, object? body) =>
        SendAsync(method, path, request => request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token), body);

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string token) => SendAsync(method, path, token, null);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, Action<HttpRequestMessage> headers, object? body)
    {
        using var http = factory.CreateClient();
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType());
        }

        headers(request);
        return await http.SendAsync(request, Ct);
    }

    private static async Task ShouldHaveCodeAsync(HttpResponseMessage response, HttpStatusCode status, int code)
    {
        using (response)
        {
            response.StatusCode.ShouldBe(status, await response.Content.ReadAsStringAsync(Ct));
            (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errorCode").GetString().ShouldBe($"AUX-{code}");
        }
    }

    public sealed class Factory : AccountSecurityEndpointsTests.Factory
    {
        /// <summary>A public integration (no secret): ALTCHA.</summary>
        public static (string Id, string? Secret) PublicClient => ("tests-registration-site", null);

        /// <summary>The confidential test client (WebBff): no captcha.</summary>
        public static (string Id, string? Secret) Confidential(Factory factory) => (AuthEndpointsTests.ClientId, factory.ClientSecret);

        public override async ValueTask InitializeAsync()
        {
            await base.InitializeAsync();
            var options = new DbContextOptionsBuilder<CatalogDbContext>();
            CatalogPersistence.Configure(options, ApiDatabase.Instance.CatalogConnectionString);
            await using var catalog = new CatalogDbContext(options.Options);
            if (!await catalog.ClientApplications.AnyAsync(item => item.ClientId == PublicClient.Id))
            {
                catalog.ClientApplications.Add(ClientApplication.Create(Guid.CreateVersion7(), PublicClient.Id, "Registration site", ClientApplicationType.Integration).Value);
                await catalog.SaveChangesAsync();
            }
        }
    }
}
