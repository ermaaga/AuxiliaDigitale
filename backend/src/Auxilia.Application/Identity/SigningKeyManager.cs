using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Results;

using Microsoft.Extensions.Logging;

namespace Auxilia.Application.Identity;

/// <summary>
/// Rotation of the token signing key (manual, <c>auxctl keys rotate</c>; no scheduler, D-15): the new key signs from
/// now on, the previous one stays published in the JWKS for <see cref="ValidationGrace"/> so tokens it signed stay valid.
/// API nodes pick the new key up at their next ring refresh, or at once when a token signed with it arrives.
/// </summary>
public interface ISigningKeyManager
{
    /// <returns>The id (<c>kid</c>) of the new active key.</returns>
    Task<Result<string>> RotateAsync(CancellationToken cancellationToken);
}

internal sealed class SigningKeyManager : ISigningKeyManager
{
    /// <summary>Longer than the longest access token lifetime (<c>auth.accessToken.minutes</c> ≤ 60).</summary>
    public static readonly TimeSpan ValidationGrace = TimeSpan.FromHours(2);

    private readonly IOperationRunner operations;
    private readonly ISigningKeyStore keys;
    private readonly ISigningKeyFactory factory;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<SigningKeyManager> logger;

    public SigningKeyManager(
        IOperationRunner operations, ISigningKeyStore keys, ISigningKeyFactory factory, TimeProvider timeProvider, ILogger<SigningKeyManager> logger)
    {
        this.operations = operations;
        this.keys = keys;
        this.factory = factory;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    public Task<Result<string>> RotateAsync(CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Identity.RotateSigningKey, null, async _ =>
        {
            var now = timeProvider.GetUtcNow();
            foreach (var active in (await keys.ListAsync(cancellationToken)).Where(key => key.IsActive))
            {
                active.Retire(now, ValidationGrace);
            }

            var key = factory.Create(now);
            keys.Add(key);
            await keys.SaveChangesAsync(cancellationToken);
            Log.Security.SigningKeyRotated(logger, key.Id);
            return Result.Success(key.Id);
        }, cancellationToken);
}
