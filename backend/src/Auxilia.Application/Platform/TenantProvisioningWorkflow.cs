using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Identity.Public;
using Auxilia.Contracts.Messages.V1.Platform;
using Auxilia.Contracts.Platform;
using Auxilia.Diagnostics;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Platform;

/// <summary>
/// What the Worker does with <see cref="ProvisionTenantCommand"/> (N02): provisions or resumes the tenant, then — when
/// the System gave one — creates and invites the first Administrator inside the new tenant. Redelivery is harmless: a
/// provisioned tenant is left as it is and an existing Administrator is not created again.
/// </summary>
public interface ITenantProvisioningWorkflow
{
    Task<Result> RunAsync(ProvisionTenantCommand command, CancellationToken cancellationToken);
}

internal sealed class TenantProvisioningWorkflow : ITenantProvisioningWorkflow
{
    private readonly ITenantLifecycleManager lifecycle;
    private readonly ITenantDirectory tenants;
    private readonly ITenantContextSetter tenantContext;
    private readonly ITenantAdministratorManager administrators;

    public TenantProvisioningWorkflow(
        ITenantLifecycleManager lifecycle, ITenantDirectory tenants, ITenantContextSetter tenantContext, ITenantAdministratorManager administrators)
    {
        this.lifecycle = lifecycle;
        this.tenants = tenants;
        this.tenantContext = tenantContext;
        this.administrators = administrators;
    }

    public async Task<Result> RunAsync(ProvisionTenantCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var provisioned = await lifecycle.ResumeProvisioningAsync(command.Slug, cancellationToken);
        if (provisioned.IsFailure)
        {
            return Result.Failure(provisioned.Error!);
        }

        if (command.Administrator is not { } administrator)
        {
            return Result.Success();
        }

        // The Administrator lives in the tenant database: from here on the work is inside the tenant.
        if (await tenants.FindBySlugAsync(command.Slug, cancellationToken) is not { Status: TenantStatus.Active } tenant)
        {
            return Result.Success();
        }

        tenantContext.Set(tenant);
        var created = await administrators.CreateInitialAsync(
            new CreateTenantAdministratorRequest(administrator.Email, administrator.FirstName, administrator.LastName), cancellationToken);
        return created.IsSuccess || created.Error!.Code == EventCodes.Identity.AdministratorAlreadyExists
            ? Result.Success()
            : Result.Failure(created.Error);
    }
}
