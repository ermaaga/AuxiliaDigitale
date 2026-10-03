using Auxilia.Application.Abstractions.Images;

using SkiaSharp;

namespace Auxilia.Infrastructure.Adapters.Images.Skia;

/// <summary>
/// <see cref="IImageProcessor"/> with SkiaSharp (MIT, allowlisted for images): only JPEG, PNG and WebP are decoded;
/// the result is a new JPEG, so nothing of the upload (metadata, trailing bytes) is ever served.
/// </summary>
internal sealed class SkiaImageProcessor : IImageProcessor
{
    public const int JpegQuality = 85;

    /// <summary>Larger sources are refused before decoding (decompression bombs).</summary>
    public const int MaxSourcePixels = 40_000_000;

    public ProcessedImage? ResizeToJpeg(ReadOnlySpan<byte> content, int maxSide)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSide);
        if (content.IsEmpty)
        {
            return null;
        }

        using var data = SKData.CreateCopy(content);
        using var codec = SKCodec.Create(data);
        if (codec is null
            || codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp)
            || (long)codec.Info.Width * codec.Info.Height > MaxSourcePixels)
        {
            return null;
        }

        using var decoded = SKBitmap.Decode(codec);
        if (decoded is null)
        {
            return null;
        }

        var scale = Math.Min(1d, (double)maxSide / Math.Max(decoded.Width, decoded.Height));
        var width = Math.Max(1, (int)Math.Round(decoded.Width * scale));
        var height = Math.Max(1, (int)Math.Round(decoded.Height * scale));

        // JPEG has no transparency: transparent pixels become white, not black.
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.White);
        using (var source = SKImage.FromBitmap(decoded))
        {
            surface.Canvas.DrawImage(source, new SKRect(0, 0, width, height), new SKSamplingOptions(SKCubicResampler.Mitchell));
        }

        using var image = surface.Snapshot();
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
        return encoded is null ? null : new ProcessedImage(encoded.ToArray(), "image/jpeg", width, height);
    }
}
