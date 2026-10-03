using Auxilia.Application.Abstractions.Storage;

using Azure;
using Azure.Storage.Blobs;

namespace Auxilia.Infrastructure.Adapters.Storage.AzureBlob;

/// <summary>
/// Files in an Azure Blob container (legacy <c>AzureStorage:ConnectionString</c>; <c>Azure.Storage.Blobs</c>, MIT): the
/// key is the blob name. The container is created at the first write. Downloads are never public (no SAS URLs).
/// </summary>
internal sealed class AzureBlobFileStorage : IFileStorage
{
    public const string ProviderKey = "azure-blob";

    public string Provider => ProviderKey;

    public bool IsConfigured(StorageTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return !string.IsNullOrWhiteSpace(target.AzureConnectionString) && !string.IsNullOrWhiteSpace(target.AzureContainer);
    }

    public async Task WriteAsync(StorageTarget target, string key, Stream content, CancellationToken cancellationToken)
    {
        var container = Container(target);
        await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        await container.GetBlobClient(key).UploadAsync(content, overwrite: true, cancellationToken);
    }

    public async Task<Stream?> OpenReadAsync(StorageTarget target, string key, CancellationToken cancellationToken)
    {
        try
        {
            return await Container(target).GetBlobClient(key).OpenReadAsync(cancellationToken: cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    public async Task MoveAsync(StorageTarget target, string sourceKey, string destinationKey, CancellationToken cancellationToken)
    {
        var container = Container(target);
        var source = container.GetBlobClient(sourceKey);
        var copy = await container.GetBlobClient(destinationKey).StartCopyFromUriAsync(source.Uri, cancellationToken: cancellationToken);
        await copy.WaitForCompletionAsync(cancellationToken);
        await source.DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }

    public async Task DeleteAsync(StorageTarget target, string key, CancellationToken cancellationToken)
    {
        try
        {
            await Container(target).GetBlobClient(key).DeleteIfExistsAsync(cancellationToken: cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            // The container does not exist yet: nothing to delete.
        }
    }

    private static BlobContainerClient Container(StorageTarget target) => new(target.AzureConnectionString, target.AzureContainer);
}
