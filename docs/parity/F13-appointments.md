# F13 — Appointments

Status: [x] done except the dashboard (B-23) · Tasks: B-16, B-17 · Quirks: Q19, Q20, Q21, Q22

## Legacy behaviour
Entity `Appointment`: ClientId, EmployeeId, ScheduledDate, DurationMinutes (60), Status string, Notes, CreatedAt, `ShowInGlobalCalendare` (true).

**Employee** `/employee/appointments` (module `Appointments`):
- Month calendar (`AppointmentCalendar`: 6×7 grid starting Sunday, prev/next month, max 2 badges per day + "+N"), click on a future day → create; click on badge → manage. Loaded: own appointments excluding `Completed`.
- Grid (config `Appointments/Employee`): client, date/time, duration, visibility (global calendar), status; sort by date/status (default date desc); action manage.
- Create modal (`AppointmentModal`): client (searchable among **my** clients), show in global calendar, date, time (default 09:00), duration, notes; status `Approved`; past date/time refused ("ErrorOnDate"); notification to client "AppointmentNew".
- Manage modal: details; Edit (date, time, duration, status Pending/Approved/Rejected/Completed, notes, global flag) → notification "AppointmentUpdated"; Approve / Reject (when Pending) → notifications; Complete (when Approved, with confirmation); Delete (confirm).
**Client** `/client/appointments`:
- Own month calendar (past days disabled) + grid ("trainer" = employee, date, duration, status). Gym wording "Trainer" becomes "Operator".
- "Request appointment": trainer = any employee (required), preferred date-time (default tomorrow / clicked day 09:00), duration (60), notes → status `Pending`, notification to the employee "New Appointment Request" with `IsCreatedByFinalUser=true`.
- Click on own appointment → details + Delete (no confirmation, any status).
**Dashboards**: admin shows all appointments from yesterday on (read-only grid); employee dashboard uses global calendar (`ShowInGlobalCalendare && Status != Completed`), upcoming count for me, charts per day and per status (Pending/Approved/Completed/Cancelled). (`Employee/Dashboard.ApproveAppointment` exists but is not wired to any button.)
Notification routing: `Appointment` → employee page if created by final user, else client page.

## Acceptance criteria
- [x] Calendar views month/week/day/list for staff and clients; mobile agenda view. *(B-17, FullCalendar; list view on phones)*
- [x] Staff create (default Approved) for own clients (Admin: any client/employee); future-only validation in tenant time zone; client notified. *(API B-16; realtime `AppointmentChanged`, persisted notification B-19)*
- [x] Client request (Pending) choosing an employee (assigned preselected); employee notified. *(API B-16: `GET /appointments/employees` marks the one in charge)*
- [x] Edit (incl. drag & drop move), approve, reject, complete (confirm), cancel, delete — each notifies the other party; history kept. *(API B-16; UI B-17)*
- [x] Statuses Pending, Approved, Rejected, Completed, Cancelled.
- [ ] "Show in global calendar" flag honoured by the shared/global calendar and employee dashboard. *(API `calendar?global=true` B-16; dashboard B-23)*
- [x] Conflict warning when overlapping appointments for the same employee. *(API `conflicts` + `hasConflict` B-16; confirm before saving or moving, warning in the drawer B-17)*
- [x] Client can cancel its own appointment with confirmation. *(API B-16; confirmation B-17)*
