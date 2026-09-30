using System.Security.Cryptography;
using System.Text;

namespace Auxilia.Application.Identity;

/// <summary>Opaque tokens (refresh, activation, reset): 256 random bits, base64url; stored only as SHA-256 hex.</summary>
internal static class SecureTokens
{
    public static string New() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
