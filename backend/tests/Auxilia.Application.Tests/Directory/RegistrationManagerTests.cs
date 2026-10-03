using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Captcha;
using Auxilia.Application.Abstractions.Directory;
using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Abstractions.Realtime;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Directory;
using Auxilia.Application.Identity.Public;
using Auxilia.Application.Localization.Public;
using Auxilia.Application.Messaging.Public;
using Auxilia.Application.Tests.Identity;
using Auxilia.Application.Tests.Platform;
using Auxilia.Contracts.Directory;
using Auxilia.Contracts.Realtime;
using Auxilia.Diagnostics;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

namespace Auxilia.Application.Tests.Directory;

public sealed class RegistrationManagerTests : IAsyncDisposable
{
    private const string Captcha = "solved";

    private static readonly Guid Reviewer = Guid.CreateVersion7();
    private static readonly Guid DefaultEmployee = Guid.CreateVersion7();
    private static readonly RegistrationCaller Site = new("site", null);

    private readonly InMemoryRegistrationData registrations = new();
    private readonly InMemoryClientData clients = new();
    private readonly IUserAccounts accounts = Substitute.For<IUserAccounts>();
    private readonly IClientApplications clientApplications = Substitute.For<IClientApplications>();
    private readonly ICaptchaVerifier captcha = Substitute.For<ICaptchaVerifier>();
    private readonly FakeSettings settings = new();
    private readonly IModuleAccess modules = Substitute.For<IModuleAccess>();
    private readonly ITenantLanguages languages = Substitute.For<ITenantLanguages>();
    private readonly IMessageDispatcher messages = Substitute.For<IMessageDispatcher>();
    private readonly IRealtimeNotifier notifier = Substitute.For<IRealtimeNotifier>();
    private readonly ICurrentUser caller = Substitute.For<ICurrentUser>();
    private readonly ManualTimeProvider clock = new();
    private readonly RegistrationManager manager;
    private readonly RegistrationQueryService query;

