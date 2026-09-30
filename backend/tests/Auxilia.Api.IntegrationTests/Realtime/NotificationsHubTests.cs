using System.Net;
using System.Net.Http.Headers;

using Auxilia.Api.IntegrationTests.Identity;
using Auxilia.Application.Abstractions.Realtime;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Contracts.Realtime;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.Extensions.DependencyInjection;

namespace Auxilia.Api.IntegrationTests.Realtime;

/// <summary>F16/F17 realtime: hub groups per tenant/user/role/session, ForceLogout on session end, tenant isolation.</summary>
public sealed class NotificationsHubTests : IClassFixture<AuthEndpointsTests.Factory>
{
    private const string HubPath = "/hubs/notifications";
    private const string Password = "a long enough password";

    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(500);

    private readonly AuthEndpointsTests.Factory factory;

    public NotificationsHubTests(AuthEndpointsTests.Factory factory) => this.factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Logout_PushesForceLogoutToTheConnectionsOfThatSessionOnly()
    {
        var (_, userName) = await factory.AddUserAsync([TenantRole.Employee], Password);
        var first = await factory.SignInAsync(userName, Password, Ct);
        var second = await factory.SignInAsync(userName, Password, Ct);
        await using var firstTab = await HubTestClient.ConnectAsync(factory.Server, HubPath, first, Ct);
        await using var otherTab = await HubTestClient.ConnectAsync(factory.Server, HubPath, first, Ct);
        await using var otherDevice = await HubTestClient.ConnectAsync(factory.Server, HubPath, second, Ct);

        using var client = factory.CreateClient();
        using var logout = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        logout.Headers.Authorization = new AuthenticationHeaderValue("Bearer", first);
        (await client.SendAsync(logout, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await firstTab.NextInvocationAsync(RealtimeEvents.ForceLogout, Ct)).GetProperty("reason").GetString().ShouldBe("Logout");
        (await otherTab.NextInvocationAsync(RealtimeEvents.ForceLogout, Ct)).GetProperty("reason").GetString().ShouldBe("Logout");
        (await otherDevice.TryNextInvocationAsync(RealtimeEvents.ForceLogout, Quiet, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Pushes_ReachUserAndRoleGroupsOfTheirTenantOnly()
    {
        var (userId, userName) = await factory.AddUserAsync([TenantRole.Employee], Password);
        await using var connection = await HubTestClient.ConnectAsync(factory.Server, HubPath, await factory.SignInAsync(userName, Password, Ct), Ct);

        // Same role and user id in another tenant: nothing arrives.
        await PushAsync(ApiDatabase.TenantB, notifier => notifier.ToRoleAsync(TenantRole.Employee, RealtimeEvents.NotificationReceived, new { title = "b" }, Ct));
        await PushAsync(ApiDatabase.TenantB, notifier => notifier.ToUserAsync(userId, RealtimeEvents.NotificationReceived, new { title = "b" }, Ct));
        (await connection.TryNextInvocationAsync(RealtimeEvents.NotificationReceived, Quiet, Ct)).ShouldBeNull();

        await PushAsync(ApiDatabase.TenantA, notifier => notifier.ToRoleAsync(TenantRole.Administrator, RealtimeEvents.NotificationReceived, new { title = "admins" }, Ct));
        await PushAsync(ApiDatabase.TenantA, notifier => notifier.ToUserAsync(userId, RealtimeEvents.NotificationReceived, new { title = "user" }, Ct));
        await PushAsync(ApiDatabase.TenantA, notifier => notifier.ToRoleAsync(TenantRole.Employee, RealtimeEvents.NotificationReceived, new { title = "employees" }, Ct));
        await PushAsync(ApiDatabase.TenantA, notifier => notifier.ToTenantAsync(RealtimeEvents.NotificationReceived, new { title = "everyone" }, Ct));

        (await connection.NextInvocationAsync(RealtimeEvents.NotificationReceived, Ct)).GetProperty("title").GetString().ShouldBe("user");
        (await connection.NextInvocationAsync(RealtimeEvents.NotificationReceived, Ct)).GetProperty("title").GetString().ShouldBe("employees");
        (await connection.NextInvocationAsync(RealtimeEvents.NotificationReceived, Ct)).GetProperty("title").GetString().ShouldBe("everyone");
    }

    [Fact]
    public async Task Negotiate_WithoutOrWithARevokedToken_Is401()
    {
        var (_, userName) = await factory.AddUserAsync([TenantRole.Client], Password);
        var token = await factory.SignInAsync(userName, Password, Ct);
        using var client = factory.CreateClient();
        using var logout = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        logout.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await client.SendAsync(logout, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var anonymous = await client.PostAsync($"{HubPath}/negotiate?negotiateVersion=1", null, Ct);
        using var revoked = new HttpRequestMessage(HttpMethod.Post, $"{HubPath}/negotiate?negotiateVersion=1");
        revoked.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var revokedResponse = await client.SendAsync(revoked, Ct);

        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        revokedResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task QueryStringToken_IsIgnoredOutsideTheHub()
    {
        var (_, userName) = await factory.AddUserAsync([TenantRole.Client], Password);
        var token = await factory.SignInAsync(userName, Password, Ct);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/v1/me?access_token={Uri.EscapeDataString(token)}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task PushAsync(string tenantSlug, Func<IRealtimeNotifier, Task> push)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var tenant = await scope.ServiceProvider.GetRequiredService<ITenantDirectory>().FindBySlugAsync(tenantSlug, Ct);
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().Set(tenant!);
        await push(scope.ServiceProvider.GetRequiredService<IRealtimeNotifier>());
    }
}
