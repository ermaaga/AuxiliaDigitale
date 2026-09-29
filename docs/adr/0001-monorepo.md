# ADR 0001 — Monorepo

- Status: Accepted (2026-09-29)
- Related decisions: D-01, marketplace #1 (`docs/decisions.md`)

## Context
The rewrite needs backend, API contract and UI to change together in one story, and a single place for plan, decisions and parity documentation.

## Decision
One private repository `ermaaga/AuxiliaDigitale` (the blueprint's `auxilia-next`) with `backend/` (.NET 10 solution), `frontend/` (pnpm workspace), `deploy/`, `docs/`, `.github/`. One story = one branch = one PR; `main` protected, squash merge.

## Consequences
Atomic changes across API, contract and UI; one CI. Build times grow with the repo — CI uses path filters.

## Alternatives considered
Separate backend/frontend repositories (rejected: contract drift, double PRs).
