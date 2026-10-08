using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Domain;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Domain.Configuration;

/// <summary>The content of a saved view: hidden columns, filter values and sort, as the list sends them to the API.</summary>
public sealed record GridViewSpec(string Name, IReadOnlyList<string> HiddenColumns, IReadOnlyDictionary<string, string> Filters, string? Sort);

/// <summary>
/// A personal view of a grid (<c>configuration.user_grid_views</c>, F21): a user saves the columns, filters and sort of a
/// list under a name and may pick one as the default, applied when the list opens without filters of its own. Owned
/// by one user; the grid catalog (which columns, filters and sorts exist) is checked by the Application layer.
/// </summary>
public sealed class GridView : AggregateRoot<Guid>, IAuditable
{
    public const int NameMaxLength = 60;
    public const int SortMaxLength = 200;
    public const int FilterValueMaxLength = 200;

    /// <summary>Views one user may keep per grid.</summary>
    public const int MaxPerGrid = 20;

    private GridView(Guid id, Guid userId, string gridKey)
        : base(id)
    {
        UserId = userId;
        GridKey = gridKey;
        Name = string.Empty;
        HiddenColumns = [];
        Filters = [];
    }

    private GridView()
    {
        GridKey = Name = string.Empty;
        HiddenColumns = [];
        Filters = [];
    }

    public Guid UserId { get; private set; }

    public string GridKey { get; private set; }

    public string Name { get; private set; }

    public List<string> HiddenColumns { get; private set; }

    public Dictionary<string, string> Filters { get; private set; }

    public string? Sort { get; private set; }

    public bool IsDefault { get; private set; }

    public static Result<GridView> Create(Guid id, Guid userId, string gridKey, GridViewSpec spec)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gridKey);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(gridKey.Length, GridLayout.GridKeyMaxLength);

        var view = new GridView(id, userId, gridKey);
        var changed = view.Change(spec);
        return changed.IsSuccess ? view : Result.Failure<GridView>(changed.Error!);
    }

    public Result Change(GridViewSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var name = spec.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > NameMaxLength)
        {
            errors["name"] = ["validation.gridViews.name"];
        }

        if (spec.HiddenColumns.Count > GridLayout.MaxColumns
            || spec.HiddenColumns.Any(key => string.IsNullOrWhiteSpace(key) || key.Length > GridLayout.ColumnKeyMaxLength))
        {
            errors["hiddenColumns"] = ["validation.gridViews.columns"];
        }

        if (spec.Filters.Count > GridLayout.MaxColumns
            || spec.Filters.Any(filter => filter.Key.Length > GridLayout.ColumnKeyMaxLength || string.IsNullOrWhiteSpace(filter.Value) || filter.Value.Length > FilterValueMaxLength))
        {
            errors["filters"] = ["validation.gridViews.filters"];
        }

        if (spec.Sort is { Length: > SortMaxLength })
        {
            errors["sort"] = ["validation.paging.sort"];
        }

        if (errors.Count > 0)
        {
            return Errors.Configuration.GridViewInvalid(errors);
        }

        Name = name;
        HiddenColumns = [.. spec.HiddenColumns.Distinct(StringComparer.Ordinal)];
        Filters = spec.Filters.ToDictionary(filter => filter.Key, filter => filter.Value.Trim(), StringComparer.Ordinal);
        Sort = string.IsNullOrWhiteSpace(spec.Sort) ? null : spec.Sort.Trim();
        return Result.Success();
    }

    public void SetDefault(bool isDefault) => IsDefault = isDefault;
}
