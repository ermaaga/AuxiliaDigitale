using Auxilia.Application.Abstractions.Mailbox;

using MimeKit;

namespace Auxilia.Infrastructure.Adapters.Mail;

/// <summary>
/// The bodies and attachments of an original message with MimeKit (C-06b). The message is parsed from its temporary file
/// without copying it in memory (<c>persistent</c>); malformed parts are tolerated (MimeKit is lenient) and a message
/// that cannot be parsed at all has no body and no attachment. Attachments are the MIME parts marked as such and the
/// attached messages (a PEC's <c>postacert.eml</c>), numbered in document order.
/// </summary>
internal sealed class MimeMailContentReader : IMailContentReader
{
    public const int MaxFileNameLength = 200;
    public const int MaxContentTypeLength = 100;

    public async Task<MailContent> ReadAsync(Stream raw, int maxBodyChars, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(raw);

        if (await LoadAsync(raw, cancellationToken) is not { } message)
        {
            return new MailContent(null, null, false, []);
        }

        var (text, textCut) = Cut(message.TextBody, maxBodyChars);
        var (html, htmlCut) = Cut(message.HtmlBody, maxBodyChars);
        var attachments = new List<MailAttachmentInfo>();
        foreach (var (entity, index) in message.Attachments.Select((entity, index) => (entity, index)))
        {
            await using var counter = new CountingStream();
            await WriteAsync(entity, counter, cancellationToken);
            attachments.Add(Info(entity, index, counter.Length));
        }

        return new MailContent(text, html, textCut || htmlCut, attachments);
    }

    public async Task<MailAttachmentContent?> OpenAttachmentAsync(Stream raw, int index, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(raw);

        if (index < 0 || await LoadAsync(raw, cancellationToken) is not { } message || message.Attachments.ElementAtOrDefault(index) is not { } entity)
        {
            return null;
        }

        var content = TemporaryFiles.Create();
        try
        {
            await WriteAsync(entity, content, cancellationToken);
            await content.FlushAsync(cancellationToken);
            content.Position = 0;
            return new MailAttachmentContent(content, Info(entity, index, content.Length));
        }
        catch
        {
            await content.DisposeAsync();
            throw;
        }
    }

    /// <summary>A file name the page and the documents can hold: no path, no control characters, bounded.</summary>
    internal static string SafeFileName(string? name, int index, bool message)
    {
        var clean = new string([.. (name ?? string.Empty).Select(character =>
            char.IsControl(character) || Path.GetInvalidFileNameChars().Contains(character) || character is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|'
                ? '_'
                : character)]).Trim().Trim('.');
        if (clean.Length == 0)
        {
            clean = message ? $"message-{index + 1}.eml" : $"attachment-{index + 1}";
        }

        return clean.Length > MaxFileNameLength ? clean[..MaxFileNameLength] : clean;
    }

    private static async Task<MimeMessage?> LoadAsync(Stream raw, CancellationToken cancellationToken)
    {
        raw.Position = 0;
        try
        {
            return await MimeMessage.LoadAsync(ParserOptions.Default, raw, persistent: raw is FileStream, cancellationToken);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static Task WriteAsync(MimeEntity entity, Stream target, CancellationToken cancellationToken) => entity switch
    {
        MessagePart attached when attached.Message is not null => attached.Message.WriteToAsync(target, cancellationToken),
        MimePart part when part.Content is not null => part.Content.DecodeToAsync(target, cancellationToken),
        _ => Task.CompletedTask,
    };

    private static MailAttachmentInfo Info(MimeEntity entity, int index, long size)
    {
        var isMessage = entity is MessagePart;
        var name = entity is MimePart part ? part.FileName : entity.ContentDisposition?.FileName ?? entity.ContentType.Name;
        var contentType = entity.ContentType.MimeType;
        return new MailAttachmentInfo(
            index,
            SafeFileName(isMessage && name is not null && !name.EndsWith(".eml", StringComparison.OrdinalIgnoreCase) ? name + ".eml" : name, index, isMessage),
            contentType.Length > MaxContentTypeLength ? contentType[..MaxContentTypeLength] : contentType,
            size);
    }

    private static (string? Value, bool Cut) Cut(string? value, int maxChars) =>
        value is null ? (null, false) : value.Length > maxChars ? (value[..maxChars], true) : (value, false);

    /// <summary>Counts the bytes written (the decoded size of an attachment) without keeping them.</summary>
    private sealed class CountingStream : Stream
    {
        private long length;

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => length;

        public override long Position
        {
            get => length;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => length += count;

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            length += buffer.Length;
            return ValueTask.CompletedTask;
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            length += count;
            return Task.CompletedTask;
        }
    }
}
