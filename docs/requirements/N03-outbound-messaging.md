# N03 — Outbound messaging: channels, N accounts, rules by purpose and role

Status: [ ] not started · Tasks: P1-13, S-03, M-03 · Decisions: D-16, D-20

Legacy had a single SMTP configuration (`EmailConfiguration`, password in clear). Module `Messaging`, event codes 25000–25999.

## Acceptance criteria
**Accounts**
- [ ] Per tenant, N accounts per channel: `Email` (provider `smtp`: host, port, security None/StartTls/SslOnConnect, user, password, from address, from name) and `WhatsApp` (provider `http-gateway`: endpoint URL, sender number, secret) — WhatsApp stored in the model but **no adapter until needed**.
- [ ] Secrets encrypted with Data Protection, never returned by the API, never logged.
- [ ] Exactly one default account per channel; accounts can be deactivated; "send test" per account.

**Rules**
- [ ] Rules map (channel, purpose `Transactional` | `Notification` | `Marketing`, sender role or any) → account, with priority.
- [ ] Resolution: exact (channel, purpose, role) → (channel, purpose, any) → channel default; messages without a user (system commands) use role = none.
- [ ] Managed only by System (console); cached per tenant and invalidated on change.

**Sending**
- [ ] Every outbound message is recorded (`outbound_messages`: channel, account, purpose, recipient, template, related entity, status `Queued/Sent/Failed`, error code, timestamps) and delivered by the Worker through the queue with retries and error queue.
- [ ] Templates per code and language (Liquid), EN + IT mandatory for system templates: account activation, password reset, registration received, expiry reminder, request reply notification, campaign messages.
- [ ] Adding a channel/provider = new adapter implementing `IMessageChannel`, no change in the calling modules.
