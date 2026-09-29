# ADR 0006 — Logging: unique event codes and per-tenant daily files

- Status: Accepted (2026-09-29)
- Related decisions: D-17, D-28, marketplace log codes (`docs/decisions.md`)

## Context
Troubleshooting must start from the code the user sees; logs must be complete and kept outside the database, with retention managed on the storage account.

## Decision
Every significant event has a stable incremental code `AUX-NNNNN` in module ranges, declared once in `Auxilia.Diagnostics` (`EventCodes`, `[LoggerMessage]` `Log.*`, `Errors.*`, `Operations.*`), verified by tests and published in a generated registry. All events Information+ are written as JSON lines to daily files per tenant on the storage account (`logs/tenants/{slug}/yyyy/MM/dd.jsonl`, platform events in `logs/platform/…`), with tenant, user, client, operation, traceId and correlationId; sensitive data masked. No log tables; retention is external. The System console reads the files; the minimum level can be raised temporarily per tenant. `traceparent` travels in queue messages so one trace id finds all related lines.

## Consequences
Error code + trace id → all lines of a request across Api and Worker. Querying needs the file reader (no SQL).

## Alternatives considered
DB table for logs (rejected: growth and cleanup inside the system).
