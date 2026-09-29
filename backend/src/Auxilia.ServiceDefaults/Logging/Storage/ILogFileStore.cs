namespace Auxilia.ServiceDefaults.Logging.Storage;

/// <summary>Append-only storage of the daily log files; <c>path</c> is relative, e.g. <c>tenants/acme/2026/09/29.jsonl</c>.</summary>
public interface ILogFileStore
{
    Task AppendAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken);
}
