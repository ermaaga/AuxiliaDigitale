# ADR 0016 — Legacy import: a checked read model, writes through persistence, ids in `ops.legacy_id_map`

- Status: Accepted (2026-10-06)
- Related: D-09, D-11, D-18, D-23, D-30, ADR 0004, ADR 0010, tasks E-01…E-06, `docs/migration/mapping.md`

## Context
Phase 7 moves the data of the current customer from the legacy PostgreSQL database (EF Core model `Auxilia.Model`,
PascalCase tables in `public`) into one tenant database. The legacy database exists in more than one shape: the develop
baseline, with or without the unmerged `Security_Update` migration (D-30), and the `ImportJobs` table is in the legacy
model but no legacy migration creates it (Q61). The import must be repeatable (dry run, delta with `--since` at the
cutover) and must not trigger side effects of the new system (notifications, e-mails, outbox, realtime pushes).

## Decision
1. **Read model**: `Auxilia.MigrationRunner/LegacyImport` has its own read-only `LegacyDbContext` (no tracking,
   `SaveChanges` throws, session opened with `default_transaction_read_only=on`) mapped to the legacy tables with their
   names. Before reading, the database schema (`information_schema`) is checked against the model: a missing baseline
   table or a mapped column → `AUX-28007`, nothing is read. Optional parts (Security_Update, ImportJobs) are a
   `LegacyVariant` with one EF model per variant.
2. **One catalog** (`LegacyTables`): every legacy table with its fate (migrated, configuration, excluded) and task. A test
   compares it with the legacy schema scripts (generated with `dotnet ef migrations script` from the legacy model at the
   baseline tags) and with the table of `docs/migration/mapping.md`.
3. **Writes through persistence, not Managers**: the import writes with `TenantDbContext` and the domain constructors, in
   batches with one transaction each. Domain rules still apply (a violating row goes to the report); Manager side effects
   do not happen. This is the only writer that bypasses the Managers (ADR 0004) and it runs only from `auxctl`.
4. **Ids**: every migrated legacy row gets a Guid v7 recorded in `ops.legacy_id_map` (entity = legacy table name); a row
   already mapped keeps its Guid, so runs are repeatable and every legacy foreign key is resolved through the map.
5. **Secrets**: the legacy connection string comes from `AUXILIA_LEGACY_CONNECTION`; legacy passwords stay BCrypt hashes
   (`LegacyBcrypt`), the SMTP password is re-encrypted, reports and logs carry only counts, ids and codes.

## Consequences
- `auxctl legacy inspect` can be run on the production dump before any import, telling the variant and the rows per table.
- A legacy version newer than the baseline shows its unknown tables in `inspect`; the mapping and the catalog must be
  updated before importing it.
- The tenant slug (D-11) is an argument of the commands, so the import can be built and tested before D-11 is decided.
