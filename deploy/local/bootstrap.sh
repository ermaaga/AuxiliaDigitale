#!/usr/bin/env bash
# First run of the local stack (deploy/compose.local.yml), idempotent: migrates the Catalog and the tenants, provisions
# the tenant `demo` with its first Administrator, registers the two client applications of the web app and a System
# user for the console. Secrets are printed once and kept in the `shared` volume (never in the repository):
#   /shared/web.env          client ids and secrets read by the web container
#   /shared/credentials.txt  how to sign in the first time (temporary password, activation token)
set -euo pipefail

auxctl() { dotnet /app/Auxilia.MigrationRunner.dll "$@"; }
# The value printed after "shown only now...): " (client secret, temporary password, activation token).
secret_of() { sed -n 's/.*shown only now[^)]*): //p' | tail -n 1; }

slug="${AUXILIA_LOCAL_TENANT:-demo}"
admin="${AUXILIA_LOCAL_ADMIN:-admin@demo.local}"
system="${AUXILIA_LOCAL_SYSTEM:-system@auxilia.local}"
origin="${AUXILIA_PUBLIC_ORIGIN:-http://localhost:3000}"
credentials=/shared/credentials.txt

auxctl migrate catalog

first_run=false
if ! auxctl tenant list | cut -f1 | grep -qx "$slug"; then
  first_run=true
  auxctl tenant provision --slug "$slug" --name "Studio Demo" --language it --time-zone Europe/Rome \
    --admin-email "$admin" --admin-first-name Anna --admin-last-name Amministratrice
else
  auxctl migrate tenants --all
fi

if [[ ! -s /shared/web.env ]]; then
  if auxctl clients list | cut -f1 | grep -qx web-bff; then
    echo "bootstrap: the client applications exist but /shared/web.env is gone: run 'docker compose -f deploy/compose.local.yml down -v' to start again" >&2
    exit 1
  fi

  web_secret="$(auxctl clients add --client-id web-bff --name "Web app (local)" --type WebBff | secret_of)"
  console_secret="$(auxctl clients add --client-id console --name "Console (local)" --type PlatformConsole | secret_of)"
  umask 077
  cat > /shared/web.env <<ENV
AUXILIA_WEB_CLIENT_ID=web-bff
AUXILIA_WEB_CLIENT_SECRET=$web_secret
AUXILIA_CONSOLE_CLIENT_ID=console
AUXILIA_CONSOLE_CLIENT_SECRET=$console_secret
ENV
fi

if [[ "$first_run" == true ]]; then
  password="$(auxctl users reset-password --tenant "$slug" --user "$admin" | secret_of)"
  token="$(auxctl platform users add --email "$system" --name "System (local)" | secret_of)"
  umask 077
  cat > "$credentials" <<TXT
Auxilia — local stack, first sign-in (generated $(date -u +%Y-%m-%dT%H:%MZ))

Tenant app      $origin/$slug/login
  user          $admin
  password      $password   (temporary: you choose a new one at the first sign-in)

Console         $origin/platform/activate
  e-mail        $system
  token         $token   (one use: set the password and enrol an authenticator app for TOTP)

New tokens/passwords: docker compose -f deploy/compose.local.yml run --rm auxctl platform users reset --email $system
                      docker compose -f deploy/compose.local.yml run --rm auxctl users reset-password --tenant $slug --user $admin
TXT
fi

echo "bootstrap: ready — sign-in details: docker compose -f deploy/compose.local.yml run --rm --entrypoint cat bootstrap /shared/credentials.txt"
cat "$credentials" 2>/dev/null || true
