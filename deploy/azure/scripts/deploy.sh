#!/usr/bin/env bash
# Deploys the Bicep templates of deploy/azure (ADR 0022, runbook docs/runbooks/azure-deployment.md).
#
#   deploy/azure/scripts/deploy.sh shared [--openwa <sha>] [--agent-openwa <sha>] [--site <sha>] [--what-if]
#   deploy/azure/scripts/deploy.sh qa|prod [--image <commit sha>] [--what-if]
#
# shared: the PostgreSQL administrator password is read from Key Vault; on the very first run it is generated and
#         stored there right after the deployment. RabbitMQ, Valkey and the psql job are deployed once
#         scripts/secrets.sh has written their secrets. The satellite apps (ADR 0022 §11) keep their running tags;
#         --openwa/--agent-openwa/--site release another commit of their own repositories.
# qa|prod: without --image the tag currently running is kept (a template change never rolls the images back); with
#         no image at all only identities, Key Vault and storage are deployed.
# Needs: az (logged in, Owner of the subscription), bicep. No secret is passed on the command line.
set -euo pipefail

here="$(cd "$(dirname "$0")/.." && pwd)"
location="italynorth"
shared_vault="kv-auxilia-next-shared"

target="${1:-}"
[ $# -gt 0 ] && shift
what_if=false
image=""
openwa_tag=""
agent_openwa_tag=""
site_tag=""
while [ $# -gt 0 ]; do
  case "$1" in
    --what-if) what_if=true ;;
    --image) image="${2:?--image needs a commit sha}"; shift ;;
    --openwa) openwa_tag="${2:?--openwa needs a commit sha}"; shift ;;
    --agent-openwa) agent_openwa_tag="${2:?--agent-openwa needs a commit sha}"; shift ;;
    --site) site_tag="${2:?--site needs a commit sha}"; shift ;;
    *) echo "unknown option: $1" >&2; exit 64 ;;
  esac
  shift
done

# Tag of the image a container app runs now (empty when the app does not exist).
running_tag() {
  az containerapp show -g "$1" -n "$2" --query 'properties.template.containers[0].image' -o tsv 2>/dev/null \
    | sed 's/.*://' || true
}

secret() { az keyvault secret show --vault-name "$1" --name "$2" --query value -o tsv 2>/dev/null || true; }

# Writes a secret without putting its value on a command line.
store_secret() {
  local vault="$1" name="$2" value="$3" file attempt
  file="$(mktemp)"
  printf '%s' "$value" > "$file"
  # The Secrets Officer role of a new vault may take a few minutes to apply.
  for attempt in 1 2 3 4 5 6 7 8 9 10; do
    if az keyvault secret set --vault-name "$vault" --name "$name" --file "$file" --encoding utf-8 --output none 2>/dev/null; then
      rm -f "$file"
      return 0
    fi
    echo "waiting for access to $vault ($attempt/10)..." >&2
    sleep 30
  done
  rm -f "$file"
  return 1
}

AUXILIA_OPERATOR_OBJECT_ID="$(az ad signed-in-user show --query id -o tsv)"
export AUXILIA_OPERATOR_OBJECT_ID

generated_admin_password=""
case "$target" in
  shared)
    AUXILIA_PG_ADMIN_PASSWORD="$(secret "$shared_vault" postgres-admin-password)"
    if [ -z "$AUXILIA_PG_ADMIN_PASSWORD" ]; then
      AUXILIA_PG_ADMIN_PASSWORD="$(openssl rand -hex 24)"
      generated_admin_password="$AUXILIA_PG_ADMIN_PASSWORD"
    fi
    export AUXILIA_PG_ADMIN_PASSWORD
    if [ -n "$(secret "$shared_vault" valkey-password)" ]; then
      export AUXILIA_DEPLOY_SHARED_SERVICES=true
    else
      export AUXILIA_DEPLOY_SHARED_SERVICES=false
      echo "RabbitMQ, Valkey and the psql job are skipped until scripts/secrets.sh init has run." >&2
    fi
    AUXILIA_OPENWA_IMAGE_TAG="${openwa_tag:-$(running_tag rg-auxilia-next-shared ca-auxilia-openwa-01)}"
    AUXILIA_AGENT_OPENWA_IMAGE_TAG="${agent_openwa_tag:-$(running_tag rg-auxilia-next-shared ca-auxilia-agent-openwa-01)}"
    AUXILIA_SITE_IMAGE_TAG="${site_tag:-$(running_tag rg-auxilia-next-shared ca-auxilia-site-01)}"
    export AUXILIA_OPENWA_IMAGE_TAG AUXILIA_AGENT_OPENWA_IMAGE_TAG AUXILIA_SITE_IMAGE_TAG
    params="$here/shared.bicepparam"
    ;;
  qa|prod)
    [ -n "$openwa_tag$agent_openwa_tag$site_tag" ] && { echo "--openwa/--agent-openwa/--site belong to shared" >&2; exit 64; }
    [ -z "$image" ] && image="$(running_tag "rg-auxilia-next-$target" "ca-auxilia-api-$target")"
    [ -z "$image" ] && echo "No image tag: only identities, Key Vault and storage of $target." >&2
    export AUXILIA_IMAGE_TAG="$image"
    params="$here/$target.bicepparam"
    ;;
  *)
    echo "usage: $0 shared [--openwa|--agent-openwa|--site <sha>] [--what-if] | qa|prod [--image <sha>] [--what-if]" >&2
    exit 64
    ;;
esac

name="auxilia-next-$target-$(date -u +%Y%m%d%H%M%S)"
if $what_if; then
  az deployment sub what-if --name "$name" --location "$location" --parameters "$params"
  exit 0
fi

status=0
az deployment sub create --name "$name" --location "$location" --parameters "$params" --output none || status=$?

# First run: keep the generated administrator password even if a later resource of the deployment failed.
if [ -n "$generated_admin_password" ] && [ -n "$(az keyvault show --name "$shared_vault" --query name -o tsv 2>/dev/null || true)" ]; then
  store_secret "$shared_vault" postgres-admin-password "$generated_admin_password"
  echo "PostgreSQL administrator password stored in $shared_vault/postgres-admin-password." >&2
fi

exit "$status"
