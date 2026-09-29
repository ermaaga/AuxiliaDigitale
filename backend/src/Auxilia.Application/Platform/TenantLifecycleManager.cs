using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Caching;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Diagnostics;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Platform;

/// <inheritdoc cref="ITenantLifecycleManager"/>
internal sealed class TenantLifecycleManager : ITenantLifecycleManager
{
    private readonly IOperationRunner operations;
    private readonly ICatalogStore catalog;
    private readonly ITenantDatabaseAdmin databases;
    private readonly ITenantConnectionProtector protector;
    private readonly ICurrentUser currentUser;
    private readonly TimeProvider timeProvider;
    private readonly IReferenceDataCache cache;

    public TenantLifecycleManager(
        IOperationRunner operations,
        ICatalogStore catalog,
        ITenantDatabaseAdmin databases,
        ITenantConnectionProtector protector,
        ICurrentUser currentUser,
        TimeProvider timeProvider,
        IReferenceDataCache cache)
    {
        this.operations = operations;
        this.catalog = catalog;
        this.databases = databases;
        this.protector = protector;
        this.currentUser = currentUser;
        this.timeProvider = timeProvider;
        this.cache = cache;
    }

    public Task<Result<TenantInfo>> ProvisionAsync(ProvisionTenant request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync(Operations.Tenancy.ProvisionTenant, new { TenantSlug = request.Slug }, async scope =>
        {
            var tenant = await catalog.FindTenantAsync(request.Slug, cancellationToken);
            if (tenant is null)
            {
                var created = Tenant.Create(Guid.CreateVersion7(), request.Slug, request.DisplayName, request.DefaultLanguage, request.TimeZone);
                if (created.IsFailure)
                {
                    return Result.Failure<TenantInfo>(created.Error!);
                }

                tenant = created.Value;
                catalog.Add(tenant);
                catalog.Add(new TenantPlan(Guid.CreateVersion7(), tenant.Id, await catalog.DefaultPlanIdAsync(cancellationToken), timeProvider.GetUtcNow()));
            }
            else if (tenant.Status is not (TenantStatus.Provisioning or TenantStatus.MigrationFailed))
            {
                return Errors.Tenancy.TenantAlreadyExists(tenant.Slug);
            }

            scope.SetEntity("Tenant", tenant.Id);
            scope.OnCommitted(InvalidateTenantLookups);
            var run = new MigrationRun(Guid.CreateVersion7(), tenant.Id, MigrationRunKind.Provisioning, tenant.Slug, ActorDescription.Of(currentUser), timeProvider.GetUtcNow());
            catalog.Add(run);
            await catalog.SaveChangesAsync(cancellationToken);

            var connectionString = await ConnectionStringAsync(tenant, request, cancellationToken);
            if (connectionString is null)
            {
                var invalid = Errors.Tenancy.TenantDatabaseInvalid();
                run.Fail(timeProvider.GetUtcNow(), invalid.DisplayCode, invalid.Description);
                await catalog.SaveChangesAsync(cancellationToken);
                return invalid;
            }

            tenant.SetConnectionSecret(protector.Protect(connectionString));
            await catalog.SaveChangesAsync(cancellationToken);

            try
            {
                var version = await databases.MigrateAsync(connectionString, isNewTenant: true, cancellationToken);
                tenant.SetVersions(version.SchemaVersion, version.DataVersion);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Record the failure in the catalog (the runner logs the exception): the next run resumes from here.
                if (tenant.Status == TenantStatus.Provisioning)
                {
                    tenant.MarkMigrationFailed();
                }

                run.Fail(timeProvider.GetUtcNow(), $"AUX-{EventCodes.Runner.TenantMigrationFailed}", exception.GetType().Name);
                await catalog.SaveChangesAsync(CancellationToken.None);
                throw;
            }

            tenant.Activate();
            run.Succeed(timeProvider.GetUtcNow(), $"schema {tenant.SchemaVersion}, data {tenant.DataVersion ?? "-"}");
            await catalog.SaveChangesAsync(cancellationToken);

            return Result.Success(new TenantInfo(tenant.Id, tenant.Slug, tenant.Status, tenant.DefaultLanguage, tenant.TimeZone));
        }, cancellationToken);
    }

    public Task<Result> SuspendAsync(string slug, CancellationToken cancellationToken) =>
        ChangeStatusAsync(Operations.Tenancy.SuspendTenant, slug, tenant => tenant.Suspend(), cancellationToken);

    public Task<Result> ReactivateAsync(string slug, CancellationToken cancellationToken) =>
        ChangeStatusAsync(Operations.Tenancy.ReactivateTenant, slug, ReactivateSuspended, cancellationToken);

    public Task<Result> ArchiveAsync(string slug, CancellationToken cancellationToken) =>
        ChangeStatusAsync(Operations.Tenancy.ArchiveTenant, slug, tenant => tenant.Archive(timeProvider.GetUtcNow()), cancellationToken);

    private static Result ReactivateSuspended(Tenant tenant) =>
        tenant.Status == TenantStatus.Suspended
            ? tenant.Activate()
            : Errors.Tenancy.TenantTransitionNotAllowed(tenant.Status.ToString(), nameof(TenantStatus.Active));

    private Task<Result> ChangeStatusAsync(
        OperationDescriptor operation, string slug, Func<Tenant, Result> change, CancellationToken cancellationToken) =>
        operations.RunAsync(operation, new { TenantSlug = slug }, async scope =>
        {
            var tenant = await catalog.FindTenantAsync(slug, cancellationToken);
            if (tenant is null)
            {
                return Errors.Tenancy.TenantNotFound();
            }

            scope.SetEntity("Tenant", tenant.Id);
            var result = change(tenant);
            if (result.IsSuccess)
            {
                await catalog.SaveChangesAsync(cancellationToken);
                scope.OnCommitted(InvalidateTenantLookups);
            }

            return result;
        }, cancellationToken);

    /// <summary>Every node sees the new status at once (Api tenant resolution, ARCHITECTURE §7.3).</summary>
    private Task InvalidateTenantLookups(CancellationToken cancellationToken) =>
        cache.InvalidateAsync(CacheTags.CatalogTenants, cancellationToken);

    private async Task<string?> ConnectionStringAsync(Tenant tenant, ProvisionTenant request, CancellationToken cancellationToken)
    {
        if (request.ExistingConnectionString is { } existing)
        {
            return await databases.CanConnectAsync(existing, cancellationToken) ? existing : null;
        }

        if (tenant.ConnectionSecret is { } secret)
        {
            // Resuming: the database was created by a previous run.
            return protector.Unprotect(secret);
        }

        return await databases.CreateDatabaseAsync(tenant.Slug, cancellationToken);
    }
}
