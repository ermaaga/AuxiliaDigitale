# F32 — Docker / Aspire / configuration

Status: [x] done (verified in H-04, 2026-10-09) · Tasks: P0-02, P0-04, P1-03, H-03, R-03 · Quirks: Q46

## Legacy behaviour
- `Auxilia.AppHost` (Aspire) orchestrating Postgres, Redis/Valkey, RabbitMQ; `Auxilia.ServiceDefaults` (health checks, OTel).
- `Dockerfile`, `docker-compose.yml` (postgres, valkey, rabbitmq 4.2-management, app image `mikettone9977/auxilia:dev_local`), `ci/docker`, `cd/docker`, `run_ui_deploy.sh`, `sysadmin/postgres`.
- Feature flags in appsettings: `InitDatabase`, `UseLocalCache`/`CacheConnection`, `UseQueueForDocuments`/`RabbitMQ:*`, `SaveLog`, `AuditLog:FilePath`, `LogBlobStorage:*`, `EnableSubscriptionExpiryService`, `DocumentStorage/FtpStorage/AzureStorage`, `ReCaptcha:*`, `AppName`, `DefaultPassword`, `SessionTimeout`.
- DB connection retry (3×, 5 s).

## Acceptance criteria
- [x] One command starts Postgres, Valkey, RabbitMQ locally (`deploy/compose.dev.yml`; Aspire replaced by Docker Compose, D-32 / ADR 0013); Api, Worker, Web run with `dotnet run` / `pnpm dev`. *(H-04: `deploy/compose.dev.yml` (ADR 0013))*
- [x] Container images for Api, Worker, Web, MigrationRunner; compose for local/prod-like runs. *(H-04: `backend/Dockerfile` (api, worker, auxctl), `frontend/Dockerfile`, `deploy/compose.local.yml`, CI `local-stack.yml`)*
- [x] No secrets in versioned files (gitleaks in CI); all legacy flags mapped to either app config or tenant settings (mapping table in `docs/migration/mapping.md`). *(H-04: gitleaks in CI; legacy `appsettings` options mapped in `docs/migration/mapping.md` §5.5b)*
- [x] Health endpoints live/ready (ready reports tenants behind schema version). *(H-04: `/health/live`, `/health/ready` with `tenant-schemas` (Degraded while an active tenant is behind the newest migration, `TenantSchemaHealthCheckTests`))*
