using System.Diagnostics;
using System.Text.Json;

using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Persistence.Tenant.Conventions;
using Auxilia.Persistence.Tenant.Operations;
using Auxilia.SharedKernel.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Auxilia.Persistence.Tenant.Interceptors;

/// <summary>
/// Before saving: turns deletes of <see cref="ISoftDeletable"/> entities into soft deletes, fills the audit columns
/// and adds one <see cref="EntityChange"/> per changed <see cref="IAuditable"/> entity (same transaction), with the
/// actor type and the trace id (skill auxilia-ef-migration, data-model §3.11).
/// </summary>
internal sealed class TenantAuditInterceptor : SaveChangesInterceptor
{
    private static readonly HashSet<string> TechnicalProperties =
    [
        TenantConventions.CreatedAt, TenantConventions.CreatedBy, TenantConventions.UpdatedAt, TenantConventions.UpdatedBy,
        TenantConventions.IsDeleted, TenantConventions.DeletedAt, TenantConventions.DeletedBy, TenantConventions.Version,
    ];

    private readonly ICurrentUser currentUser;
    private readonly TimeProvider timeProvider;

    public TenantAuditInterceptor(ICurrentUser currentUser, TimeProvider timeProvider)
    {
        this.currentUser = currentUser;
        this.timeProvider = timeProvider;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Apply(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Apply(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var actorId = currentUser.UserId;
        var changes = new List<EntityChange>();

        foreach (var entry in context.ChangeTracker.Entries().Where(entry => entry.Entity is IAuditable).ToArray())
        {
            var action = entry.State switch
            {
                EntityState.Added => "Created",
                EntityState.Modified => "Updated",
                EntityState.Deleted => "Deleted",
                _ => null,
            };

            if (action is null)
            {
                continue;
            }

            if (entry.State == EntityState.Deleted && entry.Entity is ISoftDeletable)
            {
                entry.State = EntityState.Modified;
                entry.Property(TenantConventions.IsDeleted).CurrentValue = true;
                entry.Property(TenantConventions.DeletedAt).CurrentValue = now;
                entry.Property(TenantConventions.DeletedBy).CurrentValue = actorId;
            }
            else if (entry.State == EntityState.Added)
            {
                entry.Property(TenantConventions.CreatedAt).CurrentValue = now;
                entry.Property(TenantConventions.CreatedBy).CurrentValue = actorId;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Property(TenantConventions.UpdatedAt).CurrentValue = now;
                entry.Property(TenantConventions.UpdatedBy).CurrentValue = actorId;
            }

            if (EntityId(entry) is { } entityId)
            {
                changes.Add(new EntityChange
                {
                    EntityType = entry.Metadata.ClrType.Name,
                    EntityId = entityId,
                    Action = action,
                    Changes = Describe(entry, action),
                    ActorType = currentUser.ActorType.ToString(),
                    ActorId = actorId,
                    OccurredAt = now,
                    TraceId = Activity.Current?.TraceId.ToString(),
                });
            }
        }

        context.AddRange(changes);
    }

    private static Guid? EntityId(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        return key is { Properties.Count: 1 } && entry.Property(key.Properties[0].Name).CurrentValue is Guid id ? id : null;
    }

    private static string Describe(EntityEntry entry, string action)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in entry.Properties.Where(property => !TechnicalProperties.Contains(property.Metadata.Name)))
        {
            var name = JsonNamingPolicy.CamelCase.ConvertName(property.Metadata.Name);
            if (action == "Created")
            {
                values[name] = property.CurrentValue;
            }
            else if (action == "Updated" && property.IsModified && !Equals(property.OriginalValue, property.CurrentValue))
            {
                values[name] = new { old = property.OriginalValue, @new = property.CurrentValue };
            }
        }

        return JsonSerializer.Serialize(values);
    }
}
