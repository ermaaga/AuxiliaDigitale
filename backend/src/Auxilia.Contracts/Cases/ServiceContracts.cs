namespace Auxilia.Contracts.Cases;

/// <summary>A category of the service catalog (F08, Q26); <c>serviceCount</c> counts the services not deleted that use it.</summary>
public sealed record ServiceCategoryResponse(Guid Id, string Name, string? Description, bool IsActive, int ServiceCount);

public sealed record CreateServiceCategoryRequest(string Name, string? Description);

/// <summary>An inactive category stays on its services and cannot be given to others.</summary>
public sealed record UpdateServiceCategoryRequest(string Name, string? Description, bool IsActive);

public sealed record ServiceCategoryRefResponse(Guid Id, string Name, bool IsActive);

/// <summary>An Employee specialization; <c>isPrivate</c> makes the cases of the service private (F10).</summary>
public sealed record ServiceSpecializationResponse(Guid Id, string Name, bool IsPrivate);

/// <summary>A service of the catalog (F08): price in <c>currency</c> with two decimals, duration in days.</summary>
public sealed record ServiceResponse(
    Guid Id,
    string Name,
    string? Description,
    decimal Price,
    string Currency,
    int DurationDays,
    bool IsActive,
    ServiceCategoryRefResponse? Category,
    ServiceSpecializationResponse? Specialization);

/// <summary>
/// A new service: <c>categoryId</c> an active category, <c>specializationId</c> an active Employee specialization (Q27:
/// kept on create). Price ≥ 0 with at most two decimals; duration 1–3650 days.
/// </summary>
public sealed record CreateServiceRequest(
    string Name,
    string? Description,
    decimal Price,
    int DurationDays,
    Guid? CategoryId,
    Guid? SpecializationId);

/// <summary>As <see cref="CreateServiceRequest"/>; a category or specialization that became inactive may stay as it is.</summary>
public sealed record UpdateServiceRequest(
    string Name,
    string? Description,
    decimal Price,
    int DurationDays,
    Guid? CategoryId,
    Guid? SpecializationId,
    bool IsActive);

/// <summary>
/// The service list (F08): <c>name</c> matches anywhere, <c>categoryId</c>, <c>specializationId</c>, <c>active</c>;
/// <c>sort</c> one of <c>name</c> (default), <c>price</c>, <c>durationDays</c>, <c>-</c> for descending.
/// </summary>
public sealed record ServiceListQuery(
    string? Name,
    Guid? CategoryId,
    Guid? SpecializationId,
    bool? Active,
    string? Sort,
    int Page,
    int PageSize);

/// <summary>
/// A folder of a service template (F33), in tree order (depth first, siblings by <c>sortOrder</c>): <c>depth</c> 1 for
/// the root folders, <c>path</c> the names from the root (<c>A / B / C</c>).
/// </summary>
public sealed record ServiceFolderResponse(Guid Id, Guid? ParentId, string Name, int SortOrder, int Depth, string Path);

/// <summary>A new folder at the end of its siblings; <c>parentId</c> null for the root.</summary>
public sealed record CreateServiceFolderRequest(string Name, Guid? ParentId);

public sealed record RenameServiceFolderRequest(string Name);

/// <summary>Every folder under <c>parentId</c> (null for the root), in the new order.</summary>
public sealed record ReorderServiceFoldersRequest(Guid? ParentId, IReadOnlyList<Guid> FolderIds);
