using Auxilia.Infrastructure.Adapters.Images.Skia;

using SkiaSharp;

namespace Auxilia.Infrastructure.Tests.Adapters.Images;

/// <summary>F04: uploaded pictures are decoded, scaled down to at most 400 × 400 and re-encoded as JPEG.</summary>
public sealed class SkiaImageProcessorTests
{
    private readonly SkiaImageProcessor processor = new();

    [Theory]
    [InlineData(SKEncodedImageFormat.Png, 1200, 600, 400, 200)]
    [InlineData(SKEncodedImageFormat.Jpeg, 300, 900, 133, 400)]
    [InlineData(SKEncodedImageFormat.Webp, 200, 100, 200, 100)]
    public void ResizeToJpeg_ScalesDownKeepingTheAspect_NeverUp(SKEncodedImageFormat format, int width, int height, int expectedWidth, int expectedHeight)
    {
        var result = processor.ResizeToJpeg(Encode(format, width, height), 400);

        result.ShouldNotBeNull();
        (result.Width, result.Height, result.ContentType).ShouldBe((expectedWidth, expectedHeight, "image/jpeg"));
        using var decoded = SKBitmap.Decode(result.Content);
        (decoded.Width, decoded.Height).ShouldBe((expectedWidth, expectedHeight));
        result.Content.AsSpan(0, 2).ToArray().ShouldBe(new byte[] { 0xFF, 0xD8 });
    }

    [Fact]
    public void ResizeToJpeg_TransparentPixels_BecomeWhite()
    {
        var result = processor.ResizeToJpeg(Encode(SKEncodedImageFormat.Png, 10, 10, SKColors.Transparent), 400)!;

        using var decoded = SKBitmap.Decode(result.Content);
        var pixel = decoded.GetPixel(5, 5);
        (pixel.Red, pixel.Green, pixel.Blue).ShouldBe(((byte)255, (byte)255, (byte)255));
    }

    [Fact]
    public void ResizeToJpeg_NotAnAcceptedImage_IsNull()
    {
        processor.ResizeToJpeg([], 400).ShouldBeNull();
        processor.ResizeToJpeg("not an image"u8, 400).ShouldBeNull();

        // A GIF is an image, but not one of the accepted formats.
        processor.ResizeToJpeg("GIF89a\u0001\0\u0001\0\0\0\0;"u8, 400).ShouldBeNull();

        // A truncated PNG cannot be decoded.
        var png = Encode(SKEncodedImageFormat.Png, 50, 50);
        processor.ResizeToJpeg(png.AsSpan(0, 40), 400).ShouldBeNull();
    }

    private static byte[] Encode(SKEncodedImageFormat format, int width, int height, SKColor? color = null)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Erase(color ?? SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }
}
