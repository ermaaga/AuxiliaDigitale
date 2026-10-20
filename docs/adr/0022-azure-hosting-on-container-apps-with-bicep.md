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
├─ caj-auxilia-psql-01     manual job (psql bootstrap, dumps)
├─ stauxilianextshared     Azure Files: OpenWA session (§11)
├─ ca-auxilia-openwa-01    internal HTTP, 0..1 (§11)
├─ ca-auxilia-agent-openwa-01 internal HTTP, 0..1 (§11)
└─ ca-auxilia-site-01      showcase site, external, 0..2 (§11)
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
for the backplane channel (small code change, not done now). The code review behind this table is in §10.

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
(Provisioning moved to the Infrastructure workflow and environments became parameter files `environments/<name>.bicepparam`:
ADR 0024.)
`deploy/azure/`: `shared.bicep` and `environment.bicep` (subscription scope, they create their resource group), modules
in `modules/`, parameters in `shared.bicepparam`, `qa.bicepparam`, `prod.bicepparam`, scripts in `scripts/`
(`deploy.sh` with `--what-if`, `secrets.sh`, `acr-task.sh`; `secrets.sh init` also writes the RabbitMQ definitions), Postgres bootstrap SQL in
`postgres/`. Every deployment can be previewed with `what-if`. Images of RabbitMQ, Valkey and Postgres are pinned
(minor version).

### 10. Code review of the sharing (findings, no code changed)
The application was written for one environment per infrastructure: every name it uses on Postgres, Valkey and
RabbitMQ is fixed in the code, with no environment part. Each point below says what the code does, what happens when
prod and qa share the service, and what keeps them apart today. The fixes in the last column are **not done** (user's
choice: no code changes for H-03); they become necessary if the configuration rules turn out to be too fragile.

