# F12 — Role specializations

Status: [ ] not started · Tasks: 4.09, 4.12 · Quirks: Q30, Q33, Q34

## Legacy behaviour
- `SystemConfigurator/RoleSpecializations.razor` (`/system/role-specializations`, SystemConfigurator only): create/edit form: name*, role* (dropdown), description, e-mail, work number, "Private subscriptions" flag. Table: name, role, e-mail, work number, private (icon), actions assign / edit / delete (confirm). Only active specializations listed; delete = `IsActive=false`.
- `SystemConfigurator/AssignSpecialization.razor` (`/system/role-specializations/{id}/assign`): shows description/e-mail/work number; select a user **of the specialization's role** not yet assigned → Assign; assigned users table (name, username, e-mail) with unassign. Navigation to/from this page is broken (Q33).
- Specializations are also set from employee detail (Employee-role, single) and employee's client detail (Client-role, single); memberships and subscriptions reference an Employee-role specialization.

## Acceptance criteria
- [ ] `/settings/specializations`: CRUD with role (Client/Employee), description, e-mail, work number, private flag; deactivate instead of delete.
- [ ] Assignment page reachable from the list: assign/unassign users of that role (multi).
- [ ] Specializations editable from employee and client detail (multi-select).
- [ ] Private flag drives F10.
