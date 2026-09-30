using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Persistence.Catalog.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Auxilia.Persistence.Catalog.Interceptors;

/// <summary>Fills the audit columns of catalog rows with the current time and actor (<c>platform:{id}</c>, <c>system</c>…).</summary>
internal sealed class CatalogAuditInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUser currentUser;
    private readonly TimeProvider timeProvider;

    public CatalogAuditInterceptor(ICurrentUser currentUser, TimeProvider timeProvider)
    {
        this.currentUser = currentUser;
        this.timeProvider = timeProvider;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Stamp(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Stamp(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public static string ActorOf(ICurrentUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var actor = user.ActorType.ToString().ToLowerInvariant();
        return user.UserId is { } id ? $"{actor}:{id}" : actor;
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var actor = ActorOf(currentUser);

        foreach (var entry in context.ChangeTracker.Entries())
        {
            // Audited entities have the shadow audit columns; a domain CreatedAt alone (e.g. SigningKey) is not audit.
            if (entry.Metadata.FindProperty(CatalogConventions.CreatedBy) is null)
            {
                continue;
            }

            if (entry.State == EntityState.Added)
            {
                entry.Property(CatalogConventions.CreatedAt).CurrentValue = now;
                entry.Property(CatalogConventions.CreatedBy).CurrentValue = actor;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Property(CatalogConventions.UpdatedAt).CurrentValue = now;
                entry.Property(CatalogConventions.UpdatedBy).CurrentValue = actor;
            }
        }
    }
}
