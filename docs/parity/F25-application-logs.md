# F25 — Application logs

Status: [ ] not started · Tasks: P1-02, 4.36 · Quirks: Q37

## Legacy behaviour
- Serilog: console + rolling file `logs/auxilia-.log` (30 days) + optional Azure Blob sink (`LogBlobStorage`).
- `ErrorLoggingMiddleware`: unhandled exceptions → `AppLog` row (level Error, message, stack trace, source = path, IP).
- `FileAuditLogger`: every caught exception appended to `logs/audit-errors.jsonl` with code `ERR-yyyyMMdd-XXXXXXXX`, source method, exception type, message, stack.
- `/admin/logs` (module `Logs`, page disabled by seed): last 100 `AppLog` rows (level badge, message, source, IP, date) + details modal (stack trace); "Clear all" disabled; refresh.

## Acceptance criteria
- [ ] Every error has an `AUX-NNNNN` code visible in UI and in logs; traceId shown for support.
- [ ] `audit.app_logs` stores Warning+ with code, trace, user, client; `/logs` with filters (code, level, date range, user, traceId, text), paging, details.
- [ ] `audit.entity_changes` records create/update/delete of business entities (who/when/what).
- [ ] Optional Azure Blob sink kept; retention policy instead of manual clear.
