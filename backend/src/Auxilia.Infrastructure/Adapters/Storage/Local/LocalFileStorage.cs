using Auxilia.Application.Abstractions.Storage;

using Microsoft.Extensions.Options;

namespace Auxilia.Infrastructure.Adapters.Storage.Local;

/// <summary>The local folder of the documents (section <c>Storage:Local</c>, infrastructure level; legacy <c>DocumentStorage:Path</c>).</summary>
public sealed class LocalStorageOptions
{
    public const string SectionName = "Storage:Local";

    /// <summary>Absolute, or relative to the working directory.</summary>
    public string RootPath { get; set; } = "storage";
}

/// <summary>
/// Files in a local folder (development, single server). Writes go to a temporary file renamed at the end, so a
/// half-written upload is never readable; every key must stay inside the root folder.
/// </summary>
internal sealed class LocalFileStorage(IOptions<LocalStorageOptions> options) : IFileStorage
{
    public const string ProviderKey = "local";

    public string Provider => ProviderKey;

    public bool IsConfigured(StorageTarget target) => !string.IsNullOrWhiteSpace(options.Value.RootPath);

    public async Task WriteAsync(StorageTarget target, string key, Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        var path = PathOf(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".uploading";
        try
        {
            await using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await content.CopyToAsync(file, cancellationToken);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public Task<Stream?> OpenReadAsync(StorageTarget target, string key, CancellationToken cancellationToken)
    {
        var path = PathOf(key);
        return Task.FromResult<Stream?>(File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true)
            : null);
    }

    public Task MoveAsync(StorageTarget target, string sourceKey, string destinationKey, CancellationToken cancellationToken)
    {
        var destination = PathOf(destinationKey);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Move(PathOf(sourceKey), destination, overwrite: true);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(StorageTarget target, string key, CancellationToken cancellationToken)
    {
        File.Delete(PathOf(key));
        return Task.CompletedTask;
    }

    private string PathOf(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var root = Path.GetFullPath(options.Value.RootPath);
        var path = Path.GetFullPath(Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar)));
        return path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? path
            : throw new ArgumentException("The key leaves the storage folder.", nameof(key));
    }
}
