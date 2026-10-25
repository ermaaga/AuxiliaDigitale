#!/usr/bin/env bash
# Secrets of Auxilia Next in Key Vault (ADR 0022 §5, ADR 0024, runbook docs/runbooks/azure-deployment.md).
#
#   deploy/azure/scripts/secrets.sh init [shared|<environment>]...
#       Idempotent. shared: generates the missing passwords of the shared services and of every environment
#       (environments/*.bicepparam; existing values are kept) and writes the RabbitMQ definitions when the set of users
#       or passwords changed (then restarts RabbitMQ). <environment>: composes its connection strings and generates
#       its ALTCHA key (its Key Vault must exist: scripts/deploy.sh <environment> first). Without arguments: shared and
#       every environment whose Key Vault exists. Run by scripts/provision.sh; after rotating a password by hand, run it
#       again and restart the revisions (Key Vault references are read at revision start).
#   deploy/azure/scripts/secrets.sh set <shared|environment> <secret-name>
#       Writes one secret read from standard input (e.g. web-client-secret printed by `auxctl clients add`):
#       printf '%s' "$SECRET" | deploy/azure/scripts/secrets.sh set qa web-client-secret
#
# Values never appear on a command line or in the output. A secret is written only when its value changes.
set -euo pipefail

# shellcheck source=lib.sh
. "$(dirname "$0")/lib.sh"

cae="cae-auxilia-next-01"
pg_server="psql-auxilia-next-01"
rabbitmq_app="ca-auxilia-rabbitmq-01"
valkey_app="ca-auxilia-valkey-01"

vault_of() { [ "$1" = "shared" ] && echo "$shared_vault" || echo "kv-auxilia-next-$1"; }

# Keeps an existing value; otherwise stores a new random one (hex: safe in every connection string format).
ensure_random() {
  local vault="$1" name="$2" current
  current="$(secret_value "$vault" "$name")"
  [ -n "$current" ] && return 0
  put_secret "$vault" "$name" < <(openssl rand -hex 24)
}

ensure_placeholder() {
  local vault="$1" name="$2" current
  current="$(secret_value "$vault" "$name")"
  [ -n "$current" ] && return 0
  put_secret "$vault" "$name" <<< "pending"
  echo "placeholder $vault/$name (set it with: secrets.sh set ... $name)" >&2
}

# Every environment has its own Valkey database (ADR 0022 §2): stop when two parameter files share one.
check_valkey_databases() {
  local env database seen=" "
  for env in $(environment_names); do
    database="$(environment_param "$env" valkeyDatabase)"
    if ! [[ "$database" =~ ^[0-9]+$ ]] || [ "$database" -gt 15 ]; then
      echo "environments/$env.bicepparam: valkeyDatabase must be 0-15" >&2
      exit 1
    fi
    case "$seen" in *" $database "*) echo "valkeyDatabase $database is used by two environments" >&2; exit 1 ;; esac
    seen="$seen$database "
  done
}

# RabbitMQ users (password hashes, never the passwords), one vhost per environment and permissions, loaded at every
# broker start. Input on standard input: "admin <password>" then one "<environment> <password>" per line.
rabbitmq_definitions() {
  python3 -I -c '
import base64, hashlib, json, os, sys

def password_hash(password):
    salt = os.urandom(4)
    return base64.b64encode(salt + hashlib.sha256(salt + password.encode()).digest()).decode()

def user(name, password, tags):
    return {"name": name, "password_hash": password_hash(password),
            "hashing_algorithm": "rabbit_password_hashing_sha256", "tags": tags, "limits": {}}

def permission(name, vhost):
    return {"user": name, "vhost": vhost, "configure": ".*", "write": ".*", "read": ".*"}

lines = [line.split(" ", 1) for line in sys.stdin.read().splitlines() if line]
admin = lines[0][1]
environments = lines[1:]
print(json.dumps({
    "users": [user("auxilia_admin", admin, ["administrator"])]
             + [user("auxilia_" + env, password, []) for env, password in environments],
    "vhosts": [{"name": env} for env, _ in environments],
    "permissions": [permission("auxilia_admin", env) for env, _ in environments]
                   + [permission("auxilia_" + env, env) for env, _ in environments],
}))'
}

