# AuxiliaDigitale (Auxilia Next)

Multi-tenant platform for clients, cases, appointments and marketing campaigns: .NET 10 REST API (`backend/`) + Next.js tenant app and platform console with BFF (`frontend/`). Rewrite of the legacy Blazor app `../Auxilia`; this repo is the monorepo the blueprint calls `auxilia-next`.

- Plan, current status and next task: `docs/PLAN.md` — read it first, update it at the end of every session.
- Decisions (override the skills marketplace docs when they conflict): `docs/decisions.md`.
- Architecture: `docs/architecture/ARCHITECTURE.md`.
- Parity contract (no legacy feature may be lost): `docs/parity/` (F01–F35) and `docs/parity/legacy-quirks.md`; new features: `docs/requirements/` (N01–N03).
- Before every task load the `auxilia-dev` skills: always `auxilia-architecture` + `auxilia-dependency-policy`; for a story `auxilia-story`; plus those listed in the task.
- Backend (.NET SDK pinned in `backend/global.json`; on this Mac the arm64 SDK is in `~/.dotnet` → `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH`):
  - build: `dotnet build backend/Auxilia.slnx -c Release` (warnings are errors)
  - test (from `backend/`, where `global.json` selects Microsoft.Testing.Platform): `dotnet test --solution Auxilia.slnx -c Release --ignore-exit-code 8` (exit 8 = test project without tests; drop the flag once every project has tests)
  - coverage gate (CI, ≥ 80% lines, coverlet.MTP — ADR 0014): `dotnet test --project tests/Auxilia.Application.Tests -c Release --coverlet --coverlet-include "[Auxilia.Application]*" --coverlet-threshold 80 --coverlet-threshold-type line` (same for `Auxilia.Domain.Tests` with `[Auxilia.SharedKernel]*` + `[Auxilia.Domain]*`)
  - restore is locked (`packages.lock.json`); after changing packages run `dotnet restore backend/Auxilia.slnx --force-evaluate` and commit the lock files
  - audit: `dotnet list backend/Auxilia.slnx package --vulnerable --include-transitive`; licenses: `cd backend && dotnet tool restore && dotnet nuget-license -i Auxilia.slnx -t -a ../.github/license-allowlist.json -mapping ../.github/nuget-license-mappings.json -override ../.github/nuget-license-overrides.json`
- Local infrastructure (ADR 0013, no Aspire): `docker compose -f deploy/compose.dev.yml up -d` (Postgres, Valkey, RabbitMQ); run Api/Worker with `dotnet run --project backend/src/Auxilia.Api` / `…Auxilia.Worker`; the Api needs the Catalog: `dotnet user-secrets set ConnectionStrings:Catalog "Host=localhost;Database=auxilia_catalog;Username=auxilia;Password=<dev password>" --project backend/src/Auxilia.Api`. Logs: console JSON + daily files in `logs/tenants/{slug}/…` and `logs/platform/…` at the repo root (setting `AuxiliaLogging`).
- API contract: `backend/openapi/v1.json` is checked by `OpenApiContractTests`; after changing endpoints run (from `backend/`) `AUXILIA_UPDATE_CONTRACTS=1 dotnet test --project tests/Auxilia.Api.IntegrationTests -c Release`, then `pnpm --filter @auxilia/api-client generate` in `frontend/` (CI fails if either is stale). Scalar UI: `/scalar` in Development.
- Event codes: `backend/src/Auxilia.Diagnostics` (`EventCodes`, `Log`, `Errors`); after adding one regenerate the registry: `dotnet run --project backend/src/Auxilia.MigrationRunner -c Release -- diagnostics registry --output docs/log-event-registry.md` (a test fails if it is out of sync)
- EF Core migrations (`dotnet tool restore` installs `dotnet-ef`), from `backend/`: `dotnet ef migrations add Catalog_<Description> --project src/Auxilia.Persistence.Catalog --startup-project src/Auxilia.MigrationRunner --context CatalogDbContext --output-dir Migrations`; `Auxilia.Persistence.Tests` need Docker (Testcontainers) and fail if the model has changes without a migration.
- Package versions only in `backend/Directory.Packages.props` (central package management); allowlist in the `auxilia-dependency-policy` skill.
- Frontend (`frontend/`, pnpm workspace; Node 24 LTS per `.nvmrc` — on this Mac `export PATH=/opt/homebrew/opt/node@24/bin:$PATH`):
  - install: `pnpm install` · dev: `pnpm dev` · build: `pnpm build`
  - checks: `pnpm lint && pnpm typecheck && pnpm format:check && pnpm audit --audit-level moderate && pnpm licenses:check`
  - Next.js 16: read `frontend/apps/web/AGENTS.md` and the bundled docs in `node_modules/next/dist/docs/` before writing Next code.
  - shadcn components live in `frontend/packages/ui` (see its README: make imports relative after `shadcn add`).
- CI (`.github/workflows/ci.yml`): backend, frontend, gitleaks CLI, osv-scanner CLI; license allowlist and per-package exceptions in `.github/` (ADR 0011).
