using Auxilia.Domain.Directory;
using Auxilia.Domain.Identity;
using Auxilia.Persistence.Tenant.Identity;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Persistence.Tests.Tenant;

/// <summary>F04 and Q35 on PostgreSQL: theme and language of the account, the profile picture (one per user).</summary>
[Collection(TenantDatabaseGroup.Name)]
public sealed class ProfilePersistenceTests(TenantDatabaseFixture database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ThemeLanguageAndPicture_RoundTrip()
    {
        var userId = await AddUserAsync();

        await using (var data = new ProfileData(database.CreateContext()))
        {
            var user = (await data.FindUserAsync(userId, Ct))!;
            user.Theme.ShouldBe(UserTheme.System);
            user.ChangeTheme(UserTheme.Dark);
            user.ChangeLanguage("en");
            (await data.FindImageInfoAsync(userId, Ct)).ShouldBeNull();
            data.Add(UserImage.Create(userId, [0xFF, 0xD8, 0x01], "image/jpeg"));
            await data.SaveChangesAsync(Ct);
        }

        string firstHash;
        await using (var data = new ProfileData(database.CreateContext()))
        {
            var user = (await data.FindUserAsync(userId, Ct))!;
            (user.Theme, user.LanguageCode).ShouldBe((UserTheme.Dark, "en"));
            (await data.FindPersonAsync(user.PersonId, Ct)).ShouldNotBeNull();
            var image = (await data.FindImageAsync(userId, Ct))!;
            image.ContentType.ShouldBe("image/jpeg");
            image.Content.ShouldBe(new byte[] { 0xFF, 0xD8, 0x01 });
            firstHash = image.Hash;
            (await data.FindImageInfoAsync(userId, Ct))!.Hash.ShouldBe(firstHash);
            image.Replace([0xFF, 0xD8, 0x02], "image/jpeg");
            await data.SaveChangesAsync(Ct);
        }

        await using (var data = new ProfileData(database.CreateContext()))
        {
            var image = (await data.FindImageAsync(userId, Ct))!;
            image.Hash.ShouldNotBe(firstHash);
            data.Remove(image);
            await data.SaveChangesAsync(Ct);
            (await data.FindImageInfoAsync(userId, Ct)).ShouldBeNull();
        }
    }

    private async Task<Guid> AddUserAsync()
    {
        await using var db = database.CreateContext();
        var person = new Person(Guid.CreateVersion7(), "Mario", "Rossi", "mario@example.test");
        db.Set<Person>().Add(person);
        var user = User.Create(Guid.CreateVersion7(), person.Id, "profile-" + Guid.NewGuid().ToString("N")[..10], null, "it", [TenantRole.Client], isActive: true).Value;
        db.Set<User>().Add(user);
        await db.SaveChangesAsync(Ct);
        return user.Id;
    }
}
