# ADR 0022 — Azure hosting on Container Apps, described in Bicep, built by ACR Tasks, released by GitHub Actions

- Status: Accepted (2026-10-16, choices made with the user in the H-03 session; issue #132) · Related: D-12, D-17, D-15,
  D-02, H-03, ADR 0002, ADR 0006, ADR 0011, ADR 0013

## Context
D-12 (hosting) was the last architecture decision still open before go-live. The subscription "Auxilia Digitale
Production" (Pay-As-You-Go) already hosts the **legacy** application, created by hand in the portal with no
infrastructure as code. The inventory of 2026-10-16 (read only, `az` CLI) found:

| Resource | State | Problem |
|---|---|---|
| `cae-auxilia-prod-01` (Container Apps, Italy North) | Consumption, VNet `10.0.0.0/16` | no log collection, legacy and its QA in the same environment |
| `ca-auxilia-web-prod-01` | legacy image from Docker Hub (PAT) | connection strings as **plain environment variables**, no managed identity |
| `ca-auxilia-queue-prod-01` / `ca-auxilia-cache-prod-01` | RabbitMQ / Valkey `latest`, 0–1 replicas | no persistence, unpinned image |
| `psql-auxilia-prod-01` | PostgreSQL 17 B1ms, **Canada Central** | public access, firewall open to every Azure address and to Render, prod and QA databases on it |
| `stauxiliafilesprod01` / `…qa01` | blob `documents`, `logs` | shared key, open network, no lifecycle (D-17 needs one) |
| `kv-auxilia-prod-01` | access policies, no purge protection | not used by the applications |
| Container registry, Log Analytics | none | – |

The goal set by the user is **the lowest possible running cost**. Free services of the account (12 months, per
subscription) cover **750 h/month of PostgreSQL Flexible B1ms with 32 GB of storage**, i.e. one server running all
month: a second server is paid only while both exist.

## Decision

### 1. Side by side with the legacy, three resource groups, Italy North
New resource groups, nothing of the legacy is modified; the cutover (R-02) moves the domain and the legacy is retired
in R-03.

```
rg-auxilia-next-shared                                  rg-auxilia-next-prod | rg-auxilia-next-qa
├─ vnet-auxilia-next-01 10.40.0.0/16                     ├─ id-auxilia-next-<env>         runtime identity
│   ├─ snet-cae  10.40.0.0/23 (Microsoft.App)            │      AcrPull + Key Vault Secrets User
│   └─ snet-psql 10.40.2.0/28 (PostgreSQL)               ├─ id-auxilia-next-deploy-<env>  GitHub OIDC
├─ private DNS zone of Postgres                          │      Contributor on the RG, Reader on the ACR
├─ cae-auxilia-next-01     Consumption, no Log Analytics ├─ kv-auxilia-next-<env>         application secrets
├─ psql-auxilia-next-01    PostgreSQL 18 B1ms 32 GB      ├─ stauxilianext<env>            documents + logs (lifecycle)
├─ acrauxilianext01        Basic + ACR Task              ├─ ca-auxilia-api-<env>          external, 0..N
├─ kv-auxilia-next-shared  Postgres/RabbitMQ/Valkey      ├─ ca-auxilia-web-<env>          external, 0..1
├─ id-auxilia-next-shared                                ├─ ca-auxilia-worker-<env>       no ingress, 0..N (queues)
├─ ca-auxilia-rabbitmq-01  internal TCP, 0..1            └─ caj-auxilia-auxctl-<env>      manual job (migrations)
├─ ca-auxilia-valkey-01    internal TCP, 0..1
└─ caj-auxilia-psql-01     manual job (psql bootstrap, dumps)
```

The application containers of both environments run in the **one shared Container Apps environment** (an environment
costs nothing on Consumption; internal TCP ingress of RabbitMQ and Valkey is reachable only inside it), each in the
resource group of its own environment.

### 2. Shared between prod and qa: Postgres, RabbitMQ, Valkey — isolated by configuration only
No application code changes (user's choice). Isolation:

| Component | prod | qa |
|---|---|---|
| Postgres login (Catalog + provisioning, `CREATEDB`/`CREATEROLE`) | `auxilia_prod` | `auxilia_qa` |
| Catalog database | `auxilia_catalog_prod` | `auxilia_catalog_qa` |
| Tenant databases and roles (D-02, fixed name `auxilia_t_<slug>`) | slugs of prod | **slugs that never match a prod slug** (e.g. suffix `-qa`) |
| Valkey keys (`defaultDatabase` in the connection string) | database 0 | database 1 |
| Valkey pub/sub (`channelPrefix` in the connection string: cache invalidation, log levels) | `prod:` | `qa:` |
| RabbitMQ (vhost in the AMQP URI, user per vhost) | vhost `prod`, user `auxilia_prod` | vhost `qa`, user `auxilia_qa` |
| Key Vault, storage account, identities | own | own |

The SignalR backplane channel is fixed in the code (`auxilia:signalr`), and its groups are tenant-prefixed: with
different slugs the two environments never reach each other's connections. **The slug rule is therefore mandatory**
and written in the runbook. A second line of defence comes from PostgreSQL ≥ 16: a role with `CREATEROLE` may alter
only the roles it created, so a qa provisioning that hit a prod slug fails on `ALTER ROLE` instead of taking the
prod database over. Should the rule ever be too fragile, the fix is a configurable prefix for tenant databases and
for the backplane channel (small code change, not done now).

### 3. PostgreSQL Flexible Server 18, free-tier shaped, private
`Standard_B1ms` (Burstable), 32 GB `P4`, **auto-grow off** (above 32 GB the storage is paid), backup 7 days, no
geo-redundant backup, no HA; **VNet integration** (no public endpoint), password + Microsoft Entra authentication,
`azure.extensions = CITEXT` (the tenant model uses `citext`). The server is reached from inside the VNet only: admin
work goes through the `caj-auxilia-psql-01` job (or a temporary job running `auxctl`). Moving the legacy databases to
this server and deleting the Canada server (R-03) brings Postgres back inside the free tier.

### 4. Scale to zero everywhere
Every container app has `minReplicas: 0`: API and web on the HTTP scaler, RabbitMQ and Valkey on the TCP scaler
(they stay up while the API or the Worker hold a connection), the Worker on one KEDA `rabbitmq` rule per queue
(`auxilia.documents|imports|mailbox|marketing|messaging|platform|reporting`). Consequences accepted by the user:
- first request after a pause: cold start of web + API (+ Valkey/RabbitMQ), about 10–30 s;
- RabbitMQ has no disk: when it scales to zero its queues are gone; the definitions (vhosts, users, permissions) are
  loaded again at every start from a Key Vault secret mounted as a file, and the Worker declares its queues when it
  starts. Messages are produced through the outbox and RabbitMQ only stops after its last client, so the loss window
  is small; it is **verified on qa** before prod;
- the **web app runs at most one replica**: BFF sessions are kept in memory (`InMemorySessionStore`); scaling to zero
  ends them, so a user idle longer than the cooldown signs in again. A shared session store is out of scope;
- KEDA polls RabbitMQ every 30 s for the Worker: if that keeps RabbitMQ awake, the fallback (decided on qa) is a
  smaller always-on RabbitMQ (0.25 vCPU / 0.5 GiB) or a longer polling interval.

### 5. Secrets in Key Vault, read through a managed identity
- Key Vault with **RBAC** authorization, soft delete 90 days, **purge protection on**.
- Container apps declare their secrets as **Key Vault references** (`keyVaultUrl` + user-assigned identity): no
  secret value in Bicep, in the deployment history or in the app configuration.
- Values are written by `deploy/azure/scripts/secrets.sh`: generated passwords (`openssl rand`), connection strings
  composed from the deterministic resource names, client application secrets read **from standard input** (as
  `auxctl` does), never from the command line.
- Images are pulled from ACR with the same identity (`AcrPull`); no registry password.
- Not in scope: Data Protection keys stay in the Catalog (risk R4 of H-01); protecting them with Key Vault needs
  `Azure.Identity`, outside the dependency allowlist — a separate decision.

### 6. No Log Analytics
The environment has no log destination (cost). Application logs go to the storage account as D-17 requires
(`AuxiliaLogging:Storage = azure-blob`, container `logs`) and are read from the console page; the console stream
(`az containerapp logs show --follow`) works without Log Analytics for live troubleshooting. A **lifecycle rule**
deletes `logs/` blobs after `logRetentionDays` (prod 180, qa 14: the retention "outside the system" of D-17).

### 7. Build: ACR Tasks on `main` and `qa`
`acrauxilianext01` (Basic) runs one multi-step task (`deploy/azure/acr-task.yaml`) triggered by GitHub commits on
`main` and `qa` (webhook created by the task, GitHub fine-grained PAT kept in the task). It builds `auxilia/api`,
`auxilia/worker`, `auxilia/auxctl` (targets of `backend/Dockerfile`) and `auxilia/web` (`frontend/Dockerfile`), each
tagged with the **commit SHA** (immutable, used by releases) and with the branch name (moving, informative).
Registry retention is manual on Basic (`az acr run` purge, runbook).

### 8. Release: GitHub Actions with OIDC, approval for prod
ACR Tasks have no approval step, so releases live in `.github/workflows/deploy.yml`:

1. trigger `workflow_run` of **CI** completed with success on `main` or `qa` (or `workflow_dispatch` with a SHA, used
   for rollbacks); `qa` → GitHub environment `qa`, `main` → environment `production` (**required reviewer**: free on
   a public repository);
2. `azure/login` with **OIDC** on `id-auxilia-next-deploy-<env>` (federated credential
   `repo:ermaaga/AuxiliaDigitale:environment:<qa|production>`): no secret stored in GitHub, only client/tenant/
   subscription ids as environment variables;
3. wait for the ACR Task run of that commit (`az acr task list-runs`), fail if it failed;
4. run `caj-auxilia-auxctl-<env>` with the new `auxctl` image: `migrate catalog`, then `migrate tenants --all`; a
   failure stops the release with the previous version still running;
5. `az containerapp update --image …:<sha>` for worker, api, web;
6. smoke test `GET /health/ready` on the API; if it fails, the same update with the previous SHA (read before step 5).

Rollback by hand = `workflow_dispatch` with the previous SHA (migrations are expand/contract, so an older image runs on
the newer schema).

### 9. Bicep layout
`deploy/azure/`: `shared.bicep` and `environment.bicep` (subscription scope, they create their resource group), modules
in `modules/`, parameters in `shared.bicepparam`, `qa.bicepparam`, `prod.bicepparam`, scripts in `scripts/`
(`deploy.sh` with `--what-if`, `secrets.sh`, `acr-task.sh`; `secrets.sh init` also writes the RabbitMQ definitions), Postgres bootstrap SQL in
`postgres/`. Every deployment can be previewed with `what-if`. Images of RabbitMQ, Valkey and Postgres are pinned
(minor version).

## Estimated cost (Italy North, list prices, October 2026)
| Item | €/month |
|---|---|
| PostgreSQL B1ms + 32 GB (free while it is the only B1ms of the subscription within the 12 months) | 0 (≈ 17 otherwise) |
| ACR Basic | ≈ 4.5 |
| Container Apps Consumption, scale to zero (monthly free grant: 180,000 vCPU-s, 360,000 GiB-s, 2 M requests) | ≈ 0–10 |
| Storage LRS, Key Vault operations, private DNS zone (≈ 0.45), ACR Task build minutes | ≈ 1–3 |
| **Total** | **≈ 6–18** (+ ≈ 17 while the legacy Postgres in Canada exists) |

## Consequences
- The whole platform is reproducible from the repository (`what-if` before every change); no secret in Git.
- Cost is close to zero when nobody uses the application; the price is cold starts and the limits in §4.
- Prod and qa share the Postgres server, RabbitMQ and Valkey: a load test on qa slows prod; the slug rule must hold.
- The web app cannot scale out until the BFF sessions move to a shared store.
- Manual steps remain (runbook `docs/runbooks/azure-deployment.md`): GitHub PAT of the ACR Task, branch `qa`, GitHub
  environments and reviewers, client applications and first System user.

## Alternatives considered
- **Keep the legacy resources and import them into Bicep**: inherits Canada Central, the open firewall and plain-text
  secrets; rejected.
- **One RabbitMQ/Valkey per environment**: no slug rule and no shared load, almost free when idle; the user chose to
  share them as today.
- **Managed Redis / Service Bus**: fixed monthly cost and code changes (Rebus transport); rejected for cost.
- **Releases inside the ACR Task**: no approval step and a second deployment mechanism; rejected.
- **Log Analytics**: useful for queries and alerts, but billed per GB; rejected for cost, D-17 files cover the need.
