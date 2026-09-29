# F32 — Docker / Aspire / configuration

Status: [ ] not started · Tasks: P0-02, P0-04, P1-03, P7-03, P8-03 · Quirks: Q46

## Legacy behaviour
- `Auxilia.AppHost` (Aspire) orchestrating Postgres, Redis/Valkey, RabbitMQ; `Auxilia.ServiceDefaults` (health checks, OTel).
- `Dockerfile`, `docker-compose.yml` (postgres, valkey, rabbitmq 4.2-management, app image `mikettone9977/auxilia:dev_local`), `ci/docker`, `cd/docker`, `run_ui_deploy.sh`, `sysadmin/postgres`.
- Feature flags in appsettings: `InitDatabase`, `UseLocalCache`/`CacheConnection`, `UseQueueForDocuments`/`RabbitMQ:*`, `SaveLog`, `AuditLog:FilePath`, `LogBlobStorage:*`, `EnableSubscriptionExpiryService`, `DocumentStorage/FtpStorage/AzureStorage`, `ReCaptcha:*`, `AppName`, `DefaultPassword`, `SessionTimeout`.
- DB connection retry (3×, 5 s).

## Acceptance criteria
- [ ] Aspire AppHost runs Postgres, Valkey, RabbitMQ, Api, Worker, Web locally with one command.
- [ ] Container images for Api, Worker, Web, MigrationRunner; compose for local/prod-like runs.
- [ ] No secrets in versioned files (gitleaks in CI); all legacy flags mapped to either app config or tenant settings (mapping table in `docs/migration/mapping.md`).
- [ ] Health endpoints live/ready (ready reports tenants behind schema version).
