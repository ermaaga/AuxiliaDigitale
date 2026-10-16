#!/usr/bin/env bash
# Secrets of Auxilia Next in Key Vault (ADR 0022 §5, runbook docs/runbooks/azure-deployment.md).
#
#   deploy/azure/scripts/secrets.sh init
#       Generates the missing passwords (kept when they exist), writes the RabbitMQ definitions and composes the
#       connection strings of every environment whose Key Vault exists. Run again after adding an environment or
#       rotating a password; restart the revisions afterwards (Key Vault references are read at revision start).
#   deploy/azure/scripts/secrets.sh set <qa|prod|shared> <secret-name>
#       Writes one secret read from standard input (e.g. web-client-secret printed by `auxctl clients add`):
#       printf '%s' "$SECRET" | deploy/azure/scripts/secrets.sh set qa web-client-secret
#
# Values never appear on a command line or in the output.
set -euo pipefail

shared_rg="rg-auxilia-next-shared"
shared_vault="kv-auxilia-next-shared"
cae="cae-auxilia-next-01"
pg_server="psql-auxilia-next-01"
rabbitmq_app="ca-auxilia-rabbitmq-01"
valkey_app="ca-auxilia-valkey-01"

vault_of() { [ "$1" = "shared" ] && echo "$shared_vault" || echo "kv-auxilia-next-$1"; }

secret() { az keyvault secret show --vault-name "$1" --name "$2" --query value -o tsv 2>/dev/null || true; }

store() {
  local vault="$1" name="$2" file status=0
  file="$(mktemp)"
  cat > "$file"
  az keyvault secret set --vault-name "$vault" --name "$name" --file "$file" --encoding utf-8 --output none || status=$?
  rm -f "$file"
  return "$status"
}

# Keeps an existing value; otherwise stores a new random one (hex: safe in every connection string format).
ensure_random() {
  local vault="$1" name="$2"
  if [ -z "$(secret "$vault" "$name")" ]; then
    openssl rand -hex 24 | tr -d '\n' | store "$vault" "$name"
    echo "generated $vault/$name"
  fi
}

ensure_placeholder() {
  local vault="$1" name="$2"
  if [ -z "$(secret "$vault" "$name")" ]; then
    printf 'pending' | store "$vault" "$name"
    echo "placeholder $vault/$name (set it with: secrets.sh set ... $name)"
  fi
}

# RabbitMQ users (password hashes, never the passwords), vhosts and permissions, loaded at every broker start.
rabbitmq_definitions() {
  RABBIT_ADMIN="$(secret "$shared_vault" rabbitmq-admin-password)" \
  RABBIT_PROD="$(secret "$shared_vault" rabbitmq-prod-password)" \
  RABBIT_QA="$(secret "$shared_vault" rabbitmq-qa-password)" \
  python3 -I -c '
import base64, hashlib, json, os

def password_hash(password):
    salt = os.urandom(4)
    return base64.b64encode(salt + hashlib.sha256(salt + password.encode()).digest()).decode()

def user(name, variable, tags):
    return {"name": name, "password_hash": password_hash(os.environ[variable]),
            "hashing_algorithm": "rabbit_password_hashing_sha256", "tags": tags, "limits": {}}

def permission(name, vhost):
    return {"user": name, "vhost": vhost, "configure": ".*", "write": ".*", "read": ".*"}

print(json.dumps({
    "users": [user("auxilia_admin", "RABBIT_ADMIN", ["administrator"]),
              user("auxilia_prod", "RABBIT_PROD", []),
              user("auxilia_qa", "RABBIT_QA", [])],
    "vhosts": [{"name": "prod"}, {"name": "qa"}],
    "permissions": [permission("auxilia_admin", "prod"), permission("auxilia_admin", "qa"),
                    permission("auxilia_prod", "prod"), permission("auxilia_qa", "qa")],
}))'
}

init_shared() {
  local name
  for name in postgres-prod-password postgres-qa-password rabbitmq-admin-password rabbitmq-prod-password \
    rabbitmq-qa-password valkey-password; do
    ensure_random "$shared_vault" "$name"
  done
  rabbitmq_definitions | store "$shared_vault" rabbitmq-definitions
  echo "written $shared_vault/rabbitmq-definitions"
}

init_environment() {
  local env="$1" vault domain pg_host database storage
  vault="$(vault_of "$env")"
  if [ -z "$(az keyvault show --name "$vault" --query name -o tsv 2>/dev/null || true)" ]; then
    echo "skip $env: $vault does not exist (deploy.sh $env first)"
    return
  fi

  domain="$(az containerapp env show -g "$shared_rg" -n "$cae" --query properties.defaultDomain -o tsv)"
  pg_host="$(az postgres flexible-server show -g "$shared_rg" -n "$pg_server" --query fullyQualifiedDomainName -o tsv)"
  # Valkey: one database and one channel prefix per environment (ADR 0022 §2).
  [ "$env" = "prod" ] && database=0 || database=1

  printf 'Host=%s;Database=auxilia_catalog_%s;Username=auxilia_%s;Password=%s;SSL Mode=Require' \
    "$pg_host" "$env" "$env" "$(secret "$shared_vault" "postgres-$env-password")" | store "$vault" catalog-connection
  printf '%s.internal.%s:6379,password=%s,defaultDatabase=%s,channelPrefix=%s:,abortConnect=false' \
    "$valkey_app" "$domain" "$(secret "$shared_vault" valkey-password)" "$database" "$env" | store "$vault" redis-connection
  printf 'amqp://auxilia_%s:%s@%s.internal.%s:5672/%s' \
    "$env" "$(secret "$shared_vault" "rabbitmq-$env-password")" "$rabbitmq_app" "$domain" "$env" | store "$vault" rabbitmq-connection
  storage="stauxilianext$env"
  az storage account show-connection-string -g "rg-auxilia-next-$env" -n "$storage" --query connectionString -o tsv \
    | tr -d '\n' | store "$vault" storage-connection
  echo "written $vault/{catalog,redis,rabbitmq,storage}-connection"

  if [ -z "$(secret "$vault" altcha-key)" ]; then
    openssl rand -base64 48 | tr -d '\n' | store "$vault" altcha-key
    echo "generated $vault/altcha-key"
  fi
  ensure_placeholder "$vault" web-client-secret
  ensure_placeholder "$vault" console-client-secret
}

case "${1:-}" in
  init)
    init_shared
    init_environment qa
    init_environment prod
    ;;
  set)
    env="${2:?environment: qa, prod or shared}"
    name="${3:?secret name}"
    case "$env" in qa|prod|shared) ;; *) echo "unknown environment: $env" >&2; exit 64 ;; esac
    [ -t 0 ] && echo "reading the value of $name from standard input (end with Ctrl-D)..." >&2
    store "$(vault_of "$env")" "$name"
    echo "written $(vault_of "$env")/$name"
    ;;
  *)
    echo "usage: $0 init | set <qa|prod|shared> <secret-name>   (value from standard input)" >&2
    exit 64
    ;;
esac
