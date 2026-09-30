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
