using System.Security.Cryptography;

using Auxilia.Infrastructure.Adapters.Captcha.Altcha;
using Auxilia.Infrastructure.Adapters.Captcha.None;

using Ixnas.AltchaNet;

using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Auxilia.Infrastructure.Tests.Adapters.Captcha;

/// <summary>ALTCHA end to end with the library's solver (what an external client application does): once only, signed by the key.</summary>
public sealed class AltchaCaptchaVerifierTests
{
    private static readonly string Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    private readonly MemoryDistributedCache cache = new(Options.Create(new MemoryDistributedCacheOptions()));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private AltchaCaptchaVerifier Verifier(string? key = null, IDistributedCache? store = null) =>
        new(Options.Create(new AltchaOptions { Key = key ?? Key }), store ?? cache, NullLogger<AltchaCaptchaVerifier>.Instance);

    private static async Task<string> SolveAsync(AltchaCaptchaVerifier verifier)
    {
        var challenge = (await verifier.CreateChallengeAsync(Ct))!;
        var solved = Altcha.CreateSolver().Solve(new AltchaChallenge
        {
            Algorithm = challenge.Algorithm,
            Challenge = challenge.Challenge,
            Salt = challenge.Salt,
            Signature = challenge.Signature,
            Maxnumber = (int)challenge.MaxNumber,
        });
        solved.Success.ShouldBeTrue();
        return solved.Altcha;
    }

    [Fact]
    public async Task Solution_IsAcceptedOnce()
    {
        var verifier = Verifier();
        var solution = await SolveAsync(verifier);

        (await verifier.VerifyAsync(solution, Ct)).ShouldBeTrue();
        (await verifier.VerifyAsync(solution, Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task Replay_IsRefusedOnEveryNodeSharingTheCache_AndOtherKeysAreRefused()
    {
        var solution = await SolveAsync(Verifier());

        (await Verifier(Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))).VerifyAsync(solution, Ct)).ShouldBeFalse();
        (await Verifier().VerifyAsync(solution, Ct)).ShouldBeTrue();
        (await Verifier().VerifyAsync(solution, Ct)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64 at all")]
    [InlineData("eyJub3QiOiJhbHRjaGEifQ==")]
    public async Task MissingOrMalformedSolutions_AreRefused(string? solution)
    {
        (await Verifier().VerifyAsync(solution, Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task WithoutAKey_ARandomOneIsUsed_AndAWrongKeyFailsLoudly()
    {
        var ephemeral = new AltchaCaptchaVerifier(Options.Create(new AltchaOptions()), cache, NullLogger<AltchaCaptchaVerifier>.Instance);
        (await ephemeral.VerifyAsync(await SolveAsync(ephemeral), Ct)).ShouldBeTrue();

        await Should.ThrowAsync<InvalidOperationException>(() => Verifier(Convert.ToBase64String(new byte[10])).CreateChallengeAsync(Ct));
    }

    [Fact]
    public async Task NoCaptcha_AcceptsEverything_WithoutAChallenge()
    {
        var none = new NoCaptchaVerifier();

        (await none.CreateChallengeAsync(Ct)).ShouldBeNull();
        (await none.VerifyAsync(null, Ct)).ShouldBeTrue();
    }
}
