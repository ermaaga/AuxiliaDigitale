using System.Collections.Concurrent;

using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;

namespace Auxilia.ServiceDefaults.Logging.Storage;

/// <summary><c>azure-blob</c> storage (production): one append blob per daily file in the logs container.</summary>
public sealed class AzureAppendBlobLogStore : ILogFileStore
{
    private readonly BlobContainerClient container;
    private readonly ConcurrentDictionary<string, bool> createdBlobs = new(StringComparer.Ordinal);
    private volatile bool containerCreated;

    public AzureAppendBlobLogStore(BlobContainerClient container)
    {
        ArgumentNullException.ThrowIfNull(container);
        this.container = container;
    }

    public async Task AppendAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        if (!containerCreated)
        {
            await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            containerCreated = true;
        }

        var blob = container.GetAppendBlobClient(path);
        if (!createdBlobs.ContainsKey(path))
        {
            await blob.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            createdBlobs[path] = true;
        }

        var maxBlock = blob.AppendBlobMaxAppendBlockBytes;
        for (var offset = 0; offset < content.Length; offset += maxBlock)
        {
            var block = content.Slice(offset, Math.Min(maxBlock, content.Length - offset));
            using var stream = new MemoryStream(block.ToArray(), writable: false);
            await blob.AppendBlockAsync(stream, cancellationToken: cancellationToken);
        }
    }

    public async Task<Stream?> OpenReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            // allowModifications: the writers keep appending while the Log page reads.
            return await container.GetAppendBlobClient(path).OpenReadAsync(new BlobOpenReadOptions(allowModifications: true), cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }
}
