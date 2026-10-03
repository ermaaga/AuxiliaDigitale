namespace Auxilia.Application.Abstractions.Images;

/// <summary>An image re-encoded by the server: what is stored and served, never the uploaded bytes.</summary>
public sealed record ProcessedImage(byte[] Content, string ContentType, int Width, int Height);

/// <summary>Decodes and resizes uploaded pictures (adapter <c>Infrastructure/Adapters/Images</c>).</summary>
public interface IImageProcessor
{
    /// <summary>
    /// Decodes a JPEG, PNG or WebP image and re-encodes it as JPEG, scaled down (aspect kept) so that no side exceeds
    /// <paramref name="maxSide"/>; <c>null</c> when the bytes are not such an image.
    /// </summary>
    ProcessedImage? ResizeToJpeg(ReadOnlySpan<byte> content, int maxSide);
}
