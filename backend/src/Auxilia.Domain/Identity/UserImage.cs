using System.Security.Cryptography;

using Auxilia.SharedKernel.Domain;

namespace Auxilia.Domain.Identity;

/// <summary>
/// The profile picture of a user (<c>identity.user_images</c>, same id as the user, F04): an image already resized by
/// the server (at most <see cref="MaxSide"/> × <see cref="MaxSide"/>), served with its hash as ETag. Replaced as a whole.
/// </summary>
public sealed class UserImage : AggregateRoot<Guid>, IAuditable
{
    /// <summary>F04: pictures are at most 400 × 400.</summary>
    public const int MaxSide = 400;

    /// <summary>Largest accepted upload (legacy limit 2 MB), before resizing.</summary>
    public const int UploadMaxBytes = 2 * 1024 * 1024;

    public const int ContentTypeMaxLength = 30;
    public const int HashLength = 64;

    private UserImage(Guid userId, byte[] content, string contentType)
        : base(userId)
    {
        Content = content;
        ContentType = contentType;
        Hash = HashOf(content);
    }

    private UserImage()
    {
        Content = [];
        ContentType = Hash = string.Empty;
    }

    public byte[] Content { get; private set; }

    public string ContentType { get; private set; }

    /// <summary>Lower-case hex SHA-256 of <see cref="Content"/>: the ETag and the version clients put in the image URL.</summary>
    public string Hash { get; private set; }

    public static UserImage Create(Guid userId, byte[] content, string contentType)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfZero(content.Length);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        return new UserImage(userId, content, contentType);
    }

    public void Replace(byte[] content, string contentType)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfZero(content.Length);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        Content = content;
        ContentType = contentType;
        Hash = HashOf(content);
    }

    private static string HashOf(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));
}
