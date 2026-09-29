# F11 — Case expiry job

Status: [ ] not started · Tasks: P5-01, P5-03 · Quirks: Q03, Q14, Q15, Q24 · Decision D-10

## Legacy behaviour
- `SubscriptionExpiryBackgroundService`: registered only if appsettings `EnableSubscriptionExpiryService=true`; loop every **6 hours**; runs only when setting `AutoSubscriptionExpiry` is true.
- `expiringDays` = setting `SubscriptionExpiringDays` (default 7).
- For each `IsActive` subscription:
  - `EndDate <= now` → `IsActive = false`; if the user has no other active subscription (and not yet notified in this run) → `User.IsActive = false` + notification to the client "Sottoscrizione Scaduta — La tua sottoscrizione è scaduta. Il tuo account è stato disattivato." (type `Subscription`).
  - `now < EndDate <= now + expiringDays` → notification "Sottoscrizione in Scadenza — …scadrà tra N giorni." (dedup intended per 24 h but broken, Q14). One notification per user per run.
- Manual e-mail `SendExpiryNotificationAsync` ("Avviso Scadenza Abbonamento", text with membership name, "è scaduto"/"scadrà tra N giorni", date dd/MM/yyyy) — unreachable in UI (Q24).
- Because completion sets `EndDate = now` (Q03), the job effectively deactivates completed cases.

## Acceptance criteria
- [ ] Worker job every 6 h, fan-out per active tenant, distributed lock, idempotent; gated by `AutoSubscriptionExpiry` per tenant.
- [ ] Expired active cases → inactive; client status → Inactive when no other active case; one "expired" notification per client.
- [ ] Expiring within N days → one "expiring" notification per client per day (Q14 fixed).
- [ ] E-mail sent according to D-10 (localized template).
- [ ] Case action "Send expiry reminder" (with confirmation) sends the e-mail.
- [ ] Worker integration test covering expired, expiring, dedup, multi-tenant isolation.
