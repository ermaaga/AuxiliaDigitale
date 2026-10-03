# F08 — Services (Membership) and categories (MembershipType)

Status: [~] in progress (backend B-07 done; cases of a service with B-08/B-09, pages with B-15, folders F33 with B-10) · Tasks: B-07, B-15 · Quirks: Q26, Q27, Q28

## Legacy behaviour
- `Admin/Memberships.razor` (`/admin/memberships`, module `Memberships`): collapsible create/edit form: name (required), price €, duration days, type (MembershipType dropdown), specialization (Employee-role), active (edit only), description. Grid (config `Memberships/Administrator`): name, description, specialization, price, duration, status. Sort by name/price (default name). Actions: detail, delete (confirm, hard delete).
- `Admin/MembershipDetail.razor` (`/admin/memberships/{id}`): header price/duration/active; edit name, description, price, duration, specialization, active; list of subscriptions of this membership (client with photo, start, end, amount, active/expired; row click → subscription detail; export CSV/PDF); **folder template editor** (F33).
- MembershipType: service CRUD (`Create/Update/DeleteMembershipTypeAsync`) but no UI (Q26).
- Seed (dev): ISEE (€10, 1 day), 730 (€50, 7 days), F24 (€150, 30 days) with Italian descriptions.

## Backend (B-07)
API (module `cases`; read `cases.services.view` Administrator + Employee, write `cases.services.manage` Administrator):
`GET/POST /api/v1/services`, `GET/PUT/DELETE /api/v1/services/{id}` (filters `filter[name|categoryId|specializationId|active]`,
sort `name`/`price`/`durationDays`), `GET/POST /api/v1/service-categories`, `PUT/DELETE /api/v1/service-categories/{id}`.
Service names unique among those not deleted, category names among the active ones; a new category/specialization must be
active (an existing one that became inactive may stay); the specialization is an active one of the Employee role and is
kept on create (Q27); delete = soft delete (Q28), a category only while no service (deleted ones included) uses it.
Grid `cases.services`. B-08 must refuse deleting a service that has cases.

## Acceptance criteria
- [ ] `/services` list with name, category, specialization, price, duration, active; filters; export.
- [x] Create/edit service incl. category and specialization (specialization saved on create — Q27). *(API; form with B-15)*
- [ ] Deactivate instead of delete when cases exist; delete allowed only when unused (Q28).
- [ ] Service detail shows its cases (paged, export, click → case). *(API `GET /cases?filter[serviceId]=` B-09; page B-15, export B-22)*
- [ ] Categories CRUD page (Q26).
- [x] Price stored as `numeric(12,2)` + currency EUR.
- [ ] Folder template editor on service detail (F33).
