using System.Linq.Expressions;

using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Abstractions.Paging;

/// <summary>
/// Whitelist of sortable fields of a list and their key expressions (translated by EF Core to SQL). Unknown fields
/// are a validation error; a unique tiebreaker is always appended so pages are stable.
/// </summary>
public sealed class SortMap<TItem>
{
    public const string UnknownFieldKey = "validation.sort.unknownField";

    private readonly Dictionary<string, LambdaExpression> fields = new(StringComparer.OrdinalIgnoreCase);
    private readonly string defaultSort;
    private readonly LambdaExpression tiebreaker;

    /// <param name="defaultSort">Sort used when the request has none, e.g. <c>"-createdAt"</c>.</param>
    /// <param name="tiebreaker">Unique key appended to every sort (usually the id).</param>
    public SortMap(string defaultSort, Expression<Func<TItem, Guid>> tiebreaker)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultSort);
        ArgumentNullException.ThrowIfNull(tiebreaker);

        this.defaultSort = defaultSort;
        this.tiebreaker = tiebreaker;
    }

    public SortMap<TItem> Add<TKey>(string field, Expression<Func<TItem, TKey>> key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentNullException.ThrowIfNull(key);

        fields.Add(field, key);
        return this;
    }

    public Result<IOrderedQueryable<TItem>> Apply(IQueryable<TItem> source, string? sort)
    {
        ArgumentNullException.ThrowIfNull(source);

        var terms = (string.IsNullOrWhiteSpace(sort) ? defaultSort : sort)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        IOrderedQueryable<TItem>? ordered = null;
        foreach (var term in terms)
        {
            var descending = term.StartsWith('-');
            var field = descending ? term[1..] : term;
            if (!fields.TryGetValue(field, out var key))
            {
                return Result.Failure<IOrderedQueryable<TItem>>(Errors.Host.ValidationFailed(
                    new Dictionary<string, string[]>(StringComparer.Ordinal) { ["sort"] = [UnknownFieldKey] }));
            }

            ordered = OrderBy(ordered ?? source, key, descending, first: ordered is null);
        }

        return Result.Success(OrderBy(ordered ?? source, tiebreaker, descending: false, first: ordered is null));
    }

    private static IOrderedQueryable<TItem> OrderBy(IQueryable<TItem> source, LambdaExpression key, bool descending, bool first)
    {
        var method = (first, descending) switch
        {
            (true, false) => nameof(Queryable.OrderBy),
            (true, true) => nameof(Queryable.OrderByDescending),
            (false, false) => nameof(Queryable.ThenBy),
            (false, true) => nameof(Queryable.ThenByDescending),
        };

        var call = Expression.Call(
            typeof(Queryable), method, [typeof(TItem), key.ReturnType], source.Expression, Expression.Quote(key));

        return (IOrderedQueryable<TItem>)source.Provider.CreateQuery<TItem>(call);
    }
}
