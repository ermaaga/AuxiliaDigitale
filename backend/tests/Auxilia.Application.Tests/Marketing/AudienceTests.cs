using System.Text.Json;

using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Marketing;
using Auxilia.Application.Marketing;
using Auxilia.Application.Marketing.Public;
using Auxilia.Application.Tests.Identity;
using Auxilia.Application.Tests.Platform;
using Auxilia.Contracts.Marketing;
using Auxilia.Diagnostics;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Marketing;
using Auxilia.SharedKernel.Tenancy;

using NSubstitute;

namespace Auxilia.Application.Tests.Marketing;

public sealed class AudienceTests : IAsyncDisposable
{
    private static readonly Guid Employee = Guid.CreateVersion7();
    private static readonly Guid Mine = Guid.CreateVersion7();
    private static readonly Guid Theirs = Guid.CreateVersion7();
    private static readonly int[] NotALiteral = [1];

    private readonly InMemoryAudiences data = new();
    private readonly ICurrentUser caller = Substitute.For<ICurrentUser>();
    private readonly ManualTimeProvider clock = new();
    private readonly SegmentManager segments;
    private readonly SegmentQueryService segmentQuery;
    private readonly StaticListManager lists;
    private readonly StaticListQueryService listQuery;

    public AudienceTests()
    {
        CallAs(TenantRole.Administrator);
        data.Clients[Mine] = Employee;
        data.Clients[Theirs] = Guid.CreateVersion7();
        var policy = new AudiencePolicy(caller);
        segments = new SegmentManager(ManagerHarness.Runner(), data);
        segmentQuery = new SegmentQueryService(data, policy, SessionSettings.Tenant(), clock);
        lists = new StaticListManager(ManagerHarness.Runner(), data, policy, caller, clock);
        listQuery = new StaticListQueryService(data, policy);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => data.DisposeAsync();

    private void CallAs(TenantRole role)
    {
        caller.ActorType.Returns(ActorType.User);
        caller.UserId.Returns(Employee);
        caller.Roles.Returns([role]);
    }

    private static SegmentRuleRequest Rule(params SegmentConditionRequest[] conditions) => new("all", conditions, null);

    private static SegmentConditionRequest Condition(string field, string op, object? value, string? key = null) =>
        new(field, op, value is null ? null : JsonSerializer.SerializeToElement(value), key);

    [Fact]
    public async Task Segments_StoreACanonicalRule_AndAnswerWithItBack()
    {
        var tag = Guid.CreateVersion7();
        var request = new SaveSegmentRequest(" CAF clients ", null, new SegmentRuleRequest("any",
            [Condition("tag", "has", tag.ToString()), Condition("customField", "is", true, "caf")],
            [new SegmentGroupRequest("all", [Condition("age", "atLeast", 18), Condition("status", "is", "Active")])]));

        var id = (await segments.CreateAsync(request, Ct)).Value;

        var stored = SegmentRules.Deserialize(data.Segments.Single().Rule);
        stored.MatchAll.ShouldBeFalse();
        stored.Conditions.ShouldBe([new SegmentCondition(SegmentField.Tag, SegmentOperator.Has, tag.ToString()), new SegmentCondition(SegmentField.CustomField, SegmentOperator.Is, "true", "caf")]);
        stored.Groups.Single().Conditions[0].ShouldBe(new SegmentCondition(SegmentField.Age, SegmentOperator.AtLeast, "18"));

        var detail = (await segmentQuery.GetAsync(id, Ct)).Value;
        (detail.Name, detail.MemberCount, detail.Rule.Match, detail.Rule.Conditions[1].Value!.Value.GetBoolean(), detail.Rule.Groups![0].Conditions[0].Value!.Value.GetInt32())
            .ShouldBe(("CAF clients", 2, "any", true, 18));
        (await segments.CreateAsync(request with { Name = "caf clients" }, Ct)).Error!.ValidationErrors["name"].ShouldBe(["validation.segments.nameTaken"]);
        segmentQuery.Fields().ShouldContain(field => field.Field == "employee" && field.Operators.Contains("none"));
    }

    [Fact]
    public async Task InvalidRules_AreFieldErrors_WithTheRequestPaths()
    {
        var result = await segments.CreateAsync(new SaveSegmentRequest("", null, new SegmentRuleRequest("most",
            [Condition("colour", "is", "red"), Condition("status", "between", "Active"), Condition("customField", "is", NotALiteral, "caf")], null)), Ct);

        result.Error!.ValidationErrors.Keys.ShouldBe(["rule.match", "rule.conditions[0].field", "rule.conditions[1].op", "rule.conditions[2].value", "name"], ignoreOrder: true);
        (await segmentQuery.PreviewAsync(Rule(), Ct)).Error!.ValidationErrors["rule"].ShouldBe(["validation.segments.empty"]);
        (await segments.UpdateAsync(Guid.CreateVersion7(), new SaveSegmentRequest("x", null, Rule(Condition("status", "is", "Active"))), Ct)).Error!.Code
            .ShouldBe(EventCodes.Marketing.SegmentNotFound);
    }

    [Fact]
    public async Task Employees_SeeOnlyTheClientsInTheirCharge()
    {
        (await segmentQuery.PreviewAsync(Rule(Condition("status", "is", "Active")), Ct)).Value.Count.ShouldBe(2);
        data.LastScope.ShouldBe(AudienceScope.Everyone);

        CallAs(TenantRole.Employee);
        var preview = (await segmentQuery.PreviewAsync(Rule(Condition("status", "is", "Active")), Ct)).Value;
        (preview.Count, preview.Sample.Single().Id).ShouldBe((1, Mine));

        var list = (await lists.CreateAsync(new SaveStaticListRequest("Newsletter", null), Ct)).Value;
        (await lists.AddMembersAsync(list, [Mine, Theirs, Mine], Ct)).Value.ShouldBe(1);
        (await lists.AddMembersAsync(list, [Mine], Ct)).Value.ShouldBe(0);
        (await listQuery.GetAsync(list, Ct)).Value.MemberCount.ShouldBe(1);

        CallAs(TenantRole.Administrator);
        (await lists.AddMembersAsync(list, [Theirs], Ct)).Value.ShouldBe(1);
        (await listQuery.MembersAsync(list, 1, 25, Ct)).Value.TotalCount.ShouldBe(2);
        (await lists.RemoveMembersAsync(list, [Mine], Ct)).Value.ShouldBe(1);
        (await new AudienceResolver(data, SessionSettings.Tenant(), clock).ListAsync(list, Ct)).Value.ShouldBe([Theirs]);
    }

    [Fact]
    public async Task Lists_HaveUniqueNames_AndCheckTheirMembers()
    {
        var list = (await lists.CreateAsync(new SaveStaticListRequest(" Newsletter ", null), Ct)).Value;
        (await lists.CreateAsync(new SaveStaticListRequest("newsletter", null), Ct)).Error!.ValidationErrors["name"].ShouldBe(["validation.lists.nameTaken"]);
        (await lists.UpdateAsync(list, new SaveStaticListRequest("Promo", "Spring"), Ct)).IsSuccess.ShouldBeTrue();
        (await listQuery.ListAsync(Ct)).Single().ShouldSatisfyAllConditions(row => row.Name.ShouldBe("Promo"), row => row.Description.ShouldBe("Spring"));

        (await lists.AddMembersAsync(list, [], Ct)).Error!.ValidationErrors["clientIds"].ShouldBe(["validation.lists.clients"]);
        (await lists.AddMembersAsync(Guid.CreateVersion7(), [Mine], Ct)).Error!.Code.ShouldBe(EventCodes.Marketing.ListNotFound);
        (await lists.DeleteAsync(list, Ct)).IsSuccess.ShouldBeTrue();
        (await listQuery.GetAsync(list, Ct)).Error!.Code.ShouldBe(EventCodes.Marketing.ListNotFound);
        (await segments.DeleteAsync(Guid.CreateVersion7(), Ct)).Error!.Code.ShouldBe(EventCodes.Marketing.SegmentNotFound);
    }
}

/// <summary>Audiences in memory: every rule selects every client in scope (the SQL is tested on PostgreSQL).</summary>
internal sealed class InMemoryAudiences : IAudienceDataFactory, IAudienceData
{
    /// <summary>Client → employee in charge.</summary>
    public Dictionary<Guid, Guid> Clients { get; } = [];