init_shared() {
  local name env input fingerprint current
  check_valkey_databases
  # postgres-admin-password is written by deploy.sh shared together with the server, never here.
  for name in rabbitmq-admin-password valkey-password openwa-api-key; do
    ensure_random "$shared_vault" "$name"
  done
  for env in $(environment_names); do
    ensure_random "$shared_vault" "postgres-$env-password"
    ensure_random "$shared_vault" "rabbitmq-$env-password"
  done

  # The definitions carry salted hashes (a new value at every generation): they are written again only when the users
  # or their passwords changed, recognised by a fingerprint kept as a tag of the secret.
  input="admin $(secret_value "$shared_vault" rabbitmq-admin-password)"
  for env in $(environment_names); do
    input="$input"$'\n'"$env $(secret_value "$shared_vault" "rabbitmq-$env-password")"
  done
  fingerprint="$(printf '%s' "$input" | openssl dgst -sha256 -r | cut -d' ' -f1)"
  current="$(az keyvault secret show --vault-name "$shared_vault" --name rabbitmq-definitions --query tags.fingerprint -o tsv 2>/dev/null || true)"
  if [ "$current" != "$fingerprint" ]; then
    put_secret "$shared_vault" rabbitmq-definitions --tags "fingerprint=$fingerprint" < <(rabbitmq_definitions <<< "$input")
    # The broker loads the definitions only when it starts.
    restart_app "$shared_rg" "$rabbitmq_app"
  fi
}

init_environment() {
  local env="$1" vault domain pg_host database value
  require_environment "$env"
  vault="$(vault_of "$env")"
  if ! az keyvault show --name "$vault" --output none 2>/dev/null; then
    echo "skip $env: $vault does not exist (deploy.sh $env first)" >&2
    return 0
  fi

  domain="$(az containerapp env show -g "$shared_rg" -n "$cae" --query properties.defaultDomain -o tsv)"
  pg_host="$(az postgres flexible-server show -g "$shared_rg" -n "$pg_server" --query fullyQualifiedDomainName -o tsv)"
  # Valkey: one database and one channel prefix per environment (ADR 0022 §2).
  database="$(environment_param "$env" valkeyDatabase)"

  value="$(secret_value "$shared_vault" "postgres-$env-password")"
  [ -z "$value" ] && { echo "missing $shared_vault/postgres-$env-password: run secrets.sh init shared" >&2; exit 1; }
  put_secret "$vault" catalog-connection < <(printf 'Host=%s;Database=auxilia_catalog_%s;Username=auxilia_%s;Password=%s;SSL Mode=Require' \
    "$pg_host" "$env" "$env" "$value")
  value="$(secret_value "$shared_vault" valkey-password)"
  put_secret "$vault" redis-connection < <(printf '%s.internal.%s:6379,password=%s,defaultDatabase=%s,channelPrefix=%s:,abortConnect=false' \
    "$valkey_app" "$domain" "$value" "$database" "$env")
  value="$(secret_value "$shared_vault" "rabbitmq-$env-password")"
  put_secret "$vault" rabbitmq-connection < <(printf 'amqp://auxilia_%s:%s@%s.internal.%s:5672/%s' \
    "$env" "$value" "$rabbitmq_app" "$domain" "$env")
  value="$(az storage account show-connection-string -g "rg-auxilia-next-$env" -n "stauxilianext$env" --query connectionString -o tsv)"
  put_secret "$vault" storage-connection <<< "$value"

  value="$(secret_value "$vault" altcha-key)"
  [ -z "$value" ] && put_secret "$vault" altcha-key < <(openssl rand -base64 64 | tr -d '\n')
  ensure_placeholder "$vault" web-client-secret
  ensure_placeholder "$vault" console-client-secret
  echo "secrets of $env ready" >&2
}

case "${1:-}" in
  init)
    shift
    if [ $# -eq 0 ]; then
      init_shared
      for env in $(environment_names); do init_environment "$env"; done
    fi
    for target in "$@"; do
      if [ "$target" = "shared" ]; then init_shared; else init_environment "$target"; fi
    done
    ;;
  set)
    env="${2:?environment: shared or one of $(environment_names | tr '\n' ' ')}"
    name="${3:?secret name}"
    [ "$env" = "shared" ] || require_environment "$env"
    [ -t 0 ] && echo "reading the value of $name from standard input (end with Ctrl-D)..." >&2
    # Written as given, even when unchanged (explicit request).
    file="$(mktemp)"
    cat > "$file"
    status=0
    az keyvault secret set --vault-name "$(vault_of "$env")" --name "$name" --file "$file" --encoding utf-8 --output none || status=$?
    rm -f "$file"
    [ "$status" -eq 0 ] && echo "written $(vault_of "$env")/$name"
    exit "$status"
    ;;
  *)
    echo "usage: $0 init [shared|<environment>]... | set <shared|environment> <secret-name>   (value from standard input)" >&2
    exit 64
    ;;
esac
