# ADR 0025 — Releases and provisioning without GitHub environments

- Status: Accepted (2026-10-24, requested by the user) · Related: H-03, ADR 0022 §8, ADR 0024 · Amends: ADR 0022 §8
  (approval for prod), ADR 0024 §2–§5 (GitHub environment `infrastructure`, `githubEnvironment`)

## Context
ADR 0022 and ADR 0024 bound the Azure identities of the workflows to GitHub environments (federated credential
`repo:ermaaga/AuxiliaDigitale:environment:<qa|production|infrastructure>`) and used a required reviewer on
`production` and `infrastructure` as the approval. The repository is public on GitHub Free today and will become
private on GitHub Free: there, environments, environment variables and secrets, required reviewers and branch
protection are not available (GitHub Pro/Team bring environments and branch protection, required reviewers on a
private repository need Enterprise). A design that depends on environments would stop working on that day.

## Decision
1. **OIDC bound to the branch**: every federated credential (`modules/identity.bicep`, name `github-main`) trusts
   `repo:ermaaga/AuxiliaDigitale:ref:refs/heads/main`. Pull requests, forks and other branches never get a token.
   `workflow_run` runs on the default branch, so the automatic qa release has the `main` ref too; manual runs must be
   started from `main` (both workflows check `github.ref` and fail with a clear message otherwise).
2. **Repository variables** instead of environment variables: `AZURE_CLIENT_ID_INFRA` (output `clientId` of
   `automation.bicep`) and `AZURE_CLIENT_ID_<ENV>` (`AZURE_CLIENT_ID_QA`, `AZURE_CLIENT_ID_PROD`, …; the Infrastructure
   summary prints name and value, output `clientIdVariable` of `environment.bicep`). `deploy.yml` reads
   `vars[AZURE_CLIENT_ID_<ENV>]` and stops when it is missing. `ACR_TASK_GITHUB_TOKEN` becomes a repository secret.
   `githubEnvironment` disappears from the parameter files; the environment name is the resource suffix (`prod`, not
   `production`).
3. **Approval for prod = a deliberate manual run**: a push to `qa` (CI green) still releases qa automatically; a push to
   `main` builds the images but does not release. Production is released only by *Deploy → Run workflow* from `main`
   with `environment = prod` and a SHA that is `main` or one of its ancestors (checked through the compare API), so
   prod never runs a commit that was not merged to `main`.
4. **Infrastructure** stays `workflow_dispatch` only, from `main`.

## Consequences
- The trust boundary is **write access to the repository**: a collaborator who can push to `main` can change a
  workflow and obtain any of these identities (as with environments without branch protection). Keep the collaborators
  with write access to the minimum; every run stays in the Actions log. The role condition on `id-auxilia-next-infra`
  (ADR 0024 §2) still prevents it from granting Owner or User Access Administrator.
- No reviewer step: releasing prod by mistake requires typing `prod` and a SHA in a manual run.
- Already deployed identities keep their old credential `github-<environment>`, now unusable: delete it once
  (`az identity federated-credential delete`, runbook "First setup").
- Back to environments (a plan that offers them, or a public repository): set the subject to
  `environment:<name>` in `identity.bicep`, add `environment:` to the jobs and move the variables into the environments.

## Alternatives considered
- **Keep environments and wait**: blocks the workflows when the repository becomes private.
- **Custom OIDC subject with `job_workflow_ref`** (one identity per workflow file): does not separate qa from prod
  (`deploy.yml` releases both) and protects nothing against a collaborator who edits the workflow on `main`.
- **Third-party "manual approval" actions** (issue comments): an extra dependency with write permissions on issues for
  an approval that a writer can bypass anyway.
- **GitHub Pro (~4 $/month)**: environments and branch protection, but still no required reviewer on a private repository.
