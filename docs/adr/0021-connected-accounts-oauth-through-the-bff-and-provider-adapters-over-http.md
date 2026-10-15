# ADR 0021 — Connected accounts: module `mailbox`, OAuth through the BFF, provider adapters over plain HTTP

- Status: Proposed (2026-10-15, C-01) · Related: N05, D-33, D-15, D-19, D-21, ADR 0004, ADR 0007, ADR 0008, ADR 0011, issues #100, #101, #106

## Context
N05 lets an employee connect their own Google, Microsoft and PEC/IMAP accounts, see the mail and the external calendar
events in Auxilia, download a message as `.eml` and file it into a case. Synchronisation runs on demand (page open or
"Sync now") on the Worker, in chunks (#106); no scheduled job (D-15 unchanged).

C-01 has to fix before any code: the module, the OAuth flow (tokens must never reach the browser), the libraries, the
data schema and the event-code range. D-19 (Google sign-in for mobile clients) is a different thing: it signs a user
**into Auxilia**; here an already signed-in user **grants Auxilia access** to an external mailbox. The two share no
tables and no endpoints.

## Decision

### 1. Module
- Code **`mailbox`**, kind **Optional** (enabled per tenant and role from the console like the others), Application
  folder `Application/Mailbox`, endpoints `Api/Endpoints/Mailbox`, Postgres schema **`mailbox`** in the tenant
  database, Rebus queue **`auxilia.mailbox`**, web features `features/connected-accounts`, `features/mail`.
- Permissions: `mailbox.accounts.connect`, `mailbox.mail.view`, `mailbox.mail.file` (default roles Administrator,
  Employee; Clients none in v1). Every endpoint is also `RequireTenantUser()`: platform tokens get 403 (D-21).
- Event codes: range **30000** (`EventCodes.Mailbox`, owner "Mailbox: connected accounts, mail, external calendars");
  security events (account connected, disconnected, OAuth state rejected, SSRF host refused) stay in **29xxx**.
- Visibility: an account and everything copied from it belong to **its owner only**; no Administrator override in v1.
  Queries filter by the caller's user id in SQL; an id of another user's account answers 404.

### 2. OAuth through the BFF (authorization code + PKCE, confidential client)
The API is the OAuth client (it holds the client secret and stores the refresh token); the BFF only relays; the
browser sees nothing but redirects.

```
Browser            BFF (Next.js)                         API                               Provider
  │ click "Sign in with Google"                          │                                    │
  ├─► GET /api/connected-accounts/connect/google?capabilities=mail,calendar                  │
  │                 ├─► POST /api/v1/me/connected-accounts/oauth/google/authorize ─►│        │
  │                 │      { capabilities, accountId? (reconnect) }                  │        │
  │                 │   ◄─ { authorizationUrl }   (row in mailbox.oauth_requests:    │        │
  │                 │       state, user, provider, capabilities,                     │        │
  │                 │       PKCE verifier protected, expires in 10 min)              │        │
  │ ◄─ 302 authorizationUrl ────────────────────────────────────────────────────────────────►│ consent
  │ ◄──────────────────────────────── 302 {web}/api/connected-accounts/callback/google?code&state
  ├─► GET callback (cookie __Host-aux_sid: SameSite=Lax is sent on a top-level redirect)       │
  │                 ├─► POST /api/v1/me/connected-accounts/oauth/google/callback { code, state }
  │                 │      API: state exists, not used, not expired, same user and tenant →  │
  │                 │      mark used → exchange code + verifier ─────────────────────────────►│
  │                 │      ◄─ refresh/access token + id_token (address) ──────────────────────│
  │                 │      upsert account, protect refresh token, security event 29xxx       │
  │ ◄─ 302 /{tenant}/profile?tab=accounts&connected=… (or &error=<code>)                     │
```

- **One redirect URI per provider and deployment**, independent from the tenant:
  `{web origin}/api/connected-accounts/callback/{google|microsoft}`. The tenant comes from the BFF session (one
  `__Host-aux_sid` per origin, the session carries the tenant), never from the URL.
- `state`: 256-bit random, stored server-side with the PKCE `code_verifier` (S256) — the verifier never leaves the
  server, so a leaked `code` + `state` is useless. Single use, 10 minutes, bound to tenant + user; a mismatch is a
  security event and the callback redirects with an error code, never a stack trace.
- **Incremental scopes**: mail and calendar are separate capabilities; the authorize call asks only for the requested
  one (`include_granted_scopes=true` on Google; Microsoft adds the scopes on the next consent). Read-only scopes:
  - Google: `openid email` + `https://www.googleapis.com/auth/gmail.readonly` (restricted scope) and/or
    `https://www.googleapis.com/auth/calendar.events.readonly` (sensitive); `access_type=offline`,
    `prompt=consent` on first connection to get a refresh token.
  - Microsoft (Entra, authority `common`: work and personal accounts): `openid email offline_access User.Read` +
    `Mail.Read` and/or `Calendars.Read` (delegated).
- **Tokens**: the refresh token is protected with Data Protection, purpose `Auxilia.Mailbox.AccountSecret.v1` + tenant
  slug (same pattern as `UserTwoFactorSecretProtector`); access tokens live only in memory for one sync run or one
  live read. Microsoft rotates the refresh token: the new one is saved in the same transaction as the run's first
  chunk, or immediately when no chunk follows. `invalid_grant` → account `NeedsReconnect`.
- **Disconnect**: Google tokens are revoked (`https://oauth2.googleapis.com/revoke`); Microsoft has no per-grant
  revocation endpoint for delegated tokens, so the secret is deleted and the UI links to the account's app permissions
  page. Then the copied data is deleted (§4).
- **Configuration** (deployment, not tenant settings — key vault / user-secrets): `ConnectedAccounts:CallbackBaseUrl`
  (public web origin), `ConnectedAccounts:Google:ClientId|ClientSecret`,
  `ConnectedAccounts:Microsoft:ClientId|ClientSecret`. A provider without configuration is simply not offered
  (`GET /api/v1/me/connected-accounts/providers`), so dev and CI run without real credentials.
- **PEC / IMAP**: no OAuth; address + user name + password over the BFF, connection tested (login + `SELECT INBOX`)
  before saving; known providers' hosts filled in from the domain (Aruba, Legalmail, Poste, Gmail, Outlook…); a
  user-entered host is resolved and refused when **any** resolved address is private, loopback, link-local, CGNAT or
  a cloud metadata address, and the connection uses the vetted address (no second resolution: DNS rebinding); ports
  from `mailbox.imap.allowedPorts`; TLS only (`SslOnConnect` or `StartTls`, certificate validation never disabled).

### 3. Libraries: no new package
- **IMAP and `.eml`**: `MailKit` / `MimeKit` (already in the allowlist and in the solution).
- **Gmail, Google Calendar, Microsoft Graph, OAuth token endpoints**: typed `HttpClient`s in
  `Infrastructure/Adapters/Mail|Calendar/{Gmail,Graph}` and `Infrastructure/Adapters/OAuth/{Google,Microsoft}` with
  `System.Text.Json` source generation and `Microsoft.Extensions.Http.Resilience` (already allowed; the retry honours
  `Retry-After` on 429/503). The calls used are few and stable (token, revoke, userinfo; Gmail `labels`,
  `history.list`, `messages.list/get` with `format=metadata|raw`; Calendar `events.list` with `syncToken`; Graph
  `mailFolders`, `messages/delta`, `messages/{id}/$value`, `calendarView/delta`).
- Rejected: `Google.Apis.Gmail.v1` / `Google.Apis.Calendar.v3` / `Google.Apis.Auth` (Apache-2.0, but they bring
  Newtonsoft.Json and their own token store and retry model), `Microsoft.Graph` (MIT, Kiota runtime and a very large
  generated surface for six calls), `Microsoft.Identity.Client` (MIT, its token cache would duplicate our per-tenant
  protected store). Adopting one later needs the dependency-policy procedure; the ports below do not change.

### 4. Ports and data
- Ports in `Application/Abstractions`: `OAuth/IOAuthProvider` (authorization URL, code exchange, refresh, revoke,
  account identity), `Mail/IMailProvider` (folders, changed headers in pages from a cursor, raw message stream, body
  and attachment for the reader), `Calendar/ICalendarProvider` (events of a window, changes from a cursor). Adapters are
  keyed by provider (`google`, `microsoft`, `imap`) as in ADR 0008.
- Sync engine (C-04, #106): `IAccountSyncRequester.RequestAsync(userId, scope, trigger)` and
  `IAccountSyncService.SyncAsync(accountId, scope, trigger)`, `SyncTrigger` = `PageOpen | Manual | Scheduled`
  (`Scheduled` exists and is unused); `SyncConnectedAccountCommand` on `auxilia.mailbox`, handled by a thin handler.
- Tables (schema `mailbox`, tenant DB):

| Table | Content | Keys and rules |
|---|---|---|
| `connected_accounts` | user, provider (`Google`/`Microsoft`/`Imap`), address, display name, colour, status (`Active`/`NeedsReconnect`/`Disconnected`), capabilities, granted scopes, IMAP host/port/security/user name, protected secret, last error code, timestamps | unique (user, provider, address); owner only |
| `oauth_requests` | state, user, provider, capabilities, account (reconnect), protected PKCE verifier, created/expires/used | single use, 10 min; expired rows deleted on the next authorize |
| `sync_cursors` | account, scope, folder key, cursor (UID+UIDVALIDITY, `historyId`, `syncToken`, `deltaLink`), first sync completed | PK (account, scope, folder key); written in the chunk's transaction |
| `sync_runs` | account, scope, trigger, requested by, status (`Queued`/`Running`/`Succeeded`/`Failed`), start/end, error code, trace id, counts new/updated/skipped, continuation of | partial unique index (account, scope) where status in (`Queued`, `Running`): one sync at a time |
| `mail_folders` | account, provider folder id, name, kind (`Inbox`/`Sent`/`Other`) | unique (account, provider folder id) |
| `mail_messages` | account, folder, provider message id, Internet Message-ID, thread id, from, to/cc (jsonb, bounded), subject (≤ 500), dates, read, size, attachment names (bounded) | unique (account, provider message id); upsert = idempotent chunks; no bodies |
| `calendar_events` | account, provider event id, title, location, start/end (instants), all day, status, provider link | unique (account, provider event id); sync window only |
| `filed_messages` | Internet Message-ID hash, account address hash, case, document, user, filed at | **no FK to the account**: survives a disconnect, detects a message filed twice |

  Disconnect deletes the account row and cascades to cursors, runs, folders, messages and events; `filed_messages`
  and the case documents stay.
- Filing into a case goes through `Documents.Public.IFileStore` and the Documents module's public API (F10 rules
  of the case); `.eml` (`message/rfc822`) joins the document type list, checked on the MIME headers.

## Consequences
- No new dependency, licence or advisory surface; the adapters must be covered by tests with recorded responses
  (paging, 404/410 cursors, 429 with `Retry-After`) since no SDK does it for us.
- Google: `gmail.readonly` is a **restricted** scope: until the app passes Google's verification and the annual
  third-party security assessment it stays in testing mode (≤ 100 test users, refresh tokens expire after 7 days).
  Calendar alone needs only the standard verification. Microsoft: publisher verification; a customer's Entra
  administrator may have to consent for work accounts.
- The web origin of each deployment must be registered as a redirect URI at both providers (runbook
  `docs/runbooks/connected-accounts-oauth.md`).
- A future scheduled sync (#105) or an MCP server can reuse the ports and the sync service without changing them; mail
  content stays untrusted data.

## Verification (C-02…C-08)
Unit tests of the OAuth state rules (single use, expiry, other user/tenant), of the SSRF host check and of the chunk
algorithm with fake providers; IMAP against a test server in Testcontainers; Gmail/Graph adapters with recorded
responses; HTTP tests for owner-only access between users and tenants and 403 for platform tokens; E2E with fake
providers; `SecurityPerimeterTests` and `docs/security/asvs-checklist.md` updated.
