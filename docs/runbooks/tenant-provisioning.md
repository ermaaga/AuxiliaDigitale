# Runbook — tenant provisioning and migrations (`auxctl`)

`auxctl` is `backend/src/Auxilia.MigrationRunner`. Run it with `dotnet run --project backend/src/Auxilia.MigrationRunner -- <command>` (or the published binary).

## Configuration
| Setting | Meaning |
|---|---|
| `ConnectionStrings__Catalog` | Catalog database (environment or user-secrets `auxilia-api`). Required. |
| `Provisioning__AdminConnectionString` | Login with `CREATEDB` and `CREATEROLE` used to create tenant databases (D-02). Defaults to the Catalog login. |
| `ConnectionStrings__Redis` | Optional (Development: `localhost:6379`). With the same Redis as Api and Worker, suspend/reactivate/archive/provision evict the tenant lookups of every node at once; without it the nodes see the change within 30 s. |
| `ConnectionStrings__RabbitMq` | Optional for auxctl: needed by jobs that send messages (e.g. `bus.outbox`, which sends the outbox entries left pending by a RabbitMQ outage: `auxctl jobs run bus.outbox --all`). |
| `AUXILIA_TENANT_CONNECTION` | Only with `--existing-database`: connection string of a database created by a DBA. Never pass it on the command line. |

Logs go to the console and to the same daily files as Api and Worker (`logs/platform/…`, `logs/tenants/{slug}/…`).

## Commands
```bash
auxctl migrate catalog                                   # Catalog schema (never done by Api/Worker at startup)
auxctl migrate tenants --all                             # every Active, Suspended or MigrationFailed tenant
auxctl migrate tenants --tenant acme                     # one tenant: schema, then pending data-migrations
auxctl tenant provision --slug acme --name "ACME S.r.l." [--language it] [--time-zone Europe/Rome] [--existing-database]
auxctl tenant suspend|reactivate|archive --slug acme
auxctl tenant list
auxctl jobs list
auxctl jobs run <job-code> --tenant acme | --all          # manual run (D-15), recorded in ops.job_runs
auxctl keys rotate                                       # new ES256 token signing key (see "Token signing keys")
auxctl clients add --client-id web-bff --name "Tenant web" --type WebBff [--origin https://app.example.com]
auxctl clients list
auxctl platform users add --email ops@example.com --name "Operations"   (prints a one-use activation token)
auxctl platform users reset --email ops@example.com     (lost password or authenticator: new activation token)
auxctl platform users enable|disable --email ops@example.com
auxctl platform users list
auxctl users reset-password --tenant acme --user mario.rossi [--send-link]   (temporary password, or reset link)
auxctl users verify-legacy-hash     (hash and password from standard input)
auxctl diagnostics registry --output docs/log-event-registry.md
```
Exit codes: `0` success, `1` usage error, `2` failure (message `error AUX-NNNNN: …`; details in the logs).

## Provisioning steps (resumable)
1. Slug checked (3–40 lowercase letters, digits, inner hyphens; not reserved: `api`, `app`, `www`, `platform`, `admin`, …).
2. `catalog.tenants` row in `Provisioning` + plan `standard`; a `catalog.migration_runs` row.
3. Dedicated role and database `auxilia_t_<slug>` (hyphens become `_`) with a generated password; `CONNECT` revoked from `PUBLIC`, so other tenant roles cannot connect. With `--existing-database` the provided database is only checked.
4. Connection string encrypted with Data Protection (keys in the Catalog) and stored; it is never printed or logged (`***`).
5. Tenant schema migrations, initial seed, pending data-migrations; schema and data versions recorded.
6. Tenant `Active`, run `Succeeded`.

If a step fails the tenant stays `Provisioning` (database step) or becomes `MigrationFailed` (migration step) and the run is `Failed` with a code. **Re-run the same command** to resume: an existing database is reused.

Not yet: first Administrator and activation e-mail (Identity, P2), post-provisioning isolation probe via the API, provisioning from the System console (N02, through the Worker).

