# F29 — Optimistic concurrency

Status: [ ] not started · Tasks: P1-04, P1-08

## Legacy behaviour
- `BaseEntity.RowVersion` configured as row version on 3 entities (`AuxiliaDbContext`); `documentations/CONCURRENCY_EXAMPLE.md`. No UI handling of conflicts.

## Acceptance criteria
- [ ] `xmin` concurrency token on every aggregate root.
- [ ] `ETag` on single-resource GET; `If-Match` required on PUT/PATCH/DELETE → `412` on mismatch, `428` when missing.
- [ ] Frontend shows a "modified by someone else — reload" message with the `AUX-` code.
