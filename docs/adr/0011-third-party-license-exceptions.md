# ADR 0011 — Third-party license exceptions and supply-chain tooling

- Status: Accepted (2026-09-29, approved by the user)
- Related: `auxilia-dependency-policy` skill, `.github/license-allowlist.json`, `.github/license-exceptions.json`

## Context
The first frontend install (Next.js 16) pulled licenses outside the policy allowlist, and the CI needs secret and vulnerability scanners compatible with the "open source and free only" rule.

## Decision
1. **Allowlist extended** with permissive OSI-approved licenses used by build/dev tooling: `BlueOak-1.0.0` (`minimatch`), `Python-2.0` (`argparse`), `CC0-1.0` (public domain, e.g. `language-subtag-registry`, `type-fest` dual MIT/CC0).
2. **Per-package exceptions** (only these packages, not the license in general):
   - `LGPL-3.0-or-later` → `@img/sharp-libvips-*`: libvips native binary used by `sharp`, a dependency of `next` for image optimization. Used unmodified and dynamically linked, which is the normal LGPL-compliant use.
   - `CC-BY-4.0` → `caniuse-lite`: browser-compatibility **data** used at build time by Browserslist/Next.
3. **Supply-chain tooling in CI**:
   - Secrets: **gitleaks CLI** (MIT) downloaded from the official release with SHA-256 verification. The `gitleaks/gitleaks-action` GitHub Action is **not** used: it is distributed under a proprietary EULA.
   - Vulnerabilities: NuGetAudit (restore fails on moderate+), `dotnet list package --vulnerable`, `pnpm audit --audit-level moderate`, **osv-scanner CLI** (Apache-2.0) with SHA-256 verification.
   - Licenses: `nuget-license` (local dotnet tool) for NuGet, `frontend/scripts/check-licenses.mjs` for pnpm; both read the same allowlist and exceptions.
   - NuGet **lock files** (`packages.lock.json`, restore `--locked-mode` in CI) so scanners see exact transitive versions and restores are reproducible.
   - GitHub Actions pinned by commit SHA; Dependabot updates them weekly.

## Consequences
- Any new license or new package under an excepted license fails CI until reviewed (new ADR + update of the JSON files).
- Tool versions are pinned; Dependabot does not update the CLI binaries — bump `GITLEAKS_*`/`OSV_*` in `ci.yml` manually.

## Alternatives considered
- Disabling Next.js image optimization to avoid `sharp` (rejected: worse images/performance, the LGPL use is compliant).
- `gitleaks-action` (rejected: proprietary EULA). Reusable OSV workflow (rejected: less transparent failure semantics than the CLI).
