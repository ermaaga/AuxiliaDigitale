using Auxilia.Diagnostics;
using Auxilia.Domain.Engagement;

namespace Auxilia.Domain.Tests.Engagement;

public sealed class TaskItemTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid User = Guid.CreateVersion7();
    private static readonly Guid Client = Guid.CreateVersion7();

    private static TaskDetails Details(string? title = " Call ", DateOnly? dueOn = null, Guid? clientId = null, Guid? caseId = null) =>
        new(title, "  ", dueOn, User, clientId, caseId);

    [Fact]
    public void Create_TrimsAndValidates()
    {
        var task = TaskItem.Create(Guid.CreateVersion7(), Details(dueOn: new DateOnly(2026, 10, 3)), User, Now).Value;

        (task.Title, task.Notes, task.Status, task.CreatedByUserId, task.CreatedAt).ShouldBe(("Call", (string?)null, TaskItemStatus.Open, User, Now));
        task.IsOverdue(new DateOnly(2026, 10, 4)).ShouldBeTrue();
        task.IsOverdue(new DateOnly(2026, 10, 3)).ShouldBeFalse();

        var invalid = TaskItem.Create(Guid.CreateVersion7(), new TaskDetails(" ", new string('x', 2001), null, User, null, Guid.CreateVersion7()), User, Now);
        invalid.Error!.Code.ShouldBe(EventCodes.Requests.TaskInvalid);
        invalid.Error.ValidationErrors.Keys.ShouldBe(["title", "notes", "caseId"], ignoreOrder: true);
    }

    [Fact]
    public void CompleteAndReopen_AreIdempotent()
    {
        var task = TaskItem.Create(Guid.CreateVersion7(), Details(clientId: Client), User, Now).Value;

        task.Complete(User, Now).ShouldBeTrue();
        task.Complete(User, Now).ShouldBeFalse();
        (task.Status, task.CompletedAt, task.CompletedByUserId).ShouldBe((TaskItemStatus.Done, (DateTimeOffset?)Now, (Guid?)User));
        task.IsOverdue(new DateOnly(2030, 1, 1)).ShouldBeFalse();

        task.Reopen().ShouldBeTrue();
        task.Reopen().ShouldBeFalse();
        (task.Status, task.CompletedAt).ShouldBe((TaskItemStatus.Open, (DateTimeOffset?)null));
        task.Update(Details(title: "New")).IsSuccess.ShouldBeTrue();
        (task.Title, task.ClientId).ShouldBe(("New", (Guid?)null));
    }

    [Fact]
    public void Activities_NeedTextAKindAndAPastInstant()
    {
        var activity = ClientActivity.Create(Guid.CreateVersion7(), Client, ActivityKind.Meeting, " Met ", Now.AddDays(-1), User, Now).Value;
        (activity.Kind, activity.Text, activity.ClientId, activity.AuthorUserId).ShouldBe((ActivityKind.Meeting, "Met", Client, User));

        var invalid = ClientActivity.Create(Guid.CreateVersion7(), Client, (ActivityKind)42, "", Now.AddHours(1), User, Now);
        invalid.Error!.ValidationErrors.Keys.ShouldBe(["text", "kind", "occurredAt"], ignoreOrder: true);
    }
}
