using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Images;
using Auxilia.Application.Identity;
using Auxilia.Application.Localization.Public;
using Auxilia.Application.Tests.Platform;
using Auxilia.Contracts.Identity;
using Auxilia.Diagnostics;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Identity;
using Auxilia.SharedKernel.Tenancy;

using NSubstitute;

namespace Auxilia.Application.Tests.Identity;

public sealed class ProfileTests : IAsyncDisposable
{
    private readonly InMemoryProfileData data = new();
    private readonly InMemoryIdentityData identity = new();
    private readonly InMemorySessionData sessions;
    private readonly ITenantLanguages languages = Substitute.For<ITenantLanguages>();
    private readonly FakeImageProcessor images = new();
    private readonly ICurrentUser caller = Substitute.For<ICurrentUser>();
    private readonly ManualTimeProvider clock = new();
    private readonly User user;
    private readonly Person person;
    private readonly ProfileManager manager;
    private readonly ProfileQueryService query;

    public ProfileTests()
    {
        sessions = new InMemorySessionData(identity);
        person = Person.Create(
            Guid.CreateVersion7(),
            new PersonDetails("Mario", "Rossi", "mario@example.test", new DateOnly(1980, 1, 1), null, "RSSMRA80A01H501U"),
            new DateOnly(2026, 10, 3)).Value;
        user = User.Create(Guid.CreateVersion7(), person.Id, "mario.rossi", "mario@example.test", "it", [TenantRole.Client], isActive: true).Value;
        data.Users.Add(user);
        data.People.Add(person);
        CallAs(user.Id, TenantRole.Client);

        languages.IsActiveAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(call => call.Arg<string?>() is "it" or "en");

        manager = new ProfileManager(ManagerHarness.Runner(), data, languages, images, caller, clock);
        query = new ProfileQueryService(data, sessions, caller, clock);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await sessions.DisposeAsync();
        await identity.DisposeAsync();
    }

    private void CallAs(Guid? userId, params TenantRole[] roles)
    {
        caller.ActorType.Returns(userId is null ? ActorType.Platform : ActorType.User);
        caller.UserId.Returns(userId);
        caller.Roles.Returns(roles);
    }

    [Fact]
    public async Task Update_ChangesNameEmailAndPhone_KeepsUserNameAndStaffData()
    {
        (await manager.UpdateAsync(new UpdateProfileRequest(" Maria ", "Bianchi", "maria@example.test", "333 1234567"), Ct)).IsSuccess.ShouldBeTrue();

        var profile = (await query.GetAsync(Ct)).Value;
        (profile.FirstName, profile.LastName, profile.Email, profile.Phone, profile.UserName)
            .ShouldBe(("Maria", "Bianchi", "maria@example.test", "333 1234567", "mario.rossi"));
        (person.BirthDate, person.FiscalCode).ShouldBe((new DateOnly(1980, 1, 1), "RSSMRA80A01H501U"));
        user.Email.ShouldBe("maria@example.test");
    }

    [Fact]
    public async Task Update_InvalidValues_AreFieldErrors_AndTheEmailIsRequired()
    {
        var result = await manager.UpdateAsync(new UpdateProfileRequest("", "Bianchi", null, "12"), Ct);

        result.Error!.Code.ShouldBe(EventCodes.Directory.PersonInvalid);
        result.Error.ValidationErrors.Keys.ShouldBe(["firstName", "email", "phone"], ignoreOrder: true);
        user.Email.ShouldBe("mario@example.test");
    }

    [Fact]
    public async Task Language_MustBeActiveForTheTenant()
    {
        (await manager.ChangeLanguageAsync("en", Ct)).IsSuccess.ShouldBeTrue();
        (await manager.ChangeLanguageAsync("de", Ct)).Error!.Code.ShouldBe(EventCodes.Identity.LanguageNotAvailable);
        (await manager.ChangeLanguageAsync(null, Ct)).Error!.ValidationErrors.Keys.ShouldBe(["languageCode"]);

        (await query.GetAsync(Ct)).Value.LanguageCode.ShouldBe("en");
    }

    [Theory]
    [InlineData("Dark", true)]
    [InlineData("Light", true)]
    [InlineData("System", true)]
    [InlineData("dark", false)]
    [InlineData("2", false)]
    [InlineData("", false)]
    public async Task Preferences_AcceptOnlyTheThemeNames(string theme, bool accepted)
    {
        var result = await manager.UpdatePreferencesAsync(new UpdatePreferencesRequest(theme), Ct);

        result.IsSuccess.ShouldBe(accepted);
        if (accepted)
        {
            (await query.GetAsync(Ct)).Value.Theme.ShouldBe(theme);
        }
        else
        {
            result.Error!.ValidationErrors.Keys.ShouldBe(["theme"]);
        }
    }

    [Fact]
    public async Task Image_IsStoredResized_ReplacedAndRemoved()
    {
        (await manager.SetImageAsync([0xFF, 0x00], Ct)).IsSuccess.ShouldBeTrue();
        var first = (await query.GetAsync(Ct)).Value.ImageVersion;
        data.Images.ShouldHaveSingleItem().Content.ShouldBe(new byte[] { 0xFF, 0xD8, 0x01 });

        images.Next = new ProcessedImage([0xFF, 0xD8, 0x02], "image/jpeg", 10, 10);
        (await manager.SetImageAsync([0xFF, 0x01], Ct)).IsSuccess.ShouldBeTrue();
        data.Images.ShouldHaveSingleItem();
        (await query.GetAsync(Ct)).Value.ImageVersion.ShouldNotBe(first);
        (await query.ImageAsync(user.Id, Ct)).Value.Content.ShouldBe(new byte[] { 0xFF, 0xD8, 0x02 });

        (await manager.RemoveImageAsync(Ct)).IsSuccess.ShouldBeTrue();
        (await manager.RemoveImageAsync(Ct)).Error!.Code.ShouldBe(EventCodes.Identity.ProfileImageNotFound);
        (await query.GetAsync(Ct)).Value.ImageVersion.ShouldBeNull();
    }

