# ADR 0024 — Infrastructure provisioned by a GitHub Actions workflow, one parameter file per environment

- Status: Accepted (2026-10-20, requested by the user) · Related: H-03, ADR 0022 (§2, §8, §9), ADR 0023
- Amended by ADR 0025 (2026-10-24): no GitHub environment — the federated credential trusts the runs of `main`,
  `AZURE_CLIENT_ID_INFRA` and `ACR_TASK_GITHUB_TOKEN` are repository variable/secret, `githubEnvironment` is gone.

## Context
ADR 0022 described the infrastructure in Bicep but deployed it from an operator's shell: `deploy.sh shared`,
`deploy.sh qa|prod`, `secrets.sh init`, `deploy.sh shared` again, then the psql job by hand, each step with `az`
logged in as an Owner. The environments were fixed (`@allowed(['prod', 'qa'])`, RabbitMQ users and vhosts, Postgres
logins and Valkey databases written for exactly those two). The user wants:
- the first setup and every later change of the templates applied by a **GitHub Action**, not from a laptop;
- **new environments** (demo, dev, …) added without changing scripts or templates;
- one idempotent run: shared resources first, then the environment, created when missing and updated otherwise.

## Decision
1. **Workflow** `.github/workflows/infrastructure.yml`, `workflow_dispatch` only, inputs `environment`, optional
   `image` (commit SHA, normally only for the first deployment of the applications) and `what-if`. It runs
   `deploy/azure/scripts/provision.sh`, which a shell with the same rights can also run:
   shared core → shared secrets → shared services → environment core → environment secrets → psql bootstrap job
   (every environment) → applications (only with `image` or when they already run). Every step is idempotent: secrets
   are generated only when missing and written only when their value changes (no new versions, no restarts for
   nothing); the RabbitMQ definitions are rewritten (and the broker restarted) only when users or passwords change.
   No trigger on push: a template change is applied by running the workflow (after a `what-if` run when in doubt).
2. **Identity**: `id-auxilia-next-infra` in `rg-auxilia-next-automation`, federated credential
   `repo:ermaaga/AuxiliaDigitale:environment:infrastructure` (GitHub environment with a required reviewer). Rights on
   the subscription: **Contributor** plus **Role Based Access Control Administrator** with a condition that allows
   assigning or removing only the roles the templates use (Contributor, AcrPull, Reader, Key Vault Secrets User,
   Secrets Officer, Crypto Service Encryption User): it cannot make anybody Owner. It is the only resource not
   deployed by the workflow (`deploy/azure/automation.bicep`, deployed once by an Owner). The templates make it Key
   Vault Secrets Officer of every vault (`automationPrincipalIds`); users who still write secrets by hand
   (`secrets.sh set`, e.g. the client secrets printed by `auxctl clients add`) are listed in the repository variable
   `AZURE_OPERATOR_OBJECT_IDS`.
3. **Environments are data**: `deploy/azure/environments/<name>.bicepparam` (name 2–9 lowercase letters/digits — the
   Key Vault name limit —, never `shared`), with `githubEnvironment` (`production` for prod, otherwise the name) and
   `valkeyDatabase` (unique, 0–15, checked by `secrets.sh`). The scripts list the files: Postgres login and Catalog
   database, RabbitMQ user and vhost, Key Vault secrets and the psql job's environment follow automatically. The
   isolation rules of ADR 0022 §2 apply to every environment (tenant slugs never shared between environments).
4. **Releases** (`deploy.yml`): `main` → production and `qa` → qa as before; other environments are released by
   `workflow_dispatch` with their GitHub environment name (resource suffix = that name, `production` → `prod`).
5. **Optional**: the secret `ACR_TASK_GITHUB_TOKEN` of the `infrastructure` environment lets the workflow create or
   update the ACR Tasks too (`acr-task.sh`); without it they stay as they are.

## Consequences
- First setup = one Owner command (`automation.bicep`) + GitHub configuration + workflow runs; no `az` login with
  Owner rights for the day-to-day work.
- A powerful identity lives in GitHub: the required reviewer on `infrastructure` and the role condition limit it;
  Contributor on the subscription still reaches the legacy resources of the same subscription (accepted: the legacy is
  retired in R-03).
- Not automated (unchanged from the runbook): the GitHub environments and their `AZURE_CLIENT_ID` (the workflow
  prints the value), the first client applications and System user of a new environment (their secrets are printed
  by `auxctl` in the job log), the satellite apps' tags (`deploy.sh shared --openwa …`).
- `bootstrap.sql` now runs once per environment (`-v env=<name>`); the psql job loops over `AUXILIA_ENVIRONMENTS`.
