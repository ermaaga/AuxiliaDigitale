namespace Auxilia.Application.Abstractions.Captcha;

/// <summary>An ALTCHA proof-of-work challenge, as the ALTCHA widget or a solver expects it (altcha.org).</summary>
public sealed record AltchaChallenge(string Algorithm, string Challenge, string Salt, string Signature, long MaxNumber);

/// <summary>What a client application must solve before calling a public endpoint; <c>Altcha</c> is set for <c>altcha</c>.</summary>
public sealed record CaptchaChallenge(string Provider, AltchaChallenge? Altcha);

/// <summary>
/// A captcha verifier for the public endpoints called by external client applications (ARCHITECTURE §6, F02): port
/// here, adapters in <c>Infrastructure/Adapters/Captcha/&lt;Provider&gt;</c>, chosen by the client application's
/// <c>captcha_provider</c>. Never Google reCAPTCHA (dependency policy).
/// </summary>
public interface ICaptchaVerifier
{
    /// <summary>Provider key stored on the client application (<c>none</c>, <c>altcha</c>).</summary>
    string Provider { get; }

    /// <summary>A new challenge to solve; <c>null</c> when the provider needs none.</summary>
    Task<AltchaChallenge?> CreateChallengeAsync(CancellationToken cancellationToken);

    /// <summary>Whether the solution is valid and not used before (a solution is accepted once).</summary>
    Task<bool> VerifyAsync(string? solution, CancellationToken cancellationToken);
}
