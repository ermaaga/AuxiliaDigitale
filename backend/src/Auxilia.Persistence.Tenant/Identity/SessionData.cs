using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Persistence;
using Auxilia.Domain.Identity;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Tenant.Identity;

internal sealed class SessionDataFactory(ITenantDbContextFactory databases) : ISessionDataFactory
{
    public async Task<ISessionData> OpenAsync(CancellationToken cancellationToken) =>
        new SessionData(await databases.CreateAsync(cancellationToken));
}

/// <inheritdoc cref="ISessionData"/>
internal sealed class SessionData(ITenantDbContext db) : ISessionData
{
    public Task<User?> FindUserAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Set<User>().SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);

    public Task<User?> FindUserByUserNameAsync(string userName, CancellationToken cancellationToken) =>
        db.Set<User>().SingleOrDefaultAsync(user => user.UserName == userName, cancellationToken);

    public async Task<IReadOnlyList<User>> FindUsersByEmailAsync(string email, CancellationToken cancellationToken) =>
        await db.Set<User>().Where(user => user.Email == email).OrderBy(user => user.UserName).ToListAsync(cancellationToken);

    public Task<RefreshToken?> FindRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken) =>
        db.Set<RefreshToken>().SingleOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

    public Task<RefreshSession?> FindSessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
        db.Set<RefreshSession>().SingleOrDefaultAsync(session => session.Id == sessionId, cancellationToken);

    public async Task<IReadOnlyList<RefreshSession>> OpenSessionsOfUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Set<RefreshSession>().Where(session => session.UserId == userId && session.EndedAt == null).ToListAsync(cancellationToken);

    public Task<UserToken?> FindUserTokenAsync(string tokenHash, UserTokenPurpose purpose, CancellationToken cancellationToken) =>
        db.Set<UserToken>().SingleOrDefaultAsync(token => token.TokenHash == tokenHash && token.Purpose == purpose, cancellationToken);

    public async Task<IReadOnlyList<UserToken>> UnusedUserTokensAsync(Guid userId, UserTokenPurpose purpose, CancellationToken cancellationToken) =>
        await db.Set<UserToken>().Where(token => token.UserId == userId && token.Purpose == purpose && token.UsedAt == null).ToListAsync(cancellationToken);

    public void Add(RefreshSession session) => db.Set<RefreshSession>().Add(session);

    public void Add(RefreshToken token) => db.Set<RefreshToken>().Add(token);

    public void Add(UserToken token) => db.Set<UserToken>().Add(token);

    public void Add(LoginAttempt attempt) => db.Set<LoginAttempt>().Add(attempt);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public ValueTask DisposeAsync() => db.DisposeAsync();
}
