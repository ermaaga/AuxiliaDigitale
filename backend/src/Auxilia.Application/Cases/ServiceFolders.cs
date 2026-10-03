using Auxilia.Application.Abstractions.Cases;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Contracts.Cases;
using Auxilia.Diagnostics;
using Auxilia.Domain.Cases;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Cases;

/// <summary>
/// The folder template of a service (F33): a tree edited by Administrators on the service detail. Names are unique
/// among siblings; a new folder goes last; deleting a folder deletes its subfolders.
/// </summary>
public interface IServiceFolderManager
{
    Task<Result<Guid>> CreateAsync(Guid serviceId, CreateServiceFolderRequest request, CancellationToken cancellationToken);

    Task<Result> RenameAsync(Guid serviceId, Guid folderId, string? name, CancellationToken cancellationToken);

    /// <summary>The request lists every folder under the parent once, in the new order.</summary>
    Task<Result> ReorderAsync(Guid serviceId, ReorderServiceFoldersRequest request, CancellationToken cancellationToken);

    Task<Result> DeleteAsync(Guid serviceId, Guid folderId, CancellationToken cancellationToken);
}

public interface IServiceFolderQueryService
{
    /// <summary>The template in tree order; <c>AUX-14012</c> when the service does not exist.</summary>
    Task<Result<IReadOnlyList<ServiceFolderResponse>>> ListAsync(Guid serviceId, CancellationToken cancellationToken);
}

internal static class FolderTree
{
    /// <summary>Depth first, siblings by sort order then name: the order of the tree editor and of the "move to" list.</summary>
    public static IReadOnlyList<ServiceFolderResponse> Flatten(IReadOnlyList<ServiceFolder> folders)
    {
        var children = folders.ToLookup(folder => folder.ParentId);
        var result = new List<ServiceFolderResponse>(folders.Count);
        void Visit(Guid? parentId, int depth, string? path)
        {
            foreach (var folder in children[parentId].OrderBy(folder => folder.SortOrder).ThenBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase))
            {
                var folderPath = path is null ? folder.Name : $"{path} / {folder.Name}";
                result.Add(new ServiceFolderResponse(folder.Id, folder.ParentId, folder.Name, folder.SortOrder, depth, folderPath));
                Visit(folder.Id, depth + 1, folderPath);
            }
        }

        Visit(null, 1, null);
        return result;
    }

    /// <summary>The folder and every folder below it.</summary>
    public static IReadOnlyList<ServiceFolder> Subtree(IReadOnlyList<ServiceFolder> folders, Guid rootId)
    {
        var children = folders.ToLookup(folder => folder.ParentId);
        var result = new List<ServiceFolder>();
        var pending = new Stack<ServiceFolder>(folders.Where(folder => folder.Id == rootId));
        while (pending.TryPop(out var folder))
        {
            result.Add(folder);
            foreach (var child in children[folder.Id])
            {
                pending.Push(child);
            }
        }

        return result;
    }

    public static int DepthOf(IReadOnlyList<ServiceFolder> folders, Guid? folderId)
    {
        var byId = folders.ToDictionary(folder => folder.Id);
        var depth = 0;
        for (var current = folderId; current is { } id && byId.TryGetValue(id, out var folder); current = folder.ParentId)
        {
            depth++;
        }

        return depth;
    }

    public static bool NameTaken(IReadOnlyList<ServiceFolder> folders, Guid? parentId, string name, Guid? exceptId) =>
        folders.Any(folder => folder.ParentId == parentId && folder.Id != exceptId && string.Equals(folder.Name, name, StringComparison.OrdinalIgnoreCase));
}

