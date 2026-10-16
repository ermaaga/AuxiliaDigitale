namespace Auxilia.Infrastructure.Adapters.Mail;

/// <summary>Temporary files for messages read live (C-06b): seekable, deleted when the stream is closed, never in memory.</summary>
internal static class TemporaryFiles
{
    private const int BufferSize = 81920;

    public static FileStream Create() =>
        new(Path.Combine(Path.GetTempPath(), $"auxilia-mail-{Guid.NewGuid():N}.tmp"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
            BufferSize, FileOptions.DeleteOnClose | FileOptions.Asynchronous);

    /// <summary>
    /// Copies at most <paramref name="maxBytes"/> + 1 bytes of <paramref name="source"/> into a temporary file positioned at
    /// its start: a longer source is cut, so the caller sees a length above the bound and refuses it.
    /// </summary>
    public static async Task<Stream> CopyBoundedAsync(Stream source, long maxBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var target = Create();
        try
        {
            var buffer = new byte[BufferSize];
            long copied = 0;
            int read;
            while (copied <= maxBytes && (read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, maxBytes + 1 - copied)), cancellationToken)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                copied += read;
            }

            await target.FlushAsync(cancellationToken);
            target.Position = 0;
            return target;
        }
        catch
        {
            await target.DisposeAsync();
            throw;
        }
    }
}
