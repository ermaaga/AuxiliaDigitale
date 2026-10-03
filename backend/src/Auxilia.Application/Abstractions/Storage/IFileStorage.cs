namespace Auxilia.Application.Abstractions.Storage;

/// <summary>
/// Where the adapter connects (ARCHITECTURE §6): the values of the tenant's <c>documents.storage.*</c> settings, read
/// by the caller; secrets are decrypted for the call and dropped. The local adapter uses its infrastructure root path.
/// </summary>
public sealed record StorageTarget(
    string? FtpHost = null,
    int FtpPort = 21,
    string? FtpUser = null,
    string? FtpPassword = null,
    string? FtpPath = null,
    bool FtpTls = true,
    string? AzureConnectionString = null,
    string? AzureContainer = null);

/// <summary>
/// A file storage adapter (port here, adapters in <c>Infrastructure/Adapters/Storage/&lt;Provider&gt;</c>, ARCHITECTURE
/// §6), chosen by <c>documents.storage.provider</c>. Keys are relative paths with <c>/</c> (e.g.
/// <c>tenants/{slug}/documents/2026/10/{id}.pdf</c>); the caller builds and checks them. Missing files are not errors.
/// </summary>
public interface IFileStorage
{
    /// <summary><c>local</c>, <c>ftp</c>, <c>azure-blob</c>.</summary>
    string Provider { get; }

    /// <summary>Whether the target has what the adapter needs (host, connection string…).</summary>
    bool IsConfigured(StorageTarget target);

    /// <summary>Writes (or replaces) the file, reading <paramref name="content"/> to the end.</summary>
    Task WriteAsync(StorageTarget target, string key, Stream content, CancellationToken cancellationToken);

    /// <summary>The content, <c>null</c> when the file does not exist. The caller disposes the stream.</summary>
    Task<Stream?> OpenReadAsync(StorageTarget target, string key, CancellationToken cancellationToken);

    /// <summary>Moves a file to another key of the same storage (staging → commit).</summary>
    Task MoveAsync(StorageTarget target, string sourceKey, string destinationKey, CancellationToken cancellationToken);

    /// <summary>Idempotent: deleting a missing file succeeds.</summary>
    Task DeleteAsync(StorageTarget target, string key, CancellationToken cancellationToken);
}
