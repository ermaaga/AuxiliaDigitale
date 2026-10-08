# F30 — Incremental seeds / data migrations

Status: [x] done (verified in H-04, 2026-10-09) · Tasks: P1-08, P1-09 

## Legacy behaviour
- `DataSeeder.SeedAsync` for empty DB (languages, roles, admin/system/demo users, memberships, demo subscriptions, e-mail config, `User` custom fields, module configs from `environmentconfig.json`, page configs) — runs when `InitDatabase=true`.
- `SeedMigrator` + `ISeedScript` (`S_YYYYMMDD_NNN_Name`, key `YYYYMMDD_NNN`), discovered by reflection, ordered by key, recorded in `__SeedHistory`; on a fresh DB all scripts are marked applied. Six scripts exist (employee subscriptions module/page, specialization columns, folder-template/zip/file-not-found/folder-column translation keys).
- Rule (`rules.md` §4): every change to reference/config data needs a script; schema changes need an EF migration.

## Acceptance criteria
- [x] `IDataMigration` (`D_YYYYMMDD_NNN`), idempotent, recorded in `ops.data_migrations_history`, run by `auxctl migrate tenants`; fresh tenants mark covered ones as applied. *(H-04: `DataMigrationRunner`, `ops.data_migrations_history`, `auxctl migrate tenants`, `[IncludedInInitialSeed]`)*
- [x] Persistence tests run every data-migration twice. *(H-04: `DataMigrationRunnerTests.ApplyPending_EveryMigrationTwiceLeavesTheSameState`)*
- [x] Production seed contains only reference data; demo data only in Development (Q45). *(H-04: data-migrations seed only translations, templates, permissions and settings; demo data only from `deploy/local/bootstrap.sh` and `e2e/prepare.sh`)*
