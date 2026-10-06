using Auxilia.Application.Abstractions.Persistence;
using Auxilia.Persistence.Tenant.Operations;
using Auxilia.SharedKernel.Identifiers;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.MigrationRunner.LegacyImport;

/// <summary>
/// Legacy <c>int</c> id → new Guid of one tenant (<c>ops.legacy_id_map</c>), keyed by the legacy table name. A row
/// already mapped keeps its Guid, so a run repeated or limited with <c>--since</c> updates the same records instead of
/// duplicating them; every legacy foreign key is resolved through here. New mappings are added to the context and saved
/// with the rows they belong to (same transaction).
/// </summary>
internal sealed class LegacyIdMap
{
    private readonly ITenantDbContext db;
    private readonly TimeProvider timeProvider;
    private readonly Dictionary<(string Entity, int LegacyId), Guid> ids;

    private LegacyIdMap(ITenantDbContext db, TimeProvider timeProvider, Dictionary<(string Entity, int LegacyId), Guid> ids)
    {
        this.db = db;
        this.timeProvider = timeProvider;
        this.ids = ids;
    }

    public static async Task<LegacyIdMap> LoadAsync(ITenantDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        var rows = await db.Set<LegacyIdMapping>().AsNoTracking()
            .Select(mapping => new { mapping.Entity, mapping.LegacyId, mapping.NewId })
            .ToListAsync(cancellationToken);
        return new LegacyIdMap(db, timeProvider, rows.ToDictionary(row => (row.Entity, row.LegacyId), row => row.NewId));
    }

    /// <summary>The Guid of an already mapped legacy row, or <c>null</c>.</summary>
    public Guid? Find(string entity, int legacyId) =>
        ids.TryGetValue((Known(entity), legacyId), out var id) ? id : null;

    /// <summary>The Guid of a legacy row, creating (and staging) the mapping the first time.</summary>
    public (Guid Id, bool Created) GetOrAdd(string entity, int legacyId)
    {
        if (ids.TryGetValue((Known(entity), legacyId), out var existing))
        {
            return (existing, false);
        }

        var id = NewId();
        Add(entity, legacyId, id);
        return (id, true);
    }

    /// <summary>Records the Guid of a legacy row created now (after its record was built successfully).</summary>
    public void Add(string entity, int legacyId, Guid id)
    {
        if (!ids.TryAdd((Known(entity), legacyId), id))
        {
            throw new InvalidOperationException($"{entity} {legacyId} is already mapped");
        }

        db.Set<LegacyIdMapping>().Add(new LegacyIdMapping
        {
            Entity = entity,
            LegacyId = legacyId,
            NewId = id,
            ImportedAt = timeProvider.GetUtcNow(),
        });
    }

    /// <summary>A Guid v7 for a record about to be created (recorded with <see cref="Add"/> once it is valid).</summary>
    public Guid NewId() => IdGenerator.New(timeProvider);

    /// <summary>Mapped rows per legacy table.</summary>
    public IReadOnlyDictionary<string, int> Counts() =>
        ids.Keys.GroupBy(key => key.Entity, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

    /// <summary>Mapped rows per legacy table, read without loading the map.</summary>
    public static async Task<IReadOnlyDictionary<string, int>> CountsAsync(ITenantDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        return await db.Set<LegacyIdMapping>().AsNoTracking()
            .GroupBy(mapping => mapping.Entity)
            .Select(group => new { Entity = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Entity, row => row.Count, StringComparer.Ordinal, cancellationToken);
    }

    private static string Known(string entity) =>
        LegacyTables.All.Any(table => table.Name == entity && table.Disposition != LegacyDisposition.Excluded)
            ? entity
            : throw new ArgumentException($"{entity} is not a migrated legacy table", nameof(entity));
}
