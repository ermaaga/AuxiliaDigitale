using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;

using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Platform;
using Auxilia.Application.Tests.Identity;
using Auxilia.Contracts.Platform;
using Auxilia.Diagnostics;
using Auxilia.Diagnostics.Logging;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Results;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

namespace Auxilia.Application.Tests.Platform;

public sealed class TenantLogQueryServiceTests
{
    // ManualTimeProvider starts on 2026-09-30.
    private static readonly DateOnly Today = new(2026, 9, 30);

    private readonly ICatalogStore catalog = Substitute.For<ICatalogStore>();
    private readonly ITenantLogLevels levels = Substitute.For<ITenantLogLevels>();
    private readonly FakeLogFiles files = new();
    private readonly ManualTimeProvider clock = new();
    private readonly Tenant tenant = Tenant.Create(Guid.CreateVersion7(), "acme", "Acme", "it", "Europe/Rome").Value;
    private readonly TenantLogQueryService service;

    public TenantLogQueryServiceTests()
    {
        catalog.FindTenantAsync("acme", Arg.Any<CancellationToken>()).Returns(tenant);
        levels.DefaultLevel.Returns("Information");
        service = new TenantLogQueryService(catalog, levels, files, clock, NullLogger<TenantLogQueryService>.Instance);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static TenantLogQuery Query(
        DateOnly? from = null, DateOnly? to = null, string? level = null, string? code = null, string? traceId = null,
        string? userId = null, string? text = null, string? cursor = null, int? pageSize = null) =>
        new(from, to, level, code, traceId, userId, text, cursor, pageSize);

    private static string Line(
        int second, string message, string? level = null, string? code = null, string? traceId = null, string? userId = null,
        string? exception = null, DateOnly? day = null)
    {
        var line = new Dictionary<string, object?>
        {
            ["@t"] = new DateTimeOffset((day ?? Today).ToDateTime(new TimeOnly(10, 0, second)), TimeSpan.Zero),
            ["@m"] = message,
            ["@i"] = "abcd1234",
            ["TenantSlug"] = "acme",
            ["Application"] = "Auxilia.Api",
            ["EventId"] = new { Id = 1, Name = "x" },
        };
        if (level is not null)
        {
            line["@l"] = level;
        }

        if (code is not null)
        {
            line["EventCode"] = code;
        }

        if (traceId is not null)
        {
            line["@tr"] = traceId;
        }

        if (userId is not null)
        {
            line["UserId"] = userId;
        }

        if (exception is not null)
        {
            line["@x"] = exception;
        }

        return JsonSerializer.Serialize(line);
    }

    [Fact]
    public async Task SearchAsync_ReturnsTodaysEventsNewestFirst_WithColumnsAndProperties()
    {
        files.Add(Today, Line(1, "first", code: "AUX-11001", traceId: "trace-1", userId: "u-1"), Line(2, "second", level: "Warning"));

        var page = (await service.SearchAsync("acme", Query(), Ct)).Value;

        page.Items.Select(item => item.Message).ShouldBe(["second", "first"]);
        page.NextCursor.ShouldBeNull();
        var first = page.Items[1];
        (first.Level, first.EventCode, first.TraceId, first.UserId).ShouldBe(("Information", "AUX-11001", "trace-1", "u-1"));
        first.Timestamp.ShouldBe(new DateTimeOffset(2026, 9, 30, 10, 0, 1, TimeSpan.Zero));
        first.Properties.Keys.ShouldBe(["Application"]);
        page.Items[0].Level.ShouldBe("Warning");
    }

    [Fact]
    public async Task SearchAsync_FiltersByMinimumLevelCodeTraceUserAndText()
    {
        files.Add(
            Today,
            Line(1, "debug detail", level: "Debug"),
            Line(2, "saved", code: "AUX-14001", traceId: "AbC", userId: "u-1"),
            Line(3, "failed", level: "Error", exception: "System.IO.IOException: disk full"),
            Line(4, "other user", code: "AUX-14001", userId: "u-2"));

        async Task<string[]> Messages(TenantLogQuery query) =>
            (await service.SearchAsync("acme", query, Ct)).Value.Items.Select(item => item.Message).ToArray();

        (await Messages(Query(level: "warning"))).ShouldBe(["failed"]);
        (await Messages(Query(level: "Debug"))).Length.ShouldBe(4);
        (await Messages(Query(code: "14001"))).ShouldBe(["other user", "saved"]);
        (await Messages(Query(code: "aux-14001", userId: "U-1"))).ShouldBe(["saved"]);
        (await Messages(Query(traceId: "abc"))).ShouldBe(["saved"]);
        (await Messages(Query(text: "DISK FULL"))).ShouldBe(["failed"]);
        (await Messages(Query(text: "detail"))).ShouldBe(["debug detail"]);
    }

    [Fact]
    public async Task SearchAsync_PagesAcrossDaysWithTheCursor()
    {
        var yesterday = Today.AddDays(-1);
        files.Add(yesterday, Line(1, "y1", day: yesterday), Line(2, "y2", day: yesterday));
        files.Add(Today, Line(1, "t1"), Line(2, "t2"), Line(3, "t3"));

        var first = (await service.SearchAsync("acme", Query(from: yesterday, pageSize: 2), Ct)).Value;
        var second = (await service.SearchAsync("acme", Query(from: yesterday, pageSize: 2, cursor: first.NextCursor), Ct)).Value;
        var third = (await service.SearchAsync("acme", Query(from: yesterday, pageSize: 2, cursor: second.NextCursor), Ct)).Value;

        first.Items.Select(item => item.Message).ShouldBe(["t3", "t2"]);
        first.NextCursor.ShouldBe("20260930:1");
        second.Items.Select(item => item.Message).ShouldBe(["t1", "y2"]);
        third.Items.Select(item => item.Message).ShouldBe(["y1"]);
        third.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task SearchAsync_SkipsLinesThatAreNotEvents()
    {
        files.Add(Today, "{\"@t\":\"2026-09-30T10:00:0", "not json", "[1,2]", "{\"@m\":\"no time\"}", Line(1, "ok"));

        var page = (await service.SearchAsync("acme", Query(), Ct)).Value;

        page.Items.Select(item => item.Message).ShouldBe(["ok"]);
    }

    [Theory]
    [InlineData("2026-09-30", "2026-09-29", null, null, null, "from")]
    [InlineData("2026-08-30", "2026-09-30", null, null, null, "from")]
    [InlineData(null, null, "Loud", null, null, "level")]
    [InlineData(null, null, null, "bad", null, "cursor")]
    [InlineData(null, null, null, "20260101:1", null, "cursor")]
    [InlineData(null, null, null, null, 0, "pageSize")]
    [InlineData(null, null, null, null, 201, "pageSize")]
    public async Task SearchAsync_InvalidQuery_ReturnsValidationError(string? from, string? to, string? level, string? cursor, int? pageSize, string field)
    {
        var result = await service.SearchAsync(
            "acme", Query(from: from is null ? null : DateOnly.Parse(from, CultureInfo.InvariantCulture), to: to is null ? null : DateOnly.Parse(to, CultureInfo.InvariantCulture), level: level, cursor: cursor, pageSize: pageSize), Ct);

        result.Error!.Code.ShouldBe(EventCodes.Tenancy.LogQueryInvalid);
        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.Error.ValidationErrors.Keys.ShouldBe([field]);
    }

    [Fact]
    public async Task SearchAsync_ThirtyOneDays_IsAllowed()
    {
        (await service.SearchAsync("acme", Query(from: Today.AddDays(-30)), Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task SearchAsync_UnknownTenant_ReturnsNotFound()
    {
        (await service.SearchAsync("ghost", Query(), Ct)).Error!.Code.ShouldBe(EventCodes.Tenancy.TenantNotFound);
    }

    [Fact]
    public async Task SearchAsync_StorageFails_ReturnsCodedFailure()
    {
        files.Fail = true;

        var result = await service.SearchAsync("acme", Query(), Ct);

        result.Error!.Code.ShouldBe(EventCodes.Tenancy.LogFilesUnavailable);
        result.Error.Type.ShouldBe(ErrorType.Failure);
    }

    [Fact]
    public async Task GetLevelAsync_ShowsTheEndOnlyWhileItRuns()
    {
        var now = clock.GetUtcNow();
        tenant.EnableDebugLogging(now.AddMinutes(30), now);

        (await service.GetLevelAsync("acme", Ct)).Value.ShouldBe(new TenantLogLevelResponse("Information", now.AddMinutes(30)));
        clock.Advance(TimeSpan.FromMinutes(31));
        (await service.GetLevelAsync("acme", Ct)).Value.DebugUntil.ShouldBeNull();
        (await service.GetLevelAsync("ghost", Ct)).Error!.Code.ShouldBe(EventCodes.Tenancy.TenantNotFound);
    }

    private sealed class FakeLogFiles : ILogFileReader
    {
        private readonly Dictionary<DateOnly, string[]> days = [];

        public bool Fail { get; set; }

        public void Add(DateOnly day, params string[] lines) => days[day] = lines;

        public async IAsyncEnumerable<string> ReadTenantDayAsync(string tenantSlug, DateOnly day, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            if (Fail)
            {
                throw new IOException("storage down");
            }

            foreach (var line in tenantSlug == "acme" ? days.GetValueOrDefault(day, []) : [])
            {
                yield return line;
            }
        }
    }
}
