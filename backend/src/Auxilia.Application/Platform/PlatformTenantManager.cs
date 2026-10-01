using Auxilia.Application.Abstractions.Caching;
using Auxilia.Application.Abstractions.Messaging;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Abstractions.Validation;
using Auxilia.Application.Bus;
using Auxilia.Application.Platform.Modules;
using Auxilia.Contracts.Messages.V1.Platform;
using Auxilia.Contracts.Platform;
using Auxilia.Diagnostics;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

using FluentValidation;

using Microsoft.Extensions.Logging;

namespace Auxilia.Application.Platform;

/// <summary>
/// Tenant administration of the platform console and of auxctl (N02, System role): creation with asynchronous
/// provisioning, edits, status changes (archive only, never delete, D-25), plan and module overrides per role (D-18).
/// Needs no database-admin login: the provisioning itself runs in the Worker (<see cref="ProvisionTenantCommand"/>).
/// </summary>
public interface IPlatformTenantManager
{
    /// <summary>
    /// Adds the tenant (Provisioning, default plan) and queues its provisioning. When the message cannot be sent the
    /// tenant stays in Provisioning and <see cref="RetryProvisioningAsync"/> sends it again.
    /// </summary>
    Task<Result> CreateAsync(CreatePlatformTenantRequest request, CancellationToken cancellationToken);

    /// <summary>Queues the provisioning again for a tenant in Provisioning or MigrationFailed (resumable, idempotent).</summary>
    Task<Result> RetryProvisioningAsync(string slug, CancellationToken cancellationToken);

    Task<Result> UpdateAsync(string slug, UpdatePlatformTenantRequest request, CancellationToken cancellationToken);

    Task<Result> SuspendAsync(string slug, CancellationToken cancellationToken);

    Task<Result> ReactivateAsync(string slug, CancellationToken cancellationToken);

    Task<Result> ArchiveAsync(string slug, CancellationToken cancellationToken);

    /// <summary>Ends the current plan period and starts the new plan now (same plan: nothing changes).</summary>
    Task<Result> ChangePlanAsync(string slug, ChangeTenantPlanRequest request, CancellationToken cancellationToken);

    Task<Result> SetModuleOverrideAsync(string slug, string moduleCode, SetModuleOverrideRequest request, CancellationToken cancellationToken);

    /// <summary>Back to what the plan says for the module (no override: success).</summary>
    Task<Result> RemoveModuleOverrideAsync(string slug, string moduleCode, CancellationToken cancellationToken);
}

internal sealed class PlatformTenantManager : IPlatformTenantManager
{
    private readonly IOperationRunner operations;
    private readonly ICatalogStore catalog;
    private readonly IMessageSender sender;
    private readonly OutgoingMessageHeaders headers;
    private readonly IReferenceDataCache cache;
    private readonly IValidator<CreatePlatformTenantRequest> createValidator;
    private readonly IValidator<UpdatePlatformTenantRequest> updateValidator;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<PlatformTenantManager> logger;

