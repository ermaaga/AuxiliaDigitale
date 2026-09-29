# ADR 0014 — Code coverage with coverlet.MTP

- Status: Accepted (2026-09-29, approved by the user)
- Related: ADR 0011, `auxilia-dependency-policy` and `auxilia-testing` skills, task P1-05

## Context
The tests run on Microsoft.Testing.Platform (`backend/global.json`), where `coverlet.collector` (a VSTest data collector, in the allowlist) does not work. A coverage gate of ≥ 80% lines on Domain and Application is required (skill `auxilia-testing`).
Candidates:
- `Microsoft.Testing.Extensions.CodeCoverage` 18.11.2: proprietary "Microsoft .NET Library" license terms (not OSI) → not allowed by the policy.
- `coverlet.MTP` 10.1.0: the Microsoft.Testing.Platform extension of the same coverlet project, MIT; dependencies are only `Microsoft.Extensions.Configuration*`, `Microsoft.Extensions.DependencyInjection` and `Microsoft.Testing.Platform` (MIT). It supports `--coverlet-threshold`.

## Decision
1. `coverlet.MTP` **replaces** `coverlet.collector` in every test project (`backend/tests/Directory.Build.props`) and is added to the allowlist.
2. CI gate after the test step: `--coverlet --coverlet-include "[Auxilia.SharedKernel]*" "[Auxilia.Domain]*"` on `Auxilia.Domain.Tests` and `"[Auxilia.Application]*"` on `Auxilia.Application.Tests`, `--coverlet-threshold 80 --coverlet-threshold-type line`; the command fails (exit code 14) below the threshold.
3. Platform packages from dotnet/runtime used by the Application layer (`Microsoft.Extensions.DependencyInjection.Abstractions`, like `Microsoft.Extensions.Logging.Abstractions` in Diagnostics) are part of .NET itself (MIT) and do not need a separate ADR.

## Consequences
- Coverage is measured per test project on the assemblies it owns; integration tests do not count towards the gate.
- Reports (`TestResults/coverage*.json|xml`) are git-ignored; add `--coverlet-output-format cobertura` locally to inspect them.
