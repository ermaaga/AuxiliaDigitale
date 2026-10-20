#!/usr/bin/env bash
# Provisions or updates the shared resources and one environment (ADR 0024, runbook docs/runbooks/azure-deployment.md).
# Run by the Infrastructure workflow (.github/workflows/infrastructure.yml); it can also run from a shell with the same
# rights. Idempotent: the same command creates a new environment, applies a change of the Bicep templates, rotates the
# RabbitMQ definitions after a new environment, or completes a run that stopped halfway.
#
#   deploy/azure/scripts/provision.sh <environment> [--image <commit sha>] [--what-if]
#
# <environment>: a file environments/<name>.bicepparam. --image: images to run (normally only for the first deployment
# of the applications; later the releases of deploy.yml change them); without it the running images are kept.
# Steps: 1. shared core (network, Container Apps environment, Postgres, registry, Key Vault)
#        2. shared secrets (passwords of every environment, RabbitMQ definitions)
#        3. shared services (RabbitMQ, Valkey, psql job, satellite apps)
#        4. environment core (identities, Key Vault, storage)
#        5. environment secrets (connection strings, ALTCHA key, placeholders of the client secrets)
#        6. Postgres logins and Catalog databases (job caj-auxilia-psql-01, every environment)
#        7. applications of the environment (only with --image or when they already run)
# Writes a summary to $GITHUB_STEP_SUMMARY when set.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=lib.sh
. "$here/lib.sh"

env="${1:-}"
[ -z "$env" ] && { echo "usage: $0 <environment> [--image <sha>] [--what-if]" >&2; exit 64; }
shift
image=""
what_if=false
while [ $# -gt 0 ]; do
  case "$1" in
    --image) image="${2:?--image needs a commit sha}"; shift ;;
    --what-if) what_if=true ;;
    *) echo "unknown option: $1" >&2; exit 64 ;;
  esac
  shift
done
require_environment "$env"
if [ -n "$image" ] && ! [[ "$image" =~ ^[0-9a-f]{40}$ ]]; then
  echo "--image needs a full commit SHA: $image" >&2
  exit 64
fi

# Collapsible groups in the GitHub Actions log, plain titles in a shell.
step() { if [ -n "${GITHUB_ACTIONS:-}" ]; then echo "::group::$*"; else echo "== $*" >&2; fi; }
end_step() { if [ -n "${GITHUB_ACTIONS:-}" ]; then echo "::endgroup::"; fi; }

if $what_if; then
  step "What-if: shared"
  "$here/deploy.sh" shared --what-if
  end_step
  step "What-if: $env"
  if az group show --name "rg-auxilia-next-$env" --output none 2>/dev/null; then
    "$here/deploy.sh" "$env" ${image:+--image "$image"} --what-if
  else
    echo "rg-auxilia-next-$env does not exist yet: the run creates the whole environment." >&2
    "$here/deploy.sh" "$env" --core --what-if
  fi
  end_step
  exit 0
fi

step "1/7 Shared core"
"$here/deploy.sh" shared --core > /dev/null
end_step

step "2/7 Shared secrets"
"$here/secrets.sh" init shared
end_step

step "3/7 Shared services"
"$here/deploy.sh" shared > /dev/null
end_step

step "4/7 Core of $env"
outputs="$("$here/deploy.sh" "$env" --core)"
end_step

step "5/7 Secrets of $env"
"$here/secrets.sh" init "$env"
end_step

step "6/7 Postgres logins and Catalog databases"
run_job "$shared_rg" caj-auxilia-psql-01
end_step

step "7/7 Applications of $env"
running="$(running_tag "rg-auxilia-next-$env" "ca-auxilia-api-$env")"
if [ -n "$image" ] || [ -n "$running" ]; then
  outputs="$("$here/deploy.sh" "$env" ${image:+--image "$image"})"
  apps="${image:-$running}"
else
  echo "no image: the applications of $env are deployed by a run with --image <sha> (images built by the ACR Task)" >&2
  apps=""
fi
end_step

output() { python3 -I -c 'import json, sys; print(json.load(sys.stdin)[sys.argv[1]]["value"])' "$1" <<< "$outputs"; }
client_id="$(output deployClientId)"
github_environment="$(output githubEnvironment)"
api_url="$(output apiUrl)"
web_url="$(output webUrl)"

summary="$(cat <<EOF
## Infrastructure: $env

| | |
|---|---|
| Resource group | \`rg-auxilia-next-$env\` |
| Applications | ${apps:+\`$apps\`}${apps:-not deployed (run again with an image SHA)} |
| API | $api_url |
| Web | $web_url |
| GitHub environment of the releases | \`$github_environment\` |
| Its variable \`AZURE_CLIENT_ID\` | \`$client_id\` |

Releases (deploy.yml) need the GitHub environment \`$github_environment\` with the variable \`AZURE_CLIENT_ID\` above.
First applications of a new environment: runbook docs/runbooks/azure-deployment.md, "New environment".
EOF
)"
echo "$summary" >&2
if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
  echo "$summary" >> "$GITHUB_STEP_SUMMARY"
fi
