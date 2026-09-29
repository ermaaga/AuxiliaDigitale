# deploy

Container images (Api, Worker, Web, MigrationRunner), compose files and hosting configuration.
Populated in task H-03 once hosting is decided (D-12). Local infrastructure: `docker compose -f deploy/compose.dev.yml up -d` (Postgres, Valkey, RabbitMQ; ADR 0013 — no Aspire).
No secrets in this folder: configuration comes from environment variables or a secret store.
