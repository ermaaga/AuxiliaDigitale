# N03 — Outbound messaging: channels, N accounts, rules by purpose and role

Status: [~] in progress · Tasks: P1-13, S-03, M-03 · Decisions: D-16, D-20

Legacy had a single SMTP configuration (`EmailConfiguration`, password in clear). Module `Messaging`, event codes 25000–25999.

## Status notes
- P1-13 (backend): accounts (secrets protected, one default per channel, activation, test send), rules with resolution and cache, outbound log, queued delivery with retries/error queue, Liquid templates EN+IT for the system templates (campaign templates with M-03), `IMessageChannel` adapters (`smtp`; WhatsApp modelled, no adapter). Pending: console pages and endpoints (S-03), System-only authorization (P2-06).
- S-03: technical endpoints `/messaging/*` with a tenant-scoped platform token (D-21, System only): accounts (list without secrets — `hasSecret` only —, create, update with the secret kept when empty, make default, activate/deactivate), `PUT /messaging/rules/{channel}` (rules replaced as a whole), `POST /messaging/accounts/{id}/test` and `GET /messaging/outbound-messages` (paged, newest first, filters channel/status/account/recipient). The test send now answers with its outcome (`sent`, `errorCode`) and records the message in the log whatever happens: a server that cannot be reached is `AUX-25022` instead of an unhandled error. Console page `/platform/tenants/{slug}/messaging`: SMTP accounts, test dialog, e-mail rules editor, outbound log. WhatsApp accounts are listed as "not available yet" (no adapter, D-20); campaign templates with M-03.

## Acceptance criteria
**Accounts**
- [x] Per tenant, N accounts per channel: `Email` (provider `smtp`: host, port, security None/StartTls/SslOnConnect, user, password, from address, from name) and `WhatsApp` (provider `http-gateway`: endpoint URL, sender number, secret) — WhatsApp stored in the model but **no adapter until needed**.
- [x] Secrets encrypted with Data Protection, never returned by the API, never logged.
- [x] Exactly one default account per channel; accounts can be deactivated; "send test" per account.

**Rules**
- [x] Rules map (channel, purpose `Transactional` | `Notification` | `Marketing`, sender role or any) → account, with priority.
- [x] Resolution: exact (channel, purpose, role) → (channel, purpose, any) → channel default; messages without a user (system commands) use role = none.
- [x] Managed only by System (console); cached per tenant and invalidated on change.

**Sending**
- [x] Every outbound message is recorded (`outbound_messages`: channel, account, purpose, recipient, template, related entity, status `Queued/Sent/Failed`, error code, timestamps) and delivered by the Worker through the queue with retries and error queue.
- [ ] Templates per code and language (Liquid), EN + IT mandatory for system templates: account activation, password reset, registration received, expiry reminder, request reply notification, campaign messages.
- [x] Adding a channel/provider = new adapter implementing `IMessageChannel`, no change in the calling modules.
