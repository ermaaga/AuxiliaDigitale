# Log event registry

<!-- Generated from Auxilia.Diagnostics. Do not edit by hand:
     dotnet run --project backend/src/Auxilia.MigrationRunner -- diagnostics registry --output docs/log-event-registry.md -->

Every code is shown as `AUX-NNNNN` in logs, in the `errorCode` of API responses and in the UI (ADR 0006, ADR 0012).
A new code is the current max of its range + 1; codes are never reused or renumbered.

## Ranges

| Range | Name | Owner | Codes | Next code |
|---|---|---|---|---|
| 10000–10999 | Host | Host / Startup / Middleware | 12 | 10021 |
| 11000–11999 | Tenancy | Tenancy / Catalog | 8 | 11012 |
| 12000–12999 | Identity | Identity / Auth | 0 | 12001 |
| 13000–13999 | Directory | Directory (clients, employees) | 0 | 13001 |
| 14000–14999 | Cases | Cases (services, cases, payments) | 0 | 14001 |
| 15000–15999 | Scheduling | Scheduling | 0 | 15001 |
| 16000–16999 | Documents | Documents / Storage | 0 | 16001 |
| 17000–17999 | Requests | Engagement: requests | 0 | 17001 |
| 18000–18999 | Notifications | Engagement: notifications / realtime | 0 | 18001 |
| 19000–19999 | Marketing | Marketing | 0 | 19001 |
| 20000–20999 | Configuration | Configuration | 0 | 20001 |
| 21000–21999 | Localization | Localization | 0 | 21001 |
| 22000–22999 | Imports | Imports | 0 | 22001 |
| 23000–23999 | Bus | Message bus (Rebus / RabbitMQ) | 0 | 23001 |
| 24000–24999 | Cache | Cache / Redis | 0 | 24001 |
| 25000–25999 | Messaging | Messaging (outbound channels, accounts, templates) | 0 | 25001 |
| 26000–26999 | Jobs | Worker / recurring jobs (manual runs) | 0 | 26001 |
| 27000–27999 | Audit | Audit / Reporting / Export | 0 | 27001 |
| 28000–28999 | Runner | MigrationRunner / Legacy import | 0 | 28001 |
| 29000–29999 | Security | Security events | 0 | 29001 |

## Codes

| Code | Event name | Log level | Error type | Operation | Message |
|---|---|---|---|---|---|
| AUX-10001 | Host.UnhandledException | Error | Failure | – | Unhandled exception while processing {RequestMethod} {RequestPath} |
| AUX-10010 | Host.ConcurrencyConflict | Warning | Conflict | – | Concurrency conflict in operation {Operation} |
| AUX-10011 | Host.PreconditionFailed | Warning | PreconditionFailed | – | If-Match precondition failed for {RequestMethod} {RequestPath} |
| AUX-10012 | Host.IdempotencyKeyReused | Warning | Conflict | – | Idempotency key reused with a different payload for {RequestMethod} {RequestPath} |
| AUX-10013 | Host.DatabaseTimeout | Error | Failure | – | Database command timed out in operation {Operation} |
| AUX-10014 | Host.RequestCancelled | Information | – | – | Request {RequestMethod} {RequestPath} cancelled by the client |
| AUX-10015 | Host.LogStorageUnavailable | – | – | – | – |
| AUX-10016 | Host.LogStorageRecovered | – | – | – | – |
| AUX-10017 | Host.EndpointNotFound | – | – | – | – |
| AUX-10018 | Host.MethodNotAllowed | – | – | – | – |
| AUX-10019 | Host.RequestInvalid | – | – | – | – |
| AUX-10020 | Host.ValidationFailed | – | Validation | – | – |
| AUX-11004 | Tenancy.CrossTenantAttempt | Warning | Forbidden | – | Cross-tenant attempt: claim tenant {ClaimTenant}, requested tenant {RequestedTenant} |
| AUX-11005 | Tenancy.TenantSlugInvalid | – | Validation | – | – |
| AUX-11006 | Tenancy.TenantTransitionNotAllowed | – | Conflict | – | – |
| AUX-11007 | Tenancy.CatalogValueInvalid | – | Validation | – | – |
| AUX-11008 | Tenancy.TenantRequired | – | Validation | – | – |
| AUX-11009 | Tenancy.TenantNotFound | – | NotFound | – | – |
| AUX-11010 | Tenancy.TenantUnavailable | – | – | – | – |
| AUX-11011 | Tenancy.TenantSuspended | – | – | – | – |
