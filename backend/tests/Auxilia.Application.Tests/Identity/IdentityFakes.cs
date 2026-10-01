using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Identity;
using Auxilia.SharedKernel.Tenancy;

using NSubstitute;

namespace Auxilia.Application.Tests.Identity;

internal sealed class InMemoryIdentityData : IIdentityDataFactory, IIdentityData
{
    public List<User> Users { get; } = [];

    public HashSet<Guid> People { get; } = [];

    public int Saves { get; private set; }

    public Task<IIdentityData> OpenAsync(CancellationToken cancellationToken) => Task.FromResult<IIdentityData>(this);

    public Task<User?> FindByUserNameAsync(string userName, CancellationToken cancellationToken) =>
        Task.FromResult(Users.SingleOrDefault(user => string.Equals(user.UserName, userName, StringComparison.OrdinalIgnoreCase)));

    public Task<User?> FindAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(Users.SingleOrDefault(user => user.Id == userId));

    public Task<bool> UserNameExistsAsync(string userName, CancellationToken cancellationToken) =>
        Task.FromResult(Users.Any(user => string.Equals(user.UserName, userName, StringComparison.OrdinalIgnoreCase)));

    public Task<bool> PersonExistsAsync(Guid personId, CancellationToken cancellationToken) => Task.FromResult(People.Contains(personId));

    public Task<IReadOnlyList<User>> UsersWithRoleAsync(TenantRole role, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<User>>(Users.Where(user => user.Roles.Contains(role)).OrderBy(user => user.UserName, StringComparer.Ordinal).ToArray());

    public void Add(User user) => Users.Add(user);

    public List<Person> AddedPeople { get; } = [];

    public void Add(Person person)
    {
        AddedPeople.Add(person);
        People.Add(person.Id);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>"hash:" + password; legacy hashes are "bcrypt:" + password.</summary>
internal sealed class FakeHasher : IPasswordHasher
{
    public int Verifications { get; private set; }

    public string Hash(string password) => "hash:" + password;

    public PasswordVerification Verify(string passwordHash, PasswordFormat format, string password)
    {
        Verifications++;
        return format == PasswordFormat.LegacyBcrypt
            ? passwordHash == "bcrypt:" + password ? PasswordVerification.SuccessRehashNeeded : PasswordVerification.Failed
            : passwordHash == "hash:" + password ? PasswordVerification.Success : PasswordVerification.Failed;
    }
}

internal static class DefaultSettings
{
    /// <summary>A settings provider that returns every definition's default.</summary>
    public static ISettingsProvider Create()
    {
        var settings = Substitute.For<ISettingsProvider>();
        settings.GetAsync(Arg.Any<SettingDefinition<int>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<SettingDefinition<int>>().Default);
        return settings;
    }
}
