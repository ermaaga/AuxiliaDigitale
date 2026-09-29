# Legacy quirks, bugs and implicit behaviours

Source: `../Auxilia` at `8fa6622` (2026-06-23). Every entry was verified in code.
**Resolution**: `Keep` = reproduce as-is (parity) · `Fix` = the legacy behaviour is a bug, the new platform corrects it · `Decide` = needs user confirmation (see `docs/PLAN.md` §5.2).
Nothing in the `Fix` column removes a capability; if in doubt, reproduce and flag.

| ID | Area | Legacy behaviour (evidence) | Resolution |
|---|---|---|---|
| Q01 | Auth | `UserService.AuthenticateAsync` does **not** check `IsActive`, although `Login.razor` error says "…OR The user is Inactive". Inactive clients (e.g. just registered, or with no open case) can log in. | **Decide (D-05)** — default: split `users.is_active` (can log in) from `client_profiles.status` (business); legacy users imported with login enabled. |
| Q02 | Cases | `CreateSubscriptionAsync` ignores the `amountPaid` argument and always stores `AmountPaid = Membership.Price`; the "Amount paid" input on the admin create forms has no effect. | Fix — `price` snapshot = service price; money received is recorded as payments. |
| Q03 | Cases/Clients | `EndDate` is `null` at creation and only set to `now` by `UpdateSubscriptionStatusAsync(Completed)`. `UpdateSubscriptionAsync` (edit dates) is **never called** by the UI. Client `IsActive` = any sub with `IsActive && (EndDate == null || EndDate >= now)` → a client is active while it has at least one non-completed case and becomes inactive when its last case is completed. | Keep (encode as domain rule, see F05/F09). |
| Q04 | Cases | `Membership.DurationDays` is only used by the admin create panel to show a computed end date that is never saved. | **Decide (D-07)** — default keep: `expires_on` null at creation. |
| Q05 | Registration | Unique DB index on `RegistrationRequests.Email` (all rows, processed included) → an e-mail can register only once ever; the second attempt fails with the generic "Email già presente" message. Service also rejects duplicates among pending only. | Fix — uniqueness only among `Pending`; clear message if the e-mail already belongs to a user. |
| Q06 | Registration/Users | Approved registrations get password `"password"` (hard-coded); staff-created users get `DefaultPassword` from config (`"password"`). | **Decide (D-06)** — default: activation link by e-mail; staff can still set a password manually. |
| Q07 | Registration | Processed list shows Approved/Rejected by checking whether `Notes` contains `"Approvato"`. | Fix — explicit `status` enum. |
| Q08 | Registration | "Privacy consent" checkbox is required only client-side (submit disabled until checked) and is **not stored** (`RegistrationRequest` has no field; `User.PrivacyConsent` never set by any flow; privacy link `href=""`). `User.EnableConfigurationSettings` is also never used. | Fix — consent required server-side and stored with timestamp/version; privacy URL as tenant setting. |
| Q09 | Private cases | Code (`SubscriptionService.GetUserSubscriptionsAsync(userId, employeeId)`, `DocumentManager.ApplyEmployeeAccessFilter`): a case is visible to an employee if it has no specialization, **or** its specialization is not private, **or** the employee holds that specialization. Docs (`documentations/PRIVATE_SUBSCRIPTIONS.md`) and `data-model.md` say "only the assigned employee". | **Decide (D-04)** — default: follow the code; Administrators see everything. |
| Q10 | Private cases | `Employee/SubscriptionDetail.razor` loads any subscription by id with no access check (only its documents are filtered). | Fix — 404 when not visible. |
| Q11 | Private cases | Documents without a case: visible if the client is assigned to the employee, or the client has no private specialization the employee lacks. | Keep (F10). |
| Q12 | Notifications | Header click routing is role-agnostic: `Request`→`/admin/requests`, `Subscription`→`/admin/subscriptions` even for clients/employees; `WorkoutPlan`→`/client/workoutplans` (route does not exist). | Fix — deep link per type **and** role. |
| Q13 | Documents | Queued upload handler pushes `ReceiveNotification` to `Clients.User(clientId)` while hub groups are `user_{username}` → the uploader never gets the realtime confirmation. | Fix — notify the uploader (`DocumentProcessed`). |
| Q14 | Expiry job | "Expiring" de-duplication looks for `Type == "SubscriptionExpiring"` but creates `Type = "Subscription"` → a new "expiring" notification every 6 h. No e-mail is sent by the job (only by the manual action). | Fix dedup (one per day); e-mail per **D-10**. |
| Q15 | Expiry job | Registered only if appsettings `EnableSubscriptionExpiryService=true` **and** runs only if setting `AutoSubscriptionExpiry=true`. | Keep the tenant setting; the app flag disappears (job always scheduled, gated by setting). |
| Q16 | Requests | Admin "Requests" page lists only requests with `ReceiverId == null`; requests addressed to an employee are invisible to admins. | Keep default view + add admin "all requests" filter (improvement). |
| Q17 | Requests | Admin reply creates a notification for the sender; employee reply does not. | Fix — always notify the sender. |
| Q18 | Requests | UI types: `Information`, `General`, `Support`; entity comment says `Information, Appointment, General`. | Keep all four as enum values. |
| Q19 | Appointments | Statuses actually used: `Pending`, `Approved`, `Rejected`, `Completed` (`Cancelled` only in a comment/chart). | Keep all + `Cancelled` (client cancel). |
| Q20 | Appointments | "Date in the past" check compares a local date with `DateTime.UtcNow.TimeOfDay` (wrong around midnight/TZ). | Fix — validate in tenant time zone. |
| Q21 | Appointments | Client can **delete** any of its appointments (any status) without confirmation; "move" is commented out. | Keep capability as *cancel* (with confirmation); moving becomes a new request. |
| Q22 | Appointments | Client appointment request lists **all** employees ("Trainer"), not only the assigned one. | Keep; preselect assigned employee; label "Operator" (gym wording removed). |
| Q23 | Clients | `Admin/Clients.razor` "Add subscription" modal `CreateSubscription()` does nothing. | Drop (dead code; creation exists elsewhere). |
| Q24 | Cases | `ClientSubscriptionPanel` (with "send expiry e-mail") is rendered in `Employee/Clients.razor` but `ShowAsync` is never called; other "send expiry e-mail" buttons are commented out. `SendExpiryNotificationAsync` is therefore unreachable. | Keep capability as case action "Send expiry reminder" (F11). |
| Q25 | Training | `WorkoutPlanService`, PDF and e-mail exist; **no page** lists/creates plans; client dashboard links to `/client/workout-plans` (missing) and "Active workout plans" is always 0. | **Removed (D-09)** — gym dead code, not ported (see F18). |
| Q26 | Services | `MembershipType` CRUD exists in the service but there is no management UI (only a dropdown on create, not editable later). | Fix — categories UI. |
| Q27 | Services | `Memberships.razor` create with a specialization calls `UpdateMembershipAsync` without the specialization argument → the specialization chosen at creation is lost. | Fix. |
| Q28 | Services | Delete membership is a hard delete (FK `Restrict` from subscriptions → error when used). | Fix — deactivate/soft delete; block if cases exist. |
| Q29 | Users | Delete user is a hard delete (many `Restrict` FKs → failures). | Fix — soft delete. |
| Q30 | Specializations | UI assigns **one** specialization per employee/client (`FirstOrDefault`) although the model is many-to-many (the assign page allows many). | Keep many-to-many; UI multi-select. |
| Q31 | Employees | Exactly one default employee (`SetDefaultEmployeeAsync` resets all others). Default employee is auto-assigned to new clients without employee, on registration approval and on case creation. | Keep. |
| Q32 | Employees | `AssignedAdministratorId` exists (seeded) and `GetAdministratorEmployeesAsync` exists, but no UI uses it. | Keep data; expose in employee detail (read/write). |
| Q33 | Specializations | `RoleSpecializations.razor` navigates to `/admin/role-specializations/{id}/assign` and the assign page goes back to `/admin/role-specializations` — both routes don't exist (page lives at `/system/...`). Assignment page reachable only by typing the URL. | Fix. |
| Q34 | Specializations | Role dropdown is built from roles currently assigned to at least one user. | Fix — fixed list Client/Employee. |
| Q35 | UI | Light/dark choice is stored in the server session cache (lost at logout) + client script. | Fix — persisted user preference (`users.theme`). |
| Q36 | Sessions | Active sessions page shows `IP = "N/A"` and `LastActivity = now`; relies on in-process key list of the cache service (broken with Redis/multiple instances). | Fix — real data from `refresh_sessions`. |
| Q37 | Logs | Logs page shows the last 100 `AppLogs`; "Clear all" button is disabled. | Keep read-only + filters; no bulk clear (retention policy instead). |
| Q38 | Export | `DataGrid` export (CSV, PDF) exports only the **current page** (10 rows). Enabled only on Admin Subscriptions and Membership detail. | Fix — server-side export of all filtered rows, on every table (+ Excel). |
| Q39 | Access | `ModuleAccessControl` and `Sidebar` evaluate only the **first** role claim. | Fix — permissions union of all roles. |
| Q40 | Access | Page enabled when no `PageConfiguration` exists (fail-open); module disabled when no `ModuleConfiguration` exists (fail-closed); SystemConfigurator always allowed. | Keep effective result via seeded permissions (import maps current rows). |
| Q41 | Dashboard | Employee KPIs "TotalCaf"/"TotalPatron" count assigned clients whose custom field `CAF`/`PATRONATO` is `true` (names hard-coded). | Keep via generic "dashboard counter" flag on boolean custom fields; seed CAF/PATRONATO for the migrated tenant. |
| Q42 | Dashboard | Employee dashboard shows pending registrations under the label "PendingRequests" (same label twice). | Fix label. |
| Q43 | Dashboard | Admin revenue chart filter offers week/month/all but code handles month/year/all (week ⇒ all time). Default filter is "month". | Fix — week/month/year/all all working. |
| Q44 | Login | Redirect priority after login: Administrator → SystemConfigurator → Employee → Client. | Keep (landing = dashboard of highest role). |
| Q45 | Seed | `DataSeeder` inserts demo users (`Amministratore/admin`, `system/system`, `Operatore1/2`, `cliente1-3` with `password`), memberships ISEE/730/F24, demo subscriptions, e-mail config, `User` custom fields CAF/PATRONATO. | Keep demo data only for Development; production seed = reference data only. |
| Q46 | Secrets | Plaintext secrets in versioned files: Postgres password (`appsettings.json`, `docker-compose.yml`), Gmail app password (`DataSeeder.cs`). `EmailConfiguration.Password` stored in clear. | Fix — never copy; rotate at decommission (P8-03). |
| Q47 | Imports | Imported users are inactive with password `"password"`; Membership/Subscription import sets raw properties (including ids/FKs) via reflection, silently swallowing conversion errors. | Fix — per-row validation with natural keys, errors reported per row, activation per D-06. |
| Q48 | Imports | `ImportJob`/`ImportJobService` is a stub (counts rows) not used by the UI; the real flow is `Import` (+`ImportedData` JSON). | Merge into one job model (data-model §3.8). |
| Q49 | Documents | Max upload size taken from `RabbitMQ:MaxMessageSizeMB` (60) even when the queue is off; Blazor reads the whole file in memory. | Fix — tenant setting `documents.maxUploadMb` (default 60), streaming upload. |
| Q50 | Documents | Reference year must be ≥ current year − 10 (upload and edit). | Keep. |
| Q51 | Documents | File names sanitized (spaces→`_`, quotes removed, invalid chars removed); duplicate-name check only against files selected in the same batch (the `ExistingDocuments` parameter is never passed). Custom name gets the original extension appended if missing; custom name disabled when >1 file. | Keep sanitization + extension rule; Fix duplicate check (same owner/case/folder). |
| Q52 | Users | Staff can edit `Username` freely with no uniqueness check. | Fix — unique, validated. |
| Q53 | Validation | Phone: maxlength 10 in staff forms, 8–20 chars `[\d\s+\-()]` in registration. | Fix — one rule (8–20, same regex). |
| Q54 | Validation | Fiscal code: staff create uses strict regex `^[A-Z]{6}\d{2}[A-Z]\d{2}[A-Z]\d{3}[A-Z]$` + uniqueness; registration only 16 alphanumerics; no checks on edit or approval. | Fix — same rule everywhere (regex incl. omocodia) + uniqueness on create/edit/approve/import. |
| Q55 | Employees | Admin "new employee" form has only FullName (no Surname), birth date required, username = e-mail. | Fix — first/last name. |
| Q56 | Sessions | A new login sends `ForceLogout` to every other connection of the same user (single active session). | **Decide (D-08)** — default keep as tenant setting (ON). |
| Q57 | Login | "Remember username" stored in browser `localStorage`. | Keep. |
| Q58 | Sessions | Session timeout 120 min (`SessionTimeout`), cookie 2 h. | Keep as tenant setting mapped to refresh-token sliding window. |
| Q59 | Registration | Birth date range hard-coded `1900-01-01..2008-12-31` ("at least 16 years"). | Fix — computed "≥ 16 years" rule (setting). |
| Q60 | Clients | Admin can toggle a client active only when it has an assigned employee (`UnableToEnableUser`); employee "Active" select in client detail changes `IsActive` directly. | Keep rule for enabling; status changes recorded in history. |
