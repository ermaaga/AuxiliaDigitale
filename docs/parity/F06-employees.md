# F06 — Employee (operator) management

Status: [x] done (verified in H-04, 2026-10-09) · Tasks: B-02, B-05 · Quirks: Q06, Q31, Q32, Q55

## Legacy behaviour
- `Admin/Employees.razor` (`/admin/employees`, module `Employees`): create form (full name*, birth date*, e-mail*, phone, fiscal code; username = e-mail; active; password = `DefaultPassword`). Grid: photo, name, username, e-mail, phone, status (Active/Inactive + "Default" badge), specializations. Actions: set default employee, detail, toggle active, delete (confirm). PDF export exists but commented out.
- `Admin/EmployeeDetail.razor` (`/admin/employees/{id}`): edit username, name, surname, e-mail, phone, CF, specialization (single, Employee-role specializations), active, new password; read view shows default flag and created date. "Assigned clients" grid (name, e-mail, phone, status; row click → client detail) with unassign (confirm) and "assign client" modal (select among clients not yet assigned to this employee).
- Default employee: exactly one (`SetDefaultEmployeeAsync`), used for automatic assignment (Q31).
- `AssignedAdministratorId` (employee → administrator) exists in data, no UI (Q32).

## Acceptance criteria
- [~] `/employees` list with the columns above, filters, sort, export. (B-02: `GET /employees`, filters name/surname/e-mail/user name/phone/status, sorts, paging, specializations and assigned clients per row; grid `directory.employees`; photo with B-03/B-05, export with B-22; B-05 page `/{tenant}/employees` with picture, status, default badge, row actions)
- [x] Create employee with first/last name, birth date, e-mail (username), phone, CF; activation per D-06. (B-02 API: `POST /employees`, invitation when sign-in is enabled)
- [x] Set default employee: exactly one at any time; badge visible. (B-02 API: `PUT /employees/{id}/default`, unique index, the default cannot be disabled or deleted `AUX-13030`, new clients without employee go to it; B-05: badge in list and detail, "make default" with confirmation)
- [x] Toggle active; soft delete with confirmation. (B-02 API: `PUT /employees/{id}/sign-in`, `DELETE /employees/{id}` hands the clients to the default employee; B-05: confirmation dialogs)
- [x] Detail: edit personal data, specializations (multi), active flag, set password / send reset link. (B-02 API)
- [x] Assigned clients list; assign a client (moves it from previous employee, history kept); unassign with confirmation. (B-02 API: `GET /clients?filter[employeeUserId]=`, assign/unassign via `/clients/{id}/employee` from B-01; B-05: "clients in charge" tab with server-side search to assign and confirmation to remove)
- [x] Employee → administrator assignment editable (Q32). (B-02 API: `PUT/DELETE /employees/{id}/administrator`)
- [~] Workload widget: number of assigned clients, open cases, appointments this week. (B-02: `workload.assignedClients` in the detail; open cases with B-08, appointments with B-16; B-05: workload card in the overview)
