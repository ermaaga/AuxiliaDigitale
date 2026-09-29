using FluentValidation;

namespace Auxilia.Application.Abstractions.Paging;

/// <summary>
/// Offset paging of every list (<c>?page=1&amp;pageSize=25&amp;sort=-createdAt,lastName&amp;filter[status]=Active&amp;search=rossi</c>).
/// Sort and filter fields are whitelisted by each QueryService (<see cref="SortMap{TItem}"/>).
/// </summary>
public sealed record PageRequest
{
    public const int DefaultPageSize = 25;

    public const int MaxPageSize = 100;

    public const int MaxSearchLength = 200;

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;

    /// <summary>Comma-separated fields, <c>-</c> prefix for descending.</summary>
    public string? Sort { get; init; }

    public string? Search { get; init; }

    public IReadOnlyDictionary<string, string> Filters { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public int Skip => (Page - 1) * PageSize;
}

public sealed class PageRequestValidator : AbstractValidator<PageRequest>
{
    public PageRequestValidator()
    {
        RuleFor(request => request.Page).GreaterThanOrEqualTo(1);
        RuleFor(request => request.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize);
        RuleFor(request => request.Search).MaximumLength(PageRequest.MaxSearchLength);
    }
}
