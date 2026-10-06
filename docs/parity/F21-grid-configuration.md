# F21 — Grid configuration

Status: [~] in progress · Tasks: S-04, P3-07 

> **Decision D-18:** grid layouts are managed by **System** (platform console).

## Legacy behaviour
- `PageConfiguration` (unique `PageName`+`Role`, optional `ParentPageId`, `IsEnabled`, `ConfigurationGrid` JSON `[{Label, Property, FilterThisColumn, OrderThisColumn}]`). Pages read their grid config via `PageService.GetGridConfigurationAsync(page, role)` (5-min cache) and fall back to a hard-coded default.
- Pages using it: Admin Dashboard/Clients/Subscriptions/Memberships/Requests; Employee Dashboard/Clients/AllClients/Documents/Appointments/Subscriptions.
- `/system/page-configurations`: list (page, role, parent, status, created; row click → edit), create (page name*, role* Administrator/Employee/Client, parent page, enabled), delete; detail edits page/role/parent/enabled and grid columns (label = translation key, property path, filter, order; add/remove). Cache refreshed on save.
- `DataGrid`: label rendered as translation key; filter inputs for `FilterThisColumn`; clickable headers for `OrderThisColumn`; default page size 10; pager with ellipsis.

## Acceptance criteria
- [x] `grid_layouts` per grid key and role: columns (label key, field, filterable, sortable, visible, order); editor in `/platform/tenants/{slug}/grids` (System console).
- [x] Every legacy grid key exists with the legacy default columns (seeded from current `PageConfiguration` rows for the migrated tenant). *(E-05 `AccessStep`: the `ConfigurationGrid` of a page becomes the role layout of the matching grid)*
- [x] Users can save personal views (columns, filters, sort) and pick a default. *(H-04d: `configuration.user_grid_views`, `/api/v1/me/grids/{key}/views`; "Views" menu of `DataTable` on the 7 lists with a grid; the default opens an untouched list; tests `GridViewTests`, `GridViewEndpointsTests`, E2E `tenant-services.spec.ts`)*
- [x] Filters/sorts only on fields the API supports (validated server-side). *(Sortable/filterable come from the grid declaration in code, never from the layout; each QueryService keeps its whitelist.)*

## Status notes
- S-04: grids are declared by the module descriptors (`Grids`: key `<module>.<grid>`, columns with label key, sortable, filterable, can hide, visible by default, and the roles that see the grid); the first one is `identity.loginAttempts` (login audit). `configuration.grid_layouts` keeps, per grid and role, the columns in their order with their visibility (jsonb); a stored layout is read against the current declaration (removed columns dropped, new ones appended with their default, fixed columns always shown). Console page `/platform/tenants/{slug}/grids` (per role: show/hide, move up/down, save, back to default); the tenant app reads `GET /me/grids/{key}` (layout of the first of the user's roles that sees the grid) and applies it with `applyLayout` (the login audit already does). Users can still show hidden columns from the column picker. Pending: the legacy grid keys with their default columns arrive with their pages (B-xx); personal saved views (`user_saved_views`) with the business grids; the legacy `PageConfiguration` import (Fase 7).
