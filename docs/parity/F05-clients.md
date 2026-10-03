# F05 — Client management

Status: [~] in progress (backend B-01 and pages B-04 done; cases, status recomputation and the 360° tabs of later modules pending) · Tasks: B-01, B-04, B-08 · Quirks: Q01, Q03, Q23, Q29, Q30, Q52, Q53, Q54, Q60

## Legacy behaviour
**Lists**
- `Admin/Clients.razor` (`/admin/clients`, module `Clients`): grid from `PageConfiguration("Clients","Administrator")` — photo, surname, name, e-mail, phone, assigned operator, status, active membership. Filters: fullname (matches name or surname), surname, e-mail, username, phone; sort: fullname, surname, e-mail, username (default surname, name). Row actions: documents (`/admin/documents?clientId=`), detail, toggle status, delete (confirm).
- `Employee/Clients.razor` (`/employee/clients`): "My clients" = `AssignedEmployeeId == me`; actions documents + detail. Includes the "add client" form (`CreateClientForm`).
- `Employee/AllClients.razor` (`/employee/allclients`, page `AllClients`): all clients with assigned employee column.
- Custom-field columns appended automatically (see F20).

**Create**
- Admin form: name*, surname*, birth date*, e-mail*, phone (max 10), fiscal code* (max 16, upper-case), custom fields. Validations: mandatory fields, CF regex `^[A-Z]{6}[0-9]{2}[A-Z][0-9]{2}[A-Z][0-9]{3}[A-Z]$`, CF unique (`FiscalCodeExistsAsync`). Username = e-mail. `IsActive = true`. Role Client. If no employee → default employee.
- Employee form (`CreateClientForm`): same fields/validations; `IsActive = false`; `AssignedEmployeeId = current employee`.

**Detail**
- `Admin/ClientDetail.razor` (`/admin/clients/{id}`): info card (edit username, name, surname, e-mail, phone, CF, active yes/no, new password "leave empty to keep", custom fields), assigned employee select + save (assign/unassign), subscriptions grid (membership, start, end, amount, status badge) with detail/delete, "Add subscription" modal (requires an assigned employee: warning `EmployeeNotSetted`; membership searchable select shows price; start date).
- `Employee/ClientDetail.razor` (`/employee/clients/{id}`): same info edit **without** password but **with client specialization** (single select from Client-role specializations); actions "Add subscription" (only memberships with no specialization or one of the employee's specializations) and "Upload single document" (→ documents page); subscriptions filtered by private rules (F10), detail/delete only if `CanManageSubscription`, delete hidden when Completed.
- Toggle status (admin list): allowed only if the client has an assigned employee, else toast `UnableToEnableUser`.
- Delete: hard delete with confirmation (Q29).

**Implicit status rule** (`UserService.UpdateUserStatusBasedOnSubscriptionAsync`, called on case create/update/delete/complete): client `IsActive` = has any case with `IsActive && (EndDate == null || EndDate >= now)` (Q03).

## Acceptance criteria
- [~] One `/clients` page with views "My clients" (assigned to me), "All clients" and "Overview" (F07); columns configurable (F21) incl. custom fields. (B-04: views mine/all, grid layout of the role, custom field columns; Overview with F07)
- [x] Filters and sorts listed above work server-side with paging. (B-01: `GET /clients`)
- [x] Create client (wizard): mandatory fields, CF regex + uniqueness inline check, e-mail as username (unique), custom fields; created by Admin → active; created by Employee → not active and assigned to that employee; no employee → default employee (API B-01/B-02; wizard B-04: CF checked in the browser, uniqueness by the API on create)
- [~] Client 360° detail with tabs: overview, cases, documents, appointments, requests, notes/timeline, custom fields. (B-04: overview with custom fields, personal data, employee with history, specializations, account; cases, documents, appointments, requests and timeline join with their modules)
- [x] Admin can change assigned employee (history kept) or remove it. (B-01 API)
- [x] Admin can set a new password for a client (or send activation/reset link). (B-01 API: temporary password or reset link; activation link)
- [x] Employee can set the client's specialization(s). (B-01 API, many — Q30)
- [x] Enabling a client without an assigned employee is refused with a coded error. (`AUX-13019`)
- [ ] Client status recomputed on case create/delete/complete and by the expiry job, following Q03. *(create/delete/complete done in B-08; expiry job B-25)*
- [x] Delete is a soft delete with confirmation; deleted clients disappear from lists. (B-01 API; confirmation B-04)
- [ ] Creating a case from the client requires an assigned employee (admin) — or auto-assigns the default one.
