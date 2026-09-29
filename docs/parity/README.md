# Parity inventory (contract)

Golden rule: **no legacy feature may be lost.** Each file lists the legacy behaviour (verified in `../Auxilia` at `8fa6622`), the business rules to keep and Given/When/Then acceptance criteria. Tick the criteria when the parity E2E passes; set `Status` to `[x] done` when all are ticked.
Legacy bugs and ambiguities: [`legacy-quirks.md`](legacy-quirks.md). Plan and tasks: [`../PLAN.md`](../PLAN.md).

| F | Feature | Status |
|---|---|---|
| [F01](F01-login-logout.md) | Login / logout | ☐ |
| [F02](F02-public-registration.md) | Public registration | ☐ |
| [F03](F03-registration-approval.md) | Registration approval | ☐ |
| [F04](F04-user-profile.md) | User profile | ☐ |
| [F05](F05-clients.md) | Client management | ☐ |
| [F06](F06-employees.md) | Employee management | ☐ |
| [F07](F07-clients-overview-pdf.md) | Clients overview + PDF | ☐ |
| [F08](F08-services-categories.md) | Services and categories | ☐ |
| [F09](F09-cases-workflow.md) | Cases workflow | ☐ |
| [F10](F10-private-cases.md) | Private cases / visibility | ☐ |
| [F11](F11-case-expiry.md) | Case expiry job | ☐ |
| [F12](F12-specializations.md) | Specializations | ☐ |
| [F13](F13-appointments.md) | Appointments | ☐ |
| [F14](F14-documents.md) | Documents | ☐ |
| [F15](F15-requests.md) | Requests | ☐ |
| [F16](F16-notifications.md) | Notifications | ☐ |
| [F17](F17-active-sessions.md) | Active sessions / forced logout | ☐ |
| [F18](F18-workout-plans.md) | ~~Workout plans~~ — removed (D-09) | – |
| [F19](F19-data-import.md) | Data import | ☐ |
| [F20](F20-custom-fields.md) | Custom fields | ☐ |
| [F21](F21-grid-configuration.md) | Grid configuration | ☐ |
| [F22](F22-modules-pages.md) | Modules / pages per role | ☐ |
| [F23](F23-system-configuration.md) | System configuration | ☐ |
| [F24](F24-localization.md) | Localization | ☐ |
| [F25](F25-application-logs.md) | Application logs | ☐ |
| [F26](F26-grid-export.md) | Grid export | ☐ |
| [F27](F27-dashboards.md) | Dashboards | ☐ |
| [F28](F28-server-side-paging.md) | Server-side paging | ☐ |
| [F29](F29-optimistic-concurrency.md) | Optimistic concurrency | ☐ |
| [F30](F30-incremental-seeds.md) | Incremental seeds | ☐ |
| [F31](F31-password-tool.md) | Password tool | ☐ |
| [F32](F32-docker-aspire.md) | Docker / Aspire / config | ☐ |
| [F33](F33-folder-templates-zip.md) | Folder templates + ZIP (new in inventory) | ☐ |
| [F34](F34-cross-cutting-ui.md) | Cross-cutting UI behaviours (new in inventory) | ☐ |

New features (not in the legacy): [`../requirements/`](../requirements/) — N01 Marketing, N02 Platform console / tenants / plans, N03 Outbound messaging.

## Legacy route → new route
| Legacy | New |
|---|---|
| `/login`, `/register`, `/` | `/{tenant}/login`, *(register: API only, D-14)*, `/{tenant}/dashboard` |
| `/admin`, `/employee`, `/client` | `/{tenant}/dashboard` |
| `/admin/clients`, `/employee/clients`, `/employee/allclients`, `/admin/clients-overview` | `/clients` (views mine / all / overview) |
| `/admin/clients/{id}`, `/employee/clients/{id}` | `/clients/{id}/…` (360°) |
| `/admin/employees`, `/admin/employees/{id}` | `/employees`, `/employees/{id}` |
| `/admin/memberships`, `/admin/memberships/{id}` | `/services`, `/services/{id}` (+ categories inside `/services`) |
| `/admin/subscriptions[/{id}]`, `/employee/subscriptions[/{id}]`, `/client/subscriptions` | `/cases`, `/cases/{id}` |
| `/admin/documents[/{id}]`, `/employee/documents[/{id}]` | `/documents` (+ detail drawer) |
| `/employee/appointments`, `/client/appointments` | `/appointments` |
| `/admin/requests`, `/employee/requests`, `/client/requests` | `/requests` |
| `/admin/registration-requests`, `/employee/registration-requests` | API only, no page (D-14) |
| `/admin/sessions` | `/sessions` |
| `/admin/logs` | `/platform/tenants/{slug}/logs` (System console, reads daily log files, D-17) |
| `/profile` | `/profile` |
| `/system/configurations` | `/platform/tenants/{slug}/settings`, `/platform/tenants/{slug}/messaging`, `/platform/tenants/{slug}/branding` (System console) |
| `/system/page-configurations[/{id}]` | `/platform/tenants/{slug}/modules`, `/platform/tenants/{slug}/permissions`, `/platform/tenants/{slug}/grids` |
| `/system/entity-configurations[/{id}]` | `/platform/tenants/{slug}/custom-fields` |
| `/system/resources[/{id}]` | `/platform/tenants/{slug}/localization` |
| `/system/role-specializations[/{id}/assign]` | `/platform/tenants/{slug}/specializations` |
| `/system/import[/{id}]`, `/system/import-types` | `/platform/tenants/{slug}/imports` |
