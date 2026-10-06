# deploy

Compose files and, with task H-03 (once hosting is decided, D-12), the production images and hosting configuration.
No secrets in this folder: configuration comes from environment variables or a secret store.

| File | What it starts | When |
|---|---|---|
| `compose.dev.yml` | Postgres, Valkey, RabbitMQ (project `auxilia-dev`, ports 5432/6379/5672/15672) | development: Api, Worker and Web run on the host (`dotnet run`, `pnpm dev`), E2E |
| `compose.local.yml` | everything: infrastructure + API + Worker + web app/console + Mailpit (project `auxilia-local`) | trying the application in a browser |

## The whole application (`compose.local.yml`)

```bash
docker compose -f deploy/compose.local.yml up -d --build          # first run: images built, a few minutes
docker compose -f deploy/compose.local.yml logs bootstrap         # first sign-in details
```

| Address | What |
|---|---|
| http://localhost:3000/demo/login | tenant app of the tenant `demo` |
| http://localhost:3000/platform/activate | console: activate the System user (password + TOTP), then http://localhost:3000/platform/login |
| http://localhost:5080/scalar | API (Scalar UI, Development) |
| http://localhost:8025 | Mailpit: every e-mail the application sends |
| http://localhost:15673 | RabbitMQ (user `auxilia`, password `auxilia-dev`) |
| localhost:5433 | Postgres (`auxilia` / `auxilia-dev`; Catalog `auxilia_catalog`, tenant `auxilia_t_demo`) |

The one-shot `bootstrap` service (`local/bootstrap.sh`, idempotent, runs at every `up`) migrates the Catalog and the
tenants. On the first run it also:
- provisions `demo` (Italian, Europe/Rome) with the Administrator `admin@demo.local`, signing in with a temporary password;
- registers the client applications `web-bff` and `console`, whose secrets stay in the `shared` volume (`/shared/web.env`, read by the web container);
- adds the System user `system@auxilia.local` with a one-use activation token.

To see the sign-in details again: `docker compose -f deploy/compose.local.yml run --rm --entrypoint cat bootstrap /shared/credentials.txt`.

**E-mails.** Sending accounts are tenant data. To receive invitations, password resets, notifications and campaigns in
Mailpit, open the console, choose `demo` → Messaging, and add an account: provider `smtp`, host `mailpit`, port `1025`,
security `None`, any from-address. Make it the default.

**auxctl** on demand, for example `docker compose -f deploy/compose.local.yml run --rm auxctl tenant list`. To get a new
token or password:
- `run --rm auxctl platform users reset --email system@auxilia.local`
- `run --rm auxctl users reset-password --tenant demo --user admin@demo.local`

**After a code change:** `docker compose -f deploy/compose.local.yml up -d --build`. The data stays in the volumes.

**Start again from zero:** `docker compose -f deploy/compose.local.yml down -v` (removes database, files, logs and the secrets).

Notes:
- The images (`backend/Dockerfile` targets `api`/`worker`/`auxctl`, `frontend/Dockerfile`) are for this machine: Development environment, local storage and logs in volumes. Production images come with H-03.
- The CI workflow `.github/workflows/local-stack.yml` builds and starts this stack on every change to it, and signs in through the web app.
- `compose.dev.yml` and `compose.local.yml` can run together: different projects, volumes and host ports.
