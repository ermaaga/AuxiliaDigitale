using Auxilia.Application.Abstractions.Identity;
using Auxilia.Domain.Platform;
using Auxilia.Infrastructure.Security.Tokens;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Auxilia.Infrastructure.Tests.Security;

public sealed class TokenTests : IDisposable
{
    private readonly InMemoryKeyStore store = new();
    private readonly ClockProvider time = new();
    private readonly ServiceProvider services;
    private readonly SigningKeyRing ring;
    private readonly JwtAccessTokenIssuer issuer;

    public TokenTests()
    {
        services = new ServiceCollection()
            .AddSingleton<ISigningKeyStore>(store)
            .AddSingleton<ISigningKeyProtector, ReversingProtector>()
            .AddSingleton<ISigningKeyFactory, SigningKeyFactory>()
            .BuildServiceProvider();
        ring = new SigningKeyRing(services.GetRequiredService<IServiceScopeFactory>(), time);
        issuer = new JwtAccessTokenIssuer(ring, Options.Create(new TokenOptions()), time);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        ring.Dispose();
        services.Dispose();
    }

    [Fact]
    public async Task Issuer_SignsES256WithTheActiveKey_AndTheRingValidatesIt()
    {
        var session = Guid.CreateVersion7();
        var user = Guid.CreateVersion7();

        var issued = await issuer.IssueAsync(new AccessTokenRequest(user, "acme", session, "web", [TenantRole.Employee, TenantRole.Administrator], TimeSpan.FromMinutes(10)), Ct);

        var key = store.Keys.ShouldHaveSingleItem();
        key.PrivateKeyProtected.ShouldStartWith("protected:");
        var validation = await ValidateAsync(issued.Token);
        validation.IsValid.ShouldBeTrue(validation.Exception?.Message);
        var token = (JsonWebToken)validation.SecurityToken;
        (token.Alg, token.Kid, token.Subject).ShouldBe(("ES256", key.Id, user.ToString()));
        token.GetClaim(TokenClaims.Tenant).Value.ShouldBe("acme");
        token.GetClaim(TokenClaims.Session).Value.ShouldBe(session.ToString());
        token.GetClaim(TokenClaims.TokenId).Value.ShouldBe(issued.TokenId);
        token.Claims.Where(claim => claim.Type == TokenClaims.Role).Select(claim => claim.Value).ShouldBe(["Employee", "Administrator"], ignoreOrder: true);
        issued.ExpiresAt.ShouldBe(time.GetUtcNow().AddMinutes(10));
    }

