using Auxilia.Application.Abstractions.Captcha;
using Auxilia.Domain.Platform;

namespace Auxilia.Infrastructure.Adapters.Captcha.None;

/// <summary>No challenge: for confidential client applications, trusted by their secret (public ones never get it, F02).</summary>
internal sealed class NoCaptchaVerifier : ICaptchaVerifier
{
    public string Provider => ClientApplication.NoCaptcha;

    public Task<AltchaChallenge?> CreateChallengeAsync(CancellationToken cancellationToken) => Task.FromResult<AltchaChallenge?>(null);

    public Task<bool> VerifyAsync(string? solution, CancellationToken cancellationToken) => Task.FromResult(true);
}
