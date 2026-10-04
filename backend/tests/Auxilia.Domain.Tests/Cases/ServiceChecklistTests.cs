using Auxilia.Domain.Cases;

namespace Auxilia.Domain.Tests.Cases;

public sealed class ServiceChecklistTests
{
    [Fact]
    public void Check_NeedsUniqueNamesAndFoldersOfTheService()
    {
        var folder = Guid.CreateVersion7();
        ServiceChecklistItem.Check([new(null, "CU", folder, true), new(null, "730", null, false)], new HashSet<Guid> { folder }).IsSuccess.ShouldBeTrue();

        var errors = ServiceChecklistItem.Check(
            [new(null, "CU", null, true), new(null, " cu ", null, true), new(null, "", Guid.CreateVersion7(), false)], new HashSet<Guid>()).Error!.ValidationErrors;
        errors.Keys.ShouldBe(["items[1].name", "items[2].name", "items[2].folderId"], ignoreOrder: true);
        ServiceChecklistItem.Check([.. Enumerable.Range(0, 101).Select(index => new ChecklistItemDetails(null, $"D{index}", null, false))], new HashSet<Guid>())
            .Error!.ValidationErrors.ShouldContainKey("items");
    }

    [Fact]
    public void Apply_SetsTheValuesAndTheOrder_AndMarksRecordWho()
    {
        var service = Guid.CreateVersion7();
        var item = new ServiceChecklistItem(Guid.CreateVersion7(), service);
        item.Apply(new ChecklistItemDetails(null, " CU ", null, true), 3);
        (item.ServiceId, item.Name, item.IsRequired, item.Order, item.FolderId).ShouldBe((service, "CU", true, 3, (Guid?)null));

        var user = Guid.CreateVersion7();
        var mark = new CaseChecklistMark(Guid.CreateVersion7(), item.Id, user, DateTimeOffset.UnixEpoch);
        (mark.ItemId, mark.CheckedByUserId, mark.CheckedAt).ShouldBe((item.Id, (Guid?)user, DateTimeOffset.UnixEpoch));
    }
}
