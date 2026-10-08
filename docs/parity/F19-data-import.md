# F19 — Data import

Status: [x] done (verified in H-04, 2026-10-09) · Tasks: S-08 · Quirks: Q47, Q48

> **Decision D-18:** imports are operated by the platform **System** role from the console.

## Legacy behaviour
- **Import types** `/system/import-types` (SystemConfigurator): create with name (defaults to entity) and target entity: `Employee`, `Client`, `Membership`, `Subscription`; on selection shows importable fields (public writable properties minus exclusions and `[ImportIgnore]`) marking `[ImportRequired]` ones (User: Username, FullName, Surname, FiscalCode). List: name, entity, created by, created; actions: download Excel template (ClosedXML, header row, required columns red, others light grey), view fields modal, delete (confirm).
- **Import** `/system/import`: form name*, type*, Excel file (.xlsx/.xls, ≤10 MB) → `Import` row `Pending`; background task parses first worksheet (row 1 = headers, converts to property types), progress % updated every 10 rows, stores rows as JSON (`ImportedData`), status `Completed` or `Failed` (error message). List auto-refreshes every 2 s: name, type, file, status badge, progress bar, created; actions details, delete (only `Concluded`).
- **Details** `/system/import/{id}`: info (name, type, file, status, created, completed, error); when `Completed` shows preview table + "Save import" (creates entities: users inactive with password "password" + role Employee/Client; memberships; subscriptions — reflection, errors swallowed) → `Concluded`; "Cancel import" → discards data → `Concluded`; when `Concluded` → delete.

## Acceptance criteria
- [x] Import types (name defaults to the entity, unique) for Employee, Client, Service (legacy Membership), Case (legacy Subscription): `IImportTarget` per module with column keys, label keys and required flags; Excel template (header row of keys, required columns red, others light grey); a type with imports cannot be deleted.
- [x] Console page `/platform/tenants/{slug}/imports`: type → template → upload (`.xlsx` ≤ 10 MB, ≤ 5000 rows) → validation per row (required, dates, numbers, yes/no, domain rules, fiscal code, duplicates in the file, uniqueness in the tenant, references by natural keys: employee user name, client fiscal code, service/category/specialization name) → preview with errors per column → confirm or cancel. `.xls` (legacy) is not accepted: ClosedXML reads only `.xlsx`.
- [x] Validation and import in the Worker (queue `auxilia.imports`), statuses Pending/Validating/AwaitingConfirmation/Processing/Completed/Failed/Cancelled, progress saved every 50 rows; the console refreshes every 2 s while an import runs (it has no SignalR hub; no `ImportProgress` push).
- [x] People are created by the Employee/Client Managers (profiles, account, activation e-mail per D-06 — no "password" default, Q47); services and cases by their Managers; each row keeps the id of the record created (`import_job_rows.entity_id`); a failing row does not undo the others.
- [x] Delete only when finished (completed, failed, cancelled); cancel discards the rows.
