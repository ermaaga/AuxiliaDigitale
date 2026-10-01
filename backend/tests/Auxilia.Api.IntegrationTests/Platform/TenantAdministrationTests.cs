using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Auxilia.Api.IntegrationTests.Host;
using Auxilia.Contracts.Platform;
using Auxilia.Diagnostics;

namespace Auxilia.Api.IntegrationTests.Platform;

/// <summary>
/// S-01 over HTTP (N02): tenant creation with queued provisioning, edits, status (archive only, D-25), plan and module
/// overrides per role, and the first Administrator through the tenant's technical endpoints. Each test creates its own
/// tenants, so the shared ones keep their state.
/// </summary>
public sealed partial class PlatformIdentityTests
{
    private static string NewSlug() => "s01-" + Guid.NewGuid().ToString("N")[..10];

    [Fact]
    public async Task CreateTenant_AcceptsAndQueuesProvisioning_AndTheTenantIsReadableAtOnce()
    {
        var console = await ConsoleTokenAsync();
        var slug = NewSlug();

        using var created = await SendJsonAsync(HttpMethod.Post, "/api/v1/platform/tenants", console,
            new CreatePlatformTenantRequest(slug, "Studio Rossi", "it", "Europe/Rome", new TenantAdministratorInvite("anna@rossi.test", "Anna", "Rossi")));

        created.StatusCode.ShouldBe(HttpStatusCode.Accepted, await created.Content.ReadAsStringAsync(Ct));
        created.Headers.Location!.ToString().ShouldBe($"/api/v1/platform/tenants/{slug}");
        var detail = (await created.Content.ReadFromJsonAsync<PlatformTenantDetailResponse>(Ct))!;
        // Without a message bus the provisioning waits for a retry: the tenant stays in Provisioning with the default plan.
        (detail.Status, detail.DisplayName, detail.Plan!.Code).ShouldBe(("Provisioning", "Studio Rossi", "standard"));

        using var list = await SendAsync(HttpMethod.Get, "/api/v1/platform/tenants", console);
        (await list.Content.ReadFromJsonAsync<PlatformTenantResponse[]>(Ct))!.ShouldContain(tenant => tenant.Slug == slug && tenant.PlanCode == "standard");

        using var retry = await SendAsync(HttpMethod.Post, $"/api/v1/platform/tenants/{slug}/provisioning", console);
        retry.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        using var retryActive = await SendAsync(HttpMethod.Post, $"/api/v1/platform/tenants/{ApiDatabase.TenantA}/provisioning", console);
        await ShouldHaveCodeAsync(retryActive, HttpStatusCode.Conflict, EventCodes.Tenancy.TenantNotProvisioning);

        using var duplicate = await SendJsonAsync(HttpMethod.Post, "/api/v1/platform/tenants", console,
            new CreatePlatformTenantRequest(slug, "Again", "it", "Europe/Rome", null));
        await ShouldHaveCodeAsync(duplicate, HttpStatusCode.Conflict, EventCodes.Tenancy.TenantAlreadyExists);
    }

    [Fact]
    public async Task CreateTenant_InvalidValues_AreFieldErrors()
    {
        var console = await ConsoleTokenAsync();

        using var invalid = await SendJsonAsync(HttpMethod.Post, "/api/v1/platform/tenants", console,
            new CreatePlatformTenantRequest(NewSlug(), "", "de", "Nowhere/City", new TenantAdministratorInvite("nope", "Anna", "")));
        using var reserved = await SendJsonAsync(HttpMethod.Post, "/api/v1/platform/tenants", console,
            new CreatePlatformTenantRequest("platform", "Platform", "it", "Europe/Rome", null));

        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var errors = (await invalid.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors");
        errors.EnumerateObject().Select(field => field.Name)
            .ShouldBe(["displayName", "defaultLanguage", "timeZone", "administrator.email", "administrator.lastName"], ignoreOrder: true);
        await ShouldHaveCodeAsync(reserved, HttpStatusCode.BadRequest, EventCodes.Tenancy.TenantSlugReserved);
    }

    [Fact]
    public async Task Tenant_IsEditedSuspendedOnlyWhenAllowed_AndArchivedReadOnly()
    {
        var console = await ConsoleTokenAsync();
        var slug = await CreateTenantAsync(console);

        using var updated = await SendJsonAsync(HttpMethod.Put, $"/api/v1/platform/tenants/{slug}", console,
            new UpdatePlatformTenantRequest("Studio Bianchi", "Europe/London"));
        updated.StatusCode.ShouldBe(HttpStatusCode.OK);
        var detail = (await updated.Content.ReadFromJsonAsync<PlatformTenantDetailResponse>(Ct))!;
        (detail.DisplayName, detail.TimeZone).ShouldBe(("Studio Bianchi", "Europe/London"));

        // A tenant being provisioned cannot be suspended; it can be archived.
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Post, $"/api/v1/platform/tenants/{slug}/suspend", console),
            HttpStatusCode.Conflict, EventCodes.Tenancy.TenantTransitionNotAllowed);
        using var archived = await SendAsync(HttpMethod.Post, $"/api/v1/platform/tenants/{slug}/archive", console);
        archived.StatusCode.ShouldBe(HttpStatusCode.OK);
        var archivedDetail = (await archived.Content.ReadFromJsonAsync<PlatformTenantDetailResponse>(Ct))!;
        (archivedDetail.Status, archivedDetail.ArchivedAt is not null).ShouldBe(("Archived", true));

