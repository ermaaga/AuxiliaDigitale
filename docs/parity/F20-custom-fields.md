# F20 — Custom fields

Status: [~] in progress · Tasks: S-04, P3-07 · Quirks: Q41

> **Decision D-18:** custom field definitions are managed by **System** (platform console).

## Legacy behaviour
- `BaseEntity.CustomFields` (jsonb) on every `BaseEntity` subclass; definitions in `EntityConfiguration` (unique per `EntityName`, JSON list of `{PropertyName, PropertyType: text|number|date|boolean, GroupName?, BadgeColor?, VisibleOnGrid}`).
- `/system/entity-configurations`: create config choosing an entity not yet configured (all `BaseEntity` subclasses), add/remove fields (name*, type, show on grid). List (entity, created, updated; row click → detail); delete.
- `/system/entity-configurations/{id}`: table view (name, type, group, color chip, show on grid) and edit mode (name, type, group, color chip only when group set, show on grid, remove, add).
- `CustomFieldsEditor`: renders inputs by type (text/number/date/checkbox) for an entity; used on client create (admin + employee) and client detail edit.
- `DataGrid` appends one column per group (or per field without group) for fields `VisibleOnGrid`: booleans shown as colored badge with field name when true, other types show the value.
- Seed: `User` → `CAF` (boolean, group "Area", #72fa29) and `PATRONATO` (boolean, group "Area", #335cff), both visible on grid. Employee dashboard counts them (Q41).

## Acceptance criteria
- [x] Definitions per entity (at least Person/Client, Case, Appointment, Request, Document) with types Text, Number, Date, Bool, Select, MultiSelect, required flag, group, badge color, visible on grid, dashboard counter flag.
- [x] Server-side validation of `custom_fields` against definitions. *(S-04: `ICustomFieldValidator` ready; each business module calls it when it stores records, B-xx.)*
- [ ] Dynamic form renderer in create/edit forms; values shown in detail.
- [ ] Grid columns grouped with colored badges as legacy.
- [x] Legacy CAF/PATRONATO definitions and values migrated unchanged. *(E-05 `CustomFieldsStep` (same keys, dashboard counters) + values copied by E-02/E-03)*

## Status notes
- S-04: entities that carry custom fields are declared by the module descriptors (`CustomFieldEntities`: `client`, `case`, `appointment`, `request`, `document`). Definitions in `configuration.custom_field_definitions` (key unique per entity in any case, citext; type Text/Number/Date/Boolean/Select/MultiSelect; options for the selects; required; group; badge colour only with a group; visible on grid; dashboard counter only for booleans; order); entity, key and type cannot change after creation (the stored values depend on them). Console page `/platform/tenants/{slug}/custom-fields` (technical endpoints `/custom-fields`, tenant-scoped platform token). `Configuration.Public.ICustomFieldValidator` validates and normalises the values (`AUX-20018`, one error per `customFields.<key>`): unknown keys, types, options, required, dates `yyyy-MM-dd`. Web: `CustomFieldsEditor` (inputs by type, API errors under each input), `CustomFieldCell` and `customFieldColumns` (one column per group, booleans as badges in their colour with a readable text colour); signed-in users read the definitions with `GET /me/custom-fields/{entity}`. Pending: forms and grids of the business entities (B-xx), CAF/PATRONATO migrated by the legacy import (Fase 7).
