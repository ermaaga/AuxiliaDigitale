using System.Text;

using Auxilia.Diagnostics;
using Auxilia.Domain.Configuration;

namespace Auxilia.Domain.Tests.Configuration;

public sealed class BrandingAssetTests
{
    internal static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01];

    internal static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];

    internal static readonly byte[] Webp = [.. "RIFF"u8, 0x10, 0x00, 0x00, 0x00, .. "WEBP"u8, .. "VP8 "u8];

    [Fact]
    public void DetectContentType_RecognisesPngJpegAndWebpOnly()
    {
        BrandingAsset.DetectContentType(Png).ShouldBe("image/png");
        BrandingAsset.DetectContentType(Jpeg).ShouldBe("image/jpeg");
        BrandingAsset.DetectContentType(Webp).ShouldBe("image/webp");
        BrandingAsset.DetectContentType(Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"/>")).ShouldBeNull();
        BrandingAsset.DetectContentType("RIFF0000WAVE"u8).ShouldBeNull();
        BrandingAsset.DetectContentType([]).ShouldBeNull();
    }

    [Fact]
    public void Create_StoresContentTypeAndHash()
    {
        var asset = BrandingAsset.Create(Guid.CreateVersion7(), BrandingAssetKind.Logo, Png).Value;

        asset.Kind.ShouldBe(BrandingAssetKind.Logo);
        asset.ContentType.ShouldBe("image/png");
        asset.Content.ShouldBe(Png);
        asset.Hash.Length.ShouldBe(BrandingAsset.HashLength);
        asset.Hash.ShouldBe(asset.Hash.ToLowerInvariant());
    }

    [Fact]
    public void Replace_ChangesContentAndHash()
    {
        var asset = BrandingAsset.Create(Guid.CreateVersion7(), BrandingAssetKind.Background, Png).Value;
        var before = asset.Hash;

        asset.Replace(Jpeg).IsSuccess.ShouldBeTrue();

        asset.ContentType.ShouldBe("image/jpeg");
        asset.Hash.ShouldNotBe(before);
    }

    [Fact]
    public void Create_UnknownType_IsRefused()
    {
        var result = BrandingAsset.Create(Guid.CreateVersion7(), BrandingAssetKind.Logo, Encoding.UTF8.GetBytes("<svg/>"));

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe(EventCodes.Configuration.BrandingImageInvalid);
        result.Error.ValidationErrors!["file"].ShouldBe(["validation.branding.imageType"]);
    }

    [Fact]
    public void Replace_TooLargeForTheKind_KeepsThePreviousImage()
    {
        var asset = BrandingAsset.Create(Guid.CreateVersion7(), BrandingAssetKind.Logo, Png).Value;
        var large = new byte[BrandingAsset.LogoMaxBytes + 1];
        Png.CopyTo(large, 0);

        var result = asset.Replace(large);

        result.Error!.Code.ShouldBe(EventCodes.Configuration.BrandingImageTooLarge);
        asset.Content.ShouldBe(Png);
        BrandingAsset.Create(Guid.CreateVersion7(), BrandingAssetKind.Background, large).IsSuccess.ShouldBeTrue();
        BrandingAsset.MaxBytes(BrandingAssetKind.Background).ShouldBe(BrandingAsset.BackgroundMaxBytes);
    }
}
