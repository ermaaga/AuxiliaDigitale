using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Configuration;
using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Contracts.Configuration;
using Auxilia.Diagnostics;
using Auxilia.Domain.Configuration;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Configuration;

/// <summary>
/// The personal views of the signed-in user on a grid (F21): only grids the user's roles see, only the user's own views.
/// What a view holds (columns, filters, sort) is checked by the list itself when the view is applied: unknown filters
/// and sort fields are refused there (F28).
/// </summary>
public interface IGridViewQueryService
{
    Task<Result<IReadOnlyList<GridViewResponse>>> ListMineAsync(string gridKey, CancellationToken cancellationToken);
}

public interface IGridViewManager
{
    Task<Result<GridViewResponse>> CreateAsync(string gridKey, SaveGridViewRequest request, CancellationToken cancellationToken);

    Task<Result<GridViewResponse>> UpdateAsync(string gridKey, Guid id, SaveGridViewRequest request, CancellationToken cancellationToken);

    Task<Result> DeleteAsync(string gridKey, Guid id, CancellationToken cancellationToken);
}

internal static class GridViews
{
    /// <summary>The caller's user id when the grid exists and one of the caller's roles sees it.</summary>
    public static Result<Guid> Owner(IModuleRegistry modules, ICurrentUser currentUser, string gridKey) =>
        currentUser.ActorType == ActorType.User
        && currentUser.UserId is { } userId
        && modules.Grids.TryGetValue(gridKey ?? string.Empty, out var grid)
        && currentUser.Roles.Any(grid.Roles.Contains)
            ? userId
            : Errors.Configuration.GridNotFound(gridKey ?? string.Empty);

    public static GridViewSpec Spec(SaveGridViewRequest request) =>
        new(
            request.Name ?? string.Empty,
            request.HiddenColumns ?? [],
            request.Filters ?? new Dictionary<string, string>(StringComparer.Ordinal),
            request.Sort);

    public static GridViewResponse Response(GridView view) =>
        new(view.Id, view.Name, view.HiddenColumns, view.Filters, view.Sort, view.IsDefault);
}

internal sealed class GridViewQueryService(IModuleRegistry modules, ICurrentUser currentUser, ICustomizationDataFactory data)
    : IGridViewQueryService
{
    public async Task<Result<IReadOnlyList<GridViewResponse>>> ListMineAsync(string gridKey, CancellationToken cancellationToken)
    {
        var owner = GridViews.Owner(modules, currentUser, gridKey);
        if (owner.IsFailure)
        {
            return Result.Failure<IReadOnlyList<GridViewResponse>>(owner.Error!);
        }

        await using var store = await data.OpenAsync(cancellationToken);
        var views = await store.ViewsAsync(owner.Value, gridKey, readOnly: true, cancellationToken);
        return views.Select(GridViews.Response).ToList();
    }
}

internal sealed class GridViewManager(
    IOperationRunner operations, IModuleRegistry modules, ICurrentUser currentUser, ICustomizationDataFactory data) : IGridViewManager
{
    public Task<Result<GridViewResponse>> CreateAsync(string gridKey, SaveGridViewRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync<GridViewResponse>(Operations.Configuration.SaveGridView, new { GridKey = gridKey }, async scope =>
        {
            var owner = GridViews.Owner(modules, currentUser, gridKey);
            if (owner.IsFailure)
            {
                return owner.Error!;
            }

            await using var store = await data.OpenAsync(cancellationToken);
            var views = await store.ViewsAsync(owner.Value, gridKey, readOnly: false, cancellationToken);
            if (views.Count >= GridView.MaxPerGrid)
            {
                return Invalid("name", "validation.gridViews.tooMany");
            }

            var created = GridView.Create(Guid.CreateVersion7(), owner.Value, gridKey, GridViews.Spec(request));
            if (created.IsFailure)
            {
                return created.Error!;
            }

            var view = created.Value;
            if (NameTaken(views, view.Name, exceptId: null))
            {
                return Invalid("name", "validation.gridViews.nameTaken");
            }

            store.Add(view);
            await MakeDefaultAsync(store, views, view, request.IsDefault, cancellationToken);
            scope.SetEntity("GridView", view.Id);
            return GridViews.Response(view);
        }, cancellationToken);
    }

    public Task<Result<GridViewResponse>> UpdateAsync(string gridKey, Guid id, SaveGridViewRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync<GridViewResponse>(Operations.Configuration.SaveGridView, new { GridKey = gridKey, GridView = id }, async _ =>
        {
            var owner = GridViews.Owner(modules, currentUser, gridKey);
            if (owner.IsFailure)
            {
                return owner.Error!;
            }

            await using var store = await data.OpenAsync(cancellationToken);
            var views = await store.ViewsAsync(owner.Value, gridKey, readOnly: false, cancellationToken);
            if (views.FirstOrDefault(item => item.Id == id) is not { } view)
            {
                return Errors.Configuration.GridViewNotFound();
            }

            var changed = view.Change(GridViews.Spec(request));
            if (changed.IsFailure)
            {
                return changed.Error!;
            }

            if (NameTaken(views, view.Name, view.Id))
            {
                return Invalid("name", "validation.gridViews.nameTaken");
            }

            await MakeDefaultAsync(store, views, view, request.IsDefault, cancellationToken);
            return GridViews.Response(view);
        }, cancellationToken);
    }

    public Task<Result> DeleteAsync(string gridKey, Guid id, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Configuration.DeleteGridView, new { GridKey = gridKey, GridView = id }, async _ =>
        {
            var owner = GridViews.Owner(modules, currentUser, gridKey);
            if (owner.IsFailure)
            {
                return Result.Failure(owner.Error!);
            }

            await using var store = await data.OpenAsync(cancellationToken);
            var views = await store.ViewsAsync(owner.Value, gridKey, readOnly: false, cancellationToken);
            if (views.FirstOrDefault(item => item.Id == id) is not { } view)
            {
                return Result.Failure(Errors.Configuration.GridViewNotFound());
            }

            store.Remove(view);
            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);

    private static bool NameTaken(IEnumerable<GridView> views, string name, Guid? exceptId) =>
        views.Any(item => item.Id != exceptId && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// At most one default per user and grid (unique partial index): the others are cleared and saved first, then this
    /// view becomes the default, in the same transaction.
    /// </summary>
    private static async Task MakeDefaultAsync(
        ICustomizationData store, IEnumerable<GridView> views, GridView view, bool isDefault, CancellationToken cancellationToken)
    {
        view.SetDefault(false);
        if (isDefault)
        {
            foreach (var other in views.Where(item => item.Id != view.Id && item.IsDefault))
            {
                other.SetDefault(false);
            }
        }

        await store.SaveChangesAsync(cancellationToken);
        if (isDefault)
        {
            view.SetDefault(true);
            await store.SaveChangesAsync(cancellationToken);
        }
    }

    private static Error Invalid(string field, string messageKey) =>
        Errors.Configuration.GridViewInvalid(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [messageKey] });
}