| # | Where in the code | Behaviour with prod and qa sharing the service | Protection today | Possible code fix (not done) |
|---|---|---|---|---|
| C1 | `TenantDatabaseAdmin.DatabaseName` (`Persistence.Tenant/Administration`): database **and** role are `auxilia_t_<slug>` (`-` → `_`) | Same slug in both environments → same database and same role. `CreateDatabaseAsync` finds the role and runs `ALTER ROLE … PASSWORD` (the prod tenant loses its password and stops working), skips `CREATE DATABASE` because it exists, and returns a connection string to the **prod database**: qa would migrate and write prod data | Slug rule (qa slugs never match prod slugs, e.g. suffix `-qa`); PostgreSQL ≥ 16: `auxilia_qa` has `CREATEROLE` but may alter only the roles it created, so the `ALTER ROLE` fails and provisioning stops (`bootstrap.sql` grants neither login to the other) | Configurable prefix (e.g. `Provisioning:DatabasePrefix` = `auxilia_t_` / `auxilia_qa_t_`) used by `DatabaseName` |
| C2 | `RealtimeRegistration.AddRealtime` (`Infrastructure/Realtime`): the SignalR backplane **overrides** the `ChannelPrefix` of the connection string with the fixed `auxilia:signalr` | The `channelPrefix=prod:`/`qa:` of `redis-connection` does not reach the backplane, and Redis pub/sub ignores the database number: API and Worker nodes of both environments share every backplane message. Groups are `RealtimeGroups.*(slug, …)` (user, role, session, tenant), so with the same slug a qa event (notification, `ForceLogout`, `AppointmentChanged`…) is delivered to the prod users of that tenant and role | Slug rule (groups contain the slug; connection ids are random; the code sends only to groups, never `Clients.All`). Each node still receives and discards the other environment's traffic (small load) | Backplane prefix from configuration, or `auxilia:signalr` + the connection string's prefix |
| C3 | `RedisCacheInvalidationBus` (`auxilia:invalidate`) and `TenantLogLevelBus` (`auxilia:log-levels`) on the shared `RedisConnection` | Without isolation a qa invalidation would evict prod L1 entries, and a qa debug level for a slug would make prod nodes reload that slug (from their own Catalog): no data crosses, only useless work | `RedisConnection` parses the whole connection string, so `channelPrefix` applies: the channels become `prod:auxilia:…` and `qa:auxilia:…`. Do not remove `channelPrefix` from `redis-connection` | – |
| C4 | Valkey keys: HybridCache `InstanceName = "auxilia:"`, ALTCHA `auxilia:altcha:<challenge>`, token deny-list, distributed locks | Same key names in both environments; e.g. a prod deny-list entry would be seen by qa | `defaultDatabase=0` (prod) / `1` (qa) in `redis-connection`: keys live in separate databases. Memory and eviction policy are shared, and a `FLUSHALL` by hand empties both | `InstanceName` from configuration |
| C5 | `MessageRouting` (`Infrastructure/Messaging`): queues `auxilia.<module>` and `auxilia.platform`, Rebus error queue, topic exchanges | Same queue names: without isolation the qa Worker would consume prod messages (and run them against its own Catalog) | Vhost in the AMQP URI (`/prod`, `/qa`), one user per vhost with permissions only there; the Worker's KEDA rules read the queue length through the same URI, so they scale on their own vhost | – |
| C6 | `CatalogPersistence`: Data Protection with fixed application name, keys in the Catalog (`PersistKeysToDbContext`) | Separate Catalogs → separate key rings: tenant connection strings (`TenantConnectionProtector`), TOTP secrets and connected-account secrets of prod cannot be read by qa. A prod database copied into qa therefore has unreadable secrets (reset them in qa); copying the prod key ring into qa would break the separation and is not allowed | Separate Catalog databases and logins | Key Vault protection of the key ring (needs `Azure.Identity`, separate decision — §5) |
| C7 | Npgsql connection pools: one pool per connection string per process; tenant connection strings are built from `Provisioning:AdminConnectionString` (defaults to the Catalog one) with `NpgsqlConnectionStringBuilder`, so they inherit its keywords | B1ms allows about 50 connections (some reserved by Azure) for **both** environments: API, Worker and `auxctl` of prod and qa, each with a pool for the Catalog and for every tenant (default `Maximum Pool Size` 100) can exhaust the server, and prod then refuses connections because of qa | Few tenants, scale to zero closes the pools; **open point**: verify on qa and, if needed, add `Maximum Pool Size` (and `Connection Idle Lifetime`) to `catalog-connection` in `secrets.sh` — new tenants inherit it, existing ones need their stored connection string updated | – |
| C8 | Health checks (`/health/ready`: Postgres, Redis, RabbitMQ) | A shared service down or saturated makes **both** environments unhealthy; a load test on qa slows prod (Consequences) | None: accepted with the sharing | One RabbitMQ/Valkey per environment (Alternatives) |
| C9 | `InMemorySessionStore` of the web BFF (not shared, but found in the same review) | Sessions live in the replica: max 1 replica per environment, sessions end on scale to zero (§4) | `maxReplicas: 1` on `ca-auxilia-web-<env>` | Session store on Valkey (with a per-environment database) |

Not affected: logs and documents (own storage account per environment), JWT signing keys (`catalog.signing_keys`, per
Catalog), client applications and System users (per Catalog), Key Vault and identities (per environment).

Operational rules that follow (also in the runbook): qa tenant slugs never match prod slugs (C1, C2); `redis-connection`
always keeps `defaultDatabase` and `channelPrefix` (C3, C4); never copy the prod Data Protection keys into qa (C6);
watch the Postgres connections during the first weeks on qa (C7).

### 11. Satellite apps: OpenWA, agent-openwa, showcase site
Three more container apps, added on 2026-10-16 with the user's choices. They are **not** built from this repository
and are **shared** by prod and qa (one instance each, resource group `rg-auxilia-next-shared`, shared identity and
Key Vault), like RabbitMQ and Valkey. Module `deploy/azure/modules/satellite-apps.bicep`, deployed by `shared.bicep`
together with the shared services.

| App | Ingress | Replicas | Notes |
|---|---|---|---|
| `ca-auxilia-openwa-01` (OpenWA, WhatsApp Web gateway) | **internal** HTTP (`https://ca-auxilia-openwa-01.internal.<domain>`) | 0..1 | API key = Key Vault `openwa-api-key` (generated by `secrets.sh init`); session on Azure Files |
| `ca-auxilia-agent-openwa-01` | internal HTTP (`agentOpenwaExternal = true` if it must receive webhooks from outside) | 0..1 | gets `OPENWA_URL` and `OPENWA_API_KEY`; further secrets from the shared Key Vault (`agentOpenwaSecrets`) |
| `ca-auxilia-site-01` (showcase site) | **external**, Azure FQDN (domains handled by the user) | 0..2 | no secret |

