#!/usr/bin/env bash
# Creates or updates the ACR Tasks that build the images on every push to main and to qa (ADR 0022 §7).
#
#   deploy/azure/scripts/acr-task.sh < pat.txt
#
# Standard input: a GitHub fine-grained personal access token limited to ermaaga/AuxiliaDigitale with
# "Contents: read" and "Webhooks: read and write" (the task creates the commit webhook). It is kept by the task only.
set -euo pipefail

registry="acrauxilianext01"
repository="https://github.com/ermaaga/AuxiliaDigitale.git"

[ -t 0 ] && echo "paste the GitHub token, then Ctrl-D:" >&2
token="$(cat)"
[ -z "$token" ] && { echo "empty token" >&2; exit 64; }

for branch in main qa; do
  task="auxilia-build-$branch"
  options=(
    --registry "$registry" --name "$task"
    --file deploy/azure/acr-task.yaml
    --context "$repository#$branch"
    --commit-trigger-enabled true
    --pull-request-trigger-enabled false
    --base-image-trigger-enabled false
    --timeout 3600
    --output none
  )
  if az acr task show --registry "$registry" --name "$task" --output none 2>/dev/null; then
    az acr task update "${options[@]}" --git-access-token "$token"
    echo "updated $task"
  else
    az acr task create "${options[@]}" --platform linux/amd64 --git-access-token "$token"
    echo "created $task"
  fi
done