    public PlatformTenantManager(
        IOperationRunner operations,
        ICatalogStore catalog,
        IMessageSender sender,
        OutgoingMessageHeaders headers,
        IReferenceDataCache cache,
        IValidator<CreatePlatformTenantRequest> createValidator,
        IValidator<UpdatePlatformTenantRequest> updateValidator,
        TimeProvider timeProvider,
        ILogger<PlatformTenantManager> logger)
    {
        this.operations = operations;
        this.catalog = catalog;
        this.sender = sender;
        this.headers = headers;
        this.cache = cache;
        this.createValidator = createValidator;
        this.updateValidator = updateValidator;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    public Task<Result> CreateAsync(CreatePlatformTenantRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync(Operations.Tenancy.RequestProvisioning, new { TenantSlug = request.Slug }, async scope =>
        {
            var validation = await createValidator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
            {
                return validation.ToResult();
            }

            var created = Tenant.Create(Guid.CreateVersion7(), request.Slug.Trim(), request.DisplayName, request.DefaultLanguage, request.TimeZone);
            if (created.IsFailure)
            {
                return Result.Failure(created.Error!);
            }

            var tenant = created.Value;
            if (await catalog.FindTenantAsync(tenant.Slug, cancellationToken) is not null)
            {
                return Errors.Tenancy.TenantAlreadyExists(tenant.Slug);
            }

            catalog.Add(tenant);
            catalog.Add(new TenantPlan(Guid.CreateVersion7(), tenant.Id, await catalog.DefaultPlanIdAsync(cancellationToken), timeProvider.GetUtcNow()));
            await catalog.SaveChangesAsync(cancellationToken);
            scope.SetEntity("Tenant", tenant.Id);

            var administrator = request.Administrator is { } invite
                ? new ProvisionTenantAdministrator(invite.Email.Trim(), invite.FirstName.Trim(), invite.LastName.Trim())
                : null;
            scope.OnCommitted(ct => DispatchAsync(new ProvisionTenantCommand(tenant.Slug, administrator), ct));
            scope.OnCommitted(InvalidateTenantLookups);
            return Result.Success();
        }, cancellationToken);
    }

    public Task<Result> RetryProvisioningAsync(string slug, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Tenancy.RequestProvisioning, new { TenantSlug = slug }, async scope =>
        {
            var tenant = await catalog.FindTenantAsync(slug, cancellationToken);
            if (tenant is null)
            {
                return Errors.Tenancy.TenantNotFound();
            }

            if (tenant.Status is not (TenantStatus.Provisioning or TenantStatus.MigrationFailed))
            {
                return Errors.Tenancy.TenantNotProvisioning();
            }

            scope.SetEntity("Tenant", tenant.Id);
            scope.OnCommitted(ct => DispatchAsync(new ProvisionTenantCommand(tenant.Slug, null), ct));
            return Result.Success();
        }, cancellationToken);

    public Task<Result> UpdateAsync(string slug, UpdatePlatformTenantRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ChangeTenantAsync(Operations.Tenancy.UpdateTenant, slug, async (tenant, _) =>
        {
            var validation = await updateValidator.ValidateAsync(request, cancellationToken);
            return validation.IsValid ? tenant.Update(request.DisplayName, request.TimeZone) : validation.ToResult();
        }, cancellationToken);
    }

    public Task<Result> SuspendAsync(string slug, CancellationToken cancellationToken) =>
        ChangeTenantAsync(Operations.Tenancy.SuspendTenant, slug, (tenant, _) => Task.FromResult(tenant.Suspend()), cancellationToken);

    public Task<Result> ReactivateAsync(string slug, CancellationToken cancellationToken) =>
        ChangeTenantAsync(Operations.Tenancy.ReactivateTenant, slug, (tenant, _) => Task.FromResult(
            tenant.Status == TenantStatus.Suspended
                ? tenant.Activate()
                : Errors.Tenancy.TenantTransitionNotAllowed(tenant.Status.ToString(), nameof(TenantStatus.Active))), cancellationToken);

    public Task<Result> ArchiveAsync(string slug, CancellationToken cancellationToken) =>
        ChangeTenantAsync(Operations.Tenancy.ArchiveTenant, slug, (tenant, _) => Task.FromResult(tenant.Archive(timeProvider.GetUtcNow())), cancellationToken);

    public Task<Result> ChangePlanAsync(string slug, ChangeTenantPlanRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ChangeTenantAsync(Operations.Tenancy.ChangeTenantPlan, slug, async (tenant, scope) =>
        {
            var plan = (await catalog.ListPlansAsync(cancellationToken))
                .FirstOrDefault(item => item.IsActive && string.Equals(item.Code, request.PlanCode?.Trim(), StringComparison.Ordinal));
            if (plan is null)
            {
                return Errors.Tenancy.PlanNotFound();
            }

            var now = timeProvider.GetUtcNow();
            var current = await catalog.CurrentTenantPlanAsync(tenant.Id, now, cancellationToken);
            if (current?.PlanId == plan.Id)
            {
                return Result.Success();
            }

            current?.End(now);
            catalog.Add(new TenantPlan(Guid.CreateVersion7(), tenant.Id, plan.Id, now));
            scope.OnCommitted(ct => InvalidateModulesAsync(tenant.Slug, ct));
            return Result.Success();
        }, cancellationToken);
    }

    public Task<Result> SetModuleOverrideAsync(string slug, string moduleCode, SetModuleOverrideRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ChangeTenantAsync(Operations.Tenancy.ChangeModuleOverride, slug, async (tenant, scope) =>
        {
            var module = await FindConfigurableModuleAsync(moduleCode, cancellationToken);
            if (module.IsFailure)
            {
                return Result.Failure(module.Error!);
            }

            var roles = ParseRoles(request.Roles);
            if (roles is null || (request.IsEnabled && roles.Count == 0))
            {
                return Errors.Host.ValidationFailed(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["roles"] = ["validation.tenant.moduleRoles"] });
            }

            var existing = (await catalog.ListOverridesAsync(tenant.Id, cancellationToken))
                .FirstOrDefault(item => item.ModuleCode == module.Value.Id);
            if (existing is null)
            {
                catalog.Add(new TenantModuleOverride(tenant.Id, module.Value.Id, request.IsEnabled, roles));
            }
            else
            {
                existing.Set(request.IsEnabled, roles);
            }

            scope.OnCommitted(ct => InvalidateModulesAsync(tenant.Slug, ct));
            return Result.Success();
        }, cancellationToken);
    }

    public Task<Result> RemoveModuleOverrideAsync(string slug, string moduleCode, CancellationToken cancellationToken) =>
        ChangeTenantAsync(Operations.Tenancy.ChangeModuleOverride, slug, async (tenant, scope) =>
        {
            var existing = (await catalog.ListOverridesAsync(tenant.Id, cancellationToken))
                .FirstOrDefault(item => item.ModuleCode == moduleCode);
            if (existing is not null)
            {
                catalog.Remove(existing);
                scope.OnCommitted(ct => InvalidateModulesAsync(tenant.Slug, ct));
            }

            return Result.Success();
        }, cancellationToken);