## Client applications
Every caller of `/api/v1/auth/token` identifies its application with header `X-Client-Id` (D-03); confidential clients
(`WebBff`, `PlatformConsole`) also send `X-Client-Secret`. `auxctl clients add` prints the generated secret **once**
(only its hash is stored): put it in the BFF's secret store right away. `Mobile` and `Integration` clients have no secret.
A second client with the same id fails with `AUX-12027`.

## Token signing keys
Access tokens are JWT ES256; the key ring lives in `catalog.signing_keys` (private keys encrypted with Data Protection,
the same key ring as the tenant connection strings). The first key is created by the API at the first token. Public keys
are published at `GET /.well-known/jwks.json`.

`auxctl keys rotate` retires the active key (still published and valid for 2 h, longer than any access token) and makes
a new one active. API nodes pick it up within 5 minutes, or immediately when a token signed with it arrives. No
scheduler (D-15): rotate on a calendar (e.g. every 90 days) or at once if a key may be compromised; after a compromise
rotate twice two hours apart, so the compromised key leaves the JWKS.

Settings `Auth:Issuer` / `Auth:Audience` (default `https://auxilia.app` / `auxilia-api`) must be the same on every API node.

## Platform (System) users
System users sign in to the console with password **and** an authenticator code (TOTP, D-22). `auxctl platform users
add` prints an activation token once (valid 72 h, only its hash is stored): give it to the person, who opens
`/platform/activate` in the web app (or `/platform/activate?token=<token>`), pastes the token, adds the account to an
authenticator app (setup key or `otpauth://` link, shown once; API `POST /api/v1/platform/auth/enrollment`) and sets a
password of at least 12 characters with the first code (`POST /api/v1/platform/auth/activate`). Sign-in is then at
`/platform/login` (e-mail, password, code). `reset` clears password and authenticator, ends every console session and
prints a new token; `disable` ends the sessions and blocks sign-in. The console needs a client application of type
`PlatformConsole` (`auxctl clients add --type PlatformConsole …`); tenant clients cannot sign in to the console and vice
versa. Five wrong attempts lock the account for 15 minutes (doubling on repeat).

## Tenant users: password reset by an operator
`auxctl users reset-password --tenant <slug> --user <user name | e-mail>` prints a temporary password once (never
stored in clear): hand it to the person over a separate channel. Every session of the user ends and the next sign-in
asks for a new password (403 `AUX-12043` → `POST /api/v1/auth/password/change`). With `--send-link` the user receives
the normal reset e-mail instead (the tenant needs an e-mail account, N03). When several users share the e-mail, use the
user name. Each reset is logged as security event `AUX-29023`. Support only: `auxctl users verify-legacy-hash` checks a
password against a legacy BCrypt hash, e.g. `printf '%s\n%s\n' "$HASH" "$PASSWORD" | auxctl users verify-legacy-hash`.

## Rate limits (API)
Section `RateLimiting` of the API configuration (defaults in code, per node, in memory):

| Rule | Default | Partition |
|---|---|---|
| `SignIn` (`POST /auth/token`) | 30 / 1 min | tenant + IP |
| `AccountLinks` (activate, password forgot/reset) | 10 / 15 min | tenant + IP |
| `User` (every request) | 600 / 1 min | tenant + user |
| `Anonymous` (every request) | 300 / 1 min | IP |
| `Client` (every request) | 5000 / 1 min | client application |

Each rule is `PermitLimit` + `Window` (e.g. `RateLimiting__SignIn__PermitLimit=50`); `RateLimiting__Enabled=false` turns
limiting off. A rejected request gets 429 `AUX-10024` with `Retry-After` and logs `AUX-29016`. Behind a reverse proxy
configure the forwarded headers, otherwise every caller shares the proxy's IP.

## Failures
| Code | Meaning | Action |
|---|---|---|
| AUX-11005 / 11017 | slug invalid / reserved | choose another slug |
| AUX-11016 | tenant already exists (not in Provisioning/MigrationFailed) | nothing to do, or use `migrate tenants` |
| AUX-11018 | `--existing-database` cannot connect | check `AUXILIA_TENANT_CONNECTION` and network |
| AUX-28005 | tenant migration failed; tenant `MigrationFailed` (suspended tenants stay suspended) | read the tenant's log file, fix, re-run `migrate tenants --tenant <slug>` |