    public List<Segment> Segments { get; } = [];

    public List<StaticList> Lists { get; } = [];

    public List<StaticListMember> Members { get; } = [];

    public AudienceScope? LastScope { get; private set; }

    public Task<IAudienceData> OpenAsync(CancellationToken cancellationToken) => Task.FromResult<IAudienceData>(this);

    public Task<(IReadOnlyList<AudienceMemberRow> Items, int Total)> SegmentMembersAsync(
        SegmentRule rule, AudienceScope scope, DateOnly today, int skip, int take, CancellationToken cancellationToken)
    {
        LastScope = scope;
        var rows = InScope(scope).ToArray();
        return Task.FromResult<(IReadOnlyList<AudienceMemberRow>, int)>(([.. rows.Skip(skip).Take(take).Select(Row)], rows.Length));
    }

    public Task<IReadOnlyList<Guid>> SegmentMemberIdsAsync(SegmentRule rule, AudienceScope scope, DateOnly today, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>([.. InScope(scope)]);

    public Task<IReadOnlyList<SegmentRow>> SegmentsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SegmentRow>>([.. Segments.Select(segment => new SegmentRow(segment.Id, segment.Name, segment.Description, segment.Rule, DateTimeOffset.UnixEpoch))]);

    public Task<Segment?> FindSegmentAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Segments.SingleOrDefault(segment => segment.Id == id));

