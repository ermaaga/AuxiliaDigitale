# F21 — Grid configuration

Status: [ ] not started · Tasks: 4.05, 4.06, P3-06

## Legacy behaviour
- `PageConfiguration` (unique `PageName`+`Role`, optional `ParentPageId`, `IsEnabled`, `ConfigurationGrid` JSON `[{Label, Property, FilterThisColumn, OrderThisColumn}]`). Pages read their grid config via `PageService.GetGridConfigurationAsync(page, role)` (5-min cache) and fall back to a hard-coded default.
- Pages using it: Admin Dashboard/Clients/Subscriptions/Memberships/Requests; Employee Dashboard/Clients/AllClients/Documents/Appointments/Subscriptions.
- `/system/page-configurations`: list (page, role, parent, status, created; row click → edit), create (page name*, role* Administrator/Employee/Client, parent page, enabled), delete; detail edits page/role/parent/enabled and grid columns (label = translation key, property path, filter, order; add/remove). Cache refreshed on save.
- `DataGrid`: label rendered as translation key; filter inputs for `FilterThisColumn`; clickable headers for `OrderThisColumn`; default page size 10; pager with ellipsis.

## Acceptance criteria
- [ ] `grid_layouts` per grid key and role: columns (label key, field, filterable, sortable, visible, order); editor in `/settings/grids`.
- [ ] Every legacy grid key exists with the legacy default columns (seeded from current `PageConfiguration` rows for the migrated tenant).
- [ ] Users can save personal views (columns, filters, sort) and pick a default.
- [ ] Filters/sorts only on fields the API supports (validated server-side).
