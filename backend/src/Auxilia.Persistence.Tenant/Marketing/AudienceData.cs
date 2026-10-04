using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;

using Auxilia.Application.Abstractions.Marketing;
using Auxilia.Application.Abstractions.Persistence;
using Auxilia.Domain.Cases;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Identity;
using Auxilia.Domain.Marketing;
using Auxilia.Persistence.Tenant.Conventions;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Tenant.Marketing;

internal sealed class AudienceDataFactory(ITenantDbContextFactory databases) : IAudienceDataFactory
{
    public async Task<IAudienceData> OpenAsync(CancellationToken cancellationToken) => new AudienceData(await databases.CreateAsync(cancellationToken));
}

/// <inheritdoc cref="IAudienceData"/>
internal sealed class AudienceData(ITenantDbContext db) : IAudienceData
{
    public async Task<(IReadOnlyList<AudienceMemberRow> Items, int Total)> SegmentMembersAsync(
        SegmentRule rule, AudienceScope scope, DateOnly today, int skip, int take, CancellationToken cancellationToken)
    {
        var clients = Clients(scope).Where(Predicate(rule, today));
        var total = await clients.CountAsync(cancellationToken);
        return (await Members(clients, skip, take, cancellationToken), total);
    }

    public async Task<IReadOnlyList<Guid>> SegmentMemberIdsAsync(SegmentRule rule, AudienceScope scope, DateOnly today, CancellationToken cancellationToken) =>
        await Clients(scope).Where(Predicate(rule, today)).Select(row => row.Person.Id).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SegmentRow>> SegmentsAsync(CancellationToken cancellationToken) =>
        await db.Set<Segment>().AsNoTracking()
            .OrderBy(segment => segment.Name)
            .Select(segment => new SegmentRow(
                segment.Id, segment.Name, segment.Description, segment.Rule,
                EF.Property<DateTimeOffset?>(segment, TenantConventions.UpdatedAt) ?? EF.Property<DateTimeOffset>(segment, TenantConventions.CreatedAt)))
            .ToListAsync(cancellationToken);

    public Task<Segment?> FindSegmentAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<Segment>().SingleOrDefaultAsync(segment => segment.Id == id, cancellationToken);

    public Task<bool> SegmentNameTakenAsync(string name, Guid? exceptId, CancellationToken cancellationToken) =>
        db.Set<Segment>().AnyAsync(segment => segment.Name == name && segment.Id != exceptId, cancellationToken);

    public async Task<IReadOnlyList<StaticListRow>> ListsAsync(AudienceScope scope, CancellationToken cancellationToken)
    {
        var members = db.Set<StaticListMember>();
        var clients = Clients(scope);
        return await db.Set<StaticList>().AsNoTracking()
            .OrderBy(list => list.Name)
            .Select(list => new StaticListRow(
                list.Id, list.Name, list.Description,
                members.Count(member => member.ListId == list.Id && clients.Any(row => row.Person.Id == member.PersonId)),
                EF.Property<DateTimeOffset?>(list, TenantConventions.UpdatedAt) ?? EF.Property<DateTimeOffset>(list, TenantConventions.CreatedAt)))
            .ToListAsync(cancellationToken);
    }

    public Task<StaticList?> FindListAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<StaticList>().SingleOrDefaultAsync(list => list.Id == id, cancellationToken);

    public Task<bool> ListNameTakenAsync(string name, Guid? exceptId, CancellationToken cancellationToken) =>
        db.Set<StaticList>().AnyAsync(list => list.Name == name && list.Id != exceptId, cancellationToken);

    public async Task<(IReadOnlyList<AudienceMemberRow> Items, int Total)> ListMembersAsync(
        Guid listId, AudienceScope scope, int skip, int take, CancellationToken cancellationToken)
    {
        var members = db.Set<StaticListMember>();
        var clients = Clients(scope).Where(row => members.Any(member => member.ListId == listId && member.PersonId == row.Person.Id));
        var total = await clients.CountAsync(cancellationToken);
        return (await Members(clients, skip, take, cancellationToken), total);
    }

