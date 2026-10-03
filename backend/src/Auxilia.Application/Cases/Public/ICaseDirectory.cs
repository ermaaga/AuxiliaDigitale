using Auxilia.Application.Abstractions.Cases;

namespace Auxilia.Application.Cases.Public;

/// <summary>A case as the Documents module needs it, with what the current caller may do on it (F10).</summary>
public sealed record CaseSummary(Guid Id, string Number, Guid ClientId, Guid ServiceId, string ServiceName, bool CanSee, bool CanManage);

/// <summary>A folder of a service template with its path (<c>A / B / C</c>).</summary>
public sealed record FolderSummary(Guid Id, Guid? ParentId, string Name, string Path);

/// <summary>Cases and service folders for other modules (Documents, B-12): reads only, F10 rules of the caller applied.</summary>
public interface ICaseDirectory
{
    /// <summary>The case (not deleted); <c>null</c> when it does not exist.</summary>
    Task<CaseSummary?> FindAsync(Guid caseId, CancellationToken cancellationToken);

    /// <summary>The folder template of the service in tree order.</summary>
    Task<IReadOnlyList<FolderSummary>> FoldersAsync(Guid serviceId, CancellationToken cancellationToken);
}

internal sealed class CaseDirectory(ICaseDataFactory cases, IServiceCatalogDataFactory catalog, CaseAccessPolicy policy) : ICaseDirectory
{
    public async Task<CaseSummary?> FindAsync(Guid caseId, CancellationToken cancellationToken)
    {
        await using var store = await cases.OpenAsync(cancellationToken);
        if (await store.FindAsync(caseId, readOnly: true, cancellationToken) is not { } found)
        {
            return null;
        }

        var resource = await CaseResources.OfAsync(store, found, cancellationToken);
        var names = await store.NamesAsync(found, cancellationToken);
        return new CaseSummary(
            found.Id,
            found.Number,
            found.ClientId,
            found.ServiceId,
            names.ServiceName,
            await policy.CanSeeAsync(resource, cancellationToken),
            await policy.CanManageAsync(resource, cancellationToken));
    }

    public async Task<IReadOnlyList<FolderSummary>> FoldersAsync(Guid serviceId, CancellationToken cancellationToken)
    {
        await using var store = await catalog.OpenAsync(cancellationToken);
        return FolderTree.Flatten(await store.FoldersAsync(serviceId, readOnly: true, cancellationToken))
            .Select(folder => new FolderSummary(folder.Id, folder.ParentId, folder.Name, folder.Path))
            .ToArray();
    }
}
