# F22 — Module and page enabling per role

Status: [ ] not started · Tasks: P1-06, P1-11, P2-03, S-01 · Quirks: Q39, Q40

> **Decisions D-18:** only the platform **System** role enables/disables modules, per tenant **and per role**; model = plans (future pricing) + tenant overrides (ARCHITECTURE §5). The tenant `SystemConfigurator` role no longer exists.

## Legacy behaviour
- `environmentconfig.json` seeds `ModuleConfiguration(Role, ModulePath, IsEnabled)` on empty DB: Administrator {Dashboard, Employees, Clients, Requests{UserRequests, RegistrationRequests}, Subscriptions, Memberships, Logs, Sessions}; Employee {Dashboard, Clients, AllClients, Documents, Subscriptions, Appointments, RegistrationRequests, Requests}; Client {Dashboard, Appointments, Requests, Subscription}. Seed script adds Employee `Subscriptions`.
- `PageConfiguration.IsEnabled` per page/role (seed: Admin Requests/RegistrationRequests/Logs **disabled**; Employee WorkoutPlans/RegistrationRequests/Requests **disabled**; Client WorkoutPlans enabled… — `WorkoutPlans` rows are dropped, F18).
- `ModuleAccessControl ModulePath="X"`: allowed ⇔ module enabled for role (missing = denied) **and** page enabled (missing = allowed; last path segment); SystemConfigurator always allowed for pages. Otherwise "Access denied" panel.
- Sidebar shows each link only if the page is enabled for the role. SystemConfigurator menu: Role specializations, Configurations, Entity configurations, Page configurations, Resources, Import, Import types.
- Both use only the first role claim (Q39).

## Status notes
- P1-11: module catalog from descriptors, effective modules per tenant and role (cached), hidden module → 404, navigation builder with per-role entries mirroring the legacy menus. Pending: `role_permissions` + `/me/navigation` (P2-03), System editing (S-01). The tenant table `configuration.modules` is replaced by the Catalog (ARCHITECTURE §5.2).

## Acceptance criteria
- [ ] Tenant module registry (`configuration.modules`) + role permissions reproduce the **effective** legacy visibility for each role (seed + legacy import mapping table documented in `mapping.md`).
- [ ] `GET /me/navigation` returns only allowed entries (union of roles); sidebar and mobile use it.
- [ ] Disabled module → endpoints 404 and hidden from navigation; missing permission → 403.
- [ ] Only the platform System role enables/disables modules per tenant and per role (`/platform/tenants/{slug}/modules`, plans in `/platform/plans`) and edits role permissions (`/platform/tenants/{slug}/permissions`).
