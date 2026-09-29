using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Persistence;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Domain.Platform;
using Auxilia.Persistence.Tenant;
using Auxilia.Persistence.Tenant.Operations;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

namespace Auxilia.Persistence.Tests.Tenant;

/// <summary>The write transaction of an operation spans every tenant context it creates (ADR 0004).</summary>
[Collection(TenantDatabaseGroup.Name)]
public sealed class OperationTransactionTests(TenantDatabaseFixture database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Commit_PersistsWritesOfEveryContext()
    {
        await using var services = Services();
        await using var scope = services.CreateAsyncScope();
        var (transactions, factory) = Resolve(scope);
        var first = NewRun();
        var second = NewRun();

        await using (var transaction = await transactions.BeginAsync(Ct))
        {
            await AddAsync(factory, first);
            await AddAsync(factory, second);
            await transaction.CommitAsync(Ct);
        }

        (await ExistsAsync(first)).ShouldBeTrue();
        (await ExistsAsync(second)).ShouldBeTrue();
    }

    [Fact]
    public async Task DisposeWithoutCommit_RollsBack()
    {
        await using var services = Services();
        await using var scope = services.CreateAsyncScope();
        var (transactions, factory) = Resolve(scope);
        var run = NewRun();

        await using (await transactions.BeginAsync(Ct))
        {
            await AddAsync(factory, run);
        }

        (await ExistsAsync(run)).ShouldBeFalse();
    }

    [Fact]
    public async Task NestedOperation_JoinsTheOuterTransaction()
    {
        await using var services = Services();
        await using var scope = services.CreateAsyncScope();
        var (transactions, factory) = Resolve(scope);
        var run = NewRun();

        await using (await transactions.BeginAsync(Ct))
        {
            await using (var inner = await transactions.BeginAsync(Ct))
            {
                await AddAsync(factory, run);
                await inner.CommitAsync(Ct);
            }

            // outer not committed
        }

        (await ExistsAsync(run)).ShouldBeFalse();
    }

    [Fact]
    public async Task OutsideAnOperation_SaveChangesCommitsImmediately()
    {
        await using var services = Services();
        await using var scope = services.CreateAsyncScope();
        var (_, factory) = Resolve(scope);
        var run = NewRun();

        await AddAsync(factory, run);

        (await ExistsAsync(run)).ShouldBeTrue();
    }

    [Fact]
    public async Task StaleVersion_IsClassifiedAsConcurrencyConflict()
    {
        var classifier = Services().GetServices<IExceptionClassifier>().Single();

        classifier.Classify(new DbUpdateConcurrencyException("stale"))!.Code.ShouldBe(Diagnostics.EventCodes.Host.ConcurrencyConflict);
        classifier.Classify(new InvalidOperationException()).ShouldBeNull();
    }

    private ServiceProvider Services()
    {
        var tenant = new TenantInfo(Guid.CreateVersion7(), "tenant-test", TenantStatus.Active, "it", "Europe/Rome");
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.Current.Returns(tenant);
        tenantContext.Tenant.Returns(tenant);
        var directory = Substitute.For<ITenantDirectory>();
        directory.GetProtectedConnectionStringAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns("protected");
        var protector = Substitute.For<ITenantConnectionProtector>();
        protector.Unprotect("protected").Returns(database.ConnectionString);
        var user = Substitute.For<ICurrentUser>();
        user.ActorType.Returns(ActorType.System);

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(TimeProvider.System);
        services.AddScoped(_ => tenantContext);
        services.AddScoped(_ => directory);
        services.AddSingleton(protector);
        services.AddScoped(_ => user);
        services.AddTenantPersistence();
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static (IOperationTransactionFactory Transactions, ITenantDbContextFactory Factory) Resolve(AsyncServiceScope scope) =>
        (scope.ServiceProvider.GetRequiredService<IOperationTransactionFactory>(), scope.ServiceProvider.GetRequiredService<ITenantDbContextFactory>());

    private static async Task AddAsync(ITenantDbContextFactory factory, JobRun run)
    {
        await using var db = await factory.CreateAsync(Ct);
        db.Set<JobRun>().Add(run);
        await db.SaveChangesAsync(Ct);
    }

    private async Task<bool> ExistsAsync(JobRun run)
    {
        await using var db = database.CreateContext();
        return await db.Set<JobRun>().AnyAsync(item => item.Id == run.Id, Ct);
    }

    private static JobRun NewRun() => new()
    {
        Id = Guid.CreateVersion7(),
        JobCode = "test.job",
        Status = JobRunStatus.Running,
        StartedAt = DateTimeOffset.UtcNow,
        ActorType = "System",
    };
}
