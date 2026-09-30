using System.Security.Cryptography;

using Auxilia.Contracts.Identity;

namespace Auxilia.Application.Identity;

/// <summary>Random passwords handed out by an operator (F31): every character class, at least 16 characters.</summary>
internal static class TemporaryPassword
{
    public const int MinLength = 16;

    // No look-alike characters (0/O, 1/l/I): the password is read and typed by a person.
    private const string Uppercase = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lowercase = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Specials = "!#$%&*+-=?@";

    public static string Generate(PasswordPolicyResponse policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var length = Math.Max(policy.MinLength, MinLength);
        var classes = new[] { Uppercase, Lowercase, Digits, Specials };
        var all = string.Concat(classes);
        var characters = new char[length];
        for (var index = 0; index < length; index++)
        {
            // One of each class first, the rest from all of them; then shuffled.
            var pool = index < classes.Length ? classes[index] : all;
            characters[index] = pool[RandomNumberGenerator.GetInt32(pool.Length)];
        }

        RandomNumberGenerator.Shuffle(characters.AsSpan());
        return new string(characters);
    }
}