- **Images**: built in `acrauxilianext01` by one ACR Task per repository (`scripts/acr-task.sh satellite <app>
  <owner/repo> [branch] [dockerfile] [context]`), repositories `auxilia/openwa`, `auxilia/agent-openwa`,
  `auxilia/site`, tagged with the commit SHA. The shared identity gets `AcrPull`.
- **Created only when an image tag exists** (as the applications of the environments). `deploy.sh shared` keeps the
  running tags; `--openwa`, `--agent-openwa`, `--site <sha>` release another commit. Releasing them is a manual,
  explicit command (the approval); a GitHub workflow in their own repositories can come later with an OIDC identity
  like `id-auxilia-next-deploy-<env>`.
- **OpenWA scales to zero** (user's choice, lowest cost): it wakes only on an HTTP call (agent-openwa, later the
  WhatsApp `http-gateway` adapter of the API, D-20) and **receives no WhatsApp message while asleep**. One replica at
  most: a WhatsApp session cannot be opened twice. The session folder (`openwaSessionPath`, default `/sessions`) is
  an **Azure Files** share (`stauxilianextshared/openwa-sessions`, 5 GiB, LRS, mounted through the Container Apps
  environment storage with the account key), so restarts and scale to zero do not ask for a new QR code scan. The
  mount uses `mfsymlinks,nobrl` because Chromium writes symlinks and byte-range locks that plain SMB refuses — to
  verify on the first start; if the session still breaks, the fallback is a session export/import offered by the
  OpenWA image or a premium NFS share (paid).
- **Not known yet** (parameters in `shared.bicepparam`, defaults in brackets): ports (8080, 8080, 80), session
  folder, name of the API key variable (`OPENWA_API_KEY`), plain settings (`openwaEnv`, `agentOpenwaEnv`,
  `siteEnv`) and agent secrets. They are set once the repositories and images are known.
- Reachability: the internal ingress of OpenWA is reachable by every app of the environment, so also by prod and qa
  of Auxilia; it is protected by its API key.

## Estimated cost (Italy North, list prices, October 2026)
| Item | €/month |
|---|---|
| PostgreSQL B1ms + 32 GB (free while it is the only B1ms of the subscription within the 12 months) | 0 (≈ 17 otherwise) |
| ACR Basic | ≈ 4.5 |
| Container Apps Consumption, scale to zero (monthly free grant: 180,000 vCPU-s, 360,000 GiB-s, 2 M requests) | ≈ 0–10 |
| Storage LRS, Key Vault operations, private DNS zone (≈ 0.45), ACR Task build minutes | ≈ 1–3 |
| Satellite apps (§11): Azure Files 5 GiB (≈ 0.3) + compute while awake, inside the free grant at low use | ≈ 0.5–5 |
| **Total** | **≈ 6–23** (+ ≈ 17 while the legacy Postgres in Canada exists) |

## Consequences
- The whole platform is reproducible from the repository (`what-if` before every change); no secret in Git.
- Cost is close to zero when nobody uses the application; the price is cold starts and the limits in §4.
- Prod and qa share the Postgres server, RabbitMQ and Valkey: a load test on qa slows prod; the slug rule must hold.
- The web app cannot scale out until the BFF sessions move to a shared store.
- Manual steps remain (runbook `docs/runbooks/azure-deployment.md`): GitHub PAT of the ACR Task, branch `qa`, GitHub
  environments and reviewers, client applications and first System user; for the satellite apps their repositories,
  ACR Tasks, ports and settings.

## Alternatives considered
- **Keep the legacy resources and import them into Bicep**: inherits Canada Central, the open firewall and plain-text
  secrets; rejected.
- **One RabbitMQ/Valkey per environment**: no slug rule and no shared load, almost free when idle; the user chose to
  share them as today.
- **Managed Redis / Service Bus**: fixed monthly cost and code changes (Rebus transport); rejected for cost.
- **Releases inside the ACR Task**: no approval step and a second deployment mechanism; rejected.
- **Log Analytics**: useful for queries and alerts, but billed per GB; rejected for cost, D-17 files cover the need.
