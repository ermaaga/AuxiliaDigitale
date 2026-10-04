using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Exports;
using Auxilia.Application.Abstractions.Messaging;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Localization.Public;
using Auxilia.Application.Reporting;
using Auxilia.Application.Tests.Engagement;
using Auxilia.Application.Tests.Identity;
using Auxilia.Application.Tests.Platform;
using Auxilia.Contracts.Messages.V1.Reporting;
using Auxilia.Contracts.Realtime;
using Auxilia.Diagnostics;
using Auxilia.Domain.Reporting;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

using NSubstitute;

namespace Auxilia.Application.Tests.Reporting;

public sealed class ExportManagerTests : IAsyncDisposable
{
    private static readonly string[] EmployeeRole = ["Employee"];

    private static readonly Guid User = Guid.CreateVersion7();

    private readonly FakeSource source = new();
    private readonly RecordingWriter writer = new();
    private readonly InMemoryExportJobs jobs = new();
    private readonly IMessageOutbox outbox = Substitute.For<IMessageOutbox>();
    private readonly IAccessGuard guard = Substitute.For<IAccessGuard>();
    private readonly ILocalizer localizer = Substitute.For<ILocalizer>();
    private readonly IPermissionAccess permissions = Substitute.For<IPermissionAccess>();
    private readonly RecordingNotificationSender notifications = new();
    private readonly RecordingRealtimeNotifier realtime = new();
    private readonly ICurrentUser caller = Substitute.For<ICurrentUser>();
    private readonly ManualTimeProvider clock = new();
    private readonly ExportManager manager;
    private readonly ExportQueryService query;

