using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Caching;
using Auxilia.Application.Abstractions.Configuration;
using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Contracts.Configuration;
using Auxilia.Diagnostics;
using Auxilia.Domain.Configuration;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Application.Configuration;

/// <summary>Column layouts of the grids per role (F21, D-18), set by the System from the console.</summary>
public interface IGridLayoutManager
{
    /// <summary>
    /// Stores the layout of a grid for a role: known columns only, each once, the columns that cannot be hidden stay
    /// visible; columns left out are appended with their default visibility.
    /// </summary>
    Task<Result<GridRoleLayoutResponse>> SetAsync(string gridKey, string role, IReadOnlyList<GridLayoutColumnRequest> columns, CancellationToken cancellationToken);

    /// <summary>Removes the layout: the role sees the default columns again.</summary>
    Task<Result<GridRoleLayoutResponse>> ResetAsync(string gridKey, string role, CancellationToken cancellationToken);
}

public interface IGridQueryService
{
    /// <summary>Every grid with its columns and the layout of each role that sees it (System console).</summary>
    Task<IReadOnlyList<GridResponse>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The layout of a grid for the signed-in user: the first of its roles (Administrator, Employee, Client) that sees
    /// the grid. 404 when none does.
    /// </summary>
    Task<Result<MyGridLayoutResponse>> GetMineAsync(string gridKey, CancellationToken cancellationToken);
}

/// <summary>The stored layouts of the tenant (grid key → role → columns), cached with the configuration tag.</summary>
internal sealed class GridLayoutCache(IReferenceDataCache cache, ITenantContext tenantContext, ICustomizationDataFactory data)
    : ReferenceDataCache<IReadOnlyDictionary<string, IReadOnlyDictionary<string, GridLayoutColumn[]>>>(cache, tenantContext)
{
    protected override string Module => SettingsSnapshotCache.ModuleName;

    protected override string Entity => "grid-layouts";

    protected override async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, GridLayoutColumn[]>>> LoadAsync(
        TenantInfo? tenant, string variant, CancellationToken cancellationToken)
    {
        if (tenant is null)
        {
            return new Dictionary<string, IReadOnlyDictionary<string, GridLayoutColumn[]>>();
        }

        await using var store = await data.OpenAsync(cancellationToken);
        return (await store.LayoutsAsync(cancellationToken))
            .GroupBy(layout => layout.GridKey, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyDictionary<string, GridLayoutColumn[]>)group.ToDictionary(layout => layout.Role, layout => layout.Columns.ToArray(), StringComparer.Ordinal),
                StringComparer.Ordinal);
    }
}

internal static class GridLayouts
{
    public static GridLayoutColumn[] Default(GridDefinition grid) =>
        grid.Columns.Select(column => new GridLayoutColumn(column.Key, column.VisibleByDefault || !column.CanHide)).ToArray();

    /// <summary>
    /// A stored layout read against the current catalog: columns no longer declared are dropped, new ones appended with
    /// their default, columns that cannot be hidden are visible.
    /// </summary>
    public static GridLayoutColumn[] Effective(GridDefinition grid, IReadOnlyList<GridLayoutColumn>? stored)
    {
        if (stored is null)
        {
            return Default(grid);
        }

        var byKey = grid.Columns.ToDictionary(column => column.Key, StringComparer.Ordinal);
        var known = stored.Where(column => byKey.ContainsKey(column.Key))
            .Select(column => column with { Visible = column.Visible || !byKey[column.Key].CanHide })
            .ToList();
        known.AddRange(Default(grid).Where(column => known.All(item => item.Key != column.Key)));
        return [.. known];
    }

    public static GridRoleLayoutResponse Response(GridDefinition grid, string role, IReadOnlyList<GridLayoutColumn>? stored) =>
        new(role, stored is not null, Effective(grid, stored).Select(column => new GridLayoutColumnResponse(column.Key, column.Visible)).ToArray());
}

