# AuxiliaDigitale (Auxilia Next)

Multi-tenant platform for clients, cases, appointments and marketing campaigns: .NET 10 REST API (`backend/`) + Next.js tenant app and platform console with BFF (`frontend/`). Rewrite of the legacy Blazor app `../Auxilia`; this repo is the monorepo the blueprint calls `auxilia-next`.

- Plan, current status and next task: `docs/PLAN.md` — read it first, update it at the end of every session.
- Decisions (override the skills marketplace docs when they conflict): `docs/decisions.md`.
- Architecture: `docs/architecture/ARCHITECTURE.md`.
- Parity contract (no legacy feature may be lost): `docs/parity/` (F01–F34) and `docs/parity/legacy-quirks.md`; new features: `docs/requirements/` (N01–N03).
- Before every task load the `auxilia-dev` skills: always `auxilia-architecture` + `auxilia-dependency-policy`; for a story `auxilia-story`; plus those listed in the task.
- Build/test commands: to be added when the skeleton exists (task P0-02).
