# F19 — Data import

Status: [ ] not started · Tasks: 4.31, 4.32 · Quirks: Q47, Q48

## Legacy behaviour
- **Import types** `/system/import-types` (SystemConfigurator): create with name (defaults to entity) and target entity: `Employee`, `Client`, `Membership`, `Subscription`; on selection shows importable fields (public writable properties minus exclusions and `[ImportIgnore]`) marking `[ImportRequired]` ones (User: Username, FullName, Surname, FiscalCode). List: name, entity, created by, created; actions: download Excel template (ClosedXML, header row, required columns red, others light grey), view fields modal, delete (confirm).
- **Import** `/system/import`: form name*, type*, Excel file (.xlsx/.xls, ≤10 MB) → `Import` row `Pending`; background task parses first worksheet (row 1 = headers, converts to property types), progress % updated every 10 rows, stores rows as JSON (`ImportedData`), status `Completed` or `Failed` (error message). List auto-refreshes every 2 s: name, type, file, status badge, progress bar, created; actions details, delete (only `Concluded`).
- **Details** `/system/import/{id}`: info (name, type, file, status, created, completed, error); when `Completed` shows preview table + "Save import" (creates entities: users inactive with password "password" + role Employee/Client; memberships; subscriptions — reflection, errors swallowed) → `Concluded`; "Cancel import" → discards data → `Concluded`; when `Concluded` → delete.

## Acceptance criteria
- [ ] Import types for Employee, Client, Service, Case with field metadata and required flags; Excel template download with required columns highlighted.
- [ ] Wizard: type → template → upload → server validation per row (required, formats, CF, uniqueness, FK by natural keys) → preview with per-row errors → confirm or cancel.
- [ ] Processing in Worker with real-time progress (`ImportProgress`); statuses Pending/Validating/AwaitingConfirmation/Processing/Completed/Failed/Cancelled.
- [ ] Imported people get client/employee profiles and activation per D-06; imported entities linked to the job rows.
- [ ] Delete job only when finished.
