using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;

using Auxilia.Diagnostics;
using Auxilia.Persistence.Tenant.Operations;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Auxilia.Persistence.Tenant.DataMigrations;

/// <summary>
/// Applies pending data-migrations of a tenant in key order, each in its own transaction, recording them in
/// <c>ops.data_migrations_history</c>. Run by <c>auxctl migrate tenants</c> after the schema migrations (P1-09).
/// </summary>
public sealed partial class DataMigrationRunner
{
    private readonly IReadOnlyList<IDataMigration> migrations;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<DataMigrationRunner> logger;

    public DataMigrationRunner(IEnumerable<IDataMigration> migrations, TimeProvider timeProvider, ILogger<DataMigrationRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(migrations);

        this.migrations = Validate(migrations);
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    public IReadOnlyList<IDataMigration> Migrations => migrations;

    /// <summary>Every concrete <see cref="IDataMigration"/> of the assemblies, ordered by key.</summary>
    public static IReadOnlyList<IDataMigration> Discover(params Assembly[] assemblies) =>
        assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsAbstract: false, IsInterface: false } && typeof(IDataMigration).IsAssignableFrom(type))
            .Select(type => (IDataMigration)Activator.CreateInstance(type)!)
            .OrderBy(migration => migration.Key, StringComparer.Ordinal)
            .ToArray();

    public static bool IsValidKey(string key) => KeyPattern().IsMatch(key);

    /// <summary>Applies the pending data-migrations; returns the keys applied now.</summary>
    public async Task<IReadOnlyList<string>> ApplyPendingAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        var applied = await AppliedKeysAsync(db, cancellationToken);
        var done = new List<string>();

        foreach (var migration in migrations.Where(migration => !applied.Contains(migration.Key)))
        {
            var stopwatch = Stopwatch.StartNew();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                await migration.ApplyAsync(db, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                db.Set<DataMigrationHistoryEntry>().Add(new DataMigrationHistoryEntry
                {
                    Key = migration.Key,
                    Description = migration.Description,
                    AppliedAt = timeProvider.GetUtcNow(),
                    DurationMs = stopwatch.ElapsedMilliseconds,
                });
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Adds meaning (which data-migration failed) and rethrows: the tenant stays on its previous data version.
                Log.Runner.DataMigrationFailed(logger, exception, migration.Key);
                ExceptionLogging.MarkLogged(exception);
                db.ChangeTracker.Clear();
                throw;
            }

            Log.Runner.DataMigrationApplied(logger, migration.Key, stopwatch.ElapsedMilliseconds, migration.Description);
            done.Add(migration.Key);
        }

        return done;
    }

    /// <summary>
    /// For a new tenant after the initial seed: records the data-migrations the seed already covers as applied
    /// (the others — <c>[IncludedInInitialSeed(false)]</c> — stay pending and run next).
    /// </summary>
    public async Task MarkCoveredBySeedAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        var applied = await AppliedKeysAsync(db, cancellationToken);
        var now = timeProvider.GetUtcNow();
        foreach (var migration in migrations.Where(migration => !applied.Contains(migration.Key) && IsCoveredBySeed(migration)))
        {
            db.Set<DataMigrationHistoryEntry>().Add(new DataMigrationHistoryEntry
            {
                Key = migration.Key,
                Description = migration.Description,
                AppliedAt = now,
                CoveredBySeed = true,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The data version of a tenant: the latest applied key (stored in <c>catalog.tenants.data_version</c>).</summary>
    public static async Task<string?> CurrentVersionAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        return await db.Set<DataMigrationHistoryEntry>().AsNoTracking()
            .OrderByDescending(entry => entry.Key)
            .Select(entry => entry.Key)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static bool IsCoveredBySeed(IDataMigration migration) =>
        migration.GetType().GetCustomAttribute<IncludedInInitialSeedAttribute>()?.Included ?? true;

    private static async Task<HashSet<string>> AppliedKeysAsync(TenantDbContext db, CancellationToken cancellationToken) =>
        (await db.Set<DataMigrationHistoryEntry>().AsNoTracking().Select(entry => entry.Key).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

    private static IDataMigration[] Validate(IEnumerable<IDataMigration> migrations)
    {
        var ordered = migrations.OrderBy(migration => migration.Key, StringComparer.Ordinal).ToArray();

        var invalid = ordered.Where(migration => !IsValidKey(migration.Key)).Select(migration => migration.Key).ToArray();
        if (invalid.Length > 0)
        {
            throw new InvalidOperationException("Invalid data-migration keys (expected D_YYYYMMDD_NNN): " + string.Join(", ", invalid));
        }

        var duplicates = ordered.GroupBy(migration => migration.Key).Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        if (duplicates.Length > 0)
        {
            throw new InvalidOperationException("Duplicate data-migration keys: " + string.Join(", ", duplicates));
        }

        return ordered;
    }

    [GeneratedRegex(@"^D_\d{8}_\d{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}
