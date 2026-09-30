using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Diagnostics;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Identity;

/// <summary>
/// Client applications allowed to call the API (D-03, <c>auxctl clients …</c>). A confidential client gets a random
/// secret, returned once and stored only as a hash.
/// </summary>
public interface IClientApplicationManager
{
    Task<Result<NewClientApplicationResult>> AddAsync(NewClientApplication request, CancellationToken cancellationToken);

    Task<IReadOnlyList<ClientApplication>> ListAsync(CancellationToken cancellationToken);
}

public sealed record NewClientApplication(string ClientId, string Name, ClientApplicationType Type, IReadOnlyList<string> AllowedOrigins);

/// <param name="Secret">The client secret of a confidential client (shown once, never stored in clear); null for public clients.</param>
public sealed record NewClientApplicationResult(ClientApplication Client, string? Secret);

internal sealed class ClientApplicationManager : IClientApplicationManager
{
    private readonly IOperationRunner operations;
    private readonly IClientApplicationStore clients;
    private readonly IPasswordHasher hasher;

    public ClientApplicationManager(IOperationRunner operations, IClientApplicationStore clients, IPasswordHasher hasher)
    {
        this.operations = operations;
        this.clients = clients;
        this.hasher = hasher;
    }

    public Task<Result<NewClientApplicationResult>> AddAsync(NewClientApplication request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync(Operations.Identity.AddClientApplication, new { request.ClientId, Type = request.Type.ToString() }, async scope =>
        {
            var created = ClientApplication.Create(Guid.CreateVersion7(), request.ClientId, request.Name, request.Type);
            if (created.IsFailure)
            {
                return Result.Failure<NewClientApplicationResult>(created.Error!);
            }

            var client = created.Value;
            if (await clients.FindAsync(client.ClientId, cancellationToken) is not null)
            {
                return Errors.Identity.ClientIdTaken();
            }

            client.SetAllowedOrigins(request.AllowedOrigins);
            string? secret = null;
            if (client.IsConfidential)
            {
                secret = SecureTokens.New();
                client.SetSecretHash(hasher.Hash(secret));
            }

            clients.Add(client);
            await clients.SaveChangesAsync(cancellationToken);
            scope.SetEntity("ClientApplication", client.Id);
            return Result.Success(new NewClientApplicationResult(client, secret));
        }, cancellationToken);
    }

    public Task<IReadOnlyList<ClientApplication>> ListAsync(CancellationToken cancellationToken) => clients.ListAsync(cancellationToken);
}