    [Fact]
    public async Task Jwks_PublishesOnlyThePublicPart()
    {
        await ring.GetSigningCredentialsAsync(Ct);

        var jwk = (await ring.GetPublicJwksAsync(Ct)).ShouldHaveSingleItem();

        jwk.GetProperty("kty").GetString().ShouldBe("EC");
        jwk.GetProperty("crv").GetString().ShouldBe("P-256");
        jwk.GetProperty("alg").GetString().ShouldBe("ES256");
        jwk.GetProperty("kid").GetString().ShouldBe(store.Keys.Single().Id);
        jwk.TryGetProperty("d", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Rotation_OldTokensStayValidDuringTheGrace_AndUnknownKidsReloadTheRing()
    {
        var before = await issuer.IssueAsync(Request(), Ct);
        var old = store.Keys.Single();

        // Rotation made elsewhere (auxctl): this node still has the old snapshot.
        old.Retire(time.GetUtcNow(), TimeSpan.FromHours(2));
        var rotated = new SigningKeyFactory(new ReversingProtector()).Create(time.GetUtcNow());
        store.Add(rotated);

        (await issuer.IssueAsync(Request(), Ct)).Token.ShouldNotBeNull();
        time.Advance(TimeSpan.FromSeconds(11));
        await ring.EnsureLoadedAsync(rotated.Id, Ct);
        ring.ValidationKeys.Select(key => key.KeyId).ShouldBe([old.Id, rotated.Id], ignoreOrder: true);

        var after = await issuer.IssueAsync(Request(), Ct);
        new JsonWebToken(after.Token).Kid.ShouldBe(rotated.Id);
        (await ValidateAsync(before.Token)).IsValid.ShouldBeTrue();

        time.Advance(TimeSpan.FromHours(2));
        await ring.EnsureLoadedAsync(null, Ct);
        ring.ValidationKeys.Select(key => key.KeyId).ShouldBe([rotated.Id]);
    }

    [Fact]
    public async Task ForgedKid_DoesNotReloadMoreThanEveryFewSeconds()
    {
        await ring.GetSigningCredentialsAsync(Ct);
        var reads = store.Reads;

        await ring.EnsureLoadedAsync("forged", Ct);
        await ring.EnsureLoadedAsync("forged", Ct);

        store.Reads.ShouldBe(reads);
    }

    [Fact]
    public async Task DenyList_ExpiresWithTheToken()
    {
        var memory = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions { Clock = new SystemClockAdapter(time) }));
        var denyList = new DistributedAccessTokenDenyList(memory, time);
        var session = Guid.CreateVersion7();

        await denyList.DenyTokenAsync("acme", "jti-1", time.GetUtcNow().AddMinutes(5), Ct);
        await denyList.DenySessionAsync("acme", session, time.GetUtcNow().AddMinutes(5), Ct);
        await denyList.DenyTokenAsync("acme", "already-expired", time.GetUtcNow().AddMinutes(-1), Ct);

        (await denyList.IsDeniedAsync("acme", "jti-1", null, Ct)).ShouldBeTrue();
        (await denyList.IsDeniedAsync("acme", "other", session, Ct)).ShouldBeTrue();
        (await denyList.IsDeniedAsync("other-tenant", "jti-1", session, Ct)).ShouldBeFalse();
        (await denyList.IsDeniedAsync("acme", "already-expired", null, Ct)).ShouldBeFalse();

        time.Advance(TimeSpan.FromMinutes(6));
        (await denyList.IsDeniedAsync("acme", "jti-1", session, Ct)).ShouldBeFalse();
    }

    private static AccessTokenRequest Request() =>
        new(Guid.CreateVersion7(), "acme", Guid.CreateVersion7(), "web", [TenantRole.Client], TimeSpan.FromMinutes(10));

    private async Task<TokenValidationResult> ValidateAsync(string token)
    {
        await ring.EnsureLoadedAsync(new JsonWebToken(token).Kid, Ct);
        return await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = "https://auxilia.app",
            ValidAudience = "auxilia-api",
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
            IssuerSigningKeys = ring.ValidationKeys,
            LifetimeValidator = (_, expires, _, _) => expires > time.GetUtcNow().UtcDateTime,
        });
    }

    private sealed class InMemoryKeyStore : ISigningKeyStore
    {
        public List<SigningKey> Keys { get; } = [];

        public int Reads { get; private set; }

        public Task<IReadOnlyList<SigningKey>> ListAsync(CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult<IReadOnlyList<SigningKey>>(Keys.ToArray());
        }

        public void Add(SigningKey key) => Keys.Add(key);

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ReversingProtector : ISigningKeyProtector
    {
        public string Protect(string privateKey) => "protected:" + new string(privateKey.Reverse().ToArray());

        public string Unprotect(string protectedPrivateKey) => new(protectedPrivateKey["protected:".Length..].Reverse().ToArray());
    }

    private sealed class ClockProvider : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now += by;
    }

#pragma warning disable CS0618 // MemoryDistributedCache still takes the ISystemClock.
    private sealed class SystemClockAdapter(TimeProvider time) : Microsoft.Extensions.Internal.ISystemClock
    {
        public DateTimeOffset UtcNow => time.GetUtcNow();
    }
#pragma warning restore CS0618
}
