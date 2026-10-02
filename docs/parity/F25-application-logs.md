# F25 — Application logs

Status: [~] in progress · Tasks: P1-02, P1-03, S-07, H-03 · Quirks: Q37

> **Decision D-17:** all logs Information+ go to **daily files per tenant** on the storage account (`logs/tenants/{slug}/yyyy/MM/dd.jsonl`, plus `logs/platform/…`); no `app_logs` table; retention is handled outside the system; the Log page (System console) reads the files. Criteria about a DB log table are replaced accordingly.

## Legacy behaviour
- Serilog: console + rolling file `logs/auxilia-.log` (30 days) + optional Azure Blob sink (`LogBlobStorage`).
- `ErrorLoggingMiddleware`: unhandled exceptions → `AppLog` row (level Error, message, stack trace, source = path, IP).
- `FileAuditLogger`: every caught exception appended to `logs/audit-errors.jsonl` with code `ERR-yyyyMMdd-XXXXXXXX`, source method, exception type, message, stack.
- `/admin/logs` (module `Logs`, page disabled by seed): last 100 `AppLog` rows (level badge, message, source, IP, date) + details modal (stack trace); "Clear all" disabled; refresh.

## Acceptance criteria
- [ ] Every error has an `AUX-NNNNN` code visible in UI and in logs; traceId shown for support.
- [ ] All events Information+ written as JSON lines to daily files per tenant (`logs/tenants/{slug}/yyyy/MM/dd.jsonl`) and to `logs/platform/…` for events without tenant, from Api, Worker and Runner; storage pluggable (`local-file` in development, `azure-blob` in production).
- [ ] Each line carries timestamp, level, event code, message, exception, tenant, user/platform user, client app, traceId, correlationId, host, version; sensitive data masked.
- [x] System console → tenant → Logs: date range + filters (level, code, traceId, user, text) reading the files; no DB copy. (S-07; plus temporary Debug level per tenant, D-28)
- [ ] `audit.entity_changes` records create/update/delete of business entities (who — including platform actors — when, what).
- [ ] No retention logic inside the system; retention via storage lifecycle policy (H-03).
