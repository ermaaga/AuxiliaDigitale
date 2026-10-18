using Auxilia.Domain.Mailbox;

namespace Auxilia.Domain.Tests.Mailbox;

public sealed class SyncTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Account = Guid.CreateVersion7();
    private static readonly Guid User = Guid.CreateVersion7();

    private static SyncRun Run() => SyncRun.Queue(Guid.CreateVersion7(), Account, User, SyncScope.Calendar, SyncTrigger.PageOpen, Now);

    private static CalendarEventData Event(string id = "e1", string? title = "Riunione", string? link = "https://calendar.google.com/event?eid=1") =>
        new(id, title, "Studio", Now.AddHours(1), Now.AddHours(2), false, false, link);

    [Fact]
    public void SyncRun_GoesFromQueuedToSucceeded_CountingItemsAndParts()
    {
        var run = Run();
        (run.Status, run.Parts, run.IsActive).ShouldBe((SyncRunStatus.Queued, 1, true));

        run.Start(Now.AddSeconds(5));
        run.Record(3, 1, 2, Now.AddSeconds(6));
        run.Continue(Now.AddMinutes(2));
        run.Start(Now.AddMinutes(3));
        run.Record(1, 0, 0, Now.AddMinutes(3));
        run.Succeed(Now.AddMinutes(4));

        (run.Status, run.StartedAt, run.EndedAt, run.Parts).ShouldBe((SyncRunStatus.Succeeded, Now.AddSeconds(5), Now.AddMinutes(4), 2));
        (run.NewCount, run.UpdatedCount, run.SkippedCount, run.IsActive).ShouldBe((4, 1, 2, false));
    }

    [Fact]
    public void SyncRun_KeepsTheTransientCode_UntilItSucceeds_AndFailsWithItsCode()
    {
        var run = Run();
        run.Start(Now);
        run.NoteTransientFailure("AUX-30021", "trace", Now);
        run.ErrorCode.ShouldBe("AUX-30021");
        run.Succeed(Now);
        run.ErrorCode.ShouldBeNull();

        var failed = Run();
        failed.Fail("AUX-30020", null, Now);
        (failed.Status, failed.ErrorCode, failed.EndedAt).ShouldBe((SyncRunStatus.Failed, "AUX-30020", (DateTimeOffset?)Now));
        Should.Throw<InvalidOperationException>(() => failed.Start(Now));
        Should.Throw<InvalidOperationException>(() => failed.Fail("AUX-30020", null, Now));
        Should.Throw<InvalidOperationException>(() => Run().Succeed(Now));
    }

    [Fact]
    public void SyncRun_IsRecentWhileActive_OrJustEnded_AndStaleWithoutProgress()
    {
        var interval = TimeSpan.FromMinutes(2);
        var run = Run();
        run.IsRecent(Now.AddMinutes(10), interval).ShouldBeTrue();
        run.IsStale(Now + SyncRun.StaleAfter).ShouldBeTrue();
        run.IsRecent(Now + SyncRun.StaleAfter, interval).ShouldBeFalse();

        run.Start(Now);
        run.Succeed(Now.AddMinutes(1));
        run.IsRecent(Now.AddMinutes(2), interval).ShouldBeTrue();
        run.IsRecent(Now.AddMinutes(3), interval).ShouldBeFalse();
        run.IsStale(Now.AddDays(1)).ShouldBeFalse();
    }

    [Fact]
    public void SyncCursor_PagesThenCompletes_AndResetsToAFullSync()
    {
        var cursor = SyncCursor.Begin(Account, SyncScope.Calendar, SyncCursor.Primary, Now);
        cursor.IsFullSync.ShouldBeTrue();

        cursor.Advance("page-2", Now);
        cursor.PageToken.ShouldBe("page-2");
        cursor.Complete("sync-1", Now.AddMinutes(1));
        (cursor.PageToken, cursor.SyncToken, cursor.IsFullSync).ShouldBe(((string?)null, "sync-1", false));

        cursor.Reset(Now.AddDays(1));
        (cursor.SyncToken, cursor.IsFullSync, cursor.FullSyncStartedAt).ShouldBe(((string?)null, true, Now.AddDays(1)));
        Should.Throw<ArgumentOutOfRangeException>(() => cursor.Complete(new string('x', SyncCursor.TokenMaxLength + 1), Now));
    }

    [Fact]
    public void CalendarEvent_IsUsableOnlyWithAnIdAndAnOrderedRange()
    {
        CalendarEvent.IsUsable(Event()).ShouldBeTrue();
        CalendarEvent.IsUsable(Event(id: " ")).ShouldBeFalse();
        CalendarEvent.IsUsable(Event() with { EndsAt = Now }).ShouldBeFalse();
        CalendarEvent.IsUsable(Event(id: new string('x', CalendarEvent.ProviderEventIdMaxLength + 1))).ShouldBeFalse();
        Should.Throw<ArgumentException>(() => CalendarEvent.Create(Guid.CreateVersion7(), Account, Event(id: ""), Now));
    }

    [Fact]
    public void CalendarEvent_BoundsTheTexts_DropsControlCharacters_AndKeepsOnlyHttpsLinks()
    {
        var created = CalendarEvent.Create(Guid.CreateVersion7(), Account, Event(title: " Riunione\u0007 " + new string('a', 600), link: "javascript:alert(1)"), Now);

        created.Title.Length.ShouldBe(CalendarEvent.TitleMaxLength);
        created.Title.ShouldStartWith("Riunione ");
        created.Title.ShouldNotContain("\u0007");
        created.Link.ShouldBeNull();
        CalendarEvent.Create(Guid.CreateVersion7(), Account, Event(title: null, link: "http://example.com"), Now).ShouldSatisfyAllConditions(
            item => item.Title.ShouldBe(string.Empty),
            item => item.Link.ShouldBeNull());
    }

    [Fact]
    public void CalendarEvent_Apply_TellsWhetherSomethingShownChanged_AndAlwaysMarksItSynced()
    {
        var item = CalendarEvent.Create(Guid.CreateVersion7(), Account, Event(), Now);

        item.Apply(Event(), Now.AddHours(1)).ShouldBeFalse();
        item.SyncedAt.ShouldBe(Now.AddHours(1));
        item.Apply(Event() with { Tentative = true }, Now.AddHours(2)).ShouldBeTrue();
        item.Tentative.ShouldBeTrue();
    }

    [Fact]
    public void ConnectedAccount_RecordsTheSyncOfEachScope()
    {
        var account = ConnectedAccount.ConnectOAuth(
            Guid.CreateVersion7(), User, ConnectedAccountProvider.Google,
            new OAuthGrantDetails("mario@gmail.com", null, false, true, "calendar", "protected"), "blue", Now).Value;

        (account.Has(SyncScope.Mail), account.Has(SyncScope.Calendar)).ShouldBe((false, true));
        account.Synced(SyncScope.Calendar, Now);
        (account.CalendarSyncedAt, account.MailSyncedAt).ShouldBe(((DateTimeOffset?)Now, (DateTimeOffset?)null));
    }
}
