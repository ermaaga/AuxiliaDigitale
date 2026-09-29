namespace Auxilia.Contracts.Common;

/// <summary>A page of a list: <c>{ items, page, pageSize, totalCount }</c> (skill auxilia-api-contract).</summary>
public sealed record PagedResponse<TItem>(IReadOnlyList<TItem> Items, int Page, int PageSize, long TotalCount);
