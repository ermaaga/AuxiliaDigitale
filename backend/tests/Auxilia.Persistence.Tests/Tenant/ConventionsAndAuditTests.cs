using System.Text.Json;

using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Persistence.Tenant;
using Auxilia.Persistence.Tenant.Conventions;
using Auxilia.Persistence.Tenant.Interceptors;
using Auxilia.Persistence.Tenant.Operations;
using Auxilia.SharedKernel.Domain;

using Microsoft.EntityFrameworkCore;

using NSubstitute;

namespace Auxilia.Persistence.Tests.Tenant;

/// <summary>Audit columns, entity history, soft delete and xmin on a sample aggregate using the tenant conventions.</summary>
[Collection(TenantDatabaseGroup.Name)]
public sealed class ConventionsAndAuditTests(TenantDatabaseFixture database) : IAsyncLifetime
{
    private static readonly Guid UserId = Guid.Parse("0199a0b2-0000-7000-8000-0000000000aa");
    private string connectionString = string.Empty;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        connectionString = await database.CreateDatabaseAsync("conventions_" + Guid.NewGuid().ToString("N")[..8]);
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync(Ct);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Insert_FillsAuditColumnsAndRecordsCreation()
    {
        var sample = new Sample(Guid.CreateVersion7(), "first");

        await using (var db = CreateContext())
        {
            db.Samples.Add(sample);
            await db.SaveChangesAsync(Ct);
        }

        await using var read = CreateContext();
        var stored = await read.Samples.SingleAsync(item => item.Id == sample.Id, Ct);
        read.Entry(stored).Property<Guid?>(TenantConventions.CreatedBy).CurrentValue.ShouldBe(UserId);
        read.Entry(stored).Property<uint>(TenantConventions.Version).CurrentValue.ShouldBeGreaterThan(0u);
        var change = await read.Set<EntityChange>().SingleAsync(item => item.EntityId == sample.Id, Ct);
        change.Action.ShouldBe("Created");
        change.EntityType.ShouldBe(nameof(Sample));
        change.ActorType.ShouldBe("User");
        change.ActorId.ShouldBe(UserId);
        JsonDocument.Parse(change.Changes).RootElement.GetProperty("name").GetString().ShouldBe("first");
    }

    [Fact]
    public async Task Update_RecordsOldAndNewValues()
    {
        var sample = await InsertAsync("before");

        await using (var db = CreateContext())
        {
            var loaded = await db.Samples.SingleAsync(item => item.Id == sample.Id, Ct);
            loaded.Rename("after");
            await db.SaveChangesAsync(Ct);
        }

        await using var read = CreateContext();
        var change = await read.Set<EntityChange>().SingleAsync(item => item.EntityId == sample.Id && item.Action == "Updated", Ct);
        var name = JsonDocument.Parse(change.Changes).RootElement.GetProperty("name");
        name.GetProperty("old").GetString().ShouldBe("before");
        name.GetProperty("new").GetString().ShouldBe("after");
        var stored = await read.Samples.SingleAsync(item => item.Id == sample.Id, Ct);
        read.Entry(stored).Property<Guid?>(TenantConventions.UpdatedBy).CurrentValue.ShouldBe(UserId);
    }

    [Fact]
    public async Task Delete_IsSoftAndHiddenFromQueries()
    {
        var sample = await InsertAsync("to delete");

        await using (var db = CreateContext())
        {
            db.Samples.Remove(await db.Samples.SingleAsync(item => item.Id == sample.Id, Ct));
            await db.SaveChangesAsync(Ct);
        }

        await using var read = CreateContext();
        (await read.Samples.AnyAsync(item => item.Id == sample.Id, Ct)).ShouldBeFalse();
        var hidden = await read.Samples.IgnoreQueryFilters().SingleAsync(item => item.Id == sample.Id, Ct);
        read.Entry(hidden).Property<bool>(TenantConventions.IsDeleted).CurrentValue.ShouldBeTrue();
        read.Entry(hidden).Property<Guid?>(TenantConventions.DeletedBy).CurrentValue.ShouldBe(UserId);
        (await read.Set<EntityChange>().AnyAsync(item => item.EntityId == sample.Id && item.Action == "Deleted", Ct)).ShouldBeTrue();
    }

    [Fact]
    public async Task Update_WithStaleVersion_ThrowsConcurrencyException()
    {
        var sample = await InsertAsync("shared");
        await using var first = CreateContext();
        await using var second = CreateContext();
        var a = await first.Samples.SingleAsync(item => item.Id == sample.Id, Ct);
        var b = await second.Samples.SingleAsync(item => item.Id == sample.Id, Ct);

        a.Rename("first wins");
        await first.SaveChangesAsync(Ct);
        b.Rename("second loses");

        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(Ct));
    }

    [Fact]
    public void Conventions_DetectAggregateRoots()
    {
        TenantConventions.IsAggregateRoot(typeof(Sample)).ShouldBeTrue();
        TenantConventions.IsAggregateRoot(typeof(EntityChange)).ShouldBeFalse();
    }

    private async Task<Sample> InsertAsync(string name)
    {
        var sample = new Sample(Guid.CreateVersion7(), name);
        await using var db = CreateContext();
        db.Samples.Add(sample);
        await db.SaveChangesAsync(Ct);
        return sample;
    }

    private SampleContext CreateContext()
    {
        var user = Substitute.For<ICurrentUser>();
        user.ActorType.Returns(ActorType.User);
        user.UserId.Returns(UserId);

        var options = new DbContextOptionsBuilder<SampleContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantAuditInterceptor(user, TimeProvider.System))
            .Options;
        return new SampleContext(options);
    }

    public sealed class Sample : AggregateRoot<Guid>, IAuditable, ISoftDeletable
    {
        public Sample(Guid id, string name)
            : base(id) => Name = name;

        private Sample() => Name = string.Empty;

        public string Name { get; private set; }

        public void Rename(string name) => Name = name;
    }

    private sealed class SampleContext(DbContextOptions<SampleContext> options) : DbContext(options)
    {
        public DbSet<Sample> Samples => Set<Sample>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Sample>(sample =>
            {
                sample.ToTable("samples", "test");
                sample.Property(item => item.Id).ValueGeneratedNever();
                sample.Property(item => item.Name).HasMaxLength(100);
            });
            modelBuilder.ApplyConfiguration(new EntityChangeConfiguration());
            TenantConventions.Apply(modelBuilder);
        }
    }
}
