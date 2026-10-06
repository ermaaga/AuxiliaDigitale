using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Auxilia.Contracts.Platform;

using Npgsql;

namespace Auxilia.Api.IntegrationTests.Platform;

/// <summary>N02 jobs page over HTTP (D-15): registered jobs with their last run, the latest runs and "run now".</summary>
public sealed class JobEndpointsTests(PlatformIdentityTests.Factory factory) : IClassFixture<PlatformIdentityTests.Factory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Jobs_ShowTheirLastRun_AndRunNowQueuesTheJobForTheWorker()
    {
        var token = await PlatformTenantTokens.IssueAsync(factory, ApiDatabase.TenantA, Ct);

        // A run recorded by auxctl (actor System), later than any run of the other suites.
        var runId = Guid.CreateVersion7();
        await ExecuteAsync(
            "INSERT INTO ops.job_runs (id, job_code, status, started_at, finished_at, actor_type, summary) " +
            "VALUES ($1, 'cases.expiry', 'Succeeded', '2099-01-01T08:00:00Z', '2099-01-01T08:00:02Z', 'System', '2 cases expired')",
            runId);

        using var list = await SendAsync(HttpMethod.Get, "/api/v1/jobs", token);
        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        var jobs = (await list.Content.ReadFromJsonAsync<JobResponse[]>(Ct))!;
        jobs.Select(job => job.Code).ShouldBe(["bus.outbox", "cases.expiry"]);
        var expiry = jobs.Single(job => job.Code == "cases.expiry");
        (expiry.SuggestedFrequency, expiry.IsRunning, expiry.LastRun!.Id, expiry.LastRun.ActorType, expiry.LastRun.Summary)
            .ShouldBe(("daily", false, runId, "System", "2 cases expired"));

        using var runs = await SendAsync(HttpMethod.Get, "/api/v1/jobs/runs?take=5", token);
        (await runs.Content.ReadFromJsonAsync<JobRunResponse[]>(Ct))!.First().Id.ShouldBe(runId);

        var queued = await QueuedRunsAsync("cases.expiry");
        using var run = await SendAsync(HttpMethod.Post, "/api/v1/jobs/cases.expiry/run", token);
        run.StatusCode.ShouldBe(HttpStatusCode.Accepted, await run.Content.ReadAsStringAsync(Ct));
        (await QueuedRunsAsync("cases.expiry")).ShouldBe(queued + 1);

        using var unknown = await SendAsync(HttpMethod.Post, "/api/v1/jobs/no.such.job/run", token);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await unknown.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errorCode").GetString().ShouldBe("AUX-26002");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string token)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request, Ct);
    }

    private static NpgsqlDataSource TenantA() =>
        NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(ApiDatabase.Instance.CatalogConnectionString) { Database = "tenant_a" }.ConnectionString);

    private static async Task ExecuteAsync(string sql, Guid id)
    {
        await using var source = TenantA();
        await using var command = source.CreateCommand(sql);
        command.Parameters.AddWithValue(id);
        await command.ExecuteNonQueryAsync(Ct);
    }

    private static async Task<int> QueuedRunsAsync(string jobCode)
    {
        await using var source = TenantA();
        await using var command = source.CreateCommand(
            "SELECT count(*) FROM ops.outbox_messages WHERE message_type LIKE '%RunRecurringJobCommand%' AND body::text LIKE $1");
        command.Parameters.AddWithValue($"%\"{jobCode}\"%");
        return Convert.ToInt32(await command.ExecuteScalarAsync(Ct), CultureInfo.InvariantCulture);
    }
}
