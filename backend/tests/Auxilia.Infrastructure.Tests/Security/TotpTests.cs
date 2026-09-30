using System.Text;

using Auxilia.Infrastructure.Security;

namespace Auxilia.Infrastructure.Tests.Security;

public sealed class TotpTests
{
    // RFC 6238 appendix B (SHA-1 seed), last 6 digits of the 8-digit reference values.
    private const string RfcSecret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    private readonly TotpService totp = new();

    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    public void Compute_MatchesTheRfcVectors(long unixSeconds, string expected)
    {
        Base32.Encode(Encoding.ASCII.GetBytes("12345678901234567890")).ShouldBe(RfcSecret);

        var step = TotpService.StepOf(DateTimeOffset.FromUnixTimeSeconds(unixSeconds));

        TotpService.Compute(Base32.Decode(RfcSecret), step).ShouldBe(expected);
        totp.Verify(RfcSecret, expected, DateTimeOffset.FromUnixTimeSeconds(unixSeconds)).ShouldBe(step);
    }

    [Fact]
    public void Verify_AcceptsOneStepOfDrift_AndRejectsMalformedCodes()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1234567890);
        var code = TotpService.Compute(Base32.Decode(RfcSecret), TotpService.StepOf(now));

        totp.Verify(RfcSecret, code, now.AddSeconds(30)).ShouldBe(TotpService.StepOf(now));
        totp.Verify(RfcSecret, code, now.AddSeconds(-30)).ShouldBe(TotpService.StepOf(now));
        totp.Verify(RfcSecret, code, now.AddSeconds(90)).ShouldBeNull();
        totp.Verify(RfcSecret, code[..3] + " " + code[3..], now).ShouldBe(TotpService.StepOf(now));
        totp.Verify(RfcSecret, "12345", now).ShouldBeNull();
        totp.Verify(RfcSecret, "abcdef", now).ShouldBeNull();
        totp.Verify("", code, now).ShouldBeNull();
    }

    [Fact]
    public void NewSecret_IsRandom160BitBase32_AndTheUriIsForAuthenticatorApps()
    {
        var secret = totp.NewSecret();

        secret.Length.ShouldBe(32);
        Base32.Decode(secret).Length.ShouldBe(20);
        totp.NewSecret().ShouldNotBe(secret);
        totp.EnrollmentUri(secret, "ops@example.test", "Auxilia")
            .ShouldBe($"otpauth://totp/Auxilia:ops%40example.test?secret={secret}&issuer=Auxilia&algorithm=SHA1&digits=6&period=30");
        Should.Throw<FormatException>(() => Base32.Decode("NOT*BASE32"));
        Base32.Decode("mzxw6===").ShouldBe(Encoding.ASCII.GetBytes("foo"));
    }
}
