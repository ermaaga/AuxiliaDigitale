using Auxilia.Application.Abstractions.Settings;
using Auxilia.Application.Cases;
using Auxilia.Application.Directory.Public;
using Auxilia.Application.Engagement.Public;
using Auxilia.Application.Tests.Identity;
using Auxilia.Application.Tests.Platform;
using Auxilia.Domain.Cases;
using Auxilia.SharedKernel.Results;

using NSubstitute;

using Case = Auxilia.Domain.Cases.Case;

namespace Auxilia.Application.Tests.Cases;

public sealed class CaseExpiryJobTests : IAsyncDisposable
{
    private static readonly Guid Client = Guid.CreateVersion7();
    private static readonly Guid ClientUser = Guid.CreateVersion7();
    private static readonly Guid OtherClient = Guid.CreateVersion7();
    private static readonly DateOnly Today = new(2026, 9, 30);

    private readonly InMemoryCases data = new();
    private readonly IClientDirectory clients = Substitute.For<IClientDirectory>();
    private readonly INotificationSender notifications = Substitute.For<INotificationSender>();
    private readonly ISettingsProvider settings = Substitute.For<ISettingsProvider>();
    private readonly ManualTimeProvider clock = new();
    private readonly Dictionary<Guid, bool> clientStatus = [];
    private readonly List<(Guid User, NotificationMessage Message)> sent = [];
    private readonly CaseExpiryJob job;
    private readonly Guid service;

    public CaseExpiryJobTests()
    {
        service = data.AddService(100m, null);
        data.ClientUsers[Client] = ClientUser;
        Enabled(true);
        settings.GetAsync(CasesSettings.ExpiryExpiringDays, Arg.Any<CancellationToken>()).Returns(7);
        clients.UpdateStatusAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            clientStatus[call.ArgAt<Guid>(0)] = call.ArgAt<bool>(1);
            return Result.Success();
        });
        notifications.NotifyUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<NotificationMessage>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                sent.AddRange(call.ArgAt<IReadOnlyCollection<Guid>>(0).Select(user => (user, call.ArgAt<NotificationMessage>(1))));
                return Task.CompletedTask;
            });
        job = new CaseExpiryJob(ManagerHarness.Runner(), data, clients, notifications, settings, SessionSettings.Tenant(), clock);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => data.DisposeAsync();

    private void Enabled(bool enabled) => settings.GetAsync(CasesSettings.ExpiryEnabled, Arg.Any<CancellationToken>()).Returns(enabled);

    private Case Add(Guid clientId, DateOnly? dueOn, bool completed = false)
    {
        var now = clock.GetUtcNow();
        var @case = Case.Open(
            Guid.CreateVersion7(), new CaseOpening($"2026-{data.Cases.Count + 1:D5}", clientId, service, 100m, "EUR", null, Today.AddDays(-30), dueOn, "{}"), null, now).Value;
        if (completed)
        {
            @case.Advance(null, null, now);
            @case.Advance(null, null, now);
            @case.Complete(0m, false, null, null, now).IsSuccess.ShouldBeTrue();
        }

        data.Add(@case);
        return @case;
    }

    [Fact]
    public async Task Run_DeactivatesExpiredCases_RecomputesTheClient_AndTellsTheClients()
    {
        var expired = Add(Client, null, completed: true);
        var expiring = Add(Client, Today.AddDays(3));
        var later = Add(OtherClient, Today.AddDays(30));

        var result = await job.RunAsync(Ct);

        result.Value.ShouldBe("expired 1, expiring 1");
        (expired.IsActive, expiring.IsActive, later.IsActive).ShouldBe((false, true, true));
        clientStatus.ShouldBe(new Dictionary<Guid, bool> { [Client] = true });
        sent.Select(item => (item.User, item.Message.Kind, item.Message.EntityId))
            .ShouldBe([(ClientUser, NotificationKinds.CaseExpired, expired.Id), (ClientUser, NotificationKinds.CaseExpiring, expiring.Id)]);
        sent[1].Message.Parameters.ShouldBe(new Dictionary<string, string>
        {
            ["service"] = "Service 0", ["number"] = expiring.Number, ["date"] = "03/10/2026", ["days"] = "3",
        });
    }

    [Fact]
    public async Task Run_TellsAboutAnExpiringCaseOnceADay()
    {
        var expiring = Add(Client, Today.AddDays(2));

        (await job.RunAsync(Ct)).Value.ShouldBe("expired 0, expiring 1");
        (await job.RunAsync(Ct)).Value.ShouldBe("expired 0, expiring 0");
        clock.Advance(TimeSpan.FromDays(1));
        (await job.RunAsync(Ct)).Value.ShouldBe("expired 0, expiring 1");

        sent.Count.ShouldBe(2);
        expiring.ExpiryNotifiedOn.ShouldBe(Today.AddDays(1));
    }

    [Fact]
    public async Task Run_ClientsWithoutAnAccountAreNotNotified_AndTheLastActiveCaseMakesTheClientInactive()
    {
        var expired = Add(OtherClient, null, completed: true);

        (await job.RunAsync(Ct)).Value.ShouldBe("expired 1, expiring 0");

        expired.IsActive.ShouldBeFalse();
        clientStatus[OtherClient].ShouldBeFalse();
        sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Run_WhenDisabled_ChangesNothing()
    {
        Enabled(false);
        var expired = Add(Client, null, completed: true);

        (await job.RunAsync(Ct)).Value.ShouldBe("disabled");

        expired.IsActive.ShouldBeTrue();
        sent.ShouldBeEmpty();
        (job.Code, job.SuggestedFrequency).ShouldBe(("cases.expiry", "daily"));
    }
}
