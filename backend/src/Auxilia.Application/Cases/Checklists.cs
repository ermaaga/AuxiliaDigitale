using Auxilia.Application.Abstractions.Cases;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Contracts.Cases;
using Auxilia.Diagnostics;
using Auxilia.Domain.Cases;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Cases;

/// <summary>
/// The document checklist of a service (B-26, F09): the documents its cases need, optionally in a folder of the
/// service, required or not. Every case of the service shows it and ticks what it has.
/// </summary>
public interface IServiceChecklistManager
{
    /// <summary>Replaces the whole checklist; items kept by id keep their ticks.</summary>
    Task<Result> SaveAsync(Guid serviceId, SaveServiceChecklistRequest request, CancellationToken cancellationToken);
}

public interface IServiceChecklistQueryService
{
    /// <summary>In order; <c>AUX-14012</c> when the service does not exist.</summary>
    Task<Result<IReadOnlyList<ServiceChecklistItemResponse>>> ListAsync(Guid serviceId, CancellationToken cancellationToken);
}

internal sealed class ServiceChecklistManager(IOperationRunner operations, IServiceCatalogDataFactory catalog, IChecklistDataFactory data)
    : IServiceChecklistManager
{
    public Task<Result> SaveAsync(Guid serviceId, SaveServiceChecklistRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync(Operations.Cases.SaveServiceChecklist, new { ServiceId = serviceId }, async scope =>
        {
            await using var services = await catalog.OpenAsync(cancellationToken);
            if (await services.FindAsync(serviceId, cancellationToken) is null)
            {
                return Errors.Cases.ServiceNotFound();
            }

            var folders = (await services.FoldersAsync(serviceId, readOnly: true, cancellationToken)).Select(folder => folder.Id).ToHashSet();
            var items = (request.Items ?? []).Select(item => new ChecklistItemDetails(item.Id, item.Name, item.FolderId, item.Required)).ToArray();
            var checkedItems = ServiceChecklistItem.Check(items, folders);
            if (checkedItems.IsFailure)
            {
                return checkedItems;
            }

            await using var store = await data.OpenAsync(cancellationToken);
            var existing = (await store.ItemsAsync(serviceId, cancellationToken)).ToDictionary(item => item.Id);
            var kept = new HashSet<Guid>();
            for (var index = 0; index < items.Length; index++)
            {
                var details = items[index];
                ServiceChecklistItem item;
                if (details.Id is { } id && existing.TryGetValue(id, out var found))
                {
                    item = found;
                }
                else
                {
                    item = new ServiceChecklistItem(Guid.CreateVersion7(), serviceId);
                    store.Add(item);
                }

                item.Apply(details, index + 1);
                kept.Add(item.Id);
            }

            foreach (var removed in existing.Values.Where(item => !kept.Contains(item.Id)))
            {
                store.Remove(removed);
            }

            await store.SaveChangesAsync(cancellationToken);
            scope.SetEntity("Service", serviceId);
            return Result.Success();
        }, cancellationToken);
    }
}

internal sealed class ServiceChecklistQueryService(IServiceCatalogDataFactory catalog, IChecklistDataFactory data) : IServiceChecklistQueryService
{
    public async Task<Result<IReadOnlyList<ServiceChecklistItemResponse>>> ListAsync(Guid serviceId, CancellationToken cancellationToken)
    {
        await using (var services = await catalog.OpenAsync(cancellationToken))
        {
            if (await services.GetAsync(serviceId, cancellationToken) is null)
            {
                return Errors.Cases.ServiceNotFound();
            }
        }

        await using var store = await data.OpenAsync(cancellationToken);
        return (await store.ItemsAsync(serviceId, cancellationToken))
            .Select(item => new ServiceChecklistItemResponse(item.Id, item.Name, item.FolderId, item.IsRequired))
            .ToArray();
    }
}