    public async Task<IReadOnlyList<Guid>> ListMemberIdsAsync(Guid listId, AudienceScope scope, CancellationToken cancellationToken)
    {
        var members = db.Set<StaticListMember>();
        return await Clients(scope)
            .Where(row => members.Any(member => member.ListId == listId && member.PersonId == row.Person.Id))
            .Select(row => row.Person.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ClientsInScopeAsync(IReadOnlyCollection<Guid> ids, AudienceScope scope, CancellationToken cancellationToken) =>
        await Clients(scope).Where(row => ids.Contains(row.Person.Id)).Select(row => row.Person.Id).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> MembersAmongAsync(Guid listId, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await db.Set<StaticListMember>().AsNoTracking()
            .Where(member => member.ListId == listId && ids.Contains(member.PersonId))
            .Select(member => member.PersonId)
            .ToListAsync(cancellationToken);

    public Task<int> RemoveMembersAsync(Guid listId, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        db.Set<StaticListMember>().Where(member => member.ListId == listId && ids.Contains(member.PersonId)).ExecuteDeleteAsync(cancellationToken);

    public void Add(Segment segment) => db.Set<Segment>().Add(segment);

    public void Remove(Segment segment) => db.Set<Segment>().Remove(segment);

    public void Add(StaticList list) => db.Set<StaticList>().Add(list);

    public void Remove(StaticList list) => db.Set<StaticList>().Remove(list);

    public void AddMembers(IEnumerable<StaticListMember> members) => db.Set<StaticListMember>().AddRange(members);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public ValueTask DisposeAsync() => db.DisposeAsync();

    private static async Task<IReadOnlyList<AudienceMemberRow>> Members(IQueryable<ClientRow> clients, int skip, int take, CancellationToken cancellationToken) =>
        await clients
            .OrderBy(row => row.Person.LastName).ThenBy(row => row.Person.FirstName).ThenBy(row => row.Person.Id)
            .Skip(skip)
            .Take(take)
            .Select(row => new AudienceMemberRow(row.Person.Id, row.Person.FirstName, row.Person.LastName, row.Person.Email, row.Profile.Status))
            .ToListAsync(cancellationToken);

    /// <summary>Clients (people with a client profile; the soft-delete filter of people applies) within the scope.</summary>
    private IQueryable<ClientRow> Clients(AudienceScope scope)
    {
        var users = db.Set<User>();
        var clients =
            from person in db.Set<Person>().AsNoTracking()
            join profile in db.Set<ClientProfile>() on person.Id equals profile.Id
            select new ClientRow { Person = person, Profile = profile, UserId = users.Where(user => user.PersonId == person.Id).Select(user => (Guid?)user.Id).FirstOrDefault() };
        return scope.EmployeeUserId is { } employee ? clients.Where(row => row.Profile.EmployeeUserId == employee) : clients;
    }

    private Expression<Func<ClientRow, bool>> Predicate(SegmentRule rule, DateOnly today)
    {
        var parts = rule.Conditions.Select(condition => Condition(condition, today))
            .Concat(rule.Groups.Select(group => Combine(group.Conditions.Select(condition => Condition(condition, today)), group.MatchAll)));
        return Combine(parts, rule.MatchAll);
    }

    /// <summary>One condition as a predicate; every value is a typed parameter (never SQL text).</summary>
    private Expression<Func<ClientRow, bool>> Condition(SegmentCondition condition, DateOnly today)
    {
        var negate = condition.Operator is SegmentOperator.IsNot or SegmentOperator.HasNot;
        Expression<Func<ClientRow, bool>> predicate;
        switch (condition.Field)
        {
            case SegmentField.Status:
                var status = Enum.Parse<ClientStatus>(condition.Value!);
                predicate = row => row.Profile.Status == status;
                break;
            case SegmentField.Employee when condition.Operator == SegmentOperator.None:
                predicate = row => row.Profile.EmployeeUserId == null;
                break;
            case SegmentField.Employee:
                var employee = Guid.Parse(condition.Value!);
                predicate = row => row.Profile.EmployeeUserId == employee;
                break;
            case SegmentField.Tag:
                var tag = Guid.Parse(condition.Value!);
                var tags = db.Set<PersonTag>();
                predicate = row => tags.Any(assignment => assignment.PersonId == row.Person.Id && assignment.TagId == tag);
                break;
            case SegmentField.Specialization:
                var specialization = Guid.Parse(condition.Value!);
                var specializations = db.Set<Specialization>();
                predicate = row => specializations.Any(item => item.Id == specialization && item.Members.Any(member => member.UserId == row.UserId));
                break;
            case SegmentField.Service:
                var service = Guid.Parse(condition.Value!);
                var serviceCases = db.Set<Case>();
                predicate = row => serviceCases.Any(@case => @case.ClientId == row.Person.Id && @case.ServiceId == service);
                break;
            case SegmentField.CaseStatus:
                var caseStatus = Enum.Parse<CaseStatus>(condition.Value!);
                var statusCases = db.Set<Case>();
                predicate = row => statusCases.Any(@case => @case.ClientId == row.Person.Id && @case.Status == caseStatus);
                break;
            case SegmentField.Age:
                var years = int.Parse(condition.Value!, CultureInfo.InvariantCulture);
                if (condition.Operator == SegmentOperator.AtLeast)
                {
                    var bornBy = today.AddYears(-years);
                    predicate = row => row.Person.BirthDate != null && row.Person.BirthDate <= bornBy;
                }
                else
                {
                    var bornAfter = today.AddYears(-(years + 1));
                    predicate = row => row.Person.BirthDate != null && row.Person.BirthDate > bornAfter;
                }

                break;
            case SegmentField.CreatedOn:
                var day = DateOnly.ParseExact(condition.Value!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                var start = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                if (condition.Operator == SegmentOperator.OnOrAfter)
                {
                    predicate = row => EF.Property<DateTimeOffset>(row.Person, TenantConventions.CreatedAt) >= start;
                }
                else
                {
                    var end = start.AddDays(1);
                    predicate = row => EF.Property<DateTimeOffset>(row.Person, TenantConventions.CreatedAt) < end;
                }

                break;
            case SegmentField.CustomField:
                // {"key": <value>} as a JSON parameter: the value is a JSON literal validated by the Application.
                var contained = $"{{{JsonSerializer.Serialize(condition.Key)}: {condition.Value}}}";
                predicate = row => EF.Functions.JsonContains(row.Person.CustomFields, contained);
                break;
            default:
                throw new InvalidOperationException($"Unknown segment field {condition.Field}.");
        }

        return negate ? Not(predicate) : predicate;
    }

    private static Expression<Func<ClientRow, bool>> Combine(IEnumerable<Expression<Func<ClientRow, bool>>> predicates, bool all)
    {
        var parameter = Expression.Parameter(typeof(ClientRow), "row");
        Expression? body = null;
        foreach (var predicate in predicates)
        {
            var part = new Rebind(predicate.Parameters[0], parameter).Visit(predicate.Body);
            body = body is null ? part : all ? Expression.AndAlso(body, part) : Expression.OrElse(body, part);
        }

        return Expression.Lambda<Func<ClientRow, bool>>(body ?? Expression.Constant(all), parameter);
    }

    private static Expression<Func<ClientRow, bool>> Not(Expression<Func<ClientRow, bool>> predicate) =>
        Expression.Lambda<Func<ClientRow, bool>>(Expression.Not(predicate.Body), predicate.Parameters);

    /// <summary>Points the parameter of a predicate to the combined one.</summary>
    private sealed class Rebind(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }

    /// <summary>A client in the audience queries.</summary>
    private sealed class ClientRow
    {
        public Person Person { get; init; } = null!;

        public ClientProfile Profile { get; init; } = null!;

        public Guid? UserId { get; init; }
    }
}
