using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Identity.Public;
using Auxilia.Application.Platform;
using Auxilia.Contracts.Messages.V1.Platform;
using Auxilia.Contracts.Platform;
using Auxilia.Diagnostics;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Results;

using NSubstitute;

namespace Auxilia.Application.Tests.Platform;

public sealed class TenantProvisioningWorkflowTests
{
    private static readonly TenantInfo Acme = new(Guid.CreateVersion7(), "acme", TenantStatus.Active, "it", "Europe/Rome");

    private readonly ITenantLifecycleManager lifecycle = Substitute.For<ITenantLifecycleManager>();
    private readonly ITenantDirectory tenants = Substitute.For<ITenantDirectory>();
    private readonly ITenantContextSetter tenantContext = Substitute.For<ITenantContextSetter>();
    private readonly ITenantAdministratorManager administrators = Substitute.For<ITenantAdministratorManager>();
    private readonly TenantProvisioningWorkflow workflow;

    public TenantProvisioningWorkflowTests()
    {
        lifecycle.ResumeProvisioningAsync("acme", Arg.Any<CancellationToken>()).Returns(Acme);
        tenants.FindBySlugAsync("acme", Arg.Any<CancellationToken>()).Returns(Acme);
        administrators.CreateInitialAsync(Arg.Any<CreateTenantAdministratorRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TenantAdministratorInvitationResponse(Guid.CreateVersion7(), false, "AUX-15002"));
        workflow = new TenantProvisioningWorkflow(lifecycle, tenants, tenantContext, administrators);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ProvisionTenantCommand WithAdministrator() =>
        new("acme", new ProvisionTenantAdministrator("anna@acme.test", "Anna", "Bianchi"));

    [Fact]
    public async Task Provisions_ThenCreatesTheFirstAdministratorInsideTheTenant()
    {
        (await workflow.RunAsync(WithAdministrator(), Ct)).IsSuccess.ShouldBeTrue();

        Received.InOrder(() =>
        {
            lifecycle.ResumeProvisioningAsync("acme", Arg.Any<CancellationToken>());
            tenantContext.Set(Acme);
            administrators.CreateInitialAsync(new CreateTenantAdministratorRequest("anna@acme.test", "Anna", "Bianchi"), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task WithoutAdministrator_OnlyProvisions()
    {
        (await workflow.RunAsync(new ProvisionTenantCommand("acme", null), Ct)).IsSuccess.ShouldBeTrue();

        tenantContext.DidNotReceive().Set(Arg.Any<TenantInfo>());
        await administrators.DidNotReceive().CreateInitialAsync(Arg.Any<CreateTenantAdministratorRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RedeliveredMessage_WithTheAdministratorAlreadyCreated_Succeeds()
    {
        administrators.CreateInitialAsync(Arg.Any<CreateTenantAdministratorRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<TenantAdministratorInvitationResponse>(Errors.Identity.AdministratorAlreadyExists()));

        (await workflow.RunAsync(WithAdministrator(), Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task FailedProvisioning_CreatesNoAdministrator_AndAnInvalidAdministratorIsAFailure()
    {
        lifecycle.ResumeProvisioningAsync("gone", Arg.Any<CancellationToken>()).Returns(Result.Failure<TenantInfo>(Errors.Tenancy.TenantNotFound()));
        (await workflow.RunAsync(new ProvisionTenantCommand("gone", new ProvisionTenantAdministrator("a@b.c", "A", "B")), Ct)).Error!.Code
            .ShouldBe(EventCodes.Tenancy.TenantNotFound);
        await administrators.DidNotReceive().CreateInitialAsync(Arg.Any<CreateTenantAdministratorRequest>(), Arg.Any<CancellationToken>());

        administrators.CreateInitialAsync(Arg.Any<CreateTenantAdministratorRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<TenantAdministratorInvitationResponse>(Errors.Identity.UserNameTaken()));
        (await workflow.RunAsync(WithAdministrator(), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.UserNameTaken);
    }

    [Fact]
    public async Task TenantNotActiveAfterProvisioning_IsLeftWithoutAdministrator()
    {
        tenants.FindBySlugAsync("acme", Arg.Any<CancellationToken>()).Returns(Acme with { Status = TenantStatus.Suspended });

        (await workflow.RunAsync(WithAdministrator(), Ct)).IsSuccess.ShouldBeTrue();

        tenantContext.DidNotReceive().Set(Arg.Any<TenantInfo>());
    }
}
