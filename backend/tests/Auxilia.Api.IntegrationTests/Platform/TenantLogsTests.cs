using System.Net;
using System.Net.Http.Json;

using Auxilia.Api.IntegrationTests.Host;
using Auxilia.Contracts.Platform;
using Auxilia.Diagnostics;
using Auxilia.Diagnostics.Logging;

using Microsoft.Extensions.DependencyInjection;

namespace Auxilia.Api.IntegrationTests.Platform;

/// <summary>
/// S-07 over HTTP (F25, D-17, D-28): the console Log page reads the tenant's daily files written by this very host,
/// and the System enables debug logging of one tenant for a while. Console tokens only.
/// </summary>
public sealed partial class PlatformIdentityTests
{
    [Fact]
    public async Task LogLevel_IsEnabledUntilAnInstant_AppliedToThisNode_AndDisabled()
    {
        var console = await ConsoleTokenAsync();
        var slug = await CreateTenantAsync(console);
        var levels = factory.Services.GetRequiredService<ITenantLogLevels>();
        var until = DateTimeOffset.UtcNow.AddHours(1);

        using var initial = await SendAsync(HttpMethod.Get, $"/api/v1/platform/tenants/{slug}/log-level", console);
        (await initial.Content.ReadFromJsonAsync<TenantLogLevelResponse>(Ct)).ShouldBe(new TenantLogLevelResponse("Information", null));

        using var enabled = await SendJsonAsync(HttpMethod.Put, $"/api/v1/platform/tenants/{slug}/log-level", console, new EnableTenantDebugLoggingRequest(until));
        enabled.StatusCode.ShouldBe(HttpStatusCode.OK, await enabled.Content.ReadAsStringAsync(Ct));
        (await enabled.Content.ReadFromJsonAsync<TenantLogLevelResponse>(Ct))!.DebugUntil!.Value.ShouldBe(until, TimeSpan.FromMilliseconds(1));
        factory.Services.GetRequiredService<Auxilia.ServiceDefaults.Logging.TenantLogLevels>().LevelFor(slug).ShouldBe(Serilog.Events.LogEventLevel.Debug);

        await ShouldHaveCodeAsync(
            await SendJsonAsync(HttpMethod.Put, $"/api/v1/platform/tenants/{slug}/log-level", console, new EnableTenantDebugLoggingRequest(DateTimeOffset.UtcNow.AddDays(2))),
            HttpStatusCode.BadRequest, EventCodes.Tenancy.LogLevelUntilInvalid);

        using var disabled = await SendAsync(HttpMethod.Delete, $"/api/v1/platform/tenants/{slug}/log-level", console);
        (await disabled.Content.ReadFromJsonAsync<TenantLogLevelResponse>(Ct))!.DebugUntil.ShouldBeNull();
        factory.Services.GetRequiredService<Auxilia.ServiceDefaults.Logging.TenantLogLevels>().LevelFor(slug).ShouldBe(Serilog.Events.LogEventLevel.Information);
        levels.DefaultLevel.ShouldBe("Information");

        (await SendAsync(HttpMethod.Get, "/api/v1/platform/tenants/no-such-tenant/log-level", console)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Logs_ReturnTheTenantsOwnEvents_FromItsDailyFile()
    {
        var console = await ConsoleTokenAsync();
        var slug = await CreateTenantAsync(console);
        var other = await CreateTenantAsync(console);

        // Each change writes the operation outcome (AUX-11030) and the security event (AUX-29025) to the tenant's file.
        (await SendJsonAsync(HttpMethod.Put, $"/api/v1/platform/tenants/{slug}/log-level", console,
            new EnableTenantDebugLoggingRequest(DateTimeOffset.UtcNow.AddMinutes(30)))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Delete, $"/api/v1/platform/tenants/{slug}/log-level", console)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var security = await WaitForLogsAsync(console, slug, "code=AUX-29025", count: 2);
        security.Items.ShouldAllBe(item => item.EventCode == "AUX-29025" && item.Level == "Warning");
        security.Items[0].Timestamp.ShouldBeGreaterThanOrEqualTo(security.Items[1].Timestamp);
        security.Items[1].Message.ShouldContain(slug);

        var operations = await WaitForLogsAsync(console, slug, "code=11030&text=changelog", count: 2);
        operations.Items.ShouldAllBe(item => item.Operation == "Tenancy.ChangeLogLevel" && item.UserId != null);
        // F25: every line names the client application and the host version.
        operations.Items.ShouldAllBe(item => item.Properties.ContainsKey("ClientId") && item.Properties.ContainsKey("Version") && item.TraceId != null);

        var page = await WaitForLogsAsync(console, slug, "pageSize=1", count: 1);
        page.NextCursor.ShouldNotBeNull();
        var next = await WaitForLogsAsync(console, slug, $"pageSize=1&cursor={page.NextCursor}", count: 1);
        next.Items[0].ShouldNotBe(page.Items[0]);

        // Another tenant's file has nothing of this one.
        using var isolated = await SendAsync(HttpMethod.Get, $"/api/v1/platform/tenants/{other}/logs?code=AUX-29025", console);
        (await isolated.Content.ReadFromJsonAsync<TenantLogPageResponse>(Ct))!.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Logs_InvalidSearch_IsAFieldError_AndOnlyTheConsoleCanRead()
    {
        var console = await ConsoleTokenAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await ShouldHaveCodeAsync(
            await SendAsync(HttpMethod.Get, $"/api/v1/platform/tenants/{ApiDatabase.TenantA}/logs?from={today.AddDays(-40):yyyy-MM-dd}&to={today:yyyy-MM-dd}", console),
            HttpStatusCode.BadRequest, EventCodes.Tenancy.LogQueryInvalid);
        await ShouldHaveCodeAsync(
            await SendAsync(HttpMethod.Get, $"/api/v1/platform/tenants/{ApiDatabase.TenantA}/logs?level=Loud", console),
            HttpStatusCode.BadRequest, EventCodes.Tenancy.LogQueryInvalid);
        (await SendAsync(HttpMethod.Get, "/api/v1/platform/tenants/no-such-tenant/logs", console)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // D-21 perimeter: the tenant-scoped platform token is not a console token.
        var tenantToken = await PlatformTenantTokens.IssueAsync(factory, ApiDatabase.TenantA, Ct);
        await ShouldBeForbiddenAsync(await SendAsync(HttpMethod.Get, $"/api/v1/platform/tenants/{ApiDatabase.TenantA}/logs", tenantToken));
        await ShouldBeForbiddenAsync(await SendAsync(HttpMethod.Delete, $"/api/v1/platform/tenants/{ApiDatabase.TenantA}/log-level", tenantToken));
        using var client = factory.CreateClient();
        (await client.GetAsync($"/api/v1/platform/tenants/{ApiDatabase.TenantA}/logs", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>The file sink writes in batches (100 ms here): waits until the search returns enough events.</summary>
    private async Task<TenantLogPageResponse> WaitForLogsAsync(string console, string slug, string query, int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            using var response = await SendAsync(HttpMethod.Get, $"/api/v1/platform/tenants/{slug}/logs?{query}", console);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
            var page = (await response.Content.ReadFromJsonAsync<TenantLogPageResponse>(Ct))!;
            if (page.Items.Count >= count || DateTime.UtcNow > deadline)
            {
                page.Items.Count.ShouldBeGreaterThanOrEqualTo(count, $"events for {query} in {factory.LogRoot}");
                return page;
            }

            await Task.Delay(100, Ct);
        }
    }
}
