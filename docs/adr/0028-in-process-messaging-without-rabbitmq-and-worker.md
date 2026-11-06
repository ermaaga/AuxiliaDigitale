# ADR 0028 — In-process messaging: queues in the Catalog database, handlers in the Api (no RabbitMQ, no Worker)

- Status: Accepted (2026-11-06, cost review with the user) · Related: ADR 0007, ADR 0022 §2/§4, D-12, D-15

## Context
The production costs of ADR 0022 are higher than its estimate. The containers that should scale to zero do not: KEDA
polls RabbitMQ every 30 s for the Worker's queue rules, which keeps `ca-auxilia-rabbitmq-01` (0.5 vCPU / 1 GiB) awake
(the open point of ADR 0022 §4), and Valkey stays up while the Api or the Worker hold a connection. With the current
load (one customer, a few users) a separate message broker, a separate Worker and a shared cache cost more than the
work they do.

RabbitMQ is not used only for documents: every message of the contracts goes through it, and most importantly **every
outbound e-mail** (`IMessageDispatcher` queues `DeliverOutboundMessageCommand`). Removing RabbitMQ without a
replacement stops e-mails, document checks, imports, campaigns, large exports, tenant provisioning, mailbox sync and
console job runs. A configuration change is therefore not enough.

## Decision
A second messaging mode, chosen per environment with the setting **`Messaging:Mode`**:

| | `Worker` (default, unchanged) | `InProcess` |
|---|---|---|
| Queues | RabbitMQ, one queue per module | table `rebus_messages` of the **Catalog** database (Rebus PostgreSQL transport, already a dependency for `rebus_timeouts`), one queue per module |
| Consumer | `Auxilia.Worker` (KEDA rule per queue) | the **Api** itself |
| Producer | outbox → RabbitMQ (send-only bus) | outbox → table |
| auxctl | send-only on RabbitMQ | send-only on the table (`AddInProcessMessageBusClient`) |
| Deferral (second-level retries) | `rebus_timeouts` | native to the transport (`visible` column) |
| Error queue | RabbitMQ queue `error` | rows with `recipient = 'error'` |
| Parallelism | 2 messages per queue | 1 message per queue (CPU and connections shared with the requests) |

- **Handlers move** from `Auxilia.Worker/Handlers/<Module>/` to `Auxilia.Infrastructure/Messaging/Handlers/<Module>/`
  (same thin `MessageHandler<T>` classes, same namespaces per module), so that both hosts can run them; the list of
  handlers and of consumed queues is `MessageRouting.HandlerTypes` / `HandledQueues`. The Worker project keeps only
  its host.
- **Current user**: in the Api `ICurrentUser` is `HttpCurrentUser` for requests and `MessageCurrentUser` inside a
  handler, chosen by `Rebus.Pipeline.MessageContext.Current` (`AddInProcessMessageBus<THostCurrentUser>`). A test
  proves a handler never sees the HTTP caller and a request never sees a message caller.
- **Tenant provisioning** runs in the Api in this mode (`AddTenantAdministration` + `AddTenantDatabaseAdministration`).
  In Azure the Catalog login already has `CREATEDB`/`CREATEROLE` (ADR 0022 §2), so no privilege is added.
- **Durability**: a message leaves the table only when its handler has finished (the receive transaction commits
  then). If the Api stops halfway (scale to zero, restart, crash) the message is received again at the next start;
  handlers are already idempotent (`ops.processed_messages`, status checks). Nothing is lost while the Api sleeps:
  messages wait in the table.
- **Polling**: each queue polls the table with a back-off of 0.1 → 0.5 → 1 → 2 s while idle (7 small indexed queries
  per 2 s at most), only while the Api is running. It does not keep the Api awake (its scaler counts HTTP requests).
- The Worker refuses to start with `Messaging:Mode = InProcess` (two consumers of different transports would split the
  messages).
- **Valkey**: the code already works without `ConnectionStrings:Redis` (memory cache, in-process SignalR). The Bicep
  parameter `valkeyEnabled = false` removes the connection string and limits the Api to **one replica**, because
  without Valkey every replica would have its own cache, token deny-list, ALTCHA replay list and SignalR connections.
  In `InProcess` mode the realtime pushes of the handlers (export ready, document processed, sync completed) reach the
  browser directly, which in `Worker` mode needs the Valkey backplane.
- **Infrastructure** (`deploy/azure`): `environments/<env>.bicepparam` → `messagingMode` (`Worker` | `InProcess`) and
  `valkeyEnabled`; `shared.bicepparam` → `rabbitmqEnabled`, `valkeyEnabled` for the shared apps; the release workflow
  skips the worker app when it does not exist. qa switches first; prod, then the shared RabbitMQ/Valkey follow
  (runbook `docs/runbooks/azure-deployment.md`, "In-process messaging").

## Consequences
- Running cost: RabbitMQ, Valkey and the two Workers can be deleted; what remains running is web + Api (scale to zero),
  Postgres (free tier), ACR, storage and Key Vault.
- **Work runs in the Api process** (0.5 vCPU): a large import, export or campaign shares the CPU with the requests. At
  the current load this is acceptable; if it is not, `Messaging:Mode = Worker` brings the Worker back with no code
  change.
- **Long work can be interrupted by scale to zero**: the Api scales by HTTP traffic, so after the cooldown (300 s
  default) without requests it stops even while a handler runs. The message is received again at the next start and
  the handlers resume (imports and mailbox sync are resumable, the others are short). Deferred messages
  (second-level retries, mailbox continuations) are handled only while the Api is running.
- The Catalog database holds the queue table: its connections are shared with the requests (ADR 0022 C7); a running
  handler holds one connection for the receive transaction. One message per queue keeps this bounded (≤ 7).
- Without Valkey the Api cannot scale out; with the current load one replica is enough.
- Messages in the error queue are rows of `rebus_messages` (`recipient = 'error'`): inspect or move them with psql
  (runbook).

## Alternatives considered
- **Rebus in-memory transport**: no table, but messages are lost when the Api stops or scales to zero (outbox entries
  are already marked as sent). Rejected.
- **Synchronous handling inside the request**: imports, campaigns and mailbox sync last longer than an HTTP request,
  and a failure would surface to the user. Rejected.
- **Send e-mails directly, keep the Worker for the rest**: does not remove the Worker or RabbitMQ. Rejected.
- **Always-on smaller RabbitMQ** (ADR 0022 §4 fallback): still a fixed cost for a component the load does not need.
- **Azure Service Bus / Storage Queues**: fixed cost or a new dependency and transport; the PostgreSQL transport is
  already in the dependency set.
