using Auxilia.Diagnostics;
using Auxilia.Domain.Marketing;

namespace Auxilia.Domain.Tests.Marketing;

public sealed class AudienceTests
{
    private static SegmentCondition Condition(SegmentField field, SegmentOperator op, string? value, string? key = null) => new(field, op, value, key);

    [Fact]
    public void Rule_AcceptsEveryFieldWithItsOperatorsAndValues()
    {
        var rule = new SegmentRule(
            true,
            [
                Condition(SegmentField.Status, SegmentOperator.Is, "Active"),
                Condition(SegmentField.Employee, SegmentOperator.None, null),
                Condition(SegmentField.Tag, SegmentOperator.HasNot, Guid.NewGuid().ToString()),
                Condition(SegmentField.Age, SegmentOperator.AtLeast, "18"),
                Condition(SegmentField.CreatedOn, SegmentOperator.OnOrAfter, "2026-01-31"),
                Condition(SegmentField.CustomField, SegmentOperator.Is, "true", "caf"),
            ],
            [new SegmentGroup(false, [Condition(SegmentField.CaseStatus, SegmentOperator.Has, "Sent"), Condition(SegmentField.Service, SegmentOperator.Has, Guid.NewGuid().ToString())])]);

        rule.Validate().IsSuccess.ShouldBeTrue();
        rule.AllConditions.Count().ShouldBe(8);
    }

    [Fact]
    public void Rule_ReportsEveryWrongCondition()
    {
        var rule = new SegmentRule(
            true,
            [
                Condition(SegmentField.Status, SegmentOperator.Has, "Active"),
                Condition(SegmentField.Status, SegmentOperator.Is, "Gone"),
                Condition(SegmentField.Age, SegmentOperator.AtMost, "200"),
                Condition(SegmentField.CreatedOn, SegmentOperator.OnOrBefore, "31/01/2026"),
                Condition(SegmentField.CustomField, SegmentOperator.Is, "1", null),
                Condition((SegmentField)99, SegmentOperator.Is, "x"),
            ],
            [new SegmentGroup(true, [])]);

        var errors = rule.Validate().Error!;
        errors.Code.ShouldBe(EventCodes.Marketing.SegmentInvalid);
        errors.ValidationErrors.Keys.ShouldBe(
            ["rule.conditions[0].op", "rule.conditions[1].value", "rule.conditions[2].value", "rule.conditions[3].value", "rule.conditions[4].key", "rule.conditions[5].field", "rule.groups[0]"],
            ignoreOrder: true);
        new SegmentRule(true, [], []).Validate().Error!.ValidationErrors["rule"].ShouldBe(["validation.segments.empty"]);
        new SegmentRule(true, [.. Enumerable.Range(0, 51).Select(_ => Condition(SegmentField.Status, SegmentOperator.Is, "Active"))], []).Validate()
            .Error!.ValidationErrors["rule"].ShouldBe(["validation.segments.tooMany"]);
    }

    [Fact]
    public void SegmentsAndLists_NeedANameWithinTheLimits()
    {
        var segment = new Segment(Guid.NewGuid());
        segment.Update(" Active clients ", " ", "{}").IsSuccess.ShouldBeTrue();
        (segment.Name, segment.Description, segment.Rule).ShouldBe(("Active clients", (string?)null, "{}"));
        segment.Update("", new string('d', 501), "{}").Error!.ValidationErrors.Keys.ShouldBe(["name", "description"], ignoreOrder: true);

        var list = new StaticList(Guid.NewGuid());
        list.Update(" Newsletter ", "Monthly").IsSuccess.ShouldBeTrue();
        (list.Name, list.Description).ShouldBe(("Newsletter", "Monthly"));
        list.Update(" ", null).Error!.ValidationErrors["name"].ShouldBe(["validation.lists.name"]);
        list.Update("x", new string('d', 501)).Error!.ValidationErrors["description"].ShouldBe(["validation.lists.description"]);

        var member = new StaticListMember(list.Id, Guid.NewGuid(), null, DateTimeOffset.UnixEpoch);
        (member.ListId, member.AddedAt).ShouldBe((list.Id, DateTimeOffset.UnixEpoch));
    }
}
