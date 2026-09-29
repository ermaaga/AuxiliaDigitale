# AuxiliaDigitale (Auxilia Next)

Multi-tenant platform for clients, cases, appointments and marketing campaigns: .NET 10 REST API (`backend/`) + Next.js tenant app and platform console with BFF (`frontend/`). Rewrite of the legacy Blazor app `../Auxilia`; this repo is the monorepo the blueprint calls `auxilia-next`.

- Plan, current status and next task: `docs/PLAN.md` — read it first, update it at the end of every session.
- Decisions (override the skills marketplace docs when they conflict): `docs/decisions.md`.
- Architecture: `docs/architecture/ARCHITECTURE.md`.
- Parity contract (no legacy feature may be lost): `docs/parity/` (F01–F35) and `docs/parity/legacy-quirks.md`; new features: `docs/requirements/` (N01–N03).
- Before every task load the `auxilia-dev` skills: always `auxilia-architecture` + `auxilia-dependency-policy`; for a story `auxilia-story`; plus those listed in the task.
- Backend (.NET SDK pinned in `backend/global.json`; on this Mac the arm64 SDK is in `~/.dotnet` → `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH`):
  - build: `dotnet build backend/Auxilia.slnx -c Release` (warnings are errors)
  - test: `dotnet test --solution backend/Auxilia.slnx -c Release --ignore-exit-code 8` (exit 8 = test project without tests; drop the flag once every project has tests)
  - audit: `dotnet list backend/Auxilia.slnx package --vulnerable --include-transitive`
- Package versions only in `backend/Directory.Packages.props` (central package management); allowlist in the `auxilia-dependency-policy` skill.
- Frontend (`frontend/`, pnpm workspace; Node 24 LTS per `.nvmrc` — on this Mac `export PATH=/opt/homebrew/opt/node@24/bin:$PATH`):
  - install: `pnpm install` · dev: `pnpm dev` · build: `pnpm build`
  - checks: `pnpm lint && pnpm typecheck && pnpm format:check && pnpm audit --audit-level moderate`
  - Next.js 16: read `frontend/apps/web/AGENTS.md` and the bundled docs in `node_modules/next/dist/docs/` before writing Next code.
  - shadcn components live in `frontend/packages/ui` (see its README: make imports relative after `shadcn add`).
