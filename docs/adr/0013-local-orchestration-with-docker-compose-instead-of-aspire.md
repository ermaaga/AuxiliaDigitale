# ADR 0013 — Local orchestration with Docker Compose instead of Aspire

- Status: Accepted (2026-09-29, approved by the user)
- Related: D-32, ADR 0011, `auxilia-dependency-policy` skill, parity F32, task P1-03

## Context
The architecture planned an Aspire `Auxilia.AppHost` to run Postgres, Valkey, RabbitMQ, Api, Worker and Web locally (as the legacy app does).
A trial restore of `Aspire.AppHost.Sdk` + `Aspire.Hosting.PostgreSQL/Valkey/RabbitMQ/JavaScript` 13.5.4 shows that **every** `Aspire.Hosting*` package depends on `JsonPatch.Net` → `JsonPointer.Net` → `Json.More.Net` (json-everything). Their NuGet binaries ship under the **Open Source Maintenance Fee EULA** (`OSMFEULA.txt`): a monthly fee for users with annual revenue ≥ US$10,000. The dependency policy forbids licenses with revenue thresholds, including for development tools.

## Decision
1. **No Aspire.** Local infrastructure runs with **Docker Compose** (`deploy/compose.dev.yml`: Postgres, Valkey, RabbitMQ); Api, Worker and Web run with `dotnet run` / `pnpm dev`. Container images and a full compose come with H-03.
2. `Auxilia.ServiceDefaults` keeps the role Aspire gives it, built only on allowlisted packages: Serilog (console + per-tenant daily files, ADR 0006), OpenTelemetry (traces/metrics, OTLP exporter only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set), health endpoints `/health/live` and `/health/ready`, standard HTTP resilience.
3. **Blocklist**: `Aspire.Hosting*`, `Aspire.AppHost.Sdk`, `JsonPatch.Net`, `JsonPointer.Net`, `Json.More.Net` (and any other package under the OSMF EULA). Enforced by `BlocklistedPackageTests`.

## Consequences
- One command still starts the infrastructure (`docker compose -f deploy/compose.dev.yml up -d`); the parity criterion of F32 is met by Compose instead of Aspire.
- No Aspire dashboard: logs are read from the console and `logs/`; traces/metrics need an OTLP backend (decided with D-12).
- If json-everything or Aspire change licensing, this ADR can be revisited with a new trial restore.
