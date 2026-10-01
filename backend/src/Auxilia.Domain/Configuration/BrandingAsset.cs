using System.Security.Cryptography;

using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Domain;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Domain.Configuration;

public enum BrandingAssetKind
{
    /// <summary>Shown instead of the app name when <c>branding.useAppName</c> is off.</summary>
    Logo,

    /// <summary>Login page background when <c>branding.background.kind</c> is <c>Image</c>.</summary>
    Background,
}

/// <summary>
/// An image of the tenant branding (F23; <c>configuration.branding_assets</c>, one per kind). Only PNG, JPEG and WebP,
/// recognised by their signature (never SVG, which can carry scripts); the SHA-256 of the content is its version and
/// ETag, so browsers cache it until it changes.
/// </summary>
public sealed class BrandingAsset : AggregateRoot<Guid>, IAuditable
{
    public const int LogoMaxBytes = 512 * 1024;
    public const int BackgroundMaxBytes = 2 * 1024 * 1024;
    public const int ContentTypeMaxLength = 30;
    public const int HashLength = 64;

    private BrandingAsset(Guid id, BrandingAssetKind kind)
        : base(id)
    {
        Kind = kind;
        ContentType = Hash = string.Empty;
        Content = [];
    }

    private BrandingAsset()
    {
        ContentType = Hash = string.Empty;
        Content = [];
    }

    public BrandingAssetKind Kind { get; private set; }

    public string ContentType { get; private set; }

    public byte[] Content { get; private set; }

    /// <summary>Lower-case hex SHA-256 of <see cref="Content"/>.</summary>
    public string Hash { get; private set; }

    public static int MaxBytes(BrandingAssetKind kind) => kind == BrandingAssetKind.Logo ? LogoMaxBytes : BackgroundMaxBytes;

    public static Result<BrandingAsset> Create(Guid id, BrandingAssetKind kind, byte[] content)
    {
        var asset = new BrandingAsset(id, kind);
        var replaced = asset.Replace(content);
        return replaced.IsSuccess ? asset : Result.Failure<BrandingAsset>(replaced.Error!);
    }

    public Result Replace(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (content.Length > MaxBytes(Kind))
        {
            return Errors.Configuration.BrandingImageTooLarge(MaxBytes(Kind) / 1024);
        }

        if (DetectContentType(content) is not { } contentType)
        {
            return Errors.Configuration.BrandingImageInvalid();
        }

        Content = content;
        ContentType = contentType;
        Hash = Convert.ToHexStringLower(SHA256.HashData(content));
        return Result.Success();
    }

    /// <summary>The image type from the file signature, or <c>null</c> when it is not PNG, JPEG or WebP.</summary>
    public static string? DetectContentType(ReadOnlySpan<byte> content)
    {
        if (content.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return "image/png";
        }

        if (content.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
        {
            return "image/jpeg";
        }

        return content.Length >= 12 && content[..4].SequenceEqual("RIFF"u8) && content[8..12].SequenceEqual("WEBP"u8)
            ? "image/webp"
            : null;
    }
}
