using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Engagement;
using Auxilia.Application.Directory.Public;
using Auxilia.Application.Engagement;
using Auxilia.Application.Tests.Identity;
using Auxilia.Application.Tests.Platform;
using Auxilia.Contracts.Engagement;
using Auxilia.Contracts.Realtime;
using Auxilia.Diagnostics;
using Auxilia.Domain.Engagement;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

using NSubstitute;

namespace Auxilia.Application.Tests.Engagement;

public sealed class RequestManagerTests : IAsyncDisposable
{
    private static readonly Guid Admin = Guid.CreateVersion7();
    private static readonly Guid Employee = Guid.CreateVersion7();
    private static readonly Guid OtherEmployee = Guid.CreateVersion7();
    private static readonly Guid ClientUser = Guid.CreateVersion7();
    private static readonly Guid Client = Guid.CreateVersion7();
    private static readonly Guid LoneClientUser = Guid.CreateVersion7();
    private static readonly Guid LoneClient = Guid.CreateVersion7();

    private readonly InMemoryRequests data = new();
    private readonly IClientDirectory clients = Substitute.For<IClientDirectory>();
    private readonly IAccessGuard guard = Substitute.For<IAccessGuard>();
    private readonly IPermissionAccess permissions = Substitute.For<IPermissionAccess>();
    private readonly ICurrentUser caller = Substitute.For<ICurrentUser>();
    private readonly RecordingRealtimeNotifier notifier = new();
    private readonly RecordingNotificationSender notifications = new();
    private readonly ManualTimeProvider clock = new();
    private readonly RequestManager manager;
    private readonly RequestQueryService query;