    private static List<TenantRole>? ParseRoles(IReadOnlyList<string>? roles)
    {
        var parsed = new List<TenantRole>();
        foreach (var role in roles ?? [])
        {
            if (!Enum.TryParse<TenantRole>(role, ignoreCase: false, out var value) || !Enum.IsDefined(value))
            {
                return null;
            }

            parsed.Add(value);
        }

        return parsed;
    }

    private async Task<Result<PlatformModule>> FindConfigurableModuleAsync(string moduleCode, CancellationToken cancellationToken)
    {
        var module = (await catalog.ListModulesAsync(cancellationToken))
            .FirstOrDefault(item => item.IsAvailable && string.Equals(item.Id, moduleCode, StringComparison.Ordinal));
        if (module is null)
        {
            return Errors.Tenancy.ModuleNotFound();
        }

        return module.Kind == ModuleKind.Core ? Errors.Tenancy.CoreModuleNotConfigurable() : module;
    }

    /// <summary>
    /// Loads the tenant (archived: read-only, except that nothing else can be done to it anyway), applies the change and
    /// saves; the tenant lookups of every node are refreshed after the commit.
    /// </summary>
    private Task<Result> ChangeTenantAsync(
        OperationDescriptor operation, string slug, Func<Tenant, IOperationScope, Task<Result>> change, CancellationToken cancellationToken) =>
        operations.RunAsync(operation, new { TenantSlug = slug }, async scope =>
        {
            var tenant = await catalog.FindTenantAsync(slug, cancellationToken);
            if (tenant is null)
            {
                return Errors.Tenancy.TenantNotFound();
            }

            scope.SetEntity("Tenant", tenant.Id);
            if (tenant.Status == TenantStatus.Archived)
            {
                return Errors.Tenancy.TenantArchived();
            }

            var result = await change(tenant, scope);
            if (result.IsSuccess)
            {
                await catalog.SaveChangesAsync(cancellationToken);
                scope.OnCommitted(InvalidateTenantLookups);
            }

            return result;
        }, cancellationToken);

    private async Task DispatchAsync(ProvisionTenantCommand command, CancellationToken cancellationToken)
    {
        try
        {
            await sender.SendAsync(command, headers.Create(Guid.CreateVersion7()), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The tenant stays in Provisioning: the console offers "retry provisioning".
            Log.Tenancy.ProvisioningNotDispatched(logger, exception, command.Slug);
        }
    }

    /// <summary>Every node sees the new status and name at once (Api tenant resolution, ARCHITECTURE §7.3).</summary>
    private Task InvalidateTenantLookups(CancellationToken cancellationToken) =>
        cache.InvalidateAsync(CacheTags.CatalogTenants, cancellationToken);

    /// <summary>Effective modules (and so navigation and module endpoints) change without restart (N02).</summary>
    private Task InvalidateModulesAsync(string slug, CancellationToken cancellationToken) =>
        cache.InvalidateAsync(CacheTags.Tenant(slug, TenantModulesCache.ModuleName), cancellationToken);
}

/// <summary>Languages shipped with every tenant (EN + IT translations, F24).</summary>
internal static class TenantLanguages
{
    public static readonly IReadOnlySet<string> Supported = new HashSet<string>(StringComparer.Ordinal) { "it", "en" };

    public static bool IsTimeZone(string? timeZone) =>
        !string.IsNullOrWhiteSpace(timeZone) && timeZone.Length <= Tenant.TimeZoneMaxLength
        && TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out _);
}

internal sealed class CreatePlatformTenantRequestValidator : AbstractValidator<CreatePlatformTenantRequest>
{
    public CreatePlatformTenantRequestValidator()
    {
        RuleFor(request => request.Slug).NotEmpty();
        RuleFor(request => request.DisplayName).NotEmpty().MaximumLength(Tenant.DisplayNameMaxLength);
        RuleFor(request => request.DefaultLanguage)
            .Must(language => language is not null && TenantLanguages.Supported.Contains(language))
            .WithErrorCode("validation.tenant.defaultLanguage");
        RuleFor(request => request.TimeZone).Must(TenantLanguages.IsTimeZone).WithErrorCode("validation.tenant.timeZone");
        When(request => request.Administrator is not null, () =>
        {
            RuleFor(request => request.Administrator!.Email).NotEmpty().MaximumLength(Person.EmailMaxLength).EmailAddress();
            RuleFor(request => request.Administrator!.FirstName).NotEmpty().MaximumLength(Person.NameMaxLength);
            RuleFor(request => request.Administrator!.LastName).NotEmpty().MaximumLength(Person.NameMaxLength);
        });
    }
}

internal sealed class UpdatePlatformTenantRequestValidator : AbstractValidator<UpdatePlatformTenantRequest>
{
    public UpdatePlatformTenantRequestValidator()
    {
        RuleFor(request => request.DisplayName).NotEmpty().MaximumLength(Tenant.DisplayNameMaxLength);
        RuleFor(request => request.TimeZone).Must(TenantLanguages.IsTimeZone).WithErrorCode("validation.tenant.timeZone");
    }
}
