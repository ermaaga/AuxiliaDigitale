using System.Security.Cryptography;
using System.Text.Json;

using Auxilia.Application.Abstractions.Captcha;
using Auxilia.Diagnostics;

using Ixnas.AltchaNet;

using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Challenge = Auxilia.Application.Abstractions.Captcha.AltchaChallenge;

namespace Auxilia.Infrastructure.Adapters.Captcha.Altcha;

/// <summary>ALTCHA (section <c>Captcha:Altcha</c>, infrastructure level): the HMAC key shared by every Api node.</summary>
public sealed class AltchaOptions
{
    public const string SectionName = "Captcha:Altcha";

    /// <summary>64 random bytes, base64 (user-secrets / environment). Without it a random key is used until restart (one node only).</summary>
    public string? Key { get; set; }

    /// <summary>Seconds a challenge stays valid (the applicant fills the form after solving it).</summary>
    public int ExpirySeconds { get; set; } = 600;
}

/// <summary>
/// Self-hosted ALTCHA proof of work (<c>Ixnas.AltchaNet</c>, BSD-2-Clause): signed challenges, solutions accepted once
/// (verified challenges kept on the distributed cache until they expire, so a replay fails on every node).
/// </summary>
internal sealed class AltchaCaptchaVerifier : ICaptchaVerifier
{
    public const string ProviderKey = "altcha";

    private const int KeyLength = 64;

    private readonly Lazy<AltchaService> service;

    public AltchaCaptchaVerifier(IOptions<AltchaOptions> options, IDistributedCache cache, ILogger<AltchaCaptchaVerifier> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        var settings = options.Value;
        var store = new DistributedChallengeStore(cache);
        service = new Lazy<AltchaService>(() => Ixnas.AltchaNet.Altcha.CreateService(new AltchaSha256Configuration
        {
            Key = AltchaKey.FromBytes(Key(settings.Key, logger)),
            StoreFactory = () => store,
            Expiry = AltchaExpiry.FromSeconds(settings.ExpirySeconds),
        }));
    }

    public string Provider => ProviderKey;

    public Task<Challenge?> CreateChallengeAsync(CancellationToken cancellationToken)
    {
        var challenge = service.Value.Generate();
        return Task.FromResult<Challenge?>(
            new Challenge(challenge.Algorithm, challenge.Challenge, challenge.Salt, challenge.Signature, challenge.Maxnumber));
    }

    public async Task<bool> VerifyAsync(string? solution, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(solution))
        {
            return false;
        }

        try
        {
            return (await service.Value.Validate(solution, cancellationToken)).IsValid;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException)
        {
            // A malformed payload is just an invalid solution.
            return false;
        }
    }

    private static byte[] Key(string? configured, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            Log.Security.CaptchaKeyEphemeral(logger);
            return RandomNumberGenerator.GetBytes(KeyLength);
        }

        var key = Convert.FromBase64String(configured);
        return key.Length == KeyLength
            ? key
            : throw new InvalidOperationException($"{AltchaOptions.SectionName}:Key must be {KeyLength} bytes in base64.");
    }

    /// <summary>Verified challenges on the distributed cache (Redis/Valkey in production), until they expire.</summary>
    private sealed class DistributedChallengeStore(IDistributedCache cache) : IAltchaChallengeStore
    {
        private static readonly byte[] Marker = [1];

        public Task Store(string challenge, DateTimeOffset expiryUtc) => Store(challenge, expiryUtc, CancellationToken.None);

        public Task Store(string challenge, DateTimeOffset expiryUtc, CancellationToken cancellationToken) =>
            cache.SetAsync(CacheKey(challenge), Marker, new DistributedCacheEntryOptions { AbsoluteExpiration = expiryUtc }, cancellationToken);

        public Task<bool> Exists(string challenge) => Exists(challenge, CancellationToken.None);

        public async Task<bool> Exists(string challenge, CancellationToken cancellationToken) =>
            await cache.GetAsync(CacheKey(challenge), cancellationToken) is not null;

        private static string CacheKey(string challenge) => $"auxilia:altcha:{challenge}";
    }
}