        await ShouldHaveCodeAsync(
            await SendJsonAsync(HttpMethod.Put, $"/api/v1/platform/tenants/{slug}", console, new UpdatePlatformTenantRequest("Other", "Europe/Rome")),
            HttpStatusCode.Conflict, EventCodes.Tenancy.TenantArchived);
        (await SendAsync(HttpMethod.Get, $"/api/v1/platform/tenants/{slug}", console)).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var visible = await SendAsync(HttpMethod.Get, "/api/v1/platform/tenants", console);
        using var all = await SendAsync(HttpMethod.Get, "/api/v1/platform/tenants?includeArchived=true", console);
        (await visible.Content.ReadFromJsonAsync<PlatformTenantResponse[]>(Ct))!.ShouldNotContain(tenant => tenant.Slug == slug);
        (await all.Content.ReadFromJsonAsync<PlatformTenantResponse[]>(Ct))!.ShouldContain(tenant => tenant.Slug == slug && tenant.Status == "Archived");
        (await SendAsync(HttpMethod.Get, "/api/v1/platform/tenants/no-such-tenant", console)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PlanAndModuleOverrides_ChangeTheEffectiveRolesOfTheTenant()
    {
        var console = await ConsoleTokenAsync();
        var slug = await CreateTenantAsync(console);

        using var plans = await SendAsync(HttpMethod.Get, "/api/v1/platform/plans", console);
        var standard = (await plans.Content.ReadFromJsonAsync<PlanResponse[]>(Ct))!.Single(plan => plan.Code == "standard");
        standard.Modules.Single(module => module.Code == "cases").Roles.ShouldBe(["Administrator", "Employee"]);

        using var modules = await SendAsync(HttpMethod.Get, $"/api/v1/platform/tenants/{slug}/modules", console);
        var before = (await modules.Content.ReadFromJsonAsync<TenantModuleResponse[]>(Ct))!;
        before.Single(module => module.Code == "identity").Kind.ShouldBe("Core");
        before.Single(module => module.Code == "cases").EffectiveRoles.ShouldBe(["Administrator", "Employee"]);

        using var overridden = await SendJsonAsync(HttpMethod.Put, $"/api/v1/platform/tenants/{slug}/modules/cases", console,
            new SetModuleOverrideRequest(true, ["Administrator", "Client"]));
        overridden.StatusCode.ShouldBe(HttpStatusCode.OK);
        var cases = (await overridden.Content.ReadFromJsonAsync<TenantModuleResponse[]>(Ct))!.Single(module => module.Code == "cases");
        cases.Override!.IsEnabled.ShouldBeTrue();
        cases.EffectiveRoles.ShouldBe(["Administrator", "Client"]);

        using var removed = await SendAsync(HttpMethod.Delete, $"/api/v1/platform/tenants/{slug}/modules/cases", console);
        var afterRemoval = (await removed.Content.ReadFromJsonAsync<TenantModuleResponse[]>(Ct))!.Single(module => module.Code == "cases");
        afterRemoval.Override.ShouldBeNull();
        afterRemoval.EffectiveRoles.ShouldBe(["Administrator", "Employee"]);

        await ShouldHaveCodeAsync(
            await SendJsonAsync(HttpMethod.Put, $"/api/v1/platform/tenants/{slug}/modules/identity", console, new SetModuleOverrideRequest(false, [])),
            HttpStatusCode.BadRequest, EventCodes.Tenancy.CoreModuleNotConfigurable);
        await ShouldHaveCodeAsync(
            await SendJsonAsync(HttpMethod.Put, $"/api/v1/platform/tenants/{slug}/modules/unknown", console, new SetModuleOverrideRequest(false, [])),
            HttpStatusCode.NotFound, EventCodes.Tenancy.ModuleNotFound);
        await ShouldHaveCodeAsync(
            await SendJsonAsync(HttpMethod.Put, $"/api/v1/platform/tenants/{slug}/plan", console, new ChangeTenantPlanRequest("gold")),
            HttpStatusCode.NotFound, EventCodes.Tenancy.PlanNotFound);
        using var samePlan = await SendJsonAsync(HttpMethod.Put, $"/api/v1/platform/tenants/{slug}/plan", console, new ChangeTenantPlanRequest("standard"));
        samePlan.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task FirstAdministrator_IsCreatedOnce_AndTheInvitationStaysPendingWithoutASendingAccount()
    {
        var console = await ConsoleTokenAsync();
        using var opened = await SendAsync(HttpMethod.Post, $"/api/v1/platform/tenants/{ApiDatabase.TenantC}/token", console);
        var tenantToken = (await opened.Content.ReadFromJsonAsync<PlatformTenantTokenResponse>(Ct))!.AccessToken;

        using var invalid = await SendJsonAsync(HttpMethod.Post, "/api/v1/administrators", tenantToken, new CreateTenantAdministratorRequest("nope", "", "Verdi"));
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var created = await SendJsonAsync(HttpMethod.Post, "/api/v1/administrators", tenantToken, new CreateTenantAdministratorRequest("luca@verdi.test", "Luca", "Verdi"));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        var invitation = (await created.Content.ReadFromJsonAsync<TenantAdministratorInvitationResponse>(Ct))!;
        (invitation.InvitationSent, invitation.InvitationErrorCode).ShouldBe((false, $"AUX-{EventCodes.Messaging.NoAccountForMessage}"));

        using var list = await SendAsync(HttpMethod.Get, "/api/v1/administrators", tenantToken);
        (await list.Content.ReadFromJsonAsync<TenantAdministratorResponse[]>(Ct))!.ShouldHaveSingleItem()
            .ShouldBe(new TenantAdministratorResponse(invitation.UserId, "luca@verdi.test", "luca@verdi.test", true, false));

        await ShouldHaveCodeAsync(
            await SendJsonAsync(HttpMethod.Post, "/api/v1/administrators", tenantToken, new CreateTenantAdministratorRequest("second@verdi.test", "S", "V")),
            HttpStatusCode.Conflict, EventCodes.Identity.AdministratorAlreadyExists);
        using var again = await SendAsync(HttpMethod.Post, $"/api/v1/administrators/{invitation.UserId}/invitation", tenantToken);
        (await again.Content.ReadFromJsonAsync<TenantAdministratorInvitationResponse>(Ct))!.InvitationSent.ShouldBeFalse();
        (await SendAsync(HttpMethod.Post, $"/api/v1/administrators/{Guid.NewGuid()}/invitation", tenantToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // D-21 perimeter: the console token and tenant user tokens cannot reach these technical endpoints, and the
        // tenant-scoped platform token cannot administer tenants.
        await ShouldBeForbiddenAsync(await SendAsync(HttpMethod.Get, "/api/v1/administrators", console));
        await ShouldBeForbiddenAsync(await SendJsonAsync(HttpMethod.Post, "/api/v1/platform/tenants", tenantToken,
            new CreatePlatformTenantRequest(NewSlug(), "X", "it", "Europe/Rome", null)));
    }

    private async Task<string> ConsoleTokenAsync()
    {
        var (email, secret) = await ActivatedUserAsync();
        return (await SignInAsync(email, secret)).AccessToken;
    }

    private async Task<string> CreateTenantAsync(string console)
    {
        var slug = NewSlug();
        using var created = await SendJsonAsync(HttpMethod.Post, "/api/v1/platform/tenants", console,
            new CreatePlatformTenantRequest(slug, "Studio", "it", "Europe/Rome", null));
        created.StatusCode.ShouldBe(HttpStatusCode.Accepted, await created.Content.ReadAsStringAsync(Ct));
        return slug;
    }

    private async Task<HttpResponseMessage> SendJsonAsync<T>(HttpMethod method, string path, string token, T body)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request, Ct);
    }

    private static async Task ShouldHaveCodeAsync(HttpResponseMessage response, HttpStatusCode status, int code)
    {
        using (response)
        {
            response.StatusCode.ShouldBe(status, await response.Content.ReadAsStringAsync(Ct));
            (await ErrorCodeAsync(response)).ShouldBe($"AUX-{code}");
        }
    }
}
