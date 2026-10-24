# Architecture Decision Records

| ADR | Title | Status | Decisions |
|---|---|---|---|
| [0001](0001-monorepo.md) | Monorepo | Accepted | D-01, marketplace #1 |
| [0002](0002-multi-tenancy-with-catalog-db-and-database-per-tenant.md) | Multi-tenancy with Catalog DB and database per tenant | Accepted | D-02, marketplace #2, #6 |
| [0003](0003-platform-system-role-plans-and-module-visibility-per-role.md) | Platform System role, plans and module visibility per role | Accepted | D-18, D-21, D-22, D-25 |
| [0004](0004-application-logic-in-managers-with-an-operation-runner.md) | Application logic in Managers with an operation runner | Accepted | D-26, supersedes marketplace CQRS rule |
| [0005](0005-configuration-as-data-with-a-two-level-cache.md) | Configuration as data with a two-level cache | Accepted | D-16, D-18, D-28 |
| [0006](0006-logging-unique-event-codes-and-per-tenant-daily-files.md) | Logging: unique event codes and per-tenant daily files | Accepted | D-17, D-28, marketplace log codes |
| [0007](0007-no-scheduled-jobs-queue-only-worker.md) | No scheduled jobs; queue-only Worker | Accepted | D-15 |
| [0008](0008-pluggable-adapters-for-external-capabilities.md) | Pluggable adapters for external capabilities | Accepted | D-16, D-19, D-20, D-30 |
| [0009](0009-outbound-messaging-with-n-accounts-chosen-by-purpose-and-sender-role.md) | Outbound messaging with N accounts chosen by purpose and sender role | Accepted | D-16, D-31 |
| [0010](0010-legacy-parity-contract-and-baseline.md) | Legacy parity contract and baseline | Accepted | D-09, D-14, D-24, D-30 |
| [0011](0011-third-party-license-exceptions.md) | Third-party license exceptions and supply-chain tooling | Accepted | dependency policy |
| [0012](0012-error-and-exception-handling.md) | Error and exception handling | Accepted | ADR 0004, 0006, 0007 |
| [0013](0013-local-orchestration-with-docker-compose-instead-of-aspire.md) | Local orchestration with Docker Compose instead of Aspire | Accepted | D-32, ADR 0011 |
| [0014](0014-code-coverage-with-coverlet-mtp.md) | Code coverage with coverlet.MTP | Accepted | ADR 0011, P1-05 |
| [0015](0015-temporary-exception-braces-advisory.md) | Temporary exception for GHSA-vfj7-8cjw-p6xm (braces), expires 2026-11-02 | Accepted | ADR 0011 |
| [0016](0016-legacy-import-reads-a-checked-read-model-and-writes-through-persistence.md) | Legacy import: a checked read model, writes through persistence, ids in `ops.legacy_id_map` | Accepted | D-30, ADR 0004, E-01 |
| [0017](0017-lighthouse-budget-in-the-e2e-suite.md) | Lighthouse budget (≥ 90) in the E2E suite with `lighthouse` as a dev dependency | Accepted | H-02, ADR 0011 |
| [0018](0018-optimistic-concurrency-with-representation-etags.md) | Optimistic concurrency: representation ETags, `If-Match` required on shared records (412/428) | Accepted | H-04c, F29 |
| [0019](0019-qr-code-of-the-authenticator-enrolment.md) | QR code of the authenticator enrolment with `uqr` | Accepted | N02, D-22 |
| [0020](0020-document-preview-with-pdfjs.md) | Document preview in the drawer with PDF.js (`pdfjs-dist`, legacy build, own worker CSP) | Accepted | F14, ADR 0011 |
| [0021](0021-connected-accounts-oauth-through-the-bff-and-provider-adapters-over-http.md) | Connected accounts: module `mailbox`, OAuth through the BFF, provider adapters over plain HTTP | Accepted | N05, D-33, D-15, D-19 |
| [0022](0022-azure-hosting-on-container-apps-with-bicep.md) | Azure hosting on Container Apps, described in Bicep, built by ACR Tasks, released by GitHub Actions | Accepted | D-12, D-17, H-03 |
| [0023](0023-data-protection-keys-protected-by-azure-key-vault.md) | Data Protection keys protected at rest by an Azure Key Vault key (`Azure.Extensions.AspNetCore.DataProtection.Keys`, `Azure.Identity`) | Accepted | R4, H-03, ADR 0022 |
| [0024](0024-infrastructure-provisioned-by-a-github-actions-workflow.md) | Infrastructure provisioned by a GitHub Actions workflow (`infrastructure.yml`), one parameter file per environment | Accepted | H-03, ADR 0022 |
| [0025](0025-releases-without-github-environments.md) | Releases and provisioning without GitHub environments: OIDC bound to `main`, repository variables, prod released by hand | Accepted | H-03, ADR 0022, ADR 0024 |
