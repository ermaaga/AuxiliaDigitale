# F29 — Optimistic concurrency

Status: [x] done (verified in H-04, 2026-10-09) · Tasks: P1-04, P1-08 

## Legacy behaviour
- `BaseEntity.RowVersion` configured as row version on 3 entities (`AuxiliaDbContext`); `documentations/CONCURRENCY_EXAMPLE.md`. No UI handling of conflicts.

## Acceptance criteria
- [x] `xmin` concurrency token on every aggregate root. *(tenant and catalog conventions: `Version` row version on every entity; simultaneous saves → `409 AUX-10010`)*
- [x] `ETag` on single-resource GET; `If-Match` required on PUT/PATCH/DELETE → `412` on mismatch, `428` when missing. *(H-04c: representation ETags on the shared records with a detail read — 27 writes, scope in ADR 0018; test `ResourceVersioningTests`)*
- [x] Frontend shows a "modified by someone else — reload" message with the `AUX-` code. *(`errors.AUX-10011` with the code; the page reloads its data at once; E2E `tenant-services.spec.ts` with two tabs)*
