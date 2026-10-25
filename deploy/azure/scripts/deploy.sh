#!/usr/bin/env bash
# Deploys one Bicep template of deploy/azure (ADR 0022, ADR 0024, runbook docs/runbooks/azure-deployment.md). The
# Infrastructure workflow runs it through scripts/provision.sh; by hand it is useful for --what-if and the satellite
# apps.
#
#   deploy/azure/scripts/deploy.sh shared [--core] [--openwa <sha>] [--agent-openwa <sha>] [--site <sha>] [--what-if]
#   deploy/azure/scripts/deploy.sh <environment> [--core] [--image <commit sha>] [--what-if]
#
# shared: the PostgreSQL administrator password is read from Key Vault; on the very first run it is generated and
#         stored there right after the deployment. RabbitMQ, Valkey, the psql job and the satellite apps are deployed
#         once scripts/secrets.sh has written their secrets (--core: never). The satellite apps (ADR 0022 §11) keep
#         their running tags; --openwa/--agent-openwa/--site release another commit of their own repositories.
# <environment>: a file environments/<name>.bicepparam. Without --image the tag currently running is kept (a template
#         change never rolls the images back); with no image at all, or with --core, only identities, Key Vault and
#         storage are deployed (existing applications are left as they are).
# The signed-in user, or the workflow's service principal, becomes Key Vault Secrets Officer of the vault; more users
# can be added with AUXILIA_EXTRA_OPERATORS (object ids separated by commas).
# Prints the outputs of the deployment (JSON, nothing secret) on standard output, everything else on standard error.
# Needs: az (logged in), bicep, python3, openssl. No secret is passed on the command line.
set -euo pipefail

# shellcheck source=lib.sh
. "$(dirname "$0")/lib.sh"

target="${1:-}"
[ $# -gt 0 ] && shift
what_if=false
core=false
image=""
openwa_tag=""
agent_openwa_tag=""
site_tag=""
while [ $# -gt 0 ]; do
  case "$1" in
    --what-if) what_if=true ;;
    --core) core=true ;;
    --image) image="${2:?--image needs a commit sha}"; shift ;;
    --openwa) openwa_tag="${2:?--openwa needs a commit sha}"; shift ;;
    --agent-openwa) agent_openwa_tag="${2:?--agent-openwa needs a commit sha}"; shift ;;
    --site) site_tag="${2:?--site needs a commit sha}"; shift ;;
    *) echo "unknown option: $1" >&2; exit 64 ;;
  esac
  shift
done

usage() {
  echo "usage: $0 shared [--core] [--openwa|--agent-openwa|--site <sha>] [--what-if]" >&2
  echo "       $0 <environment> [--core] [--image <sha>] [--what-if]   (environments: $(environment_names | tr '\n' ' '))" >&2
  exit 64
}
[ -z "$target" ] && usage

# Who writes the secrets: a user (az login) or the workflow's service principal (OIDC), plus AUXILIA_EXTRA_OPERATORS.
operators=()
automation=()
if [ "$(az account show --query user.type -o tsv)" = "user" ]; then
  operators+=("$(az ad signed-in-user show --query id -o tsv)")
else
  automation+=("$(signed_in_principal_id)")
fi
extra_operators="${AUXILIA_EXTRA_OPERATORS:-}"
for id in ${extra_operators//,/ }; do
  if ! [[ "$id" =~ ^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$ ]]; then
    echo "AUXILIA_EXTRA_OPERATORS: not an object id: $id" >&2
    exit 64
  fi
  operators+=("$id")
done
# ${a[@]+…}: empty arrays under `set -u` on the bash 3.2 of macOS.
AUXILIA_OPERATOR_OBJECT_IDS="$(json_array ${operators[@]+"${operators[@]}"})"
AUXILIA_AUTOMATION_PRINCIPAL_IDS="$(json_array ${automation[@]+"${automation[@]}"})"
export AUXILIA_OPERATOR_OBJECT_IDS AUXILIA_AUTOMATION_PRINCIPAL_IDS

generated_admin_password=""
case "$target" in
  shared)
    [ -n "$image" ] && { echo "--image belongs to an environment" >&2; exit 64; }
    # shellcheck disable=SC2046
    AUXILIA_ENVIRONMENTS="$(json_array $(environment_names))"
    export AUXILIA_ENVIRONMENTS
    AUXILIA_PG_ADMIN_PASSWORD="$(secret_value "$shared_vault" postgres-admin-password)"
    if [ -z "$AUXILIA_PG_ADMIN_PASSWORD" ]; then
      AUXILIA_PG_ADMIN_PASSWORD="$(openssl rand -hex 24)"
      generated_admin_password="$AUXILIA_PG_ADMIN_PASSWORD"
    fi
    export AUXILIA_PG_ADMIN_PASSWORD
    valkey_password="$(secret_value "$shared_vault" valkey-password)"
    if ! $core && [ -n "$valkey_password" ]; then
      export AUXILIA_DEPLOY_SHARED_SERVICES=true
    else
      export AUXILIA_DEPLOY_SHARED_SERVICES=false
      $core || echo "RabbitMQ, Valkey and the psql job are skipped until scripts/secrets.sh init has run." >&2
    fi
    AUXILIA_OPENWA_IMAGE_TAG="${openwa_tag:-$(running_tag "$shared_rg" ca-auxilia-openwa-01)}"
    AUXILIA_AGENT_OPENWA_IMAGE_TAG="${agent_openwa_tag:-$(running_tag "$shared_rg" ca-auxilia-agent-openwa-01)}"
    AUXILIA_SITE_IMAGE_TAG="${site_tag:-$(running_tag "$shared_rg" ca-auxilia-site-01)}"
    export AUXILIA_OPENWA_IMAGE_TAG AUXILIA_AGENT_OPENWA_IMAGE_TAG AUXILIA_SITE_IMAGE_TAG
    params="$azure_dir/shared.bicepparam"
    ;;
  *)
    require_environment "$target"
    [ -n "$openwa_tag$agent_openwa_tag$site_tag" ] && { echo "--openwa/--agent-openwa/--site belong to shared" >&2; exit 64; }
    if $core; then
      [ -n "$image" ] && { echo "--core deploys no image" >&2; exit 64; }
    else
      [ -z "$image" ] && image="$(running_tag "rg-auxilia-next-$target" "ca-auxilia-api-$target")"
      [ -z "$image" ] && echo "No image tag: only identities, Key Vault and storage of $target." >&2
    fi
    if [ -n "$image" ] && ! [[ "$image" =~ ^[0-9a-f]{40}$ ]]; then
      echo "--image needs a full commit SHA: $image" >&2
      exit 64
    fi
    export AUXILIA_IMAGE_TAG="$image"
    params="$azure_dir/environments/$target.bicepparam"
    ;;
esac

name="auxilia-next-$target-$(date -u +%Y%m%d%H%M%S)"
if $what_if; then
  az deployment sub what-if --name "$name" --location "$location" --parameters "$params" >&2
  exit 0
fi

status=0
echo "deploying $target ($name)..." >&2
az deployment sub create --name "$name" --location "$location" --parameters "$params" \
  --query properties.outputs --output json || status=$?

# First run: keep the generated administrator password even if a later resource of the deployment failed.
if [ -n "$generated_admin_password" ] && az keyvault show --name "$shared_vault" --output none 2>/dev/null; then
  put_secret "$shared_vault" postgres-admin-password <<< "$generated_admin_password"
  echo "PostgreSQL administrator password stored in $shared_vault/postgres-admin-password." >&2
fi

exit "$status"
