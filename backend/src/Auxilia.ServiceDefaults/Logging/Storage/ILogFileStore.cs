namespace Auxilia.ServiceDefaults.Logging.Storage;

/// <summary>Append-only storage of the daily log files; <c>path</c> is relative, e.g. <c>tenants/acme/2026/09/29.jsonl</c>.</summary>
public interface ILogFileStore
{
    Task AppendAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken);

    /// <summary>Opens the file for reading while the writers keep appending; <c>null</c> when it does not exist.</summary>
    Task<Stream?> OpenReadAsync(string path, CancellationToken cancellationToken);
}
