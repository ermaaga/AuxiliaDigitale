using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Auxilia.Application.Abstractions.Identity;

namespace Auxilia.Infrastructure.Security;

/// <summary>
/// RFC 6238 TOTP (HMAC-SHA1, 6 digits, 30-second steps, the format of common authenticator apps) with a one-step
/// tolerance for clock drift. Implemented here (a few lines of standard crypto) rather than adding a dependency.
/// </summary>
internal sealed class TotpService : ITotpService
{
    public const int Digits = 6;
    public static readonly TimeSpan Step = TimeSpan.FromSeconds(30);

    public string NewSecret() => Base32.Encode(RandomNumberGenerator.GetBytes(20));

    public string EnrollmentUri(string secret, string accountName, string issuer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);

        var label = Uri.EscapeDataString(issuer) + ":" + Uri.EscapeDataString(accountName);
        return $"otpauth://totp/{label}?secret={secret}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={Digits}&period={(int)Step.TotalSeconds}";
    }

    public long? Verify(string secret, string code, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(secret) || code is null)
        {
            return null;
        }

        code = code.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (code.Length != Digits || !code.All(char.IsAsciiDigit))
        {
            return null;
        }

        var key = Base32.Decode(secret);
        var current = StepOf(now);
        for (var offset = -1; offset <= 1; offset++)
        {
            var step = current + offset;
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Compute(key, step)), Encoding.ASCII.GetBytes(code)))
            {
                return step;
            }
        }

        return null;
    }

    public static long StepOf(DateTimeOffset instant) => instant.ToUnixTimeSeconds() / (long)Step.TotalSeconds;

    /// <summary>The code of one time step (RFC 4226 dynamic truncation).</summary>
    public static string Compute(byte[] key, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);
        Span<byte> hash = stackalloc byte[20];
#pragma warning disable CA5350 // RFC 6238 authenticators use HMAC-SHA1; it is not used as a general-purpose hash here.
        HMACSHA1.HashData(key, counter, hash);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }
}

/// <summary>RFC 4648 Base32 without padding (the secret format of authenticator apps).</summary>
internal static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        var output = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var value in data)
        {
            buffer = (buffer << 8) | value;
            bits += 8;
            while (bits >= 5)
            {
                output.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            output.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        }

        return output.ToString();
    }

    public static byte[] Decode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var clean = text.Trim().TrimEnd('=').Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        var output = new List<byte>(clean.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var character in clean)
        {
            var value = Alphabet.IndexOf(character, StringComparison.Ordinal);
            if (value < 0)
            {
                throw new FormatException("Invalid Base32 character.");
            }

            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return [.. output];
    }
}
