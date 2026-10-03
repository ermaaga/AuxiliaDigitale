
namespace Auxilia.Application.Documents;

/// <summary>An accepted file type: extension, content type and how its first bytes look.</summary>
internal sealed record FileType(string Extension, string ContentType, Func<ReadOnlyMemory<byte>, bool> Matches);

/// <summary>
/// The whitelist of uploadable files (F14, skill auxilia-security): documents, office files, images, signed files
/// (<c>.p7m</c>) and electronic invoices (<c>.xml</c>). Each type is checked on its first bytes (magic bytes); text
/// types must not contain NUL bytes. Anything else is refused.
/// </summary>
internal static class FileTypes
{
    /// <summary>Bytes read to recognise a type.</summary>
    public const int HeaderLength = 8192;

    private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] Ole = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    private static readonly Dictionary<string, FileType> ByExtension = new FileType[]
    {
        new("pdf", "application/pdf", header => StartsWith(header, "%PDF-"u8)),
        new("jpg", "image/jpeg", header => StartsWith(header, [0xFF, 0xD8, 0xFF])),
        new("jpeg", "image/jpeg", header => StartsWith(header, [0xFF, 0xD8, 0xFF])),
        new("png", "image/png", header => StartsWith(header, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])),
        new("gif", "image/gif", header => StartsWith(header, "GIF87a"u8) || StartsWith(header, "GIF89a"u8)),
        new("webp", "image/webp", header => StartsWith(header, "RIFF"u8) && At(header, 8, "WEBP"u8)),
        new("heic", "image/heic", header => At(header, 4, "ftyp"u8) && (At(header, 8, "heic"u8) || At(header, 8, "heix"u8) || At(header, 8, "mif1"u8))),
        new("tif", "image/tiff", IsTiff),
        new("tiff", "image/tiff", IsTiff),
        new("doc", "application/msword", header => StartsWith(header, Ole)),
        new("xls", "application/vnd.ms-excel", header => StartsWith(header, Ole)),
        new("ppt", "application/vnd.ms-powerpoint", header => StartsWith(header, Ole)),
        new("docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", header => StartsWith(header, Zip)),
        new("xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", header => StartsWith(header, Zip)),
        new("pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation", header => StartsWith(header, Zip)),
        new("odt", "application/vnd.oasis.opendocument.text", header => StartsWith(header, Zip)),
        new("ods", "application/vnd.oasis.opendocument.spreadsheet", header => StartsWith(header, Zip)),
        new("zip", "application/zip", header => StartsWith(header, Zip)),
        new("rtf", "application/rtf", header => StartsWith(header, "{\\rtf"u8)),

        // Signed files (CAdES): DER (ASN.1 sequence) or the same in base64 text.
        new("p7m", "application/pkcs7-mime", header => StartsWith(header, [0x30]) || StartsWith(header, "MI"u8)),
        new("xml", "application/xml", IsText),
        new("txt", "text/plain", IsText),
        new("csv", "text/csv", IsText),
    }.ToDictionary(type => type.Extension, StringComparer.Ordinal);

    public static IReadOnlyCollection<string> Extensions => ByExtension.Keys;

    /// <summary>The type of the extension (lower case, without the dot), <c>null</c> when not accepted.</summary>
    public static FileType? Find(string extension) => ByExtension.GetValueOrDefault(extension);

    private static bool StartsWith(ReadOnlyMemory<byte> header, ReadOnlySpan<byte> prefix) => header.Span.StartsWith(prefix);

    private static bool At(ReadOnlyMemory<byte> header, int offset, ReadOnlySpan<byte> value) =>
        header.Length >= offset + value.Length && header.Span.Slice(offset, value.Length).SequenceEqual(value);

    private static bool IsTiff(ReadOnlyMemory<byte> header) => StartsWith(header, "II*\0"u8) || StartsWith(header, "MM\0*"u8);

    /// <summary>No NUL byte in the header (a binary disguised as text has them); a UTF-16 text needs its BOM.</summary>
    private static bool IsText(ReadOnlyMemory<byte> header) =>
        StartsWith(header, [0xFF, 0xFE]) || StartsWith(header, [0xFE, 0xFF]) || !header.Span.Contains((byte)0);
}
