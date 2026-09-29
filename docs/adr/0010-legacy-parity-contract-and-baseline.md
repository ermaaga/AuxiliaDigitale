# ADR 0010 — Legacy parity contract and baseline

- Status: Accepted (2026-09-29)
- Related decisions: D-09, D-14, D-24, D-30 (`docs/decisions.md`)

## Context
No legacy feature may be lost, but some legacy code is dead or out of scope, and some features live on unmerged branches.

## Decision
The functional baseline is `develop` @ `8fa6622` plus branches `Security_Update` (`e314e9a`) and `fix/zip-download-folder` (`abf49b0`); local tags `legacy-final-baseline`, `legacy-baseline-security-update`, `legacy-baseline-zip-fix` freeze them. The contract is `docs/parity/F01–F35` with Given/When/Then criteria; legacy bugs are classified in `legacy-quirks.md`. Explicit removals: workout plans (D-09), registration and approval pages (API only, D-14), self-service unsubscribe deferred (D-24).

## Consequences
Every removal is traceable to a decision; parity E2E and UAT check each F file.

## Alternatives considered
Porting from `master` (`b7e5b23`, older than develop — rejected).
