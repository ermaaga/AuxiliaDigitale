using System.Buffers;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

using Auxilia.Application.Abstractions.Settings;
using Auxilia.Application.Abstractions.Storage;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Documents.Public;
using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Results;

using Microsoft.Extensions.Logging;

namespace Auxilia.Application.Documents;

/// <summary>File names as the legacy app cleaned them (Q51): spaces to underscores, quotes and invalid characters removed.</summary>
internal static partial class FileNames
{
    public const int MaxLength = 200;

    /// <summary>The clean name (extension kept, trimmed to <see cref="MaxLength"/>), empty when nothing usable is left.</summary>
    public static string Sanitize(string? fileName)
    {
        var name = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/'));
        var clean = InvalidCharacters().Replace(name.Trim().Replace(' ', '_'), string.Empty).Trim('.');
        if (clean.Length <= MaxLength)
        {
            return clean;
        }

        var extension = Path.GetExtension(clean);
        return clean[..(MaxLength - extension.Length)] + extension;
    }

    /// <summary>Quotes plus the characters no file system accepts (the Windows set, the same on every server).</summary>
    [GeneratedRegex("[\"'<>:/\\\\|?*\\x00-\\x1F]", RegexOptions.CultureInvariant)]
    private static partial Regex InvalidCharacters();
}

/// <summary>Storage keys: always under the tenant's prefix, no parent segments, forward slashes only.</summary>
internal static partial class StorageKeys
{
    public static string Prefix(string tenantSlug) => $"tenants/{tenantSlug}/";

    public static bool BelongsTo(string key, string tenantSlug) =>
        key.StartsWith(Prefix(tenantSlug), StringComparison.Ordinal) && !key.Contains("..", StringComparison.Ordinal)
        && !key.Contains('\\', StringComparison.Ordinal) && !key.Contains("//", StringComparison.Ordinal);

    public static bool IsArea(string area) => AreaPattern().IsMatch(area);

    [GeneratedRegex("^[a-z][a-z0-9-]{0,39}$", RegexOptions.CultureInvariant)]
    private static partial Regex AreaPattern();
}

