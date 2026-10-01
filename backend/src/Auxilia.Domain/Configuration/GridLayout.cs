using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Domain;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Domain.Configuration;

/// <summary>A column of a grid layout: shown or hidden; its position is its place in the list.</summary>
public sealed record GridLayoutColumn(string Key, bool Visible);

/// <summary>
/// The column layout of a grid for a role (<c>configuration.grid_layouts</c>, F21, D-18): which columns the role sees
/// and in which order. One per grid and role; without one the grid shows its default columns. The grid catalog (what
/// columns exist, which must stay visible) is checked by the Application layer.
/// </summary>
public sealed class GridLayout : AggregateRoot<Guid>, IAuditable
{
    public const int GridKeyMaxLength = 100;
    public const int RoleMaxLength = 30;
    public const int ColumnKeyMaxLength = 50;
    public const int MaxColumns = 100;

    private GridLayout(Guid id, string gridKey, string role)
        : base(id)
    {
        GridKey = gridKey;
        Role = role;
        Columns = [];
    }

    private GridLayout()
    {
        GridKey = Role = string.Empty;
        Columns = [];
    }

    public string GridKey { get; private set; }

    public string Role { get; private set; }

    public List<GridLayoutColumn> Columns { get; private set; }

    public static Result<GridLayout> Create(Guid id, string gridKey, string role, IReadOnlyList<GridLayoutColumn> columns)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gridKey);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(gridKey.Length, GridKeyMaxLength);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(role.Length, RoleMaxLength);

        var layout = new GridLayout(id, gridKey, role);
        var replaced = layout.Replace(columns);
        return replaced.IsSuccess ? layout : Result.Failure<GridLayout>(replaced.Error!);
    }

    public Result Replace(IReadOnlyList<GridLayoutColumn> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);

        if (columns.Count is 0 or > MaxColumns
            || columns.Any(column => string.IsNullOrWhiteSpace(column.Key) || column.Key.Length > ColumnKeyMaxLength)
            || columns.Select(column => column.Key).Distinct(StringComparer.Ordinal).Count() != columns.Count)
        {
            return Errors.Configuration.GridLayoutInvalid("validation.grids.columns");
        }

        if (columns.All(column => !column.Visible))
        {
            return Errors.Configuration.GridLayoutInvalid("validation.grids.noneVisible");
        }

        Columns = [.. columns];
        return Result.Success();
    }
}
