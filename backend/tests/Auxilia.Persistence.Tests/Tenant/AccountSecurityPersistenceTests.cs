using Auxilia.Application;
using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Identity;
using Auxilia.Domain.Platform;
using Auxilia.Infrastructure;
using Auxilia.Persistence.Tenant;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

namespace Auxilia.Persistence.Tests.Tenant;

/// <summary>F35 on PostgreSQL: password history stored with the user, login attempts filtered and paged in the database.</summary>
[Collection(TenantDatabaseGroup.Name)]
public sealed class AccountSecurityPersistenceTests(TenantDatabaseFixture database)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 10, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PasswordHistory_IsStoredAndTrimmedWithTheUser()
    {
        var userId = Guid.CreateVersion7();
        await using (var db = database.CreateContext())
        {
            var person = new Person(Guid.CreateVersion7(), "Anna", "Bianchi", null);
            db.Set<Person>().Add(person);
            var user = User.Create(userId, person.Id, "history." + Guid.NewGuid().ToString("N")[..6], null, "it", [TenantRole.Client], true).Value;
            for (var index = 0; index < User.MaxPasswordHistory + 2; index++)
            {
                user.SetPassword($"hash-{index}", index == 0 ? PasswordFormat.LegacyBcrypt : PasswordFormat.Identity, Now.AddMinutes(index));
            }

            db.Set<User>().Add(user);
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = database.CreateContext())
        {
            var user = await db.Set<User>().SingleAsync(item => item.Id == userId, Ct);
            user.PasswordHistory.Count.ShouldBe(User.MaxPasswordHistory);
            user.PasswordHistory[0].PasswordHash.ShouldBe($"hash-{User.MaxPasswordHistory + 1}");
            user.SetPassword("hash-new", PasswordFormat.Identity, Now.AddHours(1));
            await db.SaveChangesAsync(Ct);
        }

        await using var read = database.CreateContext();
        (await read.Set<User>().AsNoTracking().SingleAsync(item => item.Id == userId, Ct)).PasswordHistory[0].PasswordHash.ShouldBe("hash-new");
        (await read.Database.SqlQueryRaw<int>("select count(*)::int as \"Value\" from identity.password_history where user_id = {0}", userId).SingleAsync(Ct))
            .ShouldBe(User.MaxPasswordHistory);
    }

    [Fact]
    public async Task LoginAttempts_AreFilteredSortedAndPaged()
    {
        var marker = "audit" + Guid.NewGuid().ToString("N")[..6];
        await using (var db = database.CreateContext())
        {
            for (var index = 0; index < 5; index++)
            {
                db.Set<LoginAttempt>().Add(new LoginAttempt(
                    Guid.CreateVersion7(), null, $"{marker}.{(char)('a' + index)}", index % 2 == 0 ? "password" : "email-otp", Now.AddMinutes(index),
                    index % 2 == 0, index % 2 == 0 ? null : "InvalidOtp", "10.0.0.1", null));
            }

            await db.SaveChangesAsync(Ct);
        }

        await using var services = Services();
        await using var scope = services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<ILoginAttemptReader>();

        var all = await reader.ListAsync(new LoginAttemptQuery(marker.ToUpperInvariant(), null, null, null, null, null, 1, 2), Ct);
        all.TotalCount.ShouldBe(5);
        all.Items.Select(item => item.UserName).ShouldBe([$"{marker}.e", $"{marker}.d"]);

        var failedOtp = await reader.ListAsync(new LoginAttemptQuery(marker, "email-otp", false, null, null, "userName", 1, 10), Ct);
        failedOtp.Items.Select(item => item.UserName).ShouldBe([$"{marker}.b", $"{marker}.d"]);

        var range = await reader.ListAsync(new LoginAttemptQuery(marker, null, null, Now.AddMinutes(1), Now.AddMinutes(3), "attemptedAt", 1, 10), Ct);
        range.Items.Select(item => item.UserName).ShouldBe([$"{marker}.b", $"{marker}.c", $"{marker}.d"]);

        (await reader.ListAsync(new LoginAttemptQuery("%", null, null, null, null, null, 1, 10), Ct)).Items
            .ShouldAllBe(item => item.UserName.Contains('%', StringComparison.Ordinal));
    }

    private ServiceProvider Services()
    {
        var tenant = new TenantInfo(Guid.Parse("0199a0b2-0000-7000-8000-0000000001d0"), "tenant-test", TenantStatus.Active, "it", "Europe/Rome");
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
        services.AddLogging();
        services.AddInfrastructure();
        services.AddScoped(_ => tenantContext);
        services.AddScoped(_ => directory);
        services.AddSingleton(protector);
        services.AddScoped(_ => user);
        services.AddScoped(_ => Substitute.For<IPlatformSettingStore>());
        services.AddSingleton(Substitute.For<ISettingSecretProtector>());
        services.AddApplication();
        services.AddTenantPersistence();
        return services.BuildServiceProvider(validateScopes: true);
    }
}
