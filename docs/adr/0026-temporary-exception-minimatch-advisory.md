# ADR 0026 — Temporary exception for GHSA-g3hv-75wg-m249 (minimatch)

- Status: Accepted (2026-11-01, approved by the user) · Expires: 2026-12-02
- Related: ADR 0011, ADR 0015 (same kind of exception, expired), `auxilia-dependency-policy` skill

## Context
On 2026-10-30 the advisory [GHSA-g3hv-75wg-m249](https://github.com/advisories/GHSA-g3hv-75wg-m249) (Moderate,
CWE-405: denial of service through glob-pattern amplification in `firstPhasePreProcess()`) was published for
`minimatch` < 11.0.1. The only patched release is 11.0.1, a new major: there is no backport for the 3.x, 5.x and 10.x
lines. `pnpm audit --audit-level moderate` and OSV-Scanner failed on every branch, `main` included (the Dependabot PRs
of 2026-11-01 were all red because of it).

The lockfile has three copies, all reached only through development tooling:
- `minimatch@3.1.5` — `eslint` 9 (`@eslint/config-array`, `@eslint/eslintrc`) and its plugins (`eslint-plugin-import`,
  `eslint-plugin-jsx-a11y`, `eslint-plugin-react`);
- `minimatch@5.1.9` — `openapi-typescript` → `@redocly/openapi-core` (generation of `@auxilia/api-client`);
- `minimatch@10.2.6` — `@typescript-eslint/typescript-estree` and `lighthouse` → `@sentry/node` → `glob`.

An override to 11.0.1 is not safe: 3.x exports a function, 5.x and later a named `minimatch`, so the lint and the API
client generation would break at run time.

## Non-exploitability analysis
- Every path is a devDependency (`pnpm why -r minimatch --prod` is empty): `minimatch` is not part of the `next build`
  output, of the server runtime or of any browser bundle.
- The patterns it expands come from the repository's own configuration and file tree (ESLint config, OpenAPI
  `$ref`s of our contract, Lighthouse's own bundle), never from user input or network data.
- The worst case is a slow or killed local/CI lint or generation run; no user, tenant or data is affected.

## Decision
1. Ignore GHSA-g3hv-75wg-m249 in `frontend/pnpm-workspace.yaml` (`auditConfig.ignoreGhsas`) and in
   `frontend/osv-scanner.toml` with `ignoreUntil = 2026-12-02` (the day after the next monthly Dependabot run), after
   which OSV-Scanner fails again and forces a review.
2. Remove both entries as soon as the tools above depend on `minimatch` ≥ 11.0.1 (Dependabot), or on expiry, when this
   ADR is reviewed (extended with a new analysis, or the dependency replaced).
3. Only this advisory is ignored; every other moderate+ advisory keeps failing CI.
4. The expired exception of ADR 0015 (GHSA-vfj7-8cjw-p6xm, `braces`, no longer in the tree) is removed from both files.

## Consequences
- CI is green again for unrelated work; the exception is visible in two files and in this ADR.
- `pnpm audit` has no expiry mechanism: the OSV-Scanner expiry is the reminder that removes both entries together.
