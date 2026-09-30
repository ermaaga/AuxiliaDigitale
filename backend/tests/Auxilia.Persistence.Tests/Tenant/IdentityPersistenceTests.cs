using Auxilia.Application;
using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Identity;
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

/// <summary>F01 on PostgreSQL: case-insensitive user names, legacy BCrypt upgraded on sign-in, roles and one account per person.</summary>
[Collection(TenantDatabaseGroup.Name)]
public sealed class IdentityPersistenceTests(TenantDatabaseFixture database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LegacyUser_SignsInCaseInsensitivelyAndTheHashIsUpgraded()
    {
        var userName = "mario.rossi." + Guid.NewGuid().ToString("N")[..6];
        var userId = await CreateLegacyUserAsync(userName, BCrypt.Net.BCrypt.HashPassword("Legacy!2024", workFactor: 4));

        await using var services = Services();
        await using (var scope = services.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IPasswordAuthenticator>().AuthenticateAsync(userName.ToUpperInvariant(), "Legacy!2024", Ct);
            result.Value.UserId.ShouldBe(userId);
            result.Value.Roles.ShouldBe([TenantRole.Administrator, TenantRole.Employee]);
        }

        await using var db = database.CreateContext();
        var user = await db.Set<User>().AsNoTracking().SingleAsync(item => item.Id == userId, Ct);
        user.PasswordFormat.ShouldBe(PasswordFormat.Identity);
        user.PasswordHash!.ShouldNotStartWith("$2");
        user.LastLoginAt.ShouldNotBeNull();

        await using var again = services.CreateAsyncScope();
        (await again.ServiceProvider.GetRequiredService<IPasswordAuthenticator>().AuthenticateAsync(userName, "Legacy!2024", Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Manager_CreatesTheAccountAndSetsThePassword()
    {
        var personId = await AddPersonAsync();
        await using var services = Services();
        await using var scope = services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IUserAccountManager>();
        var userName = "anna." + Guid.NewGuid().ToString("N")[..6] + "@example.test";

        var userId = (await accounts.CreateAsync(new CreateUser(personId, userName, userName, "it", [TenantRole.Client], IsActive: true), Ct)).Value;
        (await accounts.CreateAsync(new CreateUser(await AddPersonAsync(), userName.ToUpperInvariant(), null, "it", [TenantRole.Client], true), Ct))
            .Error!.Code.ShouldBe(Diagnostics.EventCodes.Identity.UserNameTaken);
        (await accounts.SetPasswordAsync(userId, "A brand new Passw0rd!", Ct)).IsSuccess.ShouldBeTrue();

        (await scope.ServiceProvider.GetRequiredService<IPasswordAuthenticator>().AuthenticateAsync(userName, "A brand new Passw0rd!", Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Database_EnforcesUniqueCaseInsensitiveUserNamesAndOneAccountPerPerson()
    {
        var personId = await AddPersonAsync();
        var userName = "unique." + Guid.NewGuid().ToString("N")[..6];
        await using (var db = database.CreateContext())
        {
            db.Set<User>().Add(User.Create(Guid.CreateVersion7(), personId, userName, null, "it", [TenantRole.Client], true).Value);
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = database.CreateContext())
        {
            db.Set<User>().Add(User.Create(Guid.CreateVersion7(), await AddPersonAsync(), userName.ToUpperInvariant(), null, "it", [TenantRole.Client], true).Value);
            await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        }

        await using (var db = database.CreateContext())
        {
            db.Set<User>().Add(User.Create(Guid.CreateVersion7(), personId, userName + "x", null, "it", [TenantRole.Client], true).Value);
            await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        }

        await using var read = database.CreateContext();
        (await read.Set<User>().CountAsync(item => item.PersonId == personId, Ct)).ShouldBe(1);
    }

    private async Task<Guid> AddPersonAsync()
    {
        await using var db = database.CreateContext();
        var person = new Person(Guid.CreateVersion7(), "Mario", "Rossi", null);
        db.Set<Person>().Add(person);
        await db.SaveChangesAsync(Ct);
        return person.Id;
    }

    private async Task<Guid> CreateLegacyUserAsync(string userName, string bcryptHash)
    {
        var personId = await AddPersonAsync();
        await using var db = database.CreateContext();
        var user = User.Create(Guid.CreateVersion7(), personId, userName, null, "it", [TenantRole.Employee, TenantRole.Administrator], isActive: true).Value;
        user.SetPassword(bcryptHash, PasswordFormat.LegacyBcrypt, DateTimeOffset.UtcNow);
        db.Set<User>().Add(user);
        await db.SaveChangesAsync(Ct);
        return user.Id;
    }

    private ServiceProvider Services()
    {
        var tenant = new TenantInfo(Guid.Parse("0199a0b2-0000-7000-8000-0000000001d0"), "tenant-test", TenantStatus.Active, "it", "Europe/Rome");
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.Current.Returns(tenant);
        tenantContext.Tenant.Returns(tenant);
        tenantContext.IsResolved.Returns(true);
        var directory = Substitute.For<ITenantDirectory>();
        directory.GetProtectedConnectionStringAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns("protected");
        var protector = Substitute.For<ITenantConnectionProtector>();
        protector.Unprotect("protected").Returns(database.ConnectionString);
        var platform = Substitute.For<IPlatformSettingStore>();
        platform.GetValuesAsync(Arg.Any<CancellationToken>()).Returns(new Dictionary<string, string>());
        var user = Substitute.For<ICurrentUser>();
        user.ActorType.Returns(ActorType.System);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure();
        services.AddScoped(_ => tenantContext);
        services.AddScoped(_ => directory);
        services.AddSingleton(protector);
        services.AddScoped(_ => user);
        services.AddScoped(_ => platform);
        services.AddSingleton(Substitute.For<ISettingSecretProtector>());
        services.AddApplication();
        services.AddTenantPersistence();
        return services.BuildServiceProvider(validateScopes: true);
    }
}
