# ADR 0007 — No scheduled jobs; queue-only Worker

- Status: Accepted (2026-09-29)
- Related decisions: D-15 (`docs/decisions.md`)

## Context
The user does not want background services running scheduled work for now, but periodic logic must not be lost (e.g. legacy case expiry).

## Decision
The Worker only consumes RabbitMQ queues (Rebus, outbox, idempotent consumers, retries, error queue). Periodic logic is declared as `IRecurringJob` by module descriptors and run manually by System (console or `auxctl jobs run`) with a distributed lock and a record in `ops.job_runs`. A future scheduler can reuse the registry without module changes.

## Consequences
No surprises from background timers; someone must remember to run jobs — the console shows the last run of each job.

## Alternatives considered
Timer-based hosted services (rejected for now).