internal sealed class ServiceFolderManager(IOperationRunner operations, IServiceCatalogDataFactory data) : IServiceFolderManager
{
    public Task<Result<Guid>> CreateAsync(Guid serviceId, CreateServiceFolderRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync<Guid>(Operations.Cases.CreateServiceFolder, new { ServiceId = serviceId }, async scope =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindAsync(serviceId, cancellationToken) is null)
            {
                return Errors.Cases.ServiceNotFound();
            }

            var folders = await store.FoldersAsync(serviceId, readOnly: false, cancellationToken);
            if (request.ParentId is { } parentId && folders.All(folder => folder.Id != parentId))
            {
                return Errors.Cases.ServiceFolderInvalid("parentId", "validation.serviceFolders.parent");
            }

            if (folders.Count >= ServiceFolder.MaxFoldersPerService)
            {
                return Errors.Cases.ServiceFolderInvalid("parentId", "validation.serviceFolders.tooMany");
            }

            if (FolderTree.DepthOf(folders, request.ParentId) >= ServiceFolder.MaxDepth)
            {
                return Errors.Cases.ServiceFolderInvalid("parentId", "validation.serviceFolders.depth");
            }

            var siblings = folders.Where(folder => folder.ParentId == request.ParentId).ToArray();
            var created = ServiceFolder.Create(Guid.CreateVersion7(), serviceId, request.ParentId, request.Name, siblings.Length == 0 ? 0 : siblings.Max(folder => folder.SortOrder) + 1);
            if (created.IsFailure)
            {
                return Result.Failure<Guid>(created.Error!);
            }

            if (FolderTree.NameTaken(folders, request.ParentId, created.Value.Name, null))
            {
                return Errors.Cases.ServiceFolderNameTaken();
            }

            store.Add(created.Value);
            await store.SaveChangesAsync(cancellationToken);
            scope.SetEntity(nameof(ServiceFolder), created.Value.Id);
            return Result.Success(created.Value.Id);
        }, cancellationToken);
    }

    public Task<Result> RenameAsync(Guid serviceId, Guid folderId, string? name, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Cases.RenameServiceFolder, new { ServiceId = serviceId, ServiceFolderId = folderId }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            var folders = await store.FoldersAsync(serviceId, readOnly: false, cancellationToken);
            if (folders.SingleOrDefault(folder => folder.Id == folderId) is not { } target)
            {
                return Errors.Cases.ServiceFolderNotFound();
            }

            var checkedName = ServiceFolder.CheckName(name);
            if (checkedName.IsFailure)
            {
                return Result.Failure(checkedName.Error!);
            }

            if (FolderTree.NameTaken(folders, target.ParentId, checkedName.Value, folderId))
            {
                return Errors.Cases.ServiceFolderNameTaken();
            }

            target.Rename(checkedName.Value);
            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);

    public Task<Result> ReorderAsync(Guid serviceId, ReorderServiceFoldersRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync(Operations.Cases.ReorderServiceFolders, new { ServiceId = serviceId }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            var folders = await store.FoldersAsync(serviceId, readOnly: false, cancellationToken);
            if (request.ParentId is { } parentId && folders.All(folder => folder.Id != parentId))
            {
                return Errors.Cases.ServiceFolderNotFound();
            }

            var siblings = folders.Where(folder => folder.ParentId == request.ParentId).ToDictionary(folder => folder.Id);
            var order = request.FolderIds ?? [];
            if (order.Count != siblings.Count || order.Distinct().Count() != order.Count || !order.All(siblings.ContainsKey))
            {
                return Errors.Cases.ServiceFolderInvalid("folderIds", "validation.serviceFolders.order");
            }

            for (var index = 0; index < order.Count; index++)
            {
                siblings[order[index]].MoveTo(index);
            }

            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);
    }

    public Task<Result> DeleteAsync(Guid serviceId, Guid folderId, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Cases.DeleteServiceFolder, new { ServiceId = serviceId, ServiceFolderId = folderId }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            var folders = await store.FoldersAsync(serviceId, readOnly: false, cancellationToken);
            var subtree = FolderTree.Subtree(folders, folderId);
            if (subtree.Count == 0)
            {
                return Errors.Cases.ServiceFolderNotFound();
            }

            foreach (var folder in subtree)
            {
                store.Remove(folder);
            }

            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);
}

internal sealed class ServiceFolderQueryService(IServiceCatalogDataFactory data) : IServiceFolderQueryService
{
    public async Task<Result<IReadOnlyList<ServiceFolderResponse>>> ListAsync(Guid serviceId, CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        if (await store.GetAsync(serviceId, cancellationToken) is null)
        {
            return Errors.Cases.ServiceNotFound();
        }

        return Result.Success(FolderTree.Flatten(await store.FoldersAsync(serviceId, readOnly: true, cancellationToken)));
    }
}