    public ExportManagerTests()
    {
        caller.ActorType.Returns(ActorType.User);
        caller.UserId.Returns(User);
        caller.Roles.Returns([TenantRole.Employee]);
        guard.EnsureAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        localizer.GetAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, object?>?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var arguments = call.ArgAt<IReadOnlyDictionary<string, object?>?>(2);
                return $"{call.ArgAt<string>(0)}@{call.ArgAt<string?>(1)}" + (arguments is null ? string.Empty : $"({string.Join(",", arguments.Select(pair => $"{pair.Key}={pair.Value}"))})");
            });
        permissions.GetGrantedAsync(Arg.Any<CancellationToken>()).Returns(new HashSet<string> { "things.view" });
        manager = new ExportManager(
            ManagerHarness.Runner(), [source], [writer], jobs, outbox, guard, localizer, notifications, realtime, SessionSettings.Tenant(), caller, clock);
        query = new ExportQueryService([source], jobs, permissions, caller, clock);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => jobs.DisposeAsync();

    private static ExportRequest Request(string? format = "csv", string? columns = null, string? ids = null, Dictionary<string, string?>? parameters = null) =>
        new("things", format, columns, ids, "it", parameters ?? new Dictionary<string, string?>(StringComparer.Ordinal) { ["filter[name]"] = "ro", ["format"] = "csv" });

    [Fact]
    public async Task SmallExports_PageEveryRow_TranslateHeadersAndFlaggedValues()
    {
        source.Total = 250;

        var result = await manager.ExportAsync(Request(columns: "status,name"), Ct);

        var file = result.Value.File.ShouldNotBeNull();
        result.Value.Queued.ShouldBeNull();
        // 09:00 UTC on 30 September is 10:00 in Rome.
        file.FileName.ShouldBe("nav_things_it_20260930_100000.csv");
        var table = writer.Last.ShouldNotBeNull();
        table.Title.ShouldBe("acme - nav.things@it");
        table.Headers.ShouldBe(["Status@it", "Name@it"]);
        table.Rows.Count.ShouldBe(250);
        table.Rows[0].ShouldBe(["Active@it", "Thing 0"]);
        table.Footer.ShouldBe("exports.total@it(count=250)");
        // Pages of 100 until the end, with the list's filters and without the export's own parameters.
        source.Pages.ShouldBe([1, 1, 2, 3]);
        source.LastParameters!.Text("filter[name]").ShouldBe("ro");
        source.LastParameters.Text("format").ShouldBeNull();
    }

    [Fact]
    public async Task SelectedIds_KeepOnlyThoseRows()
    {
        source.Total = 30;
        var wanted = new[] { FakeSource.IdOf(3), FakeSource.IdOf(7) };

        var result = await manager.ExportAsync(Request(ids: string.Join(',', wanted)), Ct);

        result.IsSuccess.ShouldBeTrue();
        writer.Last!.Rows.Select(row => row[0]).ShouldBe(["Thing 3", "Thing 7"]);
    }

    [Fact]
    public async Task InvalidRequests_AreFieldErrors_AndUnknownListsNotFound()
    {
        (await manager.ExportAsync(Request(format: "docx"), Ct)).Error!.ValidationErrors.Keys.ShouldBe(["format"]);
        (await manager.ExportAsync(Request(columns: "name,secret"), Ct)).Error!.ValidationErrors.Keys.ShouldBe(["columns"]);
        (await manager.ExportAsync(Request(ids: "nope"), Ct)).Error!.ValidationErrors.Keys.ShouldBe(["ids"]);
        (await manager.ExportAsync(Request() with { Source = "missing" }, Ct)).Error!.Code.ShouldBe(EventCodes.Audit.ExportSourceNotFound);

        guard.EnsureAsync("things.view", Arg.Any<CancellationToken>()).Returns(Result.Failure(Errors.Identity.PermissionDenied()));
        (await manager.ExportAsync(Request(), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.PermissionDenied);
    }

    [Fact]
    public async Task LargeExports_AreQueued_AndTooLargeOnesRefused()
    {
        source.Total = ExportManager.MaxSyncRows + 1;

        var queued = (await manager.ExportAsync(Request(format: "CSV"), Ct)).Value.Queued.ShouldNotBeNull();

        queued.RowCount.ShouldBe(ExportManager.MaxSyncRows + 1);
        var job = jobs.Jobs.ShouldHaveSingleItem();
        (job.Id, job.UserId, job.SourceKey, job.Format, job.Status).ShouldBe((queued.Id, User, "things", "Csv", ExportJobStatus.Queued));
        await outbox.Received(1).EnqueueAsync(
            Arg.Any<IOperationScope>(), Arg.Is<GenerateExportCommand>(command => command.ExportId == queued.Id && command.Roles.SequenceEqual(EmployeeRole)), Arg.Any<CancellationToken>());
        writer.Last.ShouldBeNull();

        source.Total = ExportManager.MaxRows + 1;
        (await manager.ExportAsync(Request(), Ct)).Error!.Code.ShouldBe(EventCodes.Audit.ExportTooLarge);
    }

    [Fact]
    public async Task Generate_WritesTheFile_ForItsOwner_AndTellsThem()
    {
        source.Total = ExportManager.MaxSyncRows + 5;
        var queued = (await manager.ExportAsync(Request(), Ct)).Value.Queued!;

        (await manager.GenerateAsync(queued.Id, Ct)).IsSuccess.ShouldBeTrue();

        var job = jobs.Jobs.Single();
        (job.Status, job.RowCount, job.ContentType, job.FileName).ShouldBe((ExportJobStatus.Ready, ExportManager.MaxSyncRows + 5, "text/test", "nav_things_it_20260930_100000.csv"));
        realtime.Pushes.ShouldHaveSingleItem().EventName.ShouldBe(RealtimeEvents.ExportReady);
        var notified = notifications.Sent.ShouldHaveSingleItem();
        (notified.Target, notified.Message.Kind, notified.Message.IncludeActor).ShouldBe(($"user:{User}", "export.ready", true));

        // A redelivery does nothing; the owner downloads it; others do not see it.
        (await manager.GenerateAsync(queued.Id, Ct)).IsSuccess.ShouldBeTrue();
        realtime.Pushes.Count.ShouldBe(1);
        (await query.FileAsync(queued.Id, Ct)).Value.Content.ShouldBe(RecordingWriter.Content);
        (await query.MineAsync(Ct)).Value.ShouldHaveSingleItem().Status.ShouldBe("Ready");

        caller.UserId.Returns(Guid.CreateVersion7());
        (await query.FileAsync(queued.Id, Ct)).Error!.Code.ShouldBe(EventCodes.Audit.ExportNotFound);
        (await manager.GenerateAsync(queued.Id, Ct)).Error!.Code.ShouldBe(EventCodes.Audit.ExportNotFound);

        caller.UserId.Returns(User);
        clock.Advance(TimeSpan.FromHours(ExportJob.RetentionHours + 1));
        (await query.FileAsync(queued.Id, Ct)).Error!.Code.ShouldBe(EventCodes.Audit.ExportNotFound);
    }

    [Fact]
    public async Task Generate_FailsTheJob_WhenTheListRefuses()
    {
        source.Total = ExportManager.MaxSyncRows + 1;
        var queued = (await manager.ExportAsync(Request(), Ct)).Value.Queued!;
        source.Refuse = true;

        (await manager.GenerateAsync(queued.Id, Ct)).IsSuccess.ShouldBeTrue();

        var job = jobs.Jobs.Single();
        (job.Status, job.ErrorCode).ShouldBe((ExportJobStatus.Failed, $"AUX-{EventCodes.Identity.PermissionDenied}"));
        (await query.FileAsync(queued.Id, Ct)).Error!.Code.ShouldBe(EventCodes.Audit.ExportNotReady);
        notifications.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Sources_ListOnlyTheGrantedOnes()
    {
        (await query.SourcesAsync(Ct)).ShouldHaveSingleItem().Columns.Select(column => column.Id).ShouldBe(["name", "status"]);

        permissions.GetGrantedAsync(Arg.Any<CancellationToken>()).Returns(new HashSet<string>());
        (await query.SourcesAsync(Ct)).ShouldBeEmpty();
    }

    [Fact]
    public void FileTitle_KeepsLettersAndDigits()
    {
        ExportManager.FileTitle("Tipologie Pratiche / 2026").ShouldBe("Tipologie_Pratiche_2026");
        ExportManager.FileTitle("***").ShouldBe("Export");
    }

    private sealed class FakeSource : IExportSource
    {
        public int Total { get; set; }

        public bool Refuse { get; set; }

        public List<int> Pages { get; } = [];

        public ExportParameters? LastParameters { get; private set; }

        public string Key => "things";

        public string TitleKey => "nav.things";

        public string Permission => "things.view";

        public IReadOnlyList<ExportColumn> Columns { get; } = [new("name", "Name"), new("status", "Status", Translated: true)];

        public static Guid IdOf(int index) => new(index, 0, 0, new byte[8]);

        public Task<Result<ExportPage>> PageAsync(ExportParameters parameters, int page, int pageSize, ExportFormatting formatting, CancellationToken cancellationToken)
        {
            Pages.Add(page);
            LastParameters = parameters;
            if (Refuse)
            {
                return Task.FromResult(Result.Failure<ExportPage>(Errors.Identity.PermissionDenied()));
            }

            var rows = Enumerable.Range((page - 1) * pageSize, Math.Max(0, Math.Min(pageSize, Total - ((page - 1) * pageSize))))
                .Select(index => new ExportRow(IdOf(index), new Dictionary<string, string>(StringComparer.Ordinal) { ["name"] = $"Thing {index}", ["status"] = "Active" }))
                .ToArray();
            return Task.FromResult(Result.Success(new ExportPage(rows, Total)));
        }
    }

    private sealed class RecordingWriter : IExportWriter
    {
        public static readonly byte[] Content = [1, 2, 3];

        public ExportTable? Last { get; private set; }

        public ExportFormat Format { get; set; } = ExportFormat.Csv;

        public string ContentType => "text/test";

        public string Extension => "csv";

        public byte[] Write(ExportTable table)
        {
            Last = table;
            return Content;
        }
    }

    /// <summary>One writer answers every format (the formats themselves are tested in Infrastructure).</summary>
    private sealed class InMemoryExportJobs : IExportJobDataFactory, IExportJobData
    {
        public List<ExportJob> Jobs { get; } = [];

        public Task<IExportJobData> OpenAsync(CancellationToken cancellationToken) => Task.FromResult<IExportJobData>(this);

        public Task<ExportJob?> FindAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Jobs.SingleOrDefault(job => job.Id == id));

        public Task<IReadOnlyList<ExportJobRow>> OfUserAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExportJobRow>>(Jobs.Where(job => job.UserId == userId && job.ExpiresAt > now)
                .Select(job => new ExportJobRow(job.Id, job.SourceKey, job.Format, job.Status, job.FileName, job.RowCount, job.ErrorCode, job.CreatedAt, job.ExpiresAt)).ToArray());

        public Task<int> DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(Jobs.RemoveAll(job => job.ExpiresAt <= now));

        public void Add(ExportJob job) => Jobs.Add(job);

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
