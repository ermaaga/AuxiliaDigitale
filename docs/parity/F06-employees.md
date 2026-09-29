# F06 — Employee (operator) management

Status: [ ] not started · Tasks: B-02, B-05 · Quirks: Q06, Q31, Q32, Q55

## Legacy behaviour
- `Admin/Employees.razor` (`/admin/employees`, module `Employees`): create form (full name*, birth date*, e-mail*, phone, fiscal code; username = e-mail; active; password = `DefaultPassword`). Grid: photo, name, username, e-mail, phone, status (Active/Inactive + "Default" badge), specializations. Actions: set default employee, detail, toggle active, delete (confirm). PDF export exists but commented out.
- `Admin/EmployeeDetail.razor` (`/admin/employees/{id}`): edit username, name, surname, e-mail, phone, CF, specialization (single, Employee-role specializations), active, new password; read view shows default flag and created date. "Assigned clients" grid (name, e-mail, phone, status; row click → client detail) with unassign (confirm) and "assign client" modal (select among clients not yet assigned to this employee).
- Default employee: exactly one (`SetDefaultEmployeeAsync`), used for automatic assignment (Q31).
- `AssignedAdministratorId` (employee → administrator) exists in data, no UI (Q32).

## Acceptance criteria
- [ ] `/employees` list with the columns above, filters, sort, export.
- [ ] Create employee with first/last name, birth date, e-mail (username), phone, CF; activation per D-06.
- [ ] Set default employee: exactly one at any time; badge visible.
- [ ] Toggle active; soft delete with confirmation.
- [ ] Detail: edit personal data, specializations (multi), active flag, set password / send reset link.
- [ ] Assigned clients list; assign a client (moves it from previous employee, history kept); unassign with confirmation.
- [ ] Employee → administrator assignment editable (Q32).
- [ ] Workload widget: number of assigned clients, open cases, appointments this week.
