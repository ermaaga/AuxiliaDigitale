using Auxilia.Application.Abstractions.Identity;
using Auxilia.Domain.Platform;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Catalog.Identity;

internal sealed class SigningKeyStore(CatalogDbContext catalog) : ISigningKeyStore
{
    public async Task<IReadOnlyList<SigningKey>> ListAsync(CancellationToken cancellationToken) =>
        await catalog.SigningKeys.OrderByDescending(key => key.CreatedAt).ToListAsync(cancellationToken);

    public void Add(SigningKey key) => catalog.SigningKeys.Add(key);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => catalog.SaveChangesAsync(cancellationToken);
}

internal sealed class ClientApplicationStore(CatalogDbContext catalog) : IClientApplicationStore
{
    public Task<ClientApplication?> FindAsync(string clientId, CancellationToken cancellationToken) =>
        catalog.ClientApplications.AsNoTracking().SingleOrDefaultAsync(client => client.ClientId == clientId, cancellationToken);

    public async Task<IReadOnlyList<ClientApplication>> ListAsync(CancellationToken cancellationToken) =>
        await catalog.ClientApplications.AsNoTracking().OrderBy(client => client.ClientId).ToListAsync(cancellationToken);

    public void Add(ClientApplication application) => catalog.ClientApplications.Add(application);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => catalog.SaveChangesAsync(cancellationToken);
}

/// <inheritdoc cref="IPlatformIdentityStore"/>
internal sealed class PlatformIdentityStore(CatalogDbContext catalog) : IPlatformIdentityStore
{
    public Task<PlatformUser?> FindUserAsync(Guid userId, CancellationToken cancellationToken) =>
        catalog.PlatformUsers.SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);

    // email is citext: equality is case-insensitive in the database.
    public Task<PlatformUser?> FindUserByEmailAsync(string email, CancellationToken cancellationToken) =>
        catalog.PlatformUsers.SingleOrDefaultAsync(user => user.Email == email, cancellationToken);

    public async Task<IReadOnlyList<PlatformUser>> ListUsersAsync(CancellationToken cancellationToken) =>
        await catalog.PlatformUsers.AsNoTracking().OrderBy(user => user.Email).ToListAsync(cancellationToken);

    public Task<Domain.Identity.UserToken?> FindUserTokenAsync(string tokenHash, Domain.Identity.UserTokenPurpose purpose, CancellationToken cancellationToken) =>
        catalog.PlatformUserTokens.SingleOrDefaultAsync(token => token.TokenHash == tokenHash && token.Purpose == purpose, cancellationToken);

    public async Task<IReadOnlyList<Domain.Identity.UserToken>> UnusedUserTokensAsync(Guid userId, Domain.Identity.UserTokenPurpose purpose, CancellationToken cancellationToken) =>
        await catalog.PlatformUserTokens.Where(token => token.UserId == userId && token.Purpose == purpose && token.UsedAt == null).ToListAsync(cancellationToken);

    public Task<Domain.Identity.RefreshSession?> FindSessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
        catalog.PlatformSessions.SingleOrDefaultAsync(session => session.Id == sessionId, cancellationToken);

    public async Task<IReadOnlyList<Domain.Identity.RefreshSession>> OpenSessionsOfUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await catalog.PlatformSessions.Where(session => session.UserId == userId && session.EndedAt == null).ToListAsync(cancellationToken);

    public Task<Domain.Identity.RefreshToken?> FindRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken) =>
        catalog.PlatformRefreshTokens.SingleOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

    public void Add(PlatformUser user) => catalog.PlatformUsers.Add(user);

    public void Add(Domain.Identity.UserToken token) => catalog.PlatformUserTokens.Add(token);

    public void Add(Domain.Identity.RefreshSession session) => catalog.PlatformSessions.Add(session);

    public void Add(Domain.Identity.RefreshToken token) => catalog.PlatformRefreshTokens.Add(token);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => catalog.SaveChangesAsync(cancellationToken);
}
