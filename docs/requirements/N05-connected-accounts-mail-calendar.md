# N05 — Connected accounts: mailbox (e-mail and PEC) and external calendars

Status: [~] in progress (C-01…C-07 done) · Decided with the user on 2026-10-15 · Tasks: C-01…C-08 · Issues: #100 (mailbox), #101 (calendar), #106 (on-demand sync) · Related: F13, F14, F16, N03, D-13 (perimeter extended by D-33), D-15 (unchanged), D-19, D-21

Design: ADR 0021 (module `mailbox`, OAuth through the BFF with PKCE, adapters over HTTP without new packages, schema
`mailbox`, event codes 30xxx); OAuth app registration: `docs/runbooks/connected-accounts-oauth.md`.

## Goal
An employee connects one or more of their own mailboxes and calendars to Auxilia and works with them without leaving
the application:
- **Mail** (#100): sees the messages of every connected mailbox (ordinary e-mail and **PEC**), files a message into a
  client's case, downloads the original message to the PC.
- **Calendar** (#101): sees the events of the connected Google / Microsoft calendars inside the Auxilia calendar, each
  account with **its own colour**.

## Connected accounts (shared base)
- **Where**: own profile, section "Connected accounts" (`/profile?tab=accounts`), only for the user's own accounts.
  Three buttons:
  - **Sign in with Google** (OAuth 2.0 authorization code + PKCE): Gmail and Google Calendar;
  - **Sign in with Microsoft** (OAuth 2.0, Microsoft Entra, personal and work accounts): mail and calendar through
    Microsoft Graph;
  - **Connect PEC / IMAP**: address, user name, password; the server is filled in from the domain for known providers
    (Aruba, Legalmail, Poste, Gmail…), otherwise the user enters host and port. Mail only, no calendar.
- **Scopes asked when needed** (incremental consent): mail and calendar are separate capabilities of the account; a
  user who only wants the calendar never grants mail access. Read-only scopes in v1.
- **Account data**: provider, address, capabilities granted, **colour** (chosen from the theme palette, editable),
  status (`Active`, `NeedsReconnect`, `Disconnected`), last sync per capability.
- **Secrets**: OAuth refresh tokens and IMAP passwords protected per tenant (Data Protection, same pattern as the
  messaging account secrets); never returned by the API, never logged. A refresh token rotated by the provider is saved
  atomically with its use.
- **Connection test** before saving an IMAP account; the OAuth callback validates `state` and PKCE through the BFF (the
  browser never sees tokens).
- **Disconnect**: revokes the grant at the provider when it offers an endpoint, deletes the secrets and **every copied
  item** (message headers, events, sync runs); messages already filed into a case stay there as documents.
- **Visibility**: an account and its data are visible only to their owner. Administrators do not read other users'
  mail (no "shared mailbox" in v1). The System never sees any of it (D-21).
- **Module**: a new optional module, code `mailbox` (ADR 0021), enabled per tenant and
  role from the console like the others; permissions `mailbox.accounts.connect`, `mailbox.mail.view`,
  `mailbox.mail.file` (default roles Administrator, Employee). Clients have no access in v1.

## Synchronisation on demand (#106)
No scheduled job: **D-15 is unchanged**. A scheduled sync is not planned; the code is ready for it (`SyncTrigger.Scheduled`).
- **Trigger**: opening the Mail or Calendar page, or the "Sync now" button, calls
  `POST /api/v1/me/connected-accounts/sync` (`scope` = `mail` | `calendar`, `manual` for "Sync now") → `202` with the run of each account (state: `GET …/sync?scope=`). If a sync of
  that account and scope is running or ended less than 2 minutes ago, nothing new is queued (the existing one is
  returned). Rate limited.
- **Work**: a message on the module's queue; the Worker (scale to zero on the queue) syncs each account of the user.
- **UI**: the page shows the stored data at once with a "Synchronising…" bar; the realtime event
  `AccountSyncCompleted` refreshes the queries and removes the bar; a short toast "Sync completed".
- **Notification** (`INotificationSender`, kind `mailbox.received` / `mailbox.reconnect`, EN + IT): in the bell **only**
  when new messages arrived or an account needs to be reconnected; never for an empty sync.
- **Ready for a batch**: the code takes *what* to sync and *why* (`SyncTrigger` = `PageOpen` | `Manual` |
  `Scheduled`); a future `IRecurringJob` calls the same requester with `Scheduled` without touching the sync logic or
  the adapters.

### Retrieval in chunks
- IMAP (MailKit): UIDs greater than the last seen, blocks of 100, headers only (envelope, flags, body structure, size,
  internal date), **never the bodies**. Gmail API: `history.list` with page tokens, details with `format=metadata`.
  Microsoft Graph: delta queries with `$select` and a bounded page size.
- **One chunk = one transaction**: the items of the chunk and the sync cursor (UID + UIDVALIDITY, `historyId`,
  `deltaLink`) are saved together; an interrupted sync resumes from the last saved chunk.
- **Idempotent**: unique key (account, provider item id) and upsert; a redelivered message creates no duplicates.
- **First sync bounded**: last 90 days or last 2000 messages, newest first; calendar window −30 / +180 days.
- **Time budget** per run (≈ 2 minutes or N chunks): the rest is queued as a continuation; the page shows the progress.
- **One sync per account and scope at a time** (unique running row or lock).

### Errors
| Kind | Examples | Behaviour |
|---|---|---|
| Credentials | IMAP password changed, OAuth grant revoked (`invalid_grant`) | no retry; account `NeedsReconnect`; notification; the other accounts go on |
| Transient | timeout, network, IMAP `BYE`, HTTP 429/503 | retries with growing delay (Rebus second-level retries), honouring `Retry-After`; then `Failed` with its error code |
| Cursor no longer valid | UIDVALIDITY changed, Gmail `historyId` expired (404), Graph delta expired (410) | bounded full resync, not an error |
| Malformed item | broken headers, unknown charset, absurd sizes | the item is skipped and counted; the chunk goes on |
| Unexpected | unclassified exception | `Failed` with error code and trace id; cursor at the last saved chunk |

Every sync is recorded in `mailbox.sync_runs` (account, scope, trigger, user, start/end, outcome, error code, counts:
new, updated, skipped); the user sees "last updated at 10:42" and the last error per account.

## Mail (#100)
- **Page** `/mail`: accounts and folders on the left (Inbox, Sent, others), message list (sender, subject, date,
  unread, attachment icon) with paging, search and filters on the stored headers; the reading pane loads the body
  **live from the provider** when a message is opened.
- **Reading**: plain text shown as text; HTML only inside a sandboxed `iframe` without scripts, remote images blocked
  until the user allows them for that message; attachments listed with name and size, downloadable one by one.
- **Download**: "Download .eml" streams the original message (RFC 822) through the BFF. For PEC the original keeps
  `postacert.eml`, `daticert.xml` and the `smime.p7s` signature, so its legal value.
- **File into a case**: "File into case" opens a dialog with a client search (proposed from the sender's address when
  it matches a client's e-mail), the client's cases visible to the user (F10 rules) and the case folder; the original
  `.eml` is stored as a case document through `Documents.Public.IFileStore` (streamed, size limit of the documents);
  optionally each attachment as a separate document. The file type list of the documents gains `.eml`
  (`message/rfc822`, checked on the MIME headers since it has no magic bytes). The document records the source
  (account, provider message id) so the same message filed twice is detected and the user warned.
- **What is stored**: headers only (sender, recipients, subject, date, flags, size, attachment names), truncated to
  maximum lengths; bodies and attachments stay at the provider, except what is filed into a case.

## Calendar (#101)
- The appointments calendar (`/appointments?view=calendar`, FullCalendar) adds one event source per connected account
  with the calendar capability, in the account's colour; a legend lets the user hide or show each account.
- External events are **read-only** in v1 (title, time, all-day, location; opening one shows its details and a link to
  the provider); they never become Auxilia appointments and are never shown to other users.
- Events are stored for the sync window and read for the visible range; times follow the tenant time zone like the
  appointments (`TenantTime`).

## Security and privacy
- TLS always (IMAP `SslOnConnect` or `StartTls`), certificate validation never disabled.
- User-entered IMAP hosts: private, loopback, link-local and cloud metadata addresses refused (SSRF), standard IMAP
  ports only unless allowed by a setting.
- Timeouts and cancellation on every connection and call; streams instead of whole messages in memory.
- Logs with event codes and ids only: never addresses, subjects or contents.
- Mail contents are untrusted data (HTML, links, attachments; future AI tools must not treat them as instructions).
- Copy of personal data: headers and events of third parties are stored; retention = while the account is connected;
  record in the privacy documentation (DPIA to be evaluated with the user).
- Google: Gmail read scopes are **restricted** → app verification plus an annual third-party security assessment
  before the app leaves testing mode (≤ 100 test users); Calendar read scope is **sensitive** → standard verification.
  Microsoft: publisher verification; the customer's Entra administrator may have to consent for work accounts.

## Settings
`mailbox.sync.minIntervalSeconds` (120), `mailbox.sync.initialDays` (90), `mailbox.sync.initialMaxMessages` (2000),
`mailbox.calendar.pastDays` (30), `mailbox.calendar.futureDays` (180), `mailbox.imap.allowedPorts` (993, 143);
each with `settings.<key>.description` EN + IT. OAuth client ids and secrets are deployment configuration (key vault),
not tenant settings.

## Out of scope (v1)
Sending or replying to e-mails from Auxilia; writing to external calendars; shared/office mailboxes; scheduled sync and
new-mail alerts without opening the page; OAuth for providers other than Google and Microsoft; mobile app.

## Acceptance criteria
**Connected accounts**
- [ ] A user connects a Google account and a Microsoft account (OAuth through the BFF, tokens never in the browser) and a PEC/IMAP account (connection tested before saving); each has a colour that can be changed.
- [ ] Mail and calendar access are granted separately; a calendar-only account never asks for mail scopes.
- [ ] Disconnect revokes the grant when possible and deletes secrets and copied data; documents filed into cases stay.
- [ ] An account is visible only to its owner (tests between users and between tenants); the System never sees it.
- [ ] IMAP hosts on private/loopback/metadata addresses are refused; TLS is required.

**Sync**
- [x] Opening Mail or Calendar starts a sync (202); a second request within 2 minutes queues nothing.
- [x] The page shows stored data at once, the progress bar during the sync and refreshes on `AccountSyncCompleted`.
- [x] Chunked retrieval with one transaction per chunk: a sync stopped halfway resumes from the last chunk; a redelivered message creates no duplicates.
- [x] First sync bounded (days / messages); longer work continues in a queued continuation.
- [ ] Errors handled as in the table: `NeedsReconnect` with notification, retries honouring `Retry-After`, bounded resync on an invalid cursor, malformed items skipped and counted.
- [x] Bell notification only for new messages or an account to reconnect (EN + IT); `mailbox.sync_runs` records trigger and counts; `SyncTrigger.Scheduled` exists and is unused.

**Mail**
- [x] Message list per account and folder with paging and search on stored headers; body loaded live; HTML sandboxed, remote images blocked by default (allowing them per message is not in v1).
- [x] "Download .eml" returns the original message (a PEC keeps receipt and signature).
- [x] "File into case" stores the `.eml` (and optionally the attachments) in the chosen case and folder, respecting F10; a message already filed is detected.

**Calendar**
- [x] The calendar shows the events of each connected account in its colour, read-only, with a legend to hide/show accounts.

**Quality**
- [ ] EN + IT texts; event codes in the module range; Domain/Application tests (chunk algorithm with a fake provider), IMAP integration with a test server in Testcontainers, Gmail/Graph adapters with recorded responses, HTTP and E2E tests; ASVS checklist updated.
