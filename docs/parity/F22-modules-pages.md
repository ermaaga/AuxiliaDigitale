# F22 — Module and page enabling per role

Status: [~] in progress · Tasks: P1-06, P1-11, P2-03, S-01, S-06 · Quirks: Q39, Q40

> **Decisions D-18:** only the platform **System** role enables/disables modules, per tenant **and per role**; model = plans (future pricing) + tenant overrides (ARCHITECTURE §5). The tenant `SystemConfigurator` role no longer exists.

## Legacy behaviour
- `environmentconfig.json` seeds `ModuleConfiguration(Role, ModulePath, IsEnabled)` on empty DB: Administrator {Dashboard, Employees, Clients, Requests{UserRequests, RegistrationRequests}, Subscriptions, Memberships, Logs, Sessions}; Employee {Dashboard, Clients, AllClients, Documents, Subscriptions, Appointments, RegistrationRequests, Requests}; Client {Dashboard, Appointments, Requests, Subscription}. Seed script adds Employee `Subscriptions`.
- `PageConfiguration.IsEnabled` per page/role (seed: Admin Requests/RegistrationRequests/Logs **disabled**; Employee WorkoutPlans/RegistrationRequests/Requests **disabled**; Client WorkoutPlans enabled… — `WorkoutPlans` rows are dropped, F18).
- `ModuleAccessControl ModulePath="X"`: allowed ⇔ module enabled for role (missing = denied) **and** page enabled (missing = allowed; last path segment); SystemConfigurator always allowed for pages. Otherwise "Access denied" panel.
- Sidebar shows each link only if the page is enabled for the role. SystemConfigurator menu: Role specializations, Configurations, Entity configurations, Page configurations, Resources, Import, Import types.
- Both use only the first role claim (Q39).

## Status notes
- P1-11: module catalog from descriptors, effective modules per tenant and role (cached), hidden module → 404, navigation builder with per-role entries mirroring the legacy menus. Pending: `role_permissions` + `/me/navigation` (P2-03), System editing (S-01). The tenant table `configuration.modules` is replaced by the Catalog (ARCHITECTURE §5.2).
- P2-03: permissions declared by the module descriptors (`<module>.<area>.<action>`, `<Module>Permissions`), stored per tenant in `identity.permissions` + `identity.role_permissions` and synchronised at every tenant migration (a new permission is granted to its default roles once; grants of known permissions are never touched again, so the System's changes and the legacy import mapping stay; undeclared permissions are dropped). Effective permissions = union over **all** the user's roles (Q39) of the role grants whose module is visible to that role; grants cached per tenant (`t:{slug}:identity:role-permissions:current`), invalidated after `migrate tenants`. Endpoints use `RequirePermission(...)` (403 `AUX-12028`, security event `AUX-29014`) after the module filter (hidden module stays 404); Managers use `IAccessGuard` (permission, then every `IResourceAccessPolicy<T>` of the resource). `GET /me` (profile, roles, effective permissions) and `GET /me/navigation` (entries of visible modules for the user's roles **and** permissions). Pending: System editing of plans/overrides/role permissions (S-01), legacy import mapping of `ModuleConfiguration`/`PageConfiguration` rows (Fase 7), sidebar (P3).
- S-06: the System edits the role permissions from the console (`/platform/tenants/{slug}/permissions`, tenant-scoped platform token): technical endpoints `GET /api/v1/role-permissions` (every declared permission with its module, the roles holding it and its default roles), `PUT /role-permissions/{role}` (the whole set of the role, unknown role or permission → 400 `AUX-12058`), `DELETE /role-permissions/{role}` (the default grants of the modules again). A change invalidates the cached grants after commit (`t:{slug}:identity`), so it applies to the next request of every user of the role; it is logged as the security event `AUX-29024` (granted and revoked codes) and audited (`AUX-12056`/`12057`). The page shows one table per module, one checkbox per role (saved at once), and a role whose grants differ from the defaults can restore them.

Default grants (seed) — Administrator (A), Employee (E), Client (C):

| Permission | Roles | Legacy page / feature |
|---|---|---|
| `reporting.dashboard.view` | A, E, C | Dashboard (F27) |
| `directory.clients.view` / `.manage` | A, E | Clients, AllClients (F05) |
| `directory.employees.view` / `.manage` | A | Employees (F06) |
| `directory.registrations.review` | A, E | RegistrationRequests (F03) |
| `cases.cases.view` | A, E, C | Subscriptions / Subscription (F09; clients see their own) |
| `cases.cases.manage` | A, E | Subscriptions (F09) |
| `cases.cases.delete` | A, E | delete cases (employees not completed ones, F10; B-08) |
| `cases.services.view` | A, E | service choice when opening a case (F08, F09; B-07) |
| `cases.services.manage` | A | Memberships (F08) |
| `scheduling.appointments.view` / `.manage` | A, E, C | Appointments (F13) |
| `documents.files.view` / `.manage` | A, E | Documents (F14) |
| `engagement.requests.view` / `.manage` | A, E, C | Requests (F15) |
| `engagement.requests.delete` | A | Requests (F15) |
| `marketing.campaigns.view` / `.manage` | A, E | Marketing (N01) |
| `identity.users.manage` | A | account management (F01, F05, F06) |
| `identity.sessions.view` / `.revoke` | A | Sessions (F17) |
| `identity.loginAttempts.view` | A | Login audit (F35) |

The legacy seed disabled some pages (Q40, e.g. Employee Requests): new tenants follow the acceptance criteria above; migrated tenants get their effective legacy grants from the import mapping (Fase 7).

## Acceptance criteria
- [ ] Tenant module registry (`configuration.modules`) + role permissions reproduce the **effective** legacy visibility for each role (seed + legacy import mapping table documented in `mapping.md`).
- [ ] `GET /me/navigation` returns only allowed entries (union of roles); sidebar and mobile use it.
- [ ] Disabled module → endpoints 404 and hidden from navigation; missing permission → 403.
- [ ] Only the platform System role enables/disables modules per tenant and per role (`/platform/tenants/{slug}/modules`, plans in `/platform/plans`) and edits role permissions (`/platform/tenants/{slug}/permissions`). — role permissions done (S-06); module overrides per role are on the tenant overview (S-01); plans page pending.
