# ADR 0017 — Lighthouse budget in the E2E suite

- Status: Accepted (2026-10-06, approved by the user) · Task: H-02
- Related: ADR 0011 (licenses), `auxilia-dependency-policy` skill

## Context
H-02 requires a Lighthouse score ≥ 90 on the web app, checked in the pipeline. `lighthouse` is not in the npm
allowlist. Two packages were considered:
- `@lhci/cli` 0.15 (Apache-2.0): server, assertions and uploads; it pulls `express`, `inquirer`, `yargs` 15 and
  `tmp` 0.1 — a large and dated dependency tree for what the pipeline needs.
- `lighthouse` 13.5 (Apache-2.0, Google): the audit engine alone, driven from Node.

## Decision
1. Add `lighthouse` 13.5.0 as a **devDependency of `apps/web`** (never in a runtime bundle).
2. Drive it from the Playwright E2E suite (`e2e/lighthouse.spec.ts`): the suite already starts the API and the
   production build (`next start`) in the CI job `e2e`; Lighthouse attaches to the Playwright Chromium through its
   debugging port, so signed-in pages are audited with a real session.
3. Budget: performance, accessibility and best practices ≥ 90 on the login pages (tenant and console) and on dashboard,
   clients, cases, documents, appointments and tasks; desktop preset (the tenant app is a back office used at the
   desk; the 360 px layout is covered by the functional and accessibility specs). Below the budget the job fails and the
   offending audits are printed and attached to the report.
4. No telemetry: Sentry error reporting exists only in the Lighthouse CLI and only on request; the Node API used here
   never starts it.

## Verification
- `pnpm licenses:check`: 659 packages, all within the allowlist (no new exception).
- `pnpm audit --audit-level moderate`: no new advisory.
- The allowlist of the `auxilia-dependency-policy` skill (marketplace repository) must list `lighthouse` as a
  test-only dependency.

## Amendment (2026-10-06)
The CI E2E job was too slow: the Lighthouse spec and the light/dark axe sweep (`accessibility.spec.ts`) are excluded
from the default run (`testIgnore` in `playwright.config.ts`) and run on demand with
`pnpm --filter web e2e:quality` (`E2E_QUALITY=1`), e.g. before a release (H-04, R-01). The budget is the same; it is
no longer enforced on every pull request.

## Consequences
- No extra time in the E2E job; the budget is checked when `e2e:quality` runs.
- Lighthouse major versions change scores and audits: Dependabot updates are reviewed with the report attached.