internal sealed class GridLayoutManager(
    IOperationRunner operations, ICustomizationDataFactory data, IModuleRegistry modules, ITenantContext tenantContext, IReferenceDataCache cache)
    : IGridLayoutManager
{
    public Task<Result<GridRoleLayoutResponse>> SetAsync(
        string gridKey, string role, IReadOnlyList<GridLayoutColumnRequest> columns, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(columns);

        return operations.RunAsync(Operations.Configuration.SetGridLayout, new { GridKey = gridKey, Role = role }, async scope =>
        {
            if (Find(gridKey, role) is not { } grid)
            {
                return Errors.Configuration.GridNotFound(gridKey);
            }

            var byKey = grid.Columns.ToDictionary(column => column.Key, StringComparer.Ordinal);
            if (columns.Any(column => column is null || !byKey.ContainsKey(column.Key)))
            {
                return Errors.Configuration.GridLayoutInvalid("validation.grids.unknownColumn");
            }

            if (columns.Any(column => !column.Visible && !byKey[column.Key].CanHide))
            {
                return Errors.Configuration.GridLayoutInvalid("validation.grids.mustStayVisible");
            }

            var ordered = columns.Select(column => new GridLayoutColumn(column.Key, column.Visible)).ToList();
            ordered.AddRange(GridLayouts.Default(grid).Where(column => ordered.All(item => item.Key != column.Key)));

            await using var store = await data.OpenAsync(cancellationToken);
            var existing = await store.FindLayoutAsync(grid.Key, role, cancellationToken);
            Result changed;
            if (existing is null)
            {
                var created = GridLayout.Create(Guid.CreateVersion7(), grid.Key, role, ordered);
                changed = created.IsSuccess ? Result.Success() : Result.Failure(created.Error!);
                if (created.IsSuccess)
                {
                    store.Add(created.Value);
                }
            }
            else
            {
                changed = existing.Replace(ordered);
            }

            if (changed.IsFailure)
            {
                return Result.Failure<GridRoleLayoutResponse>(changed.Error!);
            }

            await store.SaveChangesAsync(cancellationToken);
            InvalidateAfterCommit(scope);
            return GridLayouts.Response(grid, role, ordered);
        }, cancellationToken);
    }

    public Task<Result<GridRoleLayoutResponse>> ResetAsync(string gridKey, string role, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Configuration.ResetGridLayout, new { GridKey = gridKey, Role = role }, async scope =>
        {
            if (Find(gridKey, role) is not { } grid)
            {
                return Result.Failure<GridRoleLayoutResponse>(Errors.Configuration.GridNotFound(gridKey));
            }

            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindLayoutAsync(grid.Key, role, cancellationToken) is { } existing)
            {
                store.Remove(existing);
                await store.SaveChangesAsync(cancellationToken);
                InvalidateAfterCommit(scope);
            }

            return Result.Success(GridLayouts.Response(grid, role, null));
        }, cancellationToken);

    /// <summary>The grid, when the role is one of those that see it.</summary>
    private GridDefinition? Find(string gridKey, string role) =>
        modules.Grids.TryGetValue(gridKey ?? string.Empty, out var grid) && grid.Roles.Any(item => item.ToString() == role) ? grid : null;

    private void InvalidateAfterCommit(IOperationScope scope)
    {
        var slug = tenantContext.Tenant.Slug;
        scope.OnCommitted(ct => cache.InvalidateAsync(CacheTags.Tenant(slug, SettingsSnapshotCache.ModuleName), ct));
    }
}

internal sealed class GridQueryService(IModuleRegistry modules, GridLayoutCache cache, ICurrentUser currentUser) : IGridQueryService
{
    public async Task<IReadOnlyList<GridResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var stored = await cache.GetAsync(cancellationToken);
        return modules.Grids.Values
            .OrderBy(grid => grid.Key, StringComparer.Ordinal)
            .Select(grid => new GridResponse(
                grid.Key,
                grid.Key[..grid.Key.IndexOf('.', StringComparison.Ordinal)],
                grid.NameKey,
                grid.Columns.Select(column => new GridColumnResponse(column.Key, column.LabelKey, column.Sortable, column.Filterable, column.CanHide, column.VisibleByDefault)).ToArray(),
                grid.Roles.Order().Select(role => GridLayouts.Response(grid, role.ToString(), StoredOf(stored, grid.Key, role.ToString()))).ToArray()))
            .ToArray();
    }

    public async Task<Result<MyGridLayoutResponse>> GetMineAsync(string gridKey, CancellationToken cancellationToken)
    {
        var roles = currentUser.ActorType == ActorType.User && modules.Grids.TryGetValue(gridKey ?? string.Empty, out var found)
            ? currentUser.Roles.Where(found.Roles.Contains).Order().ToArray()
            : [];
        if (roles.Length == 0)
        {
            return Errors.Configuration.GridNotFound(gridKey ?? string.Empty);
        }

        var grid = modules.Grids[gridKey!];
        var role = roles[0];
        var layout = GridLayouts.Response(grid, role.ToString(), StoredOf(await cache.GetAsync(cancellationToken), grid.Key, role.ToString()));
        return new MyGridLayoutResponse(grid.Key, layout.Role, layout.IsCustomized, layout.Columns);
    }

    private static GridLayoutColumn[]? StoredOf(IReadOnlyDictionary<string, IReadOnlyDictionary<string, GridLayoutColumn[]>> stored, string gridKey, string role) =>
        stored.TryGetValue(gridKey, out var roles) && roles.TryGetValue(role, out var columns) ? columns : null;
}
