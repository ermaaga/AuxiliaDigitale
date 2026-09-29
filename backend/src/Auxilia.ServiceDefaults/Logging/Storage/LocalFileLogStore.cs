namespace Auxilia.ServiceDefaults.Logging.Storage;

/// <summary><c>local-file</c> storage (development and local buffer): files under a root folder, shared by Api, Worker and Runner.</summary>
public sealed class LocalFileLogStore : ILogFileStore
{
    private readonly string root;

    public LocalFileLogStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        root = Path.GetFullPath(rootDirectory);
    }

    public async Task AppendAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(Path.Combine(root, path));
        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Log path '{path}' is outside the log folder.", nameof(path));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        // FileShare.ReadWrite: other processes append to the same daily file and the Log page reads it.
        await using var stream = new FileStream(
            fullPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, bufferSize: 1, useAsync: true);
        await stream.WriteAsync(content, cancellationToken);
    }
}
