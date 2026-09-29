# ADR 0012 — Error and exception handling

- Status: Accepted (2026-09-29)
- Related: ADR 0004 (Managers + `IOperationRunner`), ADR 0006 (event codes, log files), ADR 0007 (queue-only Worker); skills `auxilia-log-codes`, `auxilia-backend-feature`, `auxilia-api-contract`, `auxilia-frontend-feature`

## Context
The legacy app swallowed exceptions (`catch { }`), mixed technical messages with user text and gave no stable reference for support. The rewrite needs one predictable way to signal, log and show failures across Domain, Application, API, Worker and frontend, optimised for troubleshooting.

## Decision

### 1. Three categories
| Category | Examples | Mechanism |
|---|---|---|
| **Expected failure** (business rule, input, authorization, not found) | invalid fiscal code, case not found, transition not allowed, employee cannot see a private case | Returned as `Result.Failure(Error)` — never thrown. `Error` = `Code` (event code, shown as `AUX-NNNNN`), `ErrorType` (Validation, NotFound, Conflict, Forbidden, Unauthorized, Failure), technical English `Description`, `ValidationErrors` (field → translation keys). |
| **Known technical exception** | `DbUpdateConcurrencyException`, `If-Match` mismatch, idempotency key reused, DB timeout, client cancellation | Converted to its dedicated code by `IOperationRunner` / the global handler: 409 `AUX-10010`, 412 `AUX-10011`, 409 `AUX-10012`, timeout code, client cancellation not logged as an error. |
| **Unexpected exception** (bug) | unexpected null, invariant broken by a programming error | Not caught locally. Bubbles to the global `IExceptionHandler`: one Error log `AUX-10001` with exception and `TraceId`; generic 500 without internal details. |

### 2. Per layer
- **Domain**: rule violations return `Result`/`Error`; exceptions only for programming errors via BCL throw helpers (`ArgumentNullException.ThrowIfNull`, `ArgumentException.ThrowIfNullOrWhiteSpace`, …).
- **Application (Managers)**: validation → `ErrorType.Validation` with field errors; authorization → `Forbidden`/`NotFound` (not visible = not found); each operation runs in `IOperationRunner`, which rolls back the transaction and outbox on failure and writes **exactly one outcome log** (Information success, Warning expected failure, Error unexpected).
- **API**: a single mapping `Error` → ProblemDetails (RFC 9457): status from `ErrorType`, extensions `errorCode` (`AUX-NNNNN`), `traceId`, `errors` (validation). Never stack traces, exception messages or SQL in responses.
- **Worker**: transient failures retried (5 immediate + second-level with backoff); permanent failures (validation, not found) not retried; after the last attempt the message goes to the `error` queue with a coded log. Consumers are idempotent.
- **Frontend**: ProblemDetails mapped to a localized message chosen by `errorCode` (generic fallback), with copyable code + trace reference and "Retry"; field errors shown next to the fields; dedicated 403/404 pages; React error boundary for rendering errors.

### 3. Rules for all code
1. **Never swallow** an exception (`catch { }`, `catch (Exception) { return null; }` are forbidden).
2. **Never use exceptions for expected control flow**; use `Result`.
3. Catch only to **add meaning** (convert to a coded `Error`) or to **clean up**; otherwise let it propagate.
4. **One code = one meaning**, stable, never reused (`auxilia-log-codes`); every `Error` uses a registered code from `Errors.<Module>.*`.
5. `Error.Description` is technical English; user text comes from translation keys resolved by the client.
6. Log sensitive data masked (passwords, tokens, connection strings, fiscal codes, document contents).
7. The `TraceId` travels from the browser request through API, queue messages (`traceparent`) and Worker, so one reference finds every related log line.

## Consequences
- Troubleshooting path: code + reference shown to the user → daily tenant log file filtered by `TraceId` → full request history across Api and Worker.
- Managers and endpoints stay free of `try/catch` boilerplate; cross-cutting handling lives in `IOperationRunner` and the global handler.
- Implementation spread over tasks P1-01 (`Error`/`Result`, done), P1-02 (codes), P1-04 (global handler, ProblemDetails), P1-05 (`IOperationRunner`), P1-12 (retries, error queue), P3-04 (frontend).

## Alternatives considered
- Exceptions for business failures + exception filters (rejected: hidden control flow, costly, harder to test).
- Generic error codes per HTTP status only (rejected: no unique reference for support).