    public Task<bool> SegmentNameTakenAsync(string name, Guid? exceptId, CancellationToken cancellationToken) =>
        Task.FromResult(Segments.Any(segment => string.Equals(segment.Name, name, StringComparison.OrdinalIgnoreCase) && segment.Id != exceptId));

    public Task<IReadOnlyList<StaticListRow>> ListsAsync(AudienceScope scope, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<StaticListRow>>([.. Lists.Select(list => new StaticListRow(
            list.Id, list.Name, list.Description, Members.Count(member => member.ListId == list.Id && InScope(scope).Contains(member.PersonId)), DateTimeOffset.UnixEpoch))]);

    public Task<StaticList?> FindListAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Lists.SingleOrDefault(list => list.Id == id));

    public Task<bool> ListNameTakenAsync(string name, Guid? exceptId, CancellationToken cancellationToken) =>
        Task.FromResult(Lists.Any(list => string.Equals(list.Name, name, StringComparison.OrdinalIgnoreCase) && list.Id != exceptId));

    public Task<(IReadOnlyList<AudienceMemberRow> Items, int Total)> ListMembersAsync(Guid listId, AudienceScope scope, int skip, int take, CancellationToken cancellationToken)
    {
        var ids = InScope(scope).Where(id => Members.Any(member => member.ListId == listId && member.PersonId == id)).ToArray();
        return Task.FromResult<(IReadOnlyList<AudienceMemberRow>, int)>(([.. ids.Skip(skip).Take(take).Select(Row)], ids.Length));
    }

    public Task<IReadOnlyList<Guid>> ListMemberIdsAsync(Guid listId, AudienceScope scope, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>([.. InScope(scope).Where(id => Members.Any(member => member.ListId == listId && member.PersonId == id))]);

    public Task<IReadOnlyList<Guid>> ClientsInScopeAsync(IReadOnlyCollection<Guid> ids, AudienceScope scope, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>([.. InScope(scope).Where(ids.Contains)]);

    public Task<IReadOnlyList<Guid>> MembersAmongAsync(Guid listId, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>([.. Members.Where(member => member.ListId == listId && ids.Contains(member.PersonId)).Select(member => member.PersonId)]);

    public Task<int> RemoveMembersAsync(Guid listId, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        Task.FromResult(Members.RemoveAll(member => member.ListId == listId && ids.Contains(member.PersonId)));

    public void Add(Segment segment) => Segments.Add(segment);

    public void Remove(Segment segment) => Segments.Remove(segment);

    public void Add(StaticList list) => Lists.Add(list);

    public void Remove(StaticList list)
    {
        Lists.Remove(list);
        Members.RemoveAll(member => member.ListId == list.Id);
    }

    public void AddMembers(IEnumerable<StaticListMember> members) => Members.AddRange(members);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private IEnumerable<Guid> InScope(AudienceScope scope) =>
        Clients.Where(pair => scope.EmployeeUserId is null || pair.Value == scope.EmployeeUserId).Select(pair => pair.Key).Order();

    private static AudienceMemberRow Row(Guid id) => new(id, "Mario", id.ToString("N")[..6], null, ClientStatus.Active);
}
