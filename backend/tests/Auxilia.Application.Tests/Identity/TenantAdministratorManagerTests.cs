using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Identity;
using Auxilia.Application.Identity.Public;
using Auxilia.Application.Tests.Execution;
using Auxilia.Contracts.Platform;
using Auxilia.Diagnostics;
using Auxilia.Domain.Identity;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

using NSubstitute;

namespace Auxilia.Application.Tests.Identity;

public sealed class TenantAdministratorManagerTests : IAsyncDisposable
{
    private readonly InMemoryIdentityData identity = new();
    private readonly IAccountLinkManager links = Substitute.For<IAccountLinkManager>();
    private readonly RecordingLogger<TenantAdministratorManager> log = new();
    private readonly TenantAdministratorManager manager;

    public TenantAdministratorManagerTests()
    {
        links.SendActivationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        var tenant = Substitute.For<ITenantContext>();
        tenant.Current.Returns(new TenantInfo(Guid.CreateVersion7(), "acme", TenantStatus.Active, "en", "Europe/Rome"));
        manager = new TenantAdministratorManager(
            Platform.ManagerHarness.Runner(), identity, links, tenant, new CreateTenantAdministratorRequestValidator(), log);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => identity.DisposeAsync();

    private static CreateTenantAdministratorRequest Request(string email = "anna@acme.test") => new(email, "Anna", "Bianchi");

    [Fact]
    public async Task CreateInitial_CreatesPersonAndAdministrator_AndEmailsTheInvitation()
    {
        var result = (await manager.CreateInitialAsync(Request(" anna@acme.test "), Ct)).Value;

        var person = identity.AddedPeople.ShouldHaveSingleItem();
        (person.FirstName, person.LastName, person.Email).ShouldBe(("Anna", "Bianchi", "anna@acme.test"));
        var user = identity.Users.ShouldHaveSingleItem();
        (user.UserName, user.Email, user.PersonId, user.LanguageCode, user.IsActive).ShouldBe(("anna@acme.test", "anna@acme.test", person.Id, "en", true));
        user.Roles.ShouldBe([TenantRole.Administrator]);
        user.PasswordHash.ShouldBeNull();
        result.ShouldBe(new TenantAdministratorInvitationResponse(user.Id, true, null));
        await links.Received(1).SendActivationAsync(user.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateInitial_WithoutASendingAccount_KeepsTheAccountAndLeavesTheInvitationPending()
    {
        links.SendActivationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(Errors.Messaging.NoAccountForMessage("Email", "Transactional")));

        var result = (await manager.CreateInitialAsync(Request(), Ct)).Value;

        result.InvitationSent.ShouldBeFalse();
        result.InvitationErrorCode.ShouldBe($"AUX-{EventCodes.Messaging.NoAccountForMessage}");
        identity.Users.ShouldHaveSingleItem();
        log.Entries.ShouldContain(entry => entry.EventId.Id == EventCodes.Identity.InvitationPending);
    }

    [Fact]
    public async Task CreateInitial_IsOnlyForTheFirstAdministrator()
    {
        (await manager.CreateInitialAsync(Request(), Ct)).IsSuccess.ShouldBeTrue();

        (await manager.CreateInitialAsync(Request("other@acme.test"), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.AdministratorAlreadyExists);
        identity.Users.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task CreateInitial_RefusesInvalidValuesAndTakenUserNames()
    {
        (await manager.CreateInitialAsync(new CreateTenantAdministratorRequest("nope", "", "B"), Ct)).Error!.ValidationErrors.Keys
            .ShouldBe(["email", "firstName"], ignoreOrder: true);

        identity.Users.Add(User.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "anna@acme.test", null, "it", [TenantRole.Client], isActive: true).Value);
        (await manager.CreateInitialAsync(Request(), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.UserNameTaken);
        identity.AddedPeople.ShouldBeEmpty();
    }

    [Fact]
    public async Task SendInvitation_OnlyForAdministratorsWhoHaveNotActivated()
    {
        var created = (await manager.CreateInitialAsync(Request(), Ct)).Value;
        links.ClearReceivedCalls();

        (await manager.SendInvitationAsync(created.UserId, Ct)).Value.InvitationSent.ShouldBeTrue();
        await links.Received(1).SendActivationAsync(created.UserId, Arg.Any<CancellationToken>());

        (await manager.SendInvitationAsync(Guid.CreateVersion7(), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.UserNotFound);
        identity.Users.Single().SetPassword("hash:secret", PasswordFormat.Identity, DateTimeOffset.UnixEpoch);
        (await manager.SendInvitationAsync(created.UserId, Ct)).Error!.Code.ShouldBe(EventCodes.Identity.AccountAlreadyActivated);

        (await manager.ListAsync(Ct)).ShouldHaveSingleItem()
            .ShouldBe(new TenantAdministratorResponse(created.UserId, "anna@acme.test", "anna@acme.test", true, true));
    }
}