    public RegistrationManagerTests()
    {
        settings.Set(DirectorySettings.RegistrationEnabled, true);
        clientApplications.AuthenticateAsync("site", null, Arg.Any<CancellationToken>())
            .Returns(new CallingClient("site", ClientApplicationType.Integration, false, "altcha"));
        captcha.Provider.Returns("altcha");
        captcha.VerifyAsync(Captcha, Arg.Any<CancellationToken>()).Returns(true);
        captcha.CreateChallengeAsync(Arg.Any<CancellationToken>()).Returns(new AltchaChallenge("SHA-256", "c", "s", "sig", 100_000));
        modules.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new TenantModules(new Dictionary<string, TenantRole[]> { [DirectoryModule.ModuleCode] = [TenantRole.Administrator, TenantRole.Employee] }));
        languages.IsActiveAsync("en", Arg.Any<CancellationToken>()).Returns(true);
        messages.QueueAsync(Arg.Any<OutboundMessageRequest>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Guid.CreateVersion7()));
        accounts.CreateAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string?>(), TenantRole.Client, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(Guid.CreateVersion7()));
        accounts.SendActivationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        caller.ActorType.Returns(ActorType.User);
        caller.UserId.Returns(Reviewer);
        caller.Roles.Returns([TenantRole.Employee]);
        var tenant = Substitute.For<ITenantContext>();
        tenant.Tenant.Returns(new TenantInfo(Guid.CreateVersion7(), "demo", TenantStatus.Active, "it", "Europe/Rome"));

        manager = new RegistrationManager(
            ManagerHarness.Runner(), registrations, clients, accounts, clientApplications, [captcha], settings, modules, languages, messages, notifier,
            tenant, caller, clock, NullLogger<RegistrationManager>.Instance);
        query = new RegistrationQueryService(registrations);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await registrations.DisposeAsync();
        await clients.DisposeAsync();
    }

    private static SubmitRegistrationRequest Request(string email = "Mario.Rossi@Example.test", string fiscalCode = "RSSMRA80A01H501U", string? language = null) =>
        new("Mario", "Rossi", email, "333 1234567", new DateOnly(1980, 1, 1), fiscalCode, true, "2026-01", language, Captcha);

    private async Task<Guid> SubmitAsync(SubmitRegistrationRequest? request = null) =>
        (await manager.SubmitAsync(request ?? Request(), Site, Ct)).Value.Id;

    [Fact]
    public async Task Submit_StoresAPendingRequest_InTheDefaultLanguage_WithoutE_mailOrPushByDefault()
    {
        var id = await SubmitAsync();

        var stored = registrations.Requests.Single();
        (stored.Id, stored.Email, stored.Language, stored.ClientApplication, stored.Status).ShouldBe((id, "mario.rossi@example.test", "it", "site", RegistrationStatus.Pending));
        await messages.DidNotReceiveWithAnyArgs().QueueAsync(default!, Ct);
        await notifier.DidNotReceiveWithAnyArgs().ToRoleAsync(default, default!, default!, Ct);
    }

    [Fact]
    public async Task Submit_WithTheSettingsOn_SendsTheConfirmation_AndNotifiesTheAdministrators()
    {
        settings.Set(DirectorySettings.RegistrationSendConfirmationEmail, true);
        settings.Set(DirectorySettings.RegistrationNotifyAdmins, true);

        var id = await SubmitAsync(Request(language: "en"));

        await messages.Received(1).QueueAsync(
            Arg.Is<OutboundMessageRequest>(message =>
                message.Recipient == "mario.rossi@example.test" && message.TemplateCode == MessageTemplates.RegistrationReceived && message.Language == "en"
                && message.RelatedEntityId == id),
            Arg.Any<CancellationToken>());
        await notifier.Received(1).ToRoleAsync(
            TenantRole.Administrator, RealtimeEvents.RegistrationRequested, Arg.Is<RegistrationRequestedEvent>(pushed => pushed.RegistrationId == id), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Submit_ConfirmationThatCannotBeQueued_DoesNotFailTheRequest()
    {
        settings.Set(DirectorySettings.RegistrationSendConfirmationEmail, true);
        messages.QueueAsync(Arg.Any<OutboundMessageRequest>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<Guid>(Errors.Host.Unexpected()));

        (await manager.SubmitAsync(Request(), Site, Ct)).IsSuccess.ShouldBeTrue();
        registrations.Requests.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Submit_Closed_IsForbidden_ForEveryReason()
    {
        settings.Set(DirectorySettings.RegistrationEnabled, false);
        (await manager.SubmitAsync(Request(), Site, Ct)).Error!.Code.ShouldBe(EventCodes.Directory.RegistrationDisabled);
        (await manager.CreateCaptchaAsync(Site, Ct)).Error!.Code.ShouldBe(EventCodes.Directory.RegistrationDisabled);

        // Registrations on, but no reviewer sees the directory: nobody could ever process the request.
        settings.Set(DirectorySettings.RegistrationEnabled, true);
        modules.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new TenantModules(new Dictionary<string, TenantRole[]> { [DirectoryModule.ModuleCode] = [TenantRole.Client] }));
        (await manager.SubmitAsync(Request(), Site, Ct)).Error!.Code.ShouldBe(EventCodes.Directory.RegistrationDisabled);
        registrations.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Submit_UnknownClient_OrClientWithoutVerifier_IsRefused()
    {
        (await manager.SubmitAsync(Request(), new RegistrationCaller("unknown", null), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.ClientInvalid);

        clientApplications.AuthenticateAsync("odd", null, Arg.Any<CancellationToken>())
            .Returns(new CallingClient("odd", ClientApplicationType.WebBff, true, "hcaptcha"));
        (await manager.SubmitAsync(Request(), new RegistrationCaller("odd", null), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.ClientInvalid);
    }

    [Fact]
    public async Task Submit_InvalidCaptcha_IsRefused_AfterTheFieldChecks()
    {
        var invalidFields = await manager.SubmitAsync(Request() with { FirstName = "", Captcha = null }, Site, Ct);
        invalidFields.Error!.Code.ShouldBe(EventCodes.Directory.RegistrationInvalid);
        await captcha.DidNotReceiveWithAnyArgs().VerifyAsync(default, Ct);

        (await manager.SubmitAsync(Request() with { Captcha = "forged" }, Site, Ct)).Error!.Code.ShouldBe(EventCodes.Directory.RegistrationCaptchaInvalid);
        registrations.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Submit_Duplicates_AreConflicts()
    {
        await SubmitAsync();
        (await manager.SubmitAsync(Request(email: " MARIO.rossi@example.TEST "), Site, Ct)).Error!.Code.ShouldBe(EventCodes.Directory.RegistrationPending);

        registrations.RegisteredEmails.Add("luigi@example.test");
        (await manager.SubmitAsync(Request(email: "Luigi@example.test"), Site, Ct)).Error!.Code.ShouldBe(EventCodes.Directory.RegistrationEmailRegistered);
    }

    [Fact]
    public async Task Submit_AfterAProcessedRequest_IsAccepted()
    {
        var first = await SubmitAsync();
        (await manager.RejectAsync(first, null, Ct)).IsSuccess.ShouldBeTrue();

        (await manager.SubmitAsync(Request(), Site, Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateCaptcha_ReturnsTheChallengeOfTheClientsProvider()
    {
        var challenge = (await manager.CreateCaptchaAsync(Site, Ct)).Value;

        (challenge.Provider, challenge.Altcha!.Signature).ShouldBe(("altcha", "sig"));
    }

    [Fact]
    public async Task Approve_CreatesTheClient_ForTheDefaultEmployee_AndInvitesIt()
    {
        clients.DefaultEmployee = DefaultEmployee;
        var id = await SubmitAsync();

        var approved = (await manager.ApproveAsync(id, "ok", Ct)).Value;

        (approved.InvitationSent, approved.InvitationErrorCode).ShouldBe((true, null));
        var person = clients.People.Single();
        (person.Id, person.Email, person.FiscalCode).ShouldBe((approved.ClientId, "mario.rossi@example.test", "RSSMRA80A01H501U"));
        clients.Profiles.Single().EmployeeUserId.ShouldBe(DefaultEmployee);
        await accounts.Received(1).CreateAsync(person.Id, "mario.rossi@example.test", "mario.rossi@example.test", TenantRole.Client, true, Arg.Any<CancellationToken>());
        await accounts.Received(1).SendActivationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());

        var detail = (await query.GetAsync(id, Ct)).Value;
        (detail.Status, detail.ClientId, detail.ProcessedBy!.UserId, detail.Notes).ShouldBe(("Approved", person.Id, Reviewer, "ok"));
        (await manager.ApproveAsync(id, null, Ct)).Error!.Code.ShouldBe(EventCodes.Directory.RegistrationProcessed);
        (await manager.RejectAsync(id, null, Ct)).Error!.Code.ShouldBe(EventCodes.Directory.RegistrationProcessed);
    }

    [Fact]
    public async Task Approve_WithoutADefaultEmployee_TheClientCannotSignInYet_AndIsNotInvited()
    {
        var id = await SubmitAsync();

        var approved = (await manager.ApproveAsync(id, null, Ct)).Value;

        (approved.InvitationSent, approved.InvitationErrorCode).ShouldBe((false, $"AUX-{EventCodes.Directory.ClientEmployeeRequired}"));
        clients.Profiles.Single().EmployeeUserId.ShouldBeNull();
        await accounts.Received(1).CreateAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string?>(), TenantRole.Client, false, Arg.Any<CancellationToken>());
        await accounts.DidNotReceiveWithAnyArgs().SendActivationAsync(default, Ct);
    }

    [Fact]
    public async Task Approve_InvitationThatCannotLeave_TheClientExistsAnyway()
    {
        clients.DefaultEmployee = DefaultEmployee;
        accounts.SendActivationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Result.Failure(Errors.Identity.UserEmailMissing()));
        var id = await SubmitAsync();

        var approved = (await manager.ApproveAsync(id, null, Ct)).Value;

        (approved.InvitationSent, approved.InvitationErrorCode).ShouldBe((false, $"AUX-{EventCodes.Identity.UserEmailMissing}"));
        registrations.Requests.Single().Status.ShouldBe(RegistrationStatus.Approved);
    }

    [Fact]
    public async Task Approve_Collisions_AreReported_AndTheRequestStaysPending()
    {
        var id = await SubmitAsync();
        clients.People.Add(Person.Create(Guid.CreateVersion7(), new PersonDetails("Anna", "Bianchi", null, null, null, "RSSMRA80A01H501U"), new DateOnly(2026, 1, 1)).Value);
        (await manager.ApproveAsync(id, null, Ct)).Error!.Code.ShouldBe(EventCodes.Directory.FiscalCodeTaken);

        clients.People.Clear();
        accounts.CreateAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string?>(), TenantRole.Client, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<Guid>(Errors.Identity.UserNameTaken()));
        (await manager.ApproveAsync(id, null, Ct)).Error!.Code.ShouldBe(EventCodes.Identity.UserNameTaken);

        registrations.Requests.Single().Status.ShouldBe(RegistrationStatus.Pending);
    }

    [Fact]
    public async Task Process_UnknownRequest_IsNotFound_AndRejectKeepsTheNotes()
    {
        (await manager.ApproveAsync(Guid.CreateVersion7(), null, Ct)).Error!.Code.ShouldBe(EventCodes.Directory.RegistrationNotFound);
        (await manager.RejectAsync(Guid.CreateVersion7(), null, Ct)).Error!.Code.ShouldBe(EventCodes.Directory.RegistrationNotFound);
        (await query.GetAsync(Guid.CreateVersion7(), Ct)).Error!.Code.ShouldBe(EventCodes.Directory.RegistrationNotFound);

        var id = await SubmitAsync();
        (await manager.ApproveAsync(id, new string('x', 501), Ct)).Error!.ValidationErrors.Keys.ShouldBe(["notes"]);
        (await manager.RejectAsync(id, "duplicate", Ct)).IsSuccess.ShouldBeTrue();

        var detail = (await query.GetAsync(id, Ct)).Value;
        (detail.Status, detail.Notes, detail.ClientId).ShouldBe(("Rejected", "duplicate", (Guid?)null));
        clients.People.ShouldBeEmpty();
    }

    [Fact]
    public async Task Process_WithoutAUser_IsDenied()
    {
        var id = await SubmitAsync();
        caller.UserId.Returns((Guid?)null);

        (await manager.ApproveAsync(id, null, Ct)).Error!.Code.ShouldBe(EventCodes.Identity.PermissionDenied);
        (await manager.RejectAsync(id, null, Ct)).Error!.Code.ShouldBe(EventCodes.Identity.PermissionDenied);
    }

    [Fact]
    public async Task List_ValidatesTheQuery_AndPassesTheFilter()
    {
        await SubmitAsync();

        var page = (await query.ListAsync(new RegistrationListQuery("Pending", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), " rossi ", "-processedAt", 1, 10), Ct)).Value;

        page.TotalCount.ShouldBe(1);
        var filter = registrations.LastFilter!;
        (filter.Status, filter.Search, filter.Sort, filter.Descending).ShouldBe((RegistrationStatus.Pending, "rossi", RegistrationSort.ProcessedAt, true));
        (filter.From, filter.Before).ShouldBe((new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero)));

        var invalid = await query.ListAsync(new RegistrationListQuery("Gone", new DateOnly(2026, 2, 1), new DateOnly(2026, 1, 1), new string('x', 201), "age", 0, 500), Ct);
        invalid.Error!.ValidationErrors.Keys.ShouldBe(["page", "pageSize", "sort", "status", "to", "search"], ignoreOrder: true);
    }
}

/// <summary>Registration requests in memory: the page applies no filter (the SQL filters are tested on PostgreSQL).</summary>
internal sealed class InMemoryRegistrationData : IRegistrationDataFactory, IRegistrationData
{
    public List<RegistrationRequest> Requests { get; } = [];

    public HashSet<string> RegisteredEmails { get; } = [];

    public RegistrationFilter? LastFilter { get; private set; }

    public Task<IRegistrationData> OpenAsync(CancellationToken cancellationToken) => Task.FromResult<IRegistrationData>(this);

    public Task<(IReadOnlyList<RegistrationRow> Items, int Total)> PageAsync(RegistrationFilter filter, CancellationToken cancellationToken)
    {
        LastFilter = filter;
        return Task.FromResult<(IReadOnlyList<RegistrationRow>, int)>((Requests.Select(Row).ToArray(), Requests.Count));
    }

    public Task<RegistrationRow?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Requests.SingleOrDefault(request => request.Id == id) is { } request ? Row(request) : null);

    public Task<RegistrationRequest?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Requests.SingleOrDefault(request => request.Id == id));

    public Task<bool> PendingExistsAsync(string email, CancellationToken cancellationToken) =>
        Task.FromResult(Requests.Any(request => request.Status == RegistrationStatus.Pending && request.Email == email));

    public Task<bool> EmailRegisteredAsync(string email, CancellationToken cancellationToken) => Task.FromResult(RegisteredEmails.Contains(email));

    public void Add(RegistrationRequest request) => Requests.Add(request);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static RegistrationRow Row(RegistrationRequest request) => new(request, request.ProcessedByUserId is null ? null : "Paola Neri");
}

/// <summary>Setting values set by the test, the definition's default otherwise.</summary>
internal sealed class FakeSettings : ISettingsProvider
{
    private readonly Dictionary<string, object> values = new(StringComparer.Ordinal);

    public void Set<T>(SettingDefinition<T> definition, T value)
        where T : notnull => values[definition.Key] = value;

    public Task<T> GetAsync<T>(SettingDefinition<T> definition, CancellationToken cancellationToken)
        where T : notnull => Task.FromResult(values.TryGetValue(definition.Key, out var value) ? (T)value : definition.Default);

    public Task<string?> GetSecretAsync(SecretSettingDefinition definition, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
}
