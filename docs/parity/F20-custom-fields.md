# F20 — Custom fields

Status: [ ] not started · Tasks: S-04, P3-07 · Quirks: Q41

> **Decision D-18:** custom field definitions are managed by **System** (platform console).

## Legacy behaviour
- `BaseEntity.CustomFields` (jsonb) on every `BaseEntity` subclass; definitions in `EntityConfiguration` (unique per `EntityName`, JSON list of `{PropertyName, PropertyType: text|number|date|boolean, GroupName?, BadgeColor?, VisibleOnGrid}`).
- `/system/entity-configurations`: create config choosing an entity not yet configured (all `BaseEntity` subclasses), add/remove fields (name*, type, show on grid). List (entity, created, updated; row click → detail); delete.
- `/system/entity-configurations/{id}`: table view (name, type, group, color chip, show on grid) and edit mode (name, type, group, color chip only when group set, show on grid, remove, add).
- `CustomFieldsEditor`: renders inputs by type (text/number/date/checkbox) for an entity; used on client create (admin + employee) and client detail edit.
- `DataGrid` appends one column per group (or per field without group) for fields `VisibleOnGrid`: booleans shown as colored badge with field name when true, other types show the value.
- Seed: `User` → `CAF` (boolean, group "Area", #72fa29) and `PATRONATO` (boolean, group "Area", #335cff), both visible on grid. Employee dashboard counts them (Q41).

## Acceptance criteria
- [ ] Definitions per entity (at least Person/Client, Case, Appointment, Request, Document) with types Text, Number, Date, Bool, Select, MultiSelect, required flag, group, badge color, visible on grid, dashboard counter flag.
- [ ] Server-side validation of `custom_fields` against definitions.
- [ ] Dynamic form renderer in create/edit forms; values shown in detail.
- [ ] Grid columns grouped with colored badges as legacy.
- [ ] Legacy CAF/PATRONATO definitions and values migrated unchanged.
