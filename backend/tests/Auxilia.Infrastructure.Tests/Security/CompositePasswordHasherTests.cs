using Auxilia.Application.Abstractions.Identity;
using Auxilia.Domain.Identity;
using Auxilia.Infrastructure.Security;

namespace Auxilia.Infrastructure.Tests.Security;

public sealed class CompositePasswordHasherTests
{
    private readonly CompositePasswordHasher hasher = new();

    [Fact]
    public void IdentityHash_RoundTripsWithARandomSalt()
    {
        var hash = hasher.Hash("correct horse battery staple");

        hash.ShouldNotContain("correct");
        hasher.Hash("correct horse battery staple").ShouldNotBe(hash);
        hasher.Verify(hash, PasswordFormat.Identity, "correct horse battery staple").ShouldBe(PasswordVerification.Success);
        hasher.Verify(hash, PasswordFormat.Identity, "wrong").ShouldBe(PasswordVerification.Failed);
    }

    [Fact]
    public void LegacyBcrypt_IsVerifiedAndAlwaysNeedsARehash()
    {
        // Same format as the legacy UserService (BCrypt.Net, work factor 11).
        var legacy = BCrypt.Net.BCrypt.HashPassword("Legacy!2024", workFactor: 4);

        hasher.Verify(legacy, PasswordFormat.LegacyBcrypt, "Legacy!2024").ShouldBe(PasswordVerification.SuccessRehashNeeded);
        hasher.Verify(legacy, PasswordFormat.LegacyBcrypt, "legacy!2024").ShouldBe(PasswordVerification.Failed);
    }

    [Theory]
    [InlineData("not-a-hash", PasswordFormat.LegacyBcrypt)]
    [InlineData("not-base64!!", PasswordFormat.Identity)]
    [InlineData("AQID", PasswordFormat.Identity)]
    public void MalformedHashes_FailWithoutThrowing(string hash, PasswordFormat format) =>
        hasher.Verify(hash, format, "x").ShouldBe(PasswordVerification.Failed);
}