    [Fact]
    public async Task Image_NotAnImageEmptyOrTooLarge_IsRefused()
    {

        (await manager.SetImageAsync([0x00, 0x01], Ct)).Error!.Code.ShouldBe(EventCodes.Identity.ProfileImageInvalid);
        images.Calls.ShouldBe(1);
        (await manager.SetImageAsync([], Ct)).Error!.Code.ShouldBe(EventCodes.Identity.ProfileImageInvalid);
        (await manager.SetImageAsync(new byte[UserImage.UploadMaxBytes + 1], Ct)).Error!.ValidationErrors.Keys.ShouldBe(["file"]);
        data.Images.ShouldBeEmpty();
    }

    [Fact]
    public async Task ImageOfAnotherUser_OnlyForStaff()
    {
        var other = Guid.CreateVersion7();
        data.Images.Add(UserImage.Create(other, [1], "image/jpeg"));

        (await query.ImageAsync(other, Ct)).Error!.Code.ShouldBe(EventCodes.Identity.ProfileImageNotFound);
        CallAs(user.Id, TenantRole.Employee);
        (await query.ImageAsync(other, Ct)).Value.ContentType.ShouldBe("image/jpeg");
        (await query.ImageAsync(user.Id, Ct)).Error!.Code.ShouldBe(EventCodes.Identity.ProfileImageNotFound);
    }

    [Fact]
    public async Task Sessions_AreTheOpenOnesOfTheCaller_TheCurrentMarked()
    {
        var now = clock.GetUtcNow();
        var current = Session(user.Id, now.AddMinutes(-30));
        var older = Session(user.Id, now.AddHours(-2));
        var expired = Session(user.Id, now.AddDays(-30));
        var ended = Session(user.Id, now.AddMinutes(-5));
        ended.End(now, SessionEndReason.Logout);
        var foreign = Session(Guid.CreateVersion7(), now.AddMinutes(-1));
        sessions.Sessions.AddRange([older, current, expired, ended, foreign]);

        var mine = (await query.SessionsAsync(current.Id, Ct)).Value;

        mine.Select(session => (session.Id, session.IsCurrent)).ShouldBe([(current.Id, true), (older.Id, false)]);
        (mine[0].ClientId, mine[0].IpAddress, mine[0].UserAgent).ShouldBe(("web", "10.0.0.1", "tests"));
        mine[0].ExpiresAt.ShouldBe(current.IdleExpiresAt);
    }

    [Fact]
    public async Task EveryCall_NeedsATenantUserWithAnActiveAccount()
    {
        CallAs(null);
        (await query.GetAsync(Ct)).Error!.Code.ShouldBe(EventCodes.Identity.UserNotFound);
        (await query.SessionsAsync(null, Ct)).Error!.Code.ShouldBe(EventCodes.Identity.UserNotFound);
        (await query.ImageAsync(user.Id, Ct)).Error!.Code.ShouldBe(EventCodes.Identity.ProfileImageNotFound);
        (await manager.ChangeLanguageAsync("en", Ct)).Error!.Code.ShouldBe(EventCodes.Identity.UserNotFound);

        CallAs(user.Id, TenantRole.Client);
        user.SetActive(false);
        (await manager.UpdatePreferencesAsync(new UpdatePreferencesRequest("Dark"), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.UserNotFound);
        (await query.GetAsync(Ct)).Error!.Code.ShouldBe(EventCodes.Identity.UserNotFound);
    }

    /// <summary>A session opened (and last used) at <paramref name="lastUsedAt"/>: idle 8 hours, absolute 14 days.</summary>
    private static RefreshSession Session(Guid userId, DateTimeOffset lastUsedAt) =>
        new(Guid.CreateVersion7(), userId, "web", "stamp", lastUsedAt, lastUsedAt.AddHours(8), lastUsedAt.AddDays(14), "10.0.0.1", "tests");
}

internal sealed class InMemoryProfileData : IProfileDataFactory, IProfileData
{
    public List<User> Users { get; } = [];

    public List<Person> People { get; } = [];

    public List<UserImage> Images { get; } = [];

    public Task<IProfileData> OpenAsync(CancellationToken cancellationToken) => Task.FromResult<IProfileData>(this);

    public Task<User?> FindUserAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(Users.SingleOrDefault(user => user.Id == userId));

    public Task<Person?> FindPersonAsync(Guid personId, CancellationToken cancellationToken) =>
        Task.FromResult(People.SingleOrDefault(person => person.Id == personId));

    public Task<UserImage?> FindImageAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Images.SingleOrDefault(image => image.Id == userId));

    public Task<UserImageInfo?> FindImageInfoAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Images.Where(image => image.Id == userId).Select(image => new UserImageInfo(image.Id, image.Hash)).SingleOrDefault());

    public void Add(UserImage image) => Images.Add(image);

    public void Remove(UserImage image) => Images.Remove(image);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Bytes starting with 0xFF are "an image": <see cref="Next"/> is returned; anything else is refused.</summary>
internal sealed class FakeImageProcessor : IImageProcessor
{
    public ProcessedImage Next { get; set; } = new([0xFF, 0xD8, 0x01], "image/jpeg", 400, 300);

    public int Calls { get; private set; }

    public ProcessedImage? ResizeToJpeg(ReadOnlySpan<byte> content, int maxSide)
    {
        Calls++;
        return maxSide == UserImage.MaxSide && !content.IsEmpty && content[0] == 0xFF ? Next : null;
    }
}
