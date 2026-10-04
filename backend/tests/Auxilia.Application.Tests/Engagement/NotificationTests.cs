using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Engagement;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Application.Engagement;
using Auxilia.Application.Engagement.Public;
using Auxilia.Application.Localization.Public;
using Auxilia.Application.Messaging.Public;
using Auxilia.Application.Tests.Identity;
using Auxilia.Application.Tests.Platform;
using Auxilia.Contracts.Engagement;
using Auxilia.Contracts.Realtime;
using Auxilia.Diagnostics;
using Auxilia.Domain.Engagement;
using Auxilia.Domain.Messaging;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

using NSubstitute;

namespace Auxilia.Application.Tests.Engagement;

public sealed class NotificationTests : IAsyncDisposable
{
    private static readonly Guid Actor = Guid.CreateVersion7();
    private static readonly Guid Anna = Guid.CreateVersion7();
    private static readonly Guid Luca = Guid.CreateVersion7();
    private static readonly Guid Admin = Guid.CreateVersion7();

    private readonly InMemoryNotifications data = new();
    private readonly RecordingRealtimeNotifier notifier = new();
    private readonly IMessageDispatcher messages = Substitute.For<IMessageDispatcher>();
    private readonly ILocalizer localizer = Substitute.For<ILocalizer>();
    private readonly ICurrentUser caller = Substitute.For<ICurrentUser>();
    private readonly ManualTimeProvider clock = new();
    private readonly NotificationSender sender;
    private readonly NotificationManager manager;
    private readonly NotificationQueryService query;

