namespace Auxilia.Contracts.Cases;

/// <summary>An item of the document checklist of a service (B-26): <c>folderId</c> a folder of the service where the document goes.</summary>
public sealed record ServiceChecklistItemResponse(Guid Id, string Name, Guid? FolderId, bool Required);

/// <param name="Id">An existing item to keep (its ticks stay); none for a new one.</param>
public sealed record ServiceChecklistItemRequest(Guid? Id, string Name, Guid? FolderId, bool Required);

/// <summary>The whole checklist in order: items left out are removed (with their ticks).</summary>
public sealed record SaveServiceChecklistRequest(IReadOnlyList<ServiceChecklistItemRequest> Items);
