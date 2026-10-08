# F11 — Case expiry job

Status: [x] done (verified in H-04, 2026-10-09) · Tasks: P1-12, B-25 · Quirks: Q03, Q14, Q15, Q24 · Decisions D-10 (closed), D-15

> **Decision D-15:** no scheduled job. The expiry logic is the command `cases.expiry`, registered in the recurring-job registry and run **manually** by System (console or `auxctl jobs run`); each run logged in `ops.job_runs`. The e-mail question (D-10) is closed; the manual "send expiry reminder" action on a case remains.

## Legacy behaviour
- `SubscriptionExpiryBackgroundService`: registered only if appsettings `EnableSubscriptionExpiryService=true`; loop every **6 hours**; runs only when setting `AutoSubscriptionExpiry` is true.
- `expiringDays` = setting `SubscriptionExpiringDays` (default 7).
- For each `IsActive` subscription:
  - `EndDate <= now` → `IsActive = false`; if the user has no other active subscription (and not yet notified in this run) → `User.IsActive = false` + notification to the client "Sottoscrizione Scaduta — La tua sottoscrizione è scaduta. Il tuo account è stato disattivato." (type `Subscription`).
  - `now < EndDate <= now + expiringDays` → notification "Sottoscrizione in Scadenza — …scadrà tra N giorni." (dedup intended per 24 h but broken, Q14). One notification per user per run.
- Manual e-mail `SendExpiryNotificationAsync` ("Avviso Scadenza Abbonamento", text with membership name, "è scaduto"/"scadrà tra N giorni", date dd/MM/yyyy) — unreachable in UI (Q24).
- Because completion sets `EndDate = now` (Q03), the job effectively deactivates completed cases.

## Acceptance criteria
- [x] ~~Worker job every 6 h, fan-out per active tenant~~ (D-15): command `cases.expiry` (`CaseExpiryJob`) run by hand per tenant by System, lock per tenant and job (`IJobLock`), idempotent; gated by `cases.expiry.enabled` (legacy `AutoSubscriptionExpiry`), days in the tenant time zone.
- [x] Expired active cases → inactive; client status recomputed (Inactive when no other open case); the user account stays enabled (D-05, differs from legacy on purpose); in-app notification `case.expired` per case to the client's account.
- [x] Expiring within `cases.expiry.expiringDays` → `case.expiring` once a day per case (Q14 fixed, `cases.cases.expiry_notified_on`).
- [x] E-mail according to D-10/D-15: the notifications follow the client's e-mail preferences; no automatic expiry e-mail.
- [x] Case action "Send expiry reminder" (with confirmation) sends the template `case-expiry-reminder` in the client's language (`POST /api/v1/cases/{id}/expiry-reminder`; `AUX-14035` no end date, `AUX-14036` no e-mail).
- [x] Tests: Application (`CaseExpiryJobTests`: expired, expiring, dedup, no account, disabled; manager reminder) and HTTP (`CaseEndpointsTests`); tenant isolation comes from the per-tenant job scope.
