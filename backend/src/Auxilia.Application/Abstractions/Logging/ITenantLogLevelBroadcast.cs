namespace Auxilia.Application.Abstractions.Logging;

/// <summary>
/// Tells the other Api/Worker nodes that the log level of a tenant changed in the Catalog (D-28), so they reload it
/// (<see cref="Platform.ITenantLogLevelSync"/>). Best effort: a node that misses it loads the level at its next start.
/// </summary>
public interface ITenantLogLevelBroadcast
{
    Task PublishAsync(string tenantSlug, CancellationToken cancellationToken);
}
