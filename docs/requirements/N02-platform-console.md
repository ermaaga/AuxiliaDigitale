# N02 — Platform console, tenants, plans and modules (System role)

Status: [ ] not started · Tasks: P1-06, P1-09, P1-11, P2-06, P3-03, P3-08, S-01…S-08 · Decisions: D-15, D-17, D-18 · D-21 (no business data), D-22 (2FA), D-25 (archive only)

Replaces the legacy tenant role `SystemConfigurator` and adds multi-tenant administration.

## Status notes
- P2-06: System users in `catalog.platform_users` (password + mandatory TOTP, progressive lockout, security stamp), created with `auxctl platform users add` (one-use activation token, 72 h) and recovered with `reset`; activation in the console: `POST /platform/auth/enrollment` (TOTP secret + `otpauth://` URI, shown once) then `POST /platform/auth/activate` (password + current code). Sign-in `POST /platform/auth/token` (password + code, `PlatformConsole` client only; tenant sign-ins refuse that client) → console tokens (10 min access, rotating refresh, session 30 min idle / 12 h absolute in `catalog.platform_sessions`), logout revokes console and tenant-scoped tokens of the session. `GET /platform/me`, `GET /platform/tenants`, `POST /platform/tenants/{slug}/token` → 10-minute platform token with `scope=platform`, `role=System`, `tenant`, `act` (security event `AUX-29020`); technical endpoints use `RequirePlatformTenant()` (any tenant status), console ones `RequirePlatformUser()` (403 `AUX-12040` otherwise). D-21 enforced by tests: platform tokens get 404 on business and module endpoints and on `/me`, no permissions, empty navigation. Changes made with a platform token are audited as `platform:{id}`. Still to do: tenant technical endpoints (S-01…S-08).
- P3-08: console pages in the web app: `/platform/login` (e-mail, password, TOTP code; separate BFF session `__Host-aux_psid`), `/platform/activate` (activation token → setup key and `otpauth://` link shown once → password + first code), shell with tenant selector (type-ahead; the slug in `/platform/tenants/{slug}/…` is the selected tenant and switching keeps the sub-page), tenant list (search, status filter, sort, paging in the URL) and tenant overview (status, schema version, languages read through the tenant-scoped platform token). QR code of the enrolment still missing (needs a QR library outside the allowlist).

## Acceptance criteria
**System identity**
- [ ] System users live in the Catalog, log in to `/platform` (separate session), mandatory TOTP 2FA (D-22), lockout, security events `29xxx`.
- [ ] Selecting a tenant issues a short-lived platform token (`scope=platform`, `role=System`, `tenant`, `act`); tenant technical endpoints accept only it; every change is audited with the platform actor.
- [ ] D-21: System cannot read or change business data (clients, cases, documents, appointments, requests, campaigns) — enforced by API tests.

**Tenants**
- [ ] List with status, plan, schema version; create (slug rules, name, language, time zone, first Administrator e-mail → activation link) with asynchronous provisioning and progress; edit; suspend/reactivate; archive (no physical deletion, D-25).
- [ ] Same operations available through `auxctl`.

**Plans and modules**
- [ ] Module catalog generated from module descriptors (Core / Optional).
- [ ] Plans define which modules are included **for which roles**; default plan `standard` includes everything.
- [ ] Per tenant: assigned plan (validity dates) + overrides (enable/disable a module, per role).
- [ ] Effective visibility = Core, or (override ?? plan) includes the module for the role — and role permissions; hidden module → endpoints `404`, absent from `/me/navigation`; change takes effect without restart (cache invalidation).
- [ ] Only System can change plans and overrides.

**Technical configuration per tenant** (each in its own requirement file)
- [ ] Settings and branding (F23), sending accounts and rules (N03), grid layouts and custom fields (F20, F21), labels/translations (F24), role permissions and specializations (F12, F22), imports (F19), logs viewer (F25).

**Jobs (D-15)**
- [ ] Page listing the registered recurring jobs of the tenant with last run (who, when, result) and a "run now" action (e.g. `cases.expiry`); no automatic schedule.