    public NotificationTests()
    {
        CallAs(Actor);
        data.Recipients.AddRange([new NotificationRecipient(Anna, "anna@example.test", "it"), new NotificationRecipient(Luca, null, "en"), new NotificationRecipient(Actor, "me@example.test", "it")]);
        data.Administrators.Add(new NotificationRecipient(Admin, "admin@example.test", "en"));
        localizer.GetAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, object?>?>(), Arg.Any<CancellationToken>())
            .Returns(call => $"{call.ArgAt<string>(0)}:{call.ArgAt<string?>(1)}");
        messages.QueueAsync(Arg.Any<OutboundMessageRequest>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Guid.CreateVersion7()));
        var settings = new ConfigurableSettings();
        settings.Values["auth.appBaseUrl"] = "https://app.example.test";
        sender = new NotificationSender(ManagerHarness.Runner(), data, notifier, messages, localizer, settings, SessionSettings.Tenant(), caller, clock);
        manager = new NotificationManager(ManagerHarness.Runner(), data, caller, clock);
        query = new NotificationQueryService(data, caller);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static NotificationMessage Approved(Guid id) =>
        new(NotificationKinds.AppointmentApproved, id, new Dictionary<string, string>(StringComparer.Ordinal) { ["when"] = "01/10/2026 09:00" });

    public ValueTask DisposeAsync() => data.DisposeAsync();

    private void CallAs(Guid userId)
    {
        caller.ActorType.Returns(ActorType.User);
        caller.UserId.Returns(userId);
        caller.Roles.Returns([TenantRole.Employee]);
    }

    [Fact]
    public async Task Send_StoresOnePerRecipient_PushesThem_AndSkipsTheActor()
    {
        var appointment = Guid.CreateVersion7();

        await sender.NotifyUsersAsync([Anna, Luca, Actor, Anna], Approved(appointment), Ct);

        data.Notifications.Select(notification => notification.UserId).ShouldBe([Anna, Luca], ignoreOrder: true);
        var stored = data.Notifications.First();
        (stored.Kind, stored.EntityId, stored.Link, stored.Parameters).ShouldBe(
            ("appointment.approved", (Guid?)appointment, $"/appointments?open={appointment}", "{\"when\":\"01/10/2026 09:00\"}"));
        notifier.Pushes.Select(push => push.Target).ShouldBe([$"user:{Anna}", $"user:{Luca}"], ignoreOrder: true);
        notifier.Pushes.ShouldAllBe(push => push.EventName == RealtimeEvents.NotificationReceived);
        await messages.DidNotReceiveWithAnyArgs().QueueAsync(default!, Ct);
    }

    [Fact]
    public async Task Preferences_DecideInAppAndEmail()
    {
        data.Preferences.Add(new NotificationPreference(Anna, NotificationKinds.AppointmentApproved, inApp: false, email: true));
        data.Preferences.Add(new NotificationPreference(Luca, NotificationKinds.AppointmentApproved, inApp: true, email: true));
        var appointment = Guid.CreateVersion7();

        await sender.NotifyUsersAsync([Anna, Luca], Approved(appointment), Ct);

        // Anna: e-mail only; Luca: in-app only (no e-mail address).
        data.Notifications.ShouldHaveSingleItem().UserId.ShouldBe(Luca);
        await messages.Received(1).QueueAsync(
            Arg.Is<OutboundMessageRequest>(request =>
                request.Recipient == "anna@example.test" && request.TemplateCode == MessageTemplates.Notification && request.Language == "it"
                && request.Purpose == MessagePurpose.Notification
                && (string)request.Model["title"]! == "notifications.appointment.approved.title:it"
                && (string)request.Model["link"]! == $"https://app.example.test/acme/appointments?open={appointment}"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotifyRole_ReachesEveryActiveUserWithTheRole()
    {
        var registration = Guid.CreateVersion7();

        await sender.NotifyRoleAsync(TenantRole.Administrator, new NotificationMessage(NotificationKinds.RegistrationRequested, registration, new Dictionary<string, string>()), Ct);

        var stored = data.Notifications.ShouldHaveSingleItem();
        (stored.UserId, stored.Link).ShouldBe((Admin, $"/registrations?open={registration}"));
    }

    [Fact]
    public async Task ReadDeleteAndList_AreForTheOwner()
    {
        await sender.NotifyUsersAsync([Anna], Approved(Guid.CreateVersion7()), Ct);
        clock.Advance(TimeSpan.FromMinutes(1));
        await sender.NotifyUsersAsync([Anna], new NotificationMessage(NotificationKinds.AppointmentDeleted, Guid.CreateVersion7(), new Dictionary<string, string>()), Ct);
        var (first, second) = (data.Notifications[0].Id, data.Notifications[1].Id);

        CallAs(Luca);
        (await manager.MarkReadAsync(first, Ct)).Error!.Code.ShouldBe(EventCodes.Notifications.NotificationNotFound);
        (await manager.DeleteAsync(first, Ct)).Error!.Code.ShouldBe(EventCodes.Notifications.NotificationNotFound);
        (await query.UnreadCountAsync(Ct)).Value.Count.ShouldBe(0);

        CallAs(Anna);
        (await query.UnreadCountAsync(Ct)).Value.Count.ShouldBe(2);
        var page = (await query.ListAsync(new NotificationListQuery(false, 1, 10), Ct)).Value;
        page.Items.Select(item => (item.Id, item.Link)).ShouldBe([(second, "/appointments"), (first, page.Items[1].Link)]);
        (await manager.MarkReadAsync(first, Ct)).IsSuccess.ShouldBeTrue();
        (await manager.MarkReadAsync(first, Ct)).IsSuccess.ShouldBeTrue();
        (await query.ListAsync(new NotificationListQuery(true, 1, 10), Ct)).Value.Items.ShouldHaveSingleItem().Id.ShouldBe(second);

        (await manager.MarkAllReadAsync(Ct)).IsSuccess.ShouldBeTrue();
        (await query.UnreadCountAsync(Ct)).Value.Count.ShouldBe(0);
        (await manager.DeleteAsync(second, Ct)).IsSuccess.ShouldBeTrue();
        data.Notifications.Select(notification => notification.Id).ShouldBe([first]);

        (await query.ListAsync(new NotificationListQuery(false, 0, 101), Ct)).Error!.ValidationErrors.Keys.ShouldBe(["page", "pageSize"], ignoreOrder: true);
    }

    [Fact]
    public async Task Preferences_ListEveryKindWithDefaults_AndSaveOnlyKnownKinds()
    {
        CallAs(Anna);
        (await query.PreferencesAsync(Ct)).Value.ShouldAllBe(preference => preference.InApp && !preference.Email);

        (await manager.SetPreferencesAsync(new SetNotificationPreferencesRequest([new("request.replied", false, true)]), Ct)).IsSuccess.ShouldBeTrue();
        (await manager.SetPreferencesAsync(new SetNotificationPreferencesRequest([new("request.replied", true, true)]), Ct)).IsSuccess.ShouldBeTrue();
        var saved = (await query.PreferencesAsync(Ct)).Value;
        saved.Count.ShouldBe(NotificationKinds.All.Count);
        saved.Single(preference => preference.Kind == "request.replied").ShouldBe(new NotificationPreferenceResponse("request.replied", true, true));
        data.Preferences.ShouldHaveSingleItem();

        (await manager.SetPreferencesAsync(new SetNotificationPreferencesRequest([new("workout.plan", true, false)]), Ct))
            .Error!.Code.ShouldBe(EventCodes.Notifications.NotificationPreferencesInvalid);
        (await manager.SetPreferencesAsync(new SetNotificationPreferencesRequest([new("request.replied", true, false), new("request.replied", false, false)]), Ct))
            .Error!.Code.ShouldBe(EventCodes.Notifications.NotificationPreferencesInvalid);
    }

    [Fact]
    public void Links_FollowTheKind()
    {
        var id = Guid.CreateVersion7();
        NotificationKinds.Link(NotificationKinds.RequestReplied, id).ShouldBe($"/requests?open={id}");
        NotificationKinds.Link(NotificationKinds.AppointmentScheduled, null).ShouldBeNull();
        NotificationKinds.Link("unknown.kind", id).ShouldBeNull();
    }
}

internal sealed class InMemoryNotifications : INotificationDataFactory, INotificationData
{
    public List<Notification> Notifications { get; } = [];

    public List<NotificationPreference> Preferences { get; } = [];

    public List<NotificationRecipient> Recipients { get; } = [];

    public List<NotificationRecipient> Administrators { get; } = [];

    public Task<INotificationData> OpenAsync(CancellationToken cancellationToken) => Task.FromResult<INotificationData>(this);

    public Task<(IReadOnlyList<Notification> Items, int Total)> PageAsync(Guid userId, bool unreadOnly, int skip, int take, CancellationToken cancellationToken)
    {
        var mine = Notifications.Where(item => item.UserId == userId && (!unreadOnly || !item.IsRead)).OrderByDescending(item => item.CreatedAt).ToArray();
        return Task.FromResult<(IReadOnlyList<Notification>, int)>((mine.Skip(skip).Take(take).ToArray(), mine.Length));
    }

    public Task<int> UnreadCountAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Notifications.Count(item => item.UserId == userId && !item.IsRead));

    public Task<Notification?> FindAsync(Guid userId, Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Notifications.SingleOrDefault(item => item.Id == id && item.UserId == userId));

    public Task<int> MarkAllReadAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken) =>
        Task.FromResult(Notifications.Where(item => item.UserId == userId).Count(item => item.MarkRead(now)));

    public Task<IReadOnlyList<NotificationRecipient>> RecipientsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<NotificationRecipient>>(Recipients.Where(recipient => userIds.Contains(recipient.UserId)).ToArray());

    public Task<IReadOnlyList<NotificationRecipient>> RecipientsWithRoleAsync(TenantRole role, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<NotificationRecipient>>(role == TenantRole.Administrator ? Administrators : []);

    public Task<IReadOnlyDictionary<Guid, NotificationPreference>> PreferencesAsync(IReadOnlyCollection<Guid> userIds, string kind, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, NotificationPreference>>(
            Preferences.Where(preference => userIds.Contains(preference.UserId) && preference.Kind == kind).ToDictionary(preference => preference.UserId));

    public Task<IReadOnlyList<NotificationPreference>> PreferencesOfAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<NotificationPreference>>(Preferences.Where(preference => preference.UserId == userId).ToArray());

    public void Add(Notification notification) => Notifications.Add(notification);

    public void Add(NotificationPreference preference) => Preferences.Add(preference);

    public void Remove(Notification notification) => Notifications.Remove(notification);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
