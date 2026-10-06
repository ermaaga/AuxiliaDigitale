# F28 — Server-side paging, filtering, sorting

Status: [ ] not started · Tasks: P1-05, P3-07 

## Legacy behaviour
- `DataGridRequest` {Page ≥1, PageSize 1..200 (default 10), SortColumn, SortAscending, Filters dict} → `PagedResult<T>` {Items, TotalCount, Page, PageSize, TotalPages}; `ToPagedResultAsync` counts on the filtered query before includes.
- Filters are "contains, case-insensitive" per whitelisted key; unknown keys ignored; each service defines accepted sort columns and a default order.

## Acceptance criteria
- [ ] Standard query contract `page, pageSize (max 200), sort=-field, filter[field]=, search=` → `{items, page, pageSize, totalCount}`; cursor paging for mobile feeds.
- [ ] Every legacy list keeps its filter keys, sort keys and default order (listed in each Fxx).
- [x] Unknown filter/sort keys → `400` validation error (not silently ignored). *(H-04b: `filter[…]` keys the endpoint does not declare → `400 AUX-10020` with `validation.paging.filter`, `Api/Infrastructure/ListFilterKeys.cs` on every endpoint; unknown sort fields already refused by each QueryService; exports forward the list query and are exempt; test `ListQueryContractTests`)*
- [ ] All list queries are `AsNoTracking` + projection; no in-memory filtering.
