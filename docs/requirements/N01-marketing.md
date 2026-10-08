# N01 — Marketing campaigns (v1: e-mail)

Status: [x] done (self-service unsubscribe deferred, D-24); verified in H-04 · pages M-04 · Tasks: B-01, M-01, M-02, M-03, M-04, E-05 · Decisions: D-15, D-16, D-20 · D-23 (legacy clients consent = true), D-24 (unsubscribe deferred)

Not present in the legacy. Module `Marketing`, optional (plan/override per tenant and role), event codes 19000–19999.

## Scope v1
- Channel: **e-mail** only. WhatsApp later through the pluggable `IMessageChannel` (N03).
- Audiences: **dynamic segments** and **static lists**.
- Compliance: **marketing consent** required. Unsubscribe link/page **deferred** (D-24): consent revoked by staff.
- Out of scope v1: open/click tracking, scheduled or recurring sends, automations (birthday, expiring case), A/B tests.

## Acceptance criteria
**Consents and tags**
- [x] A person has a consent history per purpose (`Marketing`, `Privacy`) and channel (`Email`, later `WhatsApp`): granted/revoked, when, source (staff, import, API, `LegacyMigration`), version.
- [x] Staff can record/revoke consent from the Client 360° and via import; every change is audited. *(M-01: append-only `directory.consents` with who/when/source)*
- [x] Tags: create, assign/remove on clients (single and bulk from the clients table). *(M-01: bulk from the clients overview selection)*

**Segments and lists**
- [x] Dynamic segment = validated rule (AND/OR groups) over: client status, assigned employee, tags, specializations, services and case status, custom fields, age range, city/province, creation date; translated into a parameterised query (no raw SQL from users); live count preview.
- [x] Static list: add/remove clients manually, from a clients-table multi-selection, or by import.
- [x] Segments and lists respect tenant isolation and F10 visibility for employees. *(M-02: employees count and see the clients in their charge; city/province not available: `Person` has no address)*

**Templates**
- [x] E-mail templates with subject and body (Liquid placeholders: person first/last name, tenant name), language, preview with a sample client, **test send** to an arbitrary address.

**Campaigns**
- [x] Create campaign: name, segment or list, template, sender account resolved by rules (purpose `Marketing` × sender role, N03) with override if allowed.
- [x] States `Draft → Sending → Sent`, plus `Cancelled` and `Failed`; `scheduled_at` stored but unused (D-15).
- [x] "Send now" (confirmation with recipient count) enqueues the campaign; the Worker snapshots recipients, **excludes** people without valid e-mail marketing consent, without e-mail, or in suppressions, and sends in batches with per-provider rate limit.
- [x] Per-recipient status (`Pending`, `Sent`, `Failed`, `Excluded` + reason); campaign summary (recipients, sent, failed, excluded).
- [x] A campaign cannot be sent twice; retries never duplicate e-mails to the same recipient (idempotency per recipient).
- [x] Permissions: create/edit/send separated (e.g. only Administrator sends); module visibility per plan/role.

**Exclusions**
- [x] Staff can revoke marketing consent and add an address to `suppressions`; suppressed addresses are never contacted by marketing (transactional e-mails still allowed).
- [x] Legacy clients are migrated with e-mail marketing consent = true, source `LegacyMigration` (D-23). *(H-04: E-02 `UsersStep`, `UsersImportTests`)*
- [ ] *(Deferred, D-24)* self-service unsubscribe link/page.
