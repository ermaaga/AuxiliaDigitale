# F12 — Role specializations

Status: [x] done (verified in H-04, 2026-10-09) · Tasks: S-06, B-01, B-02, B-09 · Quirks: Q30, Q33, Q34

> **Decision D-18:** specializations are managed by the platform **System** role (legacy SystemConfigurator). Assigning specializations to employees/clients remains available where the legacy allowed it (employee detail, client detail).

## Legacy behaviour
- `SystemConfigurator/RoleSpecializations.razor` (`/system/role-specializations`, SystemConfigurator only): create/edit form: name*, role* (dropdown), description, e-mail, work number, "Private subscriptions" flag. Table: name, role, e-mail, work number, private (icon), actions assign / edit / delete (confirm). Only active specializations listed; delete = `IsActive=false`.
- `SystemConfigurator/AssignSpecialization.razor` (`/system/role-specializations/{id}/assign`): shows description/e-mail/work number; select a user **of the specialization's role** not yet assigned → Assign; assigned users table (name, username, e-mail) with unassign. Navigation to/from this page is broken (Q33).
- Specializations are also set from employee detail (Employee-role, single) and employee's client detail (Client-role, single); memberships and subscriptions reference an Employee-role specialization.

## Acceptance criteria
- [x] `/platform/tenants/{slug}/specializations` (System console): CRUD with role (Client/Employee), description, e-mail, work number, private flag; deactivate instead of delete.
- [x] Assignment page reachable from the list: assign/unassign users of that role (multi).
- [x] Specializations editable from employee and client detail (multi-select). — with the employee and client details (B-01/B-02). *(H-04: tabs `?tab=specializations` of employees and clients (B-05, B-04))*
- [x] Private flag drives F10. — stored and edited (S-06); used by the private cases (B-09). *(H-04: `CaseAccessPolicy` (private specialization), see F10)*

## Status notes
- S-06: `directory.specializations` (name `citext` unique per role among active ones, role `Client`/`Employee` only — Q34, description, e-mail, work phone, `is_private`, `is_active`) and `directory.specialization_members` (user, assigned at; many-to-many — Q30; only users of the specialization's role). Technical endpoints with a tenant-scoped platform token (D-18, D-21): `GET/POST /api/v1/specializations` (filter `filter[role]`), `PUT/DELETE /specializations/{id}` (delete deactivates, members kept, the name is free again), `GET /specializations/{id}/members`, `GET /specializations/{id}/candidates?search=` (users of the role not holding it, at most 50, search on user name, name and e-mail), `POST /specializations/{id}/members` (1–100 users), `DELETE /specializations/{id}/members/{userId}` (idempotent). Codes `AUX-13001…13009`. Console pages `/platform/tenants/{slug}/specializations` (list, role filter, create/edit, delete with confirm) and `/specializations/{id}` (details, members, add several users at once) linked both ways (Q33 fixed).
