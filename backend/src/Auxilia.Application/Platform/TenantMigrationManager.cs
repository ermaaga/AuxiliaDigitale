using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Diagnostics;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Platform;

/// <inheritdoc cref="ITenantMigrationManager"/>
internal sealed class TenantMigrationManager : ITenantMigrationManager
{
    private static readonly TenantStatus[] Migratable = [TenantStatus.Active, TenantStatus.Suspended, TenantStatus.MigrationFailed];

    private readonly IOperationRunner operations;
    private readonly ICatalogStore catalog;
    private readonly ICatalogMigrator catalogMigrator;
    private readonly ITenantDatabaseAdmin databases;
    private readonly ITenantConnectionProtector protector;
    private readonly ICurrentUser currentUser;
    private readonly TimeProvider timeProvider;

    public TenantMigrationManager(
        IOperationRunner operations,
        ICatalogStore catalog,
        ICatalogMigrator catalogMigrator,
        ITenantDatabaseAdmin databases,
        ITenantConnectionProtector protector,
        ICurrentUser currentUser,
        TimeProvider timeProvider)
    {
        this.operations = operations;
        this.catalog = catalog;
        this.catalogMigrator = catalogMigrator;
        this.databases = databases;
        this.protector = protector;
        this.currentUser = currentUser;
        this.timeProvider = timeProvider;
    }

    public Task<Result<IReadOnlyList<string>>> MigrateCatalogAsync(CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Runner.MigrateCatalog, null, async _ =>
        {
            var startedAt = timeProvider.GetUtcNow();
            var applied = await catalogMigrator.MigrateAsync(cancellationToken);

            // Recorded after migrating: on a new server the table exists only now.
            var run = new MigrationRun(Guid.CreateVersion7(), null, MigrationRunKind.CatalogSchema, applied.Count > 0 ? applied[^1] : "up-to-date", ActorDescription.Of(currentUser), startedAt);
            run.Succeed(timeProvider.GetUtcNow(), applied.Count == 0 ? "no pending migrations" : string.Join(", ", applied));
            catalog.Add(run);
            await catalog.SaveChangesAsync(cancellationToken);

            return Result.Success(applied);
        }, cancellationToken);

    public Task<Result<TenantDatabaseVersion>> MigrateTenantAsync(string slug, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Runner.MigrateTenant, new { TenantSlug = slug }, async scope =>
        {
            var tenant = await catalog.FindTenantAsync(slug, cancellationToken);
            if (tenant is null || tenant.Status == TenantStatus.Archived)
            {
                return Errors.Tenancy.TenantNotFound();
            }

            if (!Migratable.Contains(tenant.Status) || tenant.ConnectionSecret is null)
            {
                return Errors.Tenancy.TenantTransitionNotAllowed(tenant.Status.ToString(), "Migrated");
            }

            scope.SetEntity("Tenant", tenant.Id);
            var run = new MigrationRun(Guid.CreateVersion7(), tenant.Id, MigrationRunKind.TenantSchema, "latest", ActorDescription.Of(currentUser), timeProvider.GetUtcNow());
            catalog.Add(run);
            await catalog.SaveChangesAsync(cancellationToken);

            TenantDatabaseVersion version;
            try
            {
                version = await databases.MigrateAsync(protector.Unprotect(tenant.ConnectionSecret), isNewTenant: false, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A suspended tenant stays suspended; an active one stops serving until migrated (the runner logs the exception).
                if (tenant.Status == TenantStatus.Active)
                {
                    tenant.MarkMigrationFailed();
                }

                run.Fail(timeProvider.GetUtcNow(), $"AUX-{EventCodes.Runner.TenantMigrationFailed}", exception.GetType().Name);
                await catalog.SaveChangesAsync(CancellationToken.None);
                throw;
            }

            tenant.SetVersions(version.SchemaVersion, version.DataVersion);
            if (tenant.Status == TenantStatus.MigrationFailed)
            {
                tenant.Activate();
            }

            run.Succeed(timeProvider.GetUtcNow(), $"schema {version.SchemaVersion}, data {version.DataVersion ?? "-"}");
            await catalog.SaveChangesAsync(cancellationToken);
            return Result.Success(version);
        }, cancellationToken);

    public async Task<IReadOnlyList<string>> MigratableTenantsAsync(CancellationToken cancellationToken) =>
        (await catalog.ListTenantsAsync(cancellationToken))
            .Where(tenant => Migratable.Contains(tenant.Status) && tenant.ConnectionSecret is not null)
            .Select(tenant => tenant.Slug)
            .Order(StringComparer.Ordinal)
            .ToArray();
}