/// <inheritdoc cref="IFileStore"/>
internal sealed class FileStore(
    IEnumerable<IFileStorage> storages,
    ISettingsProvider settings,
    ITenantContext tenantContext,
    TimeProvider clock,
    ILogger<FileStore> logger) : IFileStore
{
    public const string StagingArea = "staging";

    public async Task<Result<StagedFile>> StageAsync(Stream content, string? fileName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        var name = FileNames.Sanitize(fileName);
        var extension = Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
        if (name.Length == 0 || extension.Length == 0 || name.Length == extension.Length + 1)
        {
            return Errors.Documents.FileInvalid();
        }

        if (FileTypes.Find(extension) is not { } type)
        {
            Log.Security.UploadRejected(logger, extension, "type");
            return Errors.Documents.FileTypeNotAllowed();
        }

        var header = await ReadHeaderAsync(content, cancellationToken);
        if (header.Length == 0)
        {
            return Errors.Documents.FileInvalid();
        }

        if (!type.Matches(header))
        {
            Log.Security.UploadRejected(logger, extension, "content");
            return Errors.Documents.FileContentMismatch();
        }

        var opened = await OpenStorageAsync(cancellationToken);
        if (opened.IsFailure)
        {
            return Result.Failure<StagedFile>(opened.Error!);
        }

        var (storage, target) = opened.Value;
        var maxBytes = (long)await settings.GetAsync(DocumentsSettings.MaxUploadMb, cancellationToken) * 1024 * 1024;
        var key = $"{StorageKeys.Prefix(tenantContext.Tenant.Slug)}{StagingArea}/{Guid.CreateVersion7():N}";
        await using var guarded = new GuardedUploadStream(header, content, maxBytes);
        try
        {
            await storage.WriteAsync(target, key, guarded, cancellationToken);
        }
        catch (Exception exception) when (exception is GuardedUploadStream.TooLargeException || exception.InnerException is GuardedUploadStream.TooLargeException)
        {
            Log.Security.UploadRejected(logger, extension, "size");
            await TryDeleteAsync(storage, target, key);
            return Errors.Documents.FileTooLarge();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Log.Documents.StorageOperationFailed(logger, exception, storage.Provider, "write", key);
            await TryDeleteAsync(storage, target, key);
            return Errors.Documents.StorageUnavailable();
        }

        return new StagedFile(key, name, extension, type.ContentType, guarded.Length, guarded.Sha256());
    }

    public async Task<Result<string>> CommitAsync(StagedFile file, string area, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(area);

        var slug = tenantContext.Tenant.Slug;
        if (!StorageKeys.IsArea(area) || area == StagingArea)
        {
            throw new ArgumentException($"'{area}' is not a storage area.", nameof(area));
        }

        if (!Owned(file.StagingKey, slug) || FileTypes.Find(file.Extension) is null)
        {
            return Errors.Identity.PermissionDenied();
        }

        var opened = await OpenStorageAsync(cancellationToken);
        if (opened.IsFailure)
        {
            return Result.Failure<string>(opened.Error!);
        }

        var (storage, target) = opened.Value;
        var now = clock.GetUtcNow().UtcDateTime;
        var key = $"{StorageKeys.Prefix(slug)}{area}/{now:yyyy}/{now:MM}/{Guid.CreateVersion7():N}.{file.Extension}";
        try
        {
            await storage.MoveAsync(target, file.StagingKey, key, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Log.Documents.StorageOperationFailed(logger, exception, storage.Provider, "commit", file.StagingKey);
            return Errors.Documents.StorageUnavailable();
        }

        Log.Documents.FileCommitted(logger, key, file.Size, file.ContentType);
        return key;
    }

    public async Task<Result<Stream?>> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        if (!Owned(key, tenantContext.Tenant.Slug))
        {
            return Errors.Identity.PermissionDenied();
        }

        var opened = await OpenStorageAsync(cancellationToken);
        if (opened.IsFailure)
        {
            return Result.Failure<Stream?>(opened.Error!);
        }

        var (storage, target) = opened.Value;
        try
        {
            return Result.Success(await storage.OpenReadAsync(target, key, cancellationToken));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Log.Documents.StorageOperationFailed(logger, exception, storage.Provider, "read", key);
            return Errors.Documents.StorageUnavailable();
        }
    }

    public async Task<Result> DeleteAsync(string key, CancellationToken cancellationToken)
    {
        if (!Owned(key, tenantContext.Tenant.Slug))
        {
            return Errors.Identity.PermissionDenied();
        }

        var opened = await OpenStorageAsync(cancellationToken);
        if (opened.IsFailure)
        {
            return Result.Failure(opened.Error!);
        }

        var (storage, target) = opened.Value;
        try
        {
            await storage.DeleteAsync(target, key, cancellationToken);
            return Result.Success();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Log.Documents.StorageOperationFailed(logger, exception, storage.Provider, "delete", key);
            return Errors.Documents.StorageUnavailable();
        }
    }

    private static async Task<ReadOnlyMemory<byte>> ReadHeaderAsync(Stream content, CancellationToken cancellationToken)
    {
        var buffer = new byte[FileTypes.HeaderLength];
        var read = 0;
        int last;
        while (read < buffer.Length && (last = await content.ReadAsync(buffer.AsMemory(read), cancellationToken)) > 0)
        {
            read += last;
        }

        return buffer.AsMemory(0, read);
    }

    private bool Owned(string? key, string slug)
    {
        if (key is not null && StorageKeys.BelongsTo(key, slug))
        {
            return true;
        }

        Log.Security.StorageKeyRejected(logger, slug);
        return false;
    }

    private async Task<Result<(IFileStorage Storage, StorageTarget Target)>> OpenStorageAsync(CancellationToken cancellationToken)
    {
        var provider = await settings.GetAsync(DocumentsSettings.StorageProvider, cancellationToken);
        var target = provider switch
        {
            "ftp" => new StorageTarget(
                FtpHost: await settings.GetAsync(DocumentsSettings.FtpHost, cancellationToken),
                FtpPort: await settings.GetAsync(DocumentsSettings.FtpPort, cancellationToken),
                FtpUser: await settings.GetAsync(DocumentsSettings.FtpUser, cancellationToken),
                FtpPassword: await settings.GetSecretAsync(DocumentsSettings.FtpPassword, cancellationToken),
                FtpPath: await settings.GetAsync(DocumentsSettings.FtpPath, cancellationToken),
                FtpTls: await settings.GetAsync(DocumentsSettings.FtpTls, cancellationToken)),
            "azure-blob" => new StorageTarget(
                AzureConnectionString: await settings.GetSecretAsync(DocumentsSettings.AzureConnectionString, cancellationToken),
                AzureContainer: await settings.GetAsync(DocumentsSettings.AzureContainer, cancellationToken)),
            _ => new StorageTarget(),
        };

        if (storages.FirstOrDefault(storage => storage.Provider == provider) is not { } adapter || !adapter.IsConfigured(target))
        {
            Log.Documents.StorageUnavailable(logger, provider);
            return Errors.Documents.StorageUnavailable();
        }

        return (adapter, target);
    }

    private async Task TryDeleteAsync(IFileStorage storage, StorageTarget target, string key)
    {
        try
        {
            await storage.DeleteAsync(target, key, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A staged leftover is harmless: staging is cleaned by the storage lifecycle rules.
            Log.Documents.StorageOperationFailed(logger, exception, storage.Provider, "delete", key);
        }
    }
}

/// <summary>
/// The upload as the storage reads it: the header already read, then the rest of the request; counts and hashes what
/// passes and stops with <see cref="TooLargeException"/> beyond the limit, so a file is never read whole into memory.
/// </summary>
internal sealed class GuardedUploadStream(ReadOnlyMemory<byte> header, Stream rest, long maxBytes) : Stream
{
    private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private int headerPosition;
    private long length;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => length;

    public override long Position
    {
        get => length;
        set => throw new NotSupportedException();
    }

    public string Sha256() => Convert.ToHexStringLower(hash.GetCurrentHash());

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var rented = ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            var read = ReadAsync(rented.AsMemory(0, buffer.Length)).AsTask().GetAwaiter().GetResult();
            rented.AsSpan(0, read).CopyTo(buffer);
            return read;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int read;
        if (headerPosition < header.Length)
        {
            read = Math.Min(buffer.Length, header.Length - headerPosition);
            header.Slice(headerPosition, read).CopyTo(buffer);
            headerPosition += read;
        }
        else
        {
            read = await rest.ReadAsync(buffer, cancellationToken);
        }

        length += read;
        if (length > maxBytes)
        {
            throw new TooLargeException();
        }

        hash.AppendData(buffer.Span[..read]);
        return read;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            hash.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>The upload went past the limit.</summary>
    public sealed class TooLargeException : IOException
    {
        public TooLargeException()
            : base("The upload is larger than the limit.")
        {
        }

        public TooLargeException(string message)
            : base(message)
        {
        }

        public TooLargeException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
