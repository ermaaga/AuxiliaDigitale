using System.Security.Cryptography;
using System.Text;

namespace Auxilia.Application.Mailbox;

/// <summary>State and PKCE values of an OAuth request (RFC 7636, S256): 256-bit random, base64url.</summary>
internal static class OAuthSecrets
{
    public static string NewRandom() => Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>The PKCE challenge of a verifier: base64url(SHA-256(verifier)).</summary>
    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    /// <summary>What is stored for a state: lower-case hex SHA-256 (64 characters).</summary>
    public static string Hash(string state) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(state)));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
