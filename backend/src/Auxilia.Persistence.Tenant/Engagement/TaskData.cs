using Auxilia.Application.Abstractions.Engagement;
using Auxilia.Application.Abstractions.Persistence;
using Auxilia.Domain.Cases;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Engagement;
using Auxilia.Domain.Identity;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Tenant.Engagement;

internal sealed class TaskDataFactory(ITenantDbContextFactory databases) : ITaskDataFactory
{
    public async Task<ITaskData> OpenAsync(CancellationToken cancellationToken) => new TaskData(await databases.CreateAsync(cancellationToken));
}

/// <inheritdoc cref="ITaskData"/>
internal sealed class TaskData(ITenantDbContext db) : ITaskData
{
    public async Task<(IReadOnlyList<TaskRow> Items, int Total)> PageAsync(TaskFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var tasks = Visible(filter.VisibleTo);
        if (filter.AssigneeUserId is { } assignee)
        {
            tasks = tasks.Where(task => task.AssigneeUserId == assignee);
        }

        if (filter.Status is { } status)
        {
            tasks = tasks.Where(task => task.Status == status);
        }

        if (filter.ClientId is { } clientId)
        {
            tasks = tasks.Where(task => task.ClientId == clientId);
        }

        if (filter.CaseId is { } caseId)
        {
            tasks = tasks.Where(task => task.CaseId == caseId);
        }

        if (filter.DueBy is { } dueBy)
        {
            tasks = tasks.Where(task => task.Status == TaskItemStatus.Open && task.DueOn != null && task.DueOn <= dueBy);
        }

        var total = await tasks.CountAsync(cancellationToken);
        var sorted = (filter.Sort, filter.Descending) switch
        {
            (TaskSort.CreatedAt, false) => tasks.OrderBy(task => task.CreatedAt),
            (TaskSort.CreatedAt, true) => tasks.OrderByDescending(task => task.CreatedAt),
            (TaskSort.Title, false) => tasks.OrderBy(task => task.Title),
            (TaskSort.Title, true) => tasks.OrderByDescending(task => task.Title),
            (_, false) => tasks.OrderBy(task => task.DueOn == null).ThenBy(task => task.DueOn),
            (_, true) => tasks.OrderBy(task => task.DueOn == null).ThenByDescending(task => task.DueOn),
        };

        var items = await Rows(sorted.ThenBy(task => task.CreatedAt).ThenBy(task => task.Id).Skip(filter.Skip).Take(filter.Take)).ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<TaskRow?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Rows(db.Set<TaskItem>().AsNoTracking().Where(task => task.Id == id)).SingleOrDefaultAsync(cancellationToken);

    public Task<TaskItem?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<TaskItem>().SingleOrDefaultAsync(task => task.Id == id, cancellationToken);

    public async Task<(int Open, int Overdue)> CountOpenAsync(Guid userId, DateOnly today, CancellationToken cancellationToken)
    {
        var open = db.Set<TaskItem>().AsNoTracking().Where(task => task.AssigneeUserId == userId && task.Status == TaskItemStatus.Open);
        return (await open.CountAsync(cancellationToken), await open.CountAsync(task => task.DueOn != null && task.DueOn < today, cancellationToken));
    }

    public async Task<IReadOnlyList<ActivityRow>> ActivitiesAsync(Guid clientId, DateTimeOffset? before, int take, CancellationToken cancellationToken)
    {
        var activities = db.Set<ClientActivity>().AsNoTracking().Where(activity => activity.ClientId == clientId);
        if (before is { } instant)
        {
            activities = activities.Where(activity => activity.OccurredAt < instant);
        }

        var users = db.Set<User>();
        var people = db.Set<Person>();
        return await activities
            .OrderByDescending(activity => activity.OccurredAt)
            .Take(take)
            .Select(activity => new ActivityRow(
                activity.Id,
                activity.ClientId,
                activity.Kind,
                activity.Text,
                activity.OccurredAt,
                activity.AuthorUserId,
                users.Where(user => user.Id == activity.AuthorUserId)
                    .Join(people, user => user.PersonId, person => person.Id, (user, person) => person.FirstName + " " + person.LastName)
                    .FirstOrDefault() ?? string.Empty))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TaskRow>> ClientTasksAsync(Guid clientId, Guid? visibleTo, DateTimeOffset? before, int take, CancellationToken cancellationToken)
    {
        var tasks = Visible(visibleTo).Where(task => task.ClientId == clientId);
        if (before is { } instant)
        {
            tasks = tasks.Where(task => (task.CompletedAt ?? task.CreatedAt) < instant);
        }

        return await Rows(tasks.OrderByDescending(task => task.CompletedAt ?? task.CreatedAt).Take(take)).ToListAsync(cancellationToken);
    }

    public Task<ClientActivity?> FindActivityAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<ClientActivity>().SingleOrDefaultAsync(activity => activity.Id == id, cancellationToken);

    public async Task<IReadOnlyList<(Guid Id, string FullName)>> AssigneesAsync(CancellationToken cancellationToken)
    {
        var staff = await (from user in db.Set<User>().AsNoTracking()
                           join person in db.Set<Person>() on user.PersonId equals person.Id
                           where user.IsActive && EF.Property<List<UserRole>>(user, "roles")
                               .Any(role => role.Role == SharedKernel.Tenancy.TenantRole.Administrator || role.Role == SharedKernel.Tenancy.TenantRole.Employee)
                           orderby person.LastName, person.FirstName
                           select new { user.Id, FullName = person.FirstName + " " + person.LastName })
            .ToListAsync(cancellationToken);
        return [.. staff.Select(item => (item.Id, item.FullName))];
    }

    public void Add(TaskItem task) => db.Set<TaskItem>().Add(task);

    public void Remove(TaskItem task) => db.Set<TaskItem>().Remove(task);

    public void Add(ClientActivity activity) => db.Set<ClientActivity>().Add(activity);

    public void Remove(ClientActivity activity) => db.Set<ClientActivity>().Remove(activity);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public ValueTask DisposeAsync() => db.DisposeAsync();

    private IQueryable<TaskItem> Visible(Guid? visibleTo)
    {
        var tasks = db.Set<TaskItem>().AsNoTracking();
        return visibleTo is { } userId ? tasks.Where(task => task.AssigneeUserId == userId || task.CreatedByUserId == userId) : tasks;
    }

    private IQueryable<TaskRow> Rows(IQueryable<TaskItem> tasks)
    {
        var users = db.Set<User>();
        var people = db.Set<Person>();
        var cases = db.Set<Case>();
        return tasks.Select(task => new TaskRow(
            task.Id,
            task.Title,
            task.Notes,
            task.DueOn,
            task.Status,
            task.AssigneeUserId,
            users.Where(user => user.Id == task.AssigneeUserId)
                .Join(people, user => user.PersonId, person => person.Id, (user, person) => person.FirstName + " " + person.LastName)
                .FirstOrDefault() ?? string.Empty,
            task.ClientId,
            people.Where(person => person.Id == task.ClientId).Select(person => person.FirstName + " " + person.LastName).FirstOrDefault(),
            task.CaseId,
            cases.Where(@case => @case.Id == task.CaseId).Select(@case => @case.Number).FirstOrDefault(),
            task.CreatedByUserId,
            users.Where(user => user.Id == task.CreatedByUserId)
                .Join(people, user => user.PersonId, person => person.Id, (user, person) => person.FirstName + " " + person.LastName)
                .FirstOrDefault() ?? string.Empty,
            task.CreatedAt,
            task.CompletedAt));
    }
}
