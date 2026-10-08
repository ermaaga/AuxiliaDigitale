# F28 — Server-side paging, filtering, sorting

Status: [x] done (verified in H-04, 2026-10-09) · Tasks: P1-05, P3-07 

## Legacy behaviour
- `DataGridRequest` {Page ≥1, PageSize 1..200 (default 10), SortColumn, SortAscending, Filters dict} → `PagedResult<T>` {Items, TotalCount, Page, PageSize, TotalPages}; `ToPagedResultAsync` counts on the filtered query before includes.
- Filters are "contains, case-insensitive" per whitelisted key; unknown keys ignored; each service defines accepted sort columns and a default order.

## Acceptance criteria
- [x] Standard query contract `page, pageSize (max 200), sort=-field, filter[field]=, search=` → `{items, page, pageSize, totalCount}`; cursor paging for mobile feeds. *(H-04: `PagedResponse` on every list (pageSize ≤ 100/200), unknown filters/sorts → 400 (H-04b); cursor paging for logs, timeline and notifications)*
- [x] Every legacy list keeps its filter keys, sort keys and default order (listed in each Fxx). *(H-04: filter and sort keys listed and checked in each Fxx (F05, F06, F08, F09, F13, F15, F17, F35); `ListFilterKeys`)*
- [x] Unknown filter/sort keys → `400` validation error (not silently ignored). *(H-04b: `filter[…]` keys the endpoint does not declare → `400 AUX-10020` with `validation.paging.filter`, `Api/Infrastructure/ListFilterKeys.cs` on every endpoint; unknown sort fields already refused by each QueryService; exports forward the list query and are exempt; test `ListQueryContractTests`)*
- [x] All list queries are `AsNoTracking` + projection; no in-memory filtering. *(H-04: list readers use `AsNoTracking` and projections; tracked loads only on write paths)*