    public RequestManagerTests()
    {
        data.Persons[ClientUser] = Client;
        data.Persons[LoneClientUser] = LoneClient;
        clients.FindAsync(Client, Arg.Any<CancellationToken>()).Returns(new ClientSummary(Client, "Mario Rossi", Employee));
        clients.FindAsync(LoneClient, Arg.Any<CancellationToken>()).Returns(new ClientSummary(LoneClient, "Anna Verdi", null));
        guard.EnsureAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        permissions.HasAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var policy = new RequestAccessPolicy(caller);
        manager = new RequestManager(ManagerHarness.Runner(), data, clients, guard, policy, notifier, notifications, clock);
        query = new RequestQueryService(data, policy, permissions);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => data.DisposeAsync();

    private void CallAs(Guid userId, params TenantRole[] roles)
    {
        caller.ActorType.Returns(ActorType.User);
        caller.UserId.Returns(userId);
        caller.Roles.Returns(roles);
    }

    private async Task<Guid> CreateAsync(Guid userId, TenantRole role, bool? askMyOperator = null)
    {
        CallAs(userId, role);
        return (await manager.CreateAsync(new CreateRequestRequest("Support", "Invoice", "Where is it?", askMyOperator), Ct)).Value;
    }

    private Request Stored(Guid id) => data.Requests.Single(request => request.Id == id);

    [Fact]
    public async Task Client_AsksTheEmployeeInCharge_ByDefault_OrTheOffice()
    {
        var toEmployee = await CreateAsync(ClientUser, TenantRole.Client);
        Stored(toEmployee).RecipientUserId.ShouldBe(Employee);
        notifier.Pushes.ShouldHaveSingleItem().Target.ShouldBe($"user:{Employee}");

        notifier.Pushes.Clear();
        var toOffice = await CreateAsync(ClientUser, TenantRole.Client, askMyOperator: false);
        Stored(toOffice).RecipientUserId.ShouldBeNull();
        notifier.Pushes.ShouldHaveSingleItem().Target.ShouldBe("role:Administrator");
        notifications.Sent[^1].Target.ShouldBe("role:Administrator");
        (notifications.Sent[^1].Message.Kind, notifications.Sent[^1].Message.Parameters["subject"]).ShouldBe(("request.created", "Invoice"));

        // Without an employee in charge the office answers (legacy).
        var lone = await CreateAsync(LoneClientUser, TenantRole.Client);
        Stored(lone).RecipientUserId.ShouldBeNull();
    }

    [Fact]
    public async Task Employees_AskTheOffice_AdministratorsDoNotCreate()
    {
        var id = await CreateAsync(Employee, TenantRole.Employee, askMyOperator: true);
        (Stored(id).SenderUserId, Stored(id).RecipientUserId).ShouldBe((Employee, (Guid?)null));

        CallAs(Admin, TenantRole.Administrator);
        (await manager.CreateAsync(new CreateRequestRequest("Support", "x", "y", null), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.PermissionDenied);

        CallAs(ClientUser, TenantRole.Client);
        (await manager.CreateAsync(new CreateRequestRequest("Urgent", "", "y", null), Ct)).Error!.ValidationErrors.Keys
            .ShouldBe(["type", "subject"], ignoreOrder: true);
    }

    [Fact]
    public async Task Replies_AlwaysTellTheOtherParty_AndCloseIsForParticipants()
    {
        var id = await CreateAsync(ClientUser, TenantRole.Client);
        notifier.Pushes.Clear();

        CallAs(Employee, TenantRole.Employee);
        (await manager.ReplyAsync(id, new ReplyToRequestRequest("It was sent"), Ct)).IsSuccess.ShouldBeTrue();
        Stored(id).Status.ShouldBe(RequestStatus.Responded);
        notifier.Pushes.ShouldHaveSingleItem().Target.ShouldBe($"user:{ClientUser}");
        ((RequestChangedEvent)notifier.Pushes[0].Payload).Change.ShouldBe("Replied");

        notifier.Pushes.Clear();
        CallAs(ClientUser, TenantRole.Client);
        (await manager.ReplyAsync(id, new ReplyToRequestRequest("Thanks"), Ct)).IsSuccess.ShouldBeTrue();
        notifier.Pushes.ShouldHaveSingleItem().Target.ShouldBe($"user:{Employee}");

        CallAs(OtherEmployee, TenantRole.Employee);
        (await manager.ReplyAsync(id, new ReplyToRequestRequest("Me too"), Ct)).Error!.Code.ShouldBe(EventCodes.Requests.RequestNotFound);
        (await manager.CloseAsync(id, Ct)).Error!.Code.ShouldBe(EventCodes.Requests.RequestNotFound);

        CallAs(ClientUser, TenantRole.Client);
        (await manager.CloseAsync(id, Ct)).IsSuccess.ShouldBeTrue();
        (await manager.ReplyAsync(id, new ReplyToRequestRequest("again"), Ct)).Error!.Code.ShouldBe(EventCodes.Requests.RequestIsClosed);
    }

    [Fact]
    public async Task Administrators_AnswerOfficeRequests_AndDelete()
    {
        var id = await CreateAsync(Employee, TenantRole.Employee);
        notifier.Pushes.Clear();

        CallAs(Admin, TenantRole.Administrator);
        (await manager.ReplyAsync(id, new ReplyToRequestRequest("Done"), Ct)).IsSuccess.ShouldBeTrue();
        notifier.Pushes.ShouldHaveSingleItem().Target.ShouldBe($"user:{Employee}");

        CallAs(Employee, TenantRole.Employee);
        (await manager.DeleteAsync(id, Ct)).Error!.Code.ShouldBe(EventCodes.Identity.PermissionDenied);

        CallAs(Admin, TenantRole.Administrator);
        (await manager.DeleteAsync(id, Ct)).IsSuccess.ShouldBeTrue();
        data.Deleted.ShouldContain(id);
        (await query.GetAsync(id, Ct)).Error!.Code.ShouldBe(EventCodes.Requests.RequestNotFound);
    }

    [Fact]
    public async Task Detail_ShowsTheThread_AndWhatTheCallerMayDo()
    {
        var id = await CreateAsync(ClientUser, TenantRole.Client);
        CallAs(Employee, TenantRole.Employee);
        await manager.ReplyAsync(id, new ReplyToRequestRequest("Here it is"), Ct);

        var detail = (await query.GetAsync(id, Ct)).Value;
        (detail.Status, detail.Type, detail.Recipient!.UserId, detail.CanReply, detail.CanClose, detail.CanDelete)
            .ShouldBe(("Responded", "Support", Employee, true, true, false));
        detail.Messages.Select(message => (message.Body, message.Mine)).ShouldBe([("Where is it?", false), ("Here it is", true)]);

        CallAs(Admin, TenantRole.Administrator);
        (await query.GetAsync(id, Ct)).Value.CanDelete.ShouldBeTrue();
    }

    [Fact]
    public async Task Inbox_DecidesTheBoxByRole_AndValidatesTheQuery()
    {
        CallAs(ClientUser, TenantRole.Client);
        await query.ListAsync(new RequestListQuery(null, null, null, null, 1, 25), Ct);
        data.LastFilter!.Box.ShouldBe(new RequestBox(null, false, ClientUser, false));

        CallAs(Employee, TenantRole.Employee);
        await query.ListAsync(new RequestListQuery(null, "Pending", "Support", "status", 1, 25), Ct);
        (data.LastFilter.Box, data.LastFilter.Status, data.LastFilter.Type, data.LastFilter.Sort, data.LastFilter.Descending)
            .ShouldBe((new RequestBox(Employee, false, null, false), (RequestStatus?)RequestStatus.Pending, (RequestType?)RequestType.Support, RequestSort.Status, false));
        (await query.ListAsync(new RequestListQuery("all", null, null, null, 1, 25), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.PermissionDenied);

        CallAs(Admin, TenantRole.Administrator);
        await query.ListAsync(new RequestListQuery(null, null, null, null, 1, 25), Ct);
        data.LastFilter.Box.ShouldBe(new RequestBox(Admin, true, null, false));
        await query.ListAsync(new RequestListQuery("all", null, null, null, 1, 25), Ct);
        data.LastFilter.Box.Everything.ShouldBeTrue();

        var invalid = await query.ListAsync(new RequestListQuery("trash", "Open", "Urgent", "subject", 0, 101), Ct);
        invalid.Error!.ValidationErrors.Keys.ShouldBe(["page", "pageSize", "sort", "status", "type", "box"], ignoreOrder: true);
    }
}

internal sealed class InMemoryRequests : IRequestDataFactory, IRequestData
{
    public List<Request> Requests { get; } = [];

    public HashSet<Guid> Deleted { get; } = [];

    /// <summary>User → person.</summary>
    public Dictionary<Guid, Guid> Persons { get; } = [];

    public RequestFilter? LastFilter { get; private set; }

    public Task<IRequestData> OpenAsync(CancellationToken cancellationToken) => Task.FromResult<IRequestData>(this);

    /// <summary>Applies no filter (the SQL is tested on PostgreSQL); records what was asked.</summary>
    public Task<(IReadOnlyList<RequestRow> Items, int Total)> PageAsync(RequestFilter filter, CancellationToken cancellationToken)
    {
        LastFilter = filter;
        var rows = Live()
            .Select(request => new RequestRow(request.Id, request.Type, request.Subject, request.Status, request.SenderUserId, "Sender", request.RecipientUserId,
                request.RecipientUserId is null ? null : "Recipient", request.SentAt, request.LastMessageAt, request.Messages.Count))
            .ToArray();
        return Task.FromResult<(IReadOnlyList<RequestRow>, int)>((rows, rows.Length));
    }

    public Task<Request?> FindAsync(Guid id, bool readOnly, CancellationToken cancellationToken) =>
        Task.FromResult(Live().SingleOrDefault(request => request.Id == id));

    public Task<IReadOnlyDictionary<Guid, string>> UserNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(userIds.ToDictionary(id => id, id => "User " + id.ToString("N")[..4]));

    public Task<Guid?> PersonOfUserAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Persons.TryGetValue(userId, out var person) ? (Guid?)person : null);

    public void Add(Request request) => Requests.Add(request);

    public void Remove(Request request) => Deleted.Add(request.Id);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private IEnumerable<Request> Live() => Requests.Where(request => !Deleted.Contains(request.Id));
}
