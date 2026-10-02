using Auxilia.Application.Abstractions.Logging;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Contracts.Platform;
using Auxilia.Diagnostics;
using Auxilia.Diagnostics.Logging;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Results;

using Microsoft.Extensions.Logging;

namespace Auxilia.Application.Platform;

/// <summary>
/// Temporary debug logging of one tenant from the console (decision D-28, System role). The end is stored in the
/// Catalog, applied to this node after the commit and announced to the others; it expires by itself (no job, no
/// restart). Every change is a security event (<c>AUX-29025</c>) and stamps the tenant row with the actor.
/// </summary>
public interface ITenantLogManager
{
    Task<Result> EnableDebugAsync(string slug, EnableTenantDebugLoggingRequest request, CancellationToken cancellationToken);

    /// <summary>Back to the default level now (not enabled: success).</summary>
    Task<Result> DisableDebugAsync(string slug, CancellationToken cancellationToken);
}

/// <summary>Brings this node's per-tenant levels in line with the Catalog: all of them at start, one when another node announces a change.</summary>
public interface ITenantLogLevelSync
{
    Task LoadAllAsync(CancellationToken cancellationToken);

    Task RefreshAsync(string slug, CancellationToken cancellationToken);
}

internal sealed class TenantLogManager(
    IOperationRunner operations,
    ICatalogStore catalog,
    ITenantLogLevels levels,
    ITenantLogLevelBroadcast broadcast,
    TimeProvider timeProvider,
    ILogger<TenantLogManager> logger) : ITenantLogManager
{
    public Task<Result> EnableDebugAsync(string slug, EnableTenantDebugLoggingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ChangeAsync(slug, tenant => tenant.EnableDebugLogging(request.Until.ToUniversalTime(), timeProvider.GetUtcNow()), cancellationToken);
    }

    public Task<Result> DisableDebugAsync(string slug, CancellationToken cancellationToken) =>
        ChangeAsync(slug, tenant =>
        {
            tenant.DisableDebugLogging();
            return Result.Success();
        }, cancellationToken);

    private Task<Result> ChangeAsync(string slug, Func<Tenant, Result> change, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Tenancy.ChangeLogLevel, new { TenantSlug = slug }, async scope =>
        {
            var tenant = await catalog.FindTenantAsync(slug, cancellationToken);
            if (tenant is null)
            {
                return Errors.Tenancy.TenantNotFound();
            }

            scope.SetEntity("Tenant", tenant.Id);
            var previous = tenant.DebugLoggingUntil;
            var result = change(tenant);
            if (result.IsFailure || tenant.DebugLoggingUntil == previous)
            {
                return result;
            }

            await catalog.SaveChangesAsync(cancellationToken);
            var until = tenant.DebugLoggingUntil;
            scope.OnCommitted(async ct =>
            {
                Log.Security.TenantLogLevelChanged(logger, tenant.Slug, until);
                if (until is { } end)
                {
                    levels.EnableDebug(tenant.Slug, end);
                }
                else
                {
                    levels.Clear(tenant.Slug);
                }

                await broadcast.PublishAsync(tenant.Slug, ct);
            });
            return Result.Success();
        }, cancellationToken);
}

internal sealed class TenantLogLevelSync(ICatalogStore catalog, ITenantLogLevels levels, TimeProvider timeProvider) : ITenantLogLevelSync
{
    public async Task LoadAllAsync(CancellationToken cancellationToken)
    {
        foreach (var tenant in await catalog.ListTenantsAsync(cancellationToken))
        {
            Apply(tenant.Slug, tenant);
        }
    }

    public async Task RefreshAsync(string slug, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        Apply(slug, await catalog.FindTenantAsync(slug, cancellationToken));
    }

    private void Apply(string slug, Tenant? tenant)
    {
        if (tenant?.ActiveDebugLoggingUntil(timeProvider.GetUtcNow()) is { } until)
        {
            levels.EnableDebug(slug, until);
        }
        else
        {
            levels.Clear(slug);
        }
    }
}
