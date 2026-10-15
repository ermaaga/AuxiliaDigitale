using Auxilia.Application.Abstractions.Mailbox;
using Auxilia.Application.Abstractions.Persistence;
using Auxilia.Domain.Mailbox;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Tenant.Mailbox;

internal sealed class ConnectedAccountDataFactory(ITenantDbContextFactory databases) : IConnectedAccountDataFactory
{
    public async Task<IConnectedAccountData> OpenAsync(CancellationToken cancellationToken) => new ConnectedAccountData(await databases.CreateAsync(cancellationToken));
}

/// <inheritdoc cref="IConnectedAccountData"/>
internal sealed class ConnectedAccountData(ITenantDbContext db) : IConnectedAccountData
{
    public async Task<IReadOnlyList<ConnectedAccount>> AccountsOfAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Set<ConnectedAccount>()
            .Where(account => account.UserId == userId)
            .OrderBy(account => account.ConnectedAt)
            .ThenBy(account => account.Id)
            .ToListAsync(cancellationToken);

    public Task<ConnectedAccount?> FindAsync(Guid userId, Guid id, CancellationToken cancellationToken) =>
        db.Set<ConnectedAccount>().SingleOrDefaultAsync(account => account.Id == id && account.UserId == userId, cancellationToken);

    public Task<ConnectedAccount?> FindByAddressAsync(Guid userId, ConnectedAccountProvider provider, string address, CancellationToken cancellationToken) =>
        db.Set<ConnectedAccount>().SingleOrDefaultAsync(
            account => account.UserId == userId && account.Provider == provider && account.Address == address, cancellationToken);

    public void Add(ConnectedAccount account) => db.Set<ConnectedAccount>().Add(account);

    public void Remove(ConnectedAccount account) => db.Set<ConnectedAccount>().Remove(account);

    public Task<OAuthRequest?> FindRequestAsync(string stateHash, CancellationToken cancellationToken) =>
        db.Set<OAuthRequest>().SingleOrDefaultAsync(request => request.StateHash == stateHash, cancellationToken);

    public void Add(OAuthRequest request) => db.Set<OAuthRequest>().Add(request);

    public Task DeleteExpiredRequestsAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Set<OAuthRequest>().Where(request => request.ExpiresAt < now).ExecuteDeleteAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public ValueTask DisposeAsync() => db.DisposeAsync();
}
