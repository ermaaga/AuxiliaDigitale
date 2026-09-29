# ADR 0004 — Application logic in Managers with an operation runner

- Status: Accepted (2026-09-29)
- Related decisions: D-26, supersedes marketplace CQRS rule (`docs/decisions.md`)

## Context
The user wants business logic organized in DI-injected managers that are simple to read, debug and observe.

## Decision
Per area: `I<Area>Manager` for state-changing operations, `I<Area>QueryService` for reads (AsNoTracking, projections, server-side paging), `I<Module>Api` as the only entry point for other modules. Every Manager operation runs in `IOperationRunner.RunAsync(Operations.<Module>.<Name>, …)`, which provides tracing, log scope, transaction + outbox, metrics, an outcome log with an event code and exception mapping. Endpoints and Worker handlers stay thin. No MediatR, no handler-per-use-case.

## Consequences
Uniform observability with no repeated code; Managers must stay focused (split by area when they grow).

## Alternatives considered
CQRS command/query handlers with decorators (rejected by the user).
