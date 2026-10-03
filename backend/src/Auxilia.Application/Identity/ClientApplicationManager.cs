using Auxilia.Application.Abstractions.Captcha;
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

/// <param name="CaptchaProvider">
/// Captcha of its public calls (F02): <c>none</c> or <c>altcha</c>; by default <c>none</c> for confidential clients and
/// <c>altcha</c> for public ones, which cannot go without.
/// </param>
public sealed record NewClientApplication(
    string ClientId, string Name, ClientApplicationType Type, IReadOnlyList<string> AllowedOrigins, string? CaptchaProvider = null);

/// <param name="Secret">The client secret of a confidential client (shown once, never stored in clear); null for public clients.</param>
public sealed record NewClientApplicationResult(ClientApplication Client, string? Secret);

internal sealed class ClientApplicationManager : IClientApplicationManager
{
    private readonly IOperationRunner operations;
    private readonly IClientApplicationStore clients;
    private readonly IPasswordHasher hasher;
    private readonly IEnumerable<ICaptchaVerifier> captchas;

    public ClientApplicationManager(
        IOperationRunner operations, IClientApplicationStore clients, IPasswordHasher hasher, IEnumerable<ICaptchaVerifier> captchas)
    {
        this.operations = operations;
        this.clients = clients;
        this.hasher = hasher;
        this.captchas = captchas;
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

            var captcha = string.IsNullOrWhiteSpace(request.CaptchaProvider)
                ? client.IsConfidential ? ClientApplication.NoCaptcha : ClientApplication.PublicClientCaptcha
                : request.CaptchaProvider.Trim();
            if (!captchas.Any(verifier => verifier.Provider == captcha) || (!client.IsConfidential && captcha == ClientApplication.NoCaptcha))
            {
                return Errors.Tenancy.CatalogValueInvalid("captchaProvider", "validation.clientApplication.captcha");
            }

            client.SetAllowedOrigins(request.AllowedOrigins);
            client.SetCaptchaProvider(captcha);
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
