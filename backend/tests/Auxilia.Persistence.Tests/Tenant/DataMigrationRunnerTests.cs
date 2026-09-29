using Auxilia.Persistence.Tenant;
using Auxilia.Persistence.Tenant.DataMigrations;
using Auxilia.Persistence.Tenant.Operations;
using Auxilia.Persistence.Tenant.Seed;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auxilia.Persistence.Tests.Tenant;

[Collection(TenantDatabaseGroup.Name)]
public sealed class DataMigrationRunnerTests(TenantDatabaseFixture database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ApplyPending_TwiceAppliesOnceAndIsIdempotent()
    {
        var scope = Scope();
        var runner = Runner(new SeedSequence("D_20000101_001", scope), new SeedSequence("D_20000101_002", scope));
        await using var db = database.CreateContext();

        var first = await runner.ApplyPendingAsync(db, Ct);
        var second = await runner.ApplyPendingAsync(db, Ct);

        first.ShouldBe(["D_20000101_001", "D_20000101_002"]);
        second.ShouldBeEmpty();
        (await db.Set<NumberSequence>().CountAsync(sequence => sequence.Scope == scope, Ct)).ShouldBe(1);
        (await db.Set<DataMigrationHistoryEntry>().CountAsync(entry => entry.Key.StartsWith("D_20000101_"), Ct)).ShouldBe(2);
    }

    [Fact]
    public async Task ApplyPending_EveryMigrationTwiceLeavesTheSameState()
    {
        // The rule of skill auxilia-data-migration: running a data-migration twice changes nothing the second time.
        var scope = Scope();
        var migration = new SeedSequence("D_20000102_001", scope);
        await using var db = database.CreateContext();

        await migration.ApplyAsync(db, Ct);
        await db.SaveChangesAsync(Ct);
        await migration.ApplyAsync(db, Ct);
        await db.SaveChangesAsync(Ct);

        (await db.Set<NumberSequence>().SingleAsync(sequence => sequence.Scope == scope, Ct)).LastValue.ShouldBe(0);
    }

    [Fact]
    public async Task ApplyPending_Failure_RollsBackAndIsNotRecorded()
    {
        var scope = Scope();
        var runner = Runner(new SeedSequence("D_20000103_001", scope), new Failing("D_20000103_002", scope));
        await using var db = database.CreateContext();

        await Should.ThrowAsync<InvalidOperationException>(() => runner.ApplyPendingAsync(db, Ct));

        (await db.Set<DataMigrationHistoryEntry>().AnyAsync(entry => entry.Key == "D_20000103_001", Ct)).ShouldBeTrue();
        (await db.Set<DataMigrationHistoryEntry>().AnyAsync(entry => entry.Key == "D_20000103_002", Ct)).ShouldBeFalse();
        (await db.Set<NumberSequence>().AnyAsync(sequence => sequence.Scope == scope + "-failed", Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task InitialSeed_MarksCoveredMigrationsAndRunsTheOthers()
    {
        var scope = Scope();
        var covered = new SeedSequence("D_20000104_001", scope);
        var notCovered = new AlwaysRuns("D_20000104_002", scope + "-new");
        var runner = Runner(covered, notCovered);
        await using var db = database.CreateContext();

        await new TenantInitialSeed(runner).ApplyAsync(db, Ct);

        var history = await db.Set<DataMigrationHistoryEntry>().Where(entry => entry.Key.StartsWith("D_20000104_")).ToListAsync(Ct);
        history.Single(entry => entry.Key == covered.Key).CoveredBySeed.ShouldBeTrue();
        history.Single(entry => entry.Key == notCovered.Key).CoveredBySeed.ShouldBeFalse();
        (await db.Set<NumberSequence>().AnyAsync(sequence => sequence.Scope == scope, Ct)).ShouldBeFalse();
        (await db.Set<NumberSequence>().AnyAsync(sequence => sequence.Scope == scope + "-new", Ct)).ShouldBeTrue();
        (await DataMigrationRunner.CurrentVersionAsync(db, Ct)).ShouldNotBeNull();
    }

    [Theory]
    [InlineData("D_2026092_001")]
    [InlineData("S_20260929_001")]
    [InlineData("D_20260929_1")]
    public void Constructor_InvalidKey_Throws(string key)
    {
        Should.Throw<InvalidOperationException>(() => Runner(new SeedSequence(key, "x")));
    }

    [Fact]
    public void Constructor_DuplicateKey_Throws()
    {
        Should.Throw<InvalidOperationException>(() => Runner(new SeedSequence("D_20000105_001", "a"), new SeedSequence("D_20000105_001", "b")));
    }

    private static DataMigrationRunner Runner(params IDataMigration[] migrations) =>
        new(migrations, TimeProvider.System, NullLogger<DataMigrationRunner>.Instance);

    private static string Scope() => "t" + Guid.NewGuid().ToString("N")[..12];

    /// <summary>Idempotent by natural key: inserts the sequence row only if missing.</summary>
    private class SeedSequence(string key, string scope) : IDataMigration
    {
        public string Key { get; } = key;

        public string Description => "Seed a number sequence";

        public virtual Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken) =>
            db.Database.ExecuteSqlAsync(
                $"insert into ops.number_sequences (scope, year, last_value) values ({scope}, 2026, 0) on conflict do nothing",
                cancellationToken);
    }

    [IncludedInInitialSeed(false)]
    private sealed class AlwaysRuns(string key, string scope) : SeedSequence(key, scope);

    private sealed class Failing(string key, string scope) : IDataMigration
    {
        public string Key { get; } = key;

        public string Description => "Fails after writing";

        public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
        {
            await db.Database.ExecuteSqlAsync(
                $"insert into ops.number_sequences (scope, year, last_value) values ({scope + "-failed"}, 2026, 0)", cancellationToken);
            throw new InvalidOperationException("broken data-migration");
        }
    }
}
