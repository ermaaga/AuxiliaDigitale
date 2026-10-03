using Auxilia.Application.Abstractions.Storage;

using FluentFTP;

namespace Auxilia.Infrastructure.Adapters.Storage.Ftp;

/// <summary>
/// Files on an FTP server (legacy <c>FtpStorageService</c>; <c>FluentFTP</c>, MIT): explicit FTPS unless
/// <c>documents.storage.ftp.tls</c> is off, keys under <c>documents.storage.ftp.path</c>. One connection per call.
/// </summary>
internal sealed class FtpFileStorage : IFileStorage
{
    public const string ProviderKey = "ftp";

    public string Provider => ProviderKey;

    public bool IsConfigured(StorageTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return !string.IsNullOrWhiteSpace(target.FtpHost);
    }

    public async Task WriteAsync(StorageTarget target, string key, Stream content, CancellationToken cancellationToken)
    {
        await using var client = await ConnectAsync(target, cancellationToken);
        var status = await client.UploadStream(content, PathOf(target, key), FtpRemoteExists.Overwrite, createRemoteDir: true, token: cancellationToken);
        if (status == FtpStatus.Failed)
        {
            throw new IOException($"FTP upload of {key} failed.");
        }
    }

    public async Task<Stream?> OpenReadAsync(StorageTarget target, string key, CancellationToken cancellationToken)
    {
        await using var client = await ConnectAsync(target, cancellationToken);
        var path = PathOf(target, key);
        if (!await client.FileExists(path, cancellationToken))
        {
            return null;
        }

        // The connection closes with this call: the file is buffered (uploads are bounded by documents.maxUploadMb).
        var buffer = new MemoryStream();
        if (!await client.DownloadStream(buffer, path, token: cancellationToken))
        {
            await buffer.DisposeAsync();
            throw new IOException($"FTP download of {key} failed.");
        }

        buffer.Position = 0;
        return buffer;
    }

    public async Task MoveAsync(StorageTarget target, string sourceKey, string destinationKey, CancellationToken cancellationToken)
    {
        await using var client = await ConnectAsync(target, cancellationToken);
        var destination = PathOf(target, destinationKey);
        await client.CreateDirectory(destination[..destination.LastIndexOf('/')], cancellationToken);
        if (!await client.MoveFile(PathOf(target, sourceKey), destination, FtpRemoteExists.Overwrite, cancellationToken))
        {
            throw new IOException($"FTP move of {sourceKey} failed.");
        }
    }

    public async Task DeleteAsync(StorageTarget target, string key, CancellationToken cancellationToken)
    {
        await using var client = await ConnectAsync(target, cancellationToken);
        var path = PathOf(target, key);
        if (await client.FileExists(path, cancellationToken))
        {
            await client.DeleteFile(path, cancellationToken);
        }
    }

    /// <summary>The remote path: the configured folder, then the key.</summary>
    internal static string PathOf(StorageTarget target, string key)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return $"{(target.FtpPath ?? "/").TrimEnd('/')}/{key.TrimStart('/')}";
    }

    private static async Task<AsyncFtpClient> ConnectAsync(StorageTarget target, CancellationToken cancellationToken)
    {
        var client = new AsyncFtpClient(target.FtpHost, target.FtpUser ?? "anonymous", target.FtpPassword ?? string.Empty, target.FtpPort);
        client.Config.EncryptionMode = target.FtpTls ? FtpEncryptionMode.Explicit : FtpEncryptionMode.None;
        try
        {
            await client.Connect(cancellationToken);
            return client;
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
    }
}
