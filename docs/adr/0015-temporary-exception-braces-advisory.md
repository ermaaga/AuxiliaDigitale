# ADR 0015 — Temporary exception for GHSA-vfj7-8cjw-p6xm (braces)

- Status: Accepted (2026-10-03, approved by the user) · Expires: 2026-11-02
- Related: ADR 0011, `auxilia-dependency-policy` skill

## Context
On 2026-10-03 the advisory [GHSA-vfj7-8cjw-p6xm](https://github.com/advisories/GHSA-vfj7-8cjw-p6xm) (High, CVSS 8.7:
stack-exhaustion denial of service through deeply nested patterns) was published for `braces` <= 3.0.3, with no patched
version. `pnpm audit --audit-level moderate` and OSV-Scanner fail on every branch, `main` included. The only path in the
lockfile is `eslint-config-next` → `@next/eslint-plugin-next` → `fast-glob` → `micromatch` → `braces@3.0.3`; the latest
`micromatch` still depends on it, so no override or upgrade removes it.

The dependency policy forbids suppressing an advisory without an ADR stating the reason, the non-exploitability
analysis and an expiry date.

## Non-exploitability analysis
- `braces` is a development dependency only (ESLint plugin of Next.js): it is not part of `next build` output, of the
  server runtime or of any browser bundle (`pnpm why braces` shows only the `eslint-config-next` devDependency path).
- It expands glob patterns coming from the repository's own lint configuration and file tree, never from user input or
  network data.
- The worst case is a crash of a local or CI lint run; no user, tenant or data is affected.

## Decision
1. Ignore GHSA-vfj7-8cjw-p6xm in `frontend/pnpm-workspace.yaml` (`auditConfig.ignoreGhsas`) and in
   `frontend/osv-scanner.toml` with `ignoreUntil = 2026-11-02`, after which OSV-Scanner fails again and forces a review.
2. Remove both entries as soon as a patched `braces`/`micromatch` reaches `eslint-config-next` (Dependabot), or at the
   latest on expiry, when this ADR is reviewed (extended with a new analysis, or the dependency replaced).
3. Only this advisory is ignored; every other moderate+ advisory keeps failing CI.

## Consequences
- CI is green again for unrelated work; the exception is visible in two files and in this ADR.
- `pnpm audit` has no expiry mechanism: the OSV-Scanner expiry is the reminder that removes both entries together.
