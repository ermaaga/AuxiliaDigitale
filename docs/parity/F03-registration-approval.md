# F03 — Registration approval

Status: [ ] not started · Tasks: B-06 · Quirks: Q06, Q07, Q54

> **Decision D-14:** API only (list pending/processed, approve, reject) for Administrator and Employee; **no pages**. Activation e-mail per D-06.

## Legacy behaviour
- Two identical pages: `Admin/AdminRegistrationRequests.razor` (`/admin/registration-requests`, module `Requests.RegistrationRequests`) and `Employee/EmployeeRegistrationRequests.razor` (`/employee/registration-requests`, no module check). Both roles can approve/reject.
- Toggle "Show processed" switches between pending (ordered by request date asc) and processed (ordered by processed date desc, with "processed by"). Refresh button.
- Pending grid: first name, last name, e-mail, phone, birth date, request date + Approve / Reject buttons with confirmation ("Sei sicuro di voler approvare/rifiutare…").
- `ProcessRequestAsync(approved)`: if approved → creates `User` with `Username = Email`, `FullName = FirstName`, `Surname = LastName`, e-mail, phone, fiscal code, password `"password"`, `IsActive = false`, `AssignedEmployeeId = default employee`, role Client. In all cases sets `IsProcessed`, `ProcessedByUserId`, `ProcessedDate`, `Notes` = "Approvato/Rifiutato dall'amministratore" (admin) or "…dal trainer" (employee).
- Processed grid shows Approved/Rejected based on `Notes` containing "Approvato" (Q07).
- Dashboards show the pending count (admin card, employee card).

## Business rules
1. Administrators and Employees can process registrations.
2. Approval creates a client account (username = e-mail) assigned to the default employee, initially not active as a client.
3. A request can be processed only once.

## Acceptance criteria
- [ ] Given a pending request, when an Employee approves it, then a person + user (role Client, username = e-mail, client status `Prospect`/inactive) is created, assigned to the default employee, and the request becomes `Approved` with processor and date.
- [ ] Approval sends an activation e-mail (D-06) in the tenant language.
- [ ] Given a request already processed, when someone tries to process it again, then `409` with code.
- [ ] Rejection stores optional notes and status `Rejected`; no user is created.
- [ ] Fiscal code / e-mail / username collision with an existing person is detected at approval and reported.
- [ ] Single list API with filters (status, date, text) for both roles, permission-based (page deferred, D-14).

## Improvements
One inbox, explicit status, activation link, optional notes on approve/reject.
