using Auxilia.Application.Abstractions.Persistence;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Platform;
using Auxilia.Contracts.Platform;
using Auxilia.Domain.Identity;
using Auxilia.Domain.Platform;
using Auxilia.Persistence.Catalog;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Auxilia.Worker.IntegrationTests;

/// <summary>
/// S-01: a tenant created from the console is provisioned by the Worker (<c>ProvisionTenantCommand</c> on
/// <c>auxilia.platform</c>): database, schema and seed, Active, and the first Administrator inside the new tenant
/// with the invitation pending (no sending account yet).
/// </summary>
[Collection(BusGroup.Name)]
public sealed class ProvisioningTests(BusFixture bus)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreatedTenant_IsProvisionedByTheWorker_WithItsFirstAdministrator()
    {
        var slug = "prov-" + Guid.NewGuid().ToString("N")[..8];
        await using (var scope = bus.Api.CreateAsyncScope())
        {
            var created = await scope.ServiceProvider.GetRequiredService<IPlatformTenantManager>().CreateAsync(
                new CreatePlatformTenantRequest(slug, "Studio Prov", "it", "Europe/Rome", new TenantAdministratorInvite("anna@prov.test", "Anna", "Prov")), Ct);
            created.IsSuccess.ShouldBeTrue();
        }

        await EventuallyAsync(async () =>
        {
            await using var scope = bus.Api.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            return await db.Tenants.AnyAsync(tenant => tenant.Slug == slug && tenant.Status == TenantStatus.Active, Ct);
        }, TimeSpan.FromSeconds(90));

        // The Administrator is created right after the tenant becomes Active, in the same handler.
        await EventuallyAsync(async () =>
        {
            await using var scope = bus.Api.CreateAsyncScope();
            var tenant = await scope.ServiceProvider.GetRequiredService<ITenantDirectory>().FindBySlugAsync(slug, Ct);
            scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().Set(tenant!);
            await using var db = await scope.ServiceProvider.GetRequiredService<ITenantDbContextFactory>().CreateAsync(Ct);
            var admin = await db.Set<User>().SingleOrDefaultAsync(user => user.UserName == "anna@prov.test", Ct);
            return admin is not null && admin.Roles.SequenceEqual([TenantRole.Administrator]) && admin.PasswordHash is null;
        }, TimeSpan.FromSeconds(30));

        await using var check = bus.Api.CreateAsyncScope();
        var catalog = check.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var stored = await catalog.Tenants.SingleAsync(tenant => tenant.Slug == slug, Ct);
        stored.SchemaVersion.ShouldNotBeNull();
        (await catalog.MigrationRuns.SingleAsync(run => run.TenantId == stored.Id, Ct)).Status.ShouldBe(MigrationRunStatus.Succeeded);
    }

    private static async Task EventuallyAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition not met in time.");
            }

            await Task.Delay(250, Ct);
        }
    }
}
