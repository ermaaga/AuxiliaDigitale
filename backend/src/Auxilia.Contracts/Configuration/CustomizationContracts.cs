namespace Auxilia.Contracts.Configuration;

/// <summary>An entity whose records carry custom fields (F20): <c>client</c>, <c>case</c>…</summary>
public sealed record CustomFieldEntityResponse(string Code, string Module, string NameKey);

/// <summary>
/// A custom field of an entity (F20). <see cref="Type"/> is <c>Text</c>, <c>Number</c>, <c>Date</c>, <c>Boolean</c>,
/// <c>Select</c> or <c>MultiSelect</c>; <see cref="Options"/> only for the two selects. Grouped fields share one grid
/// column, booleans shown as badges of <see cref="BadgeColor"/>.
/// </summary>
public sealed record CustomFieldDefinitionResponse(
    Guid Id,
    string EntityType,
    string Key,
    string Label,
    string Type,
    IReadOnlyList<string> Options,
    bool IsRequired,
    string? GroupName,
    string? BadgeColor,
    bool VisibleOnGrid,
    bool DashboardCounter,
    int Order);

public sealed record CreateCustomFieldRequest(
    string EntityType,
    string Key,
    string Label,
    string Type,
    IReadOnlyList<string>? Options,
    bool IsRequired,
    string? GroupName,
    string? BadgeColor,
    bool VisibleOnGrid,
    bool DashboardCounter,
    int Order);

/// <summary>Entity, key and type never change: the stored values depend on them.</summary>
public sealed record UpdateCustomFieldRequest(
    string Label,
    IReadOnlyList<string>? Options,
    bool IsRequired,
    string? GroupName,
    string? BadgeColor,
    bool VisibleOnGrid,
    bool DashboardCounter,
    int Order);

public sealed record CreateCustomFieldResponse(Guid Id);

/// <summary>A column of a grid as the code declares it: whether the API can sort or filter by it, whether it may be hidden.</summary>
public sealed record GridColumnResponse(string Key, string LabelKey, bool Sortable, bool Filterable, bool CanHide, bool VisibleByDefault);

/// <summary>Columns of a layout in their order.</summary>
public sealed record GridLayoutColumnResponse(string Key, bool Visible);

/// <summary>The layout a role sees: the System's one, or the default (<see cref="IsCustomized"/> false).</summary>
public sealed record GridRoleLayoutResponse(string Role, bool IsCustomized, IReadOnlyList<GridLayoutColumnResponse> Columns);

/// <summary>A grid of the tenant app (F21) with its columns and the layout of every role that sees it.</summary>
public sealed record GridResponse(
    string Key,
    string Module,
    string NameKey,
    IReadOnlyList<GridColumnResponse> Columns,
    IReadOnlyList<GridRoleLayoutResponse> Layouts);

public sealed record GridLayoutColumnRequest(string Key, bool Visible);

/// <summary><c>PUT /grids/{key}/layouts/{role}</c>: the columns in their order; columns left out keep their default visibility at the end.</summary>
public sealed record SetGridLayoutRequest(IReadOnlyList<GridLayoutColumnRequest> Columns);

/// <summary>The layout of a grid for the signed-in user (<c>GET /me/grids/{key}</c>).</summary>
public sealed record MyGridLayoutResponse(string GridKey, string Role, bool IsCustomized, IReadOnlyList<GridLayoutColumnResponse> Columns);

/// <summary>
/// A personal view of a grid (F21): the columns hidden, the filter values (<c>filter[key]</c> of the list) and the sort
/// (<c>?sort=</c>) saved under a name; the default view opens with the list.
/// </summary>
public sealed record GridViewResponse(
    Guid Id,
    string Name,
    IReadOnlyList<string> HiddenColumns,
    IReadOnlyDictionary<string, string> Filters,
    string? Sort,
    bool IsDefault);

/// <summary>Saves a personal view; <c>isDefault</c> makes it the only default of the grid for the user.</summary>
public sealed record SaveGridViewRequest(
    string Name,
    IReadOnlyList<string>? HiddenColumns,
    IReadOnlyDictionary<string, string>? Filters,
    string? Sort,
    bool IsDefault);
