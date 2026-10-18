#!/usr/bin/env bash
# Creates or updates the ACR Tasks that build the images (ADR 0022 §7, §11).
#
#   deploy/azure/scripts/acr-task.sh < pat.txt
#       Auxilia: one task per branch (main, qa) building the four images of deploy/azure/acr-task.yaml.
#   deploy/azure/scripts/acr-task.sh satellite <openwa|agent-openwa|site> <owner/repo> [branch] [dockerfile] [context] < pat.txt
#       Satellite app built from its own repository: image auxilia/<app>:<commit sha> (+ :<branch>) on every push to
#       the branch (default main); context = folder of the repository (default the root), dockerfile relative to the
#       context (default Dockerfile). Released with deploy.sh shared --<app> <sha>.
#
# Standard input: a GitHub fine-grained personal access token limited to the repository with "Contents: read" and
# "Webhooks: read and write" (the task creates the commit webhook). It is kept by the task only.
set -euo pipefail

registry="acrauxilianext01"

read_token() {
  [ -t 0 ] && echo "paste the GitHub token, then Ctrl-D:" >&2
  token="$(cat)"
  [ -z "$token" ] && { echo "empty token" >&2; exit 64; }
  return 0
}

# create_or_update <task> <options…>: the token is passed as an argument of az only (never echoed).
create_or_update() {
  local task="$1"
  shift
  if az acr task show --registry "$registry" --name "$task" --output none 2>/dev/null; then
    az acr task update --registry "$registry" --name "$task" "$@" --git-access-token "$token"
    echo "updated $task"
  else
    az acr task create --registry "$registry" --name "$task" "$@" --platform linux/amd64 --git-access-token "$token"
    echo "created $task"
  fi
}

common=(
  --commit-trigger-enabled true
  --pull-request-trigger-enabled false
  --base-image-trigger-enabled false
  --timeout 3600
  --output none
)

case "${1:-}" in
  "")
    read_token
    for branch in main qa; do
      create_or_update "auxilia-build-$branch" "${common[@]}" \
        --file deploy/azure/acr-task.yaml \
        --context "https://github.com/ermaaga/AuxiliaDigitale.git#$branch"
    done
    ;;
  satellite)
    app="${2:-}"
    case "$app" in openwa|agent-openwa|site) ;; *) echo "app: openwa, agent-openwa or site" >&2; exit 64 ;; esac
    repository="${3:?owner/repo of the GitHub repository}"
    branch="${4:-main}"
    dockerfile="${5:-Dockerfile}"
    context="${6:-}"
    read_token
    create_or_update "$app-build-$branch" "${common[@]}" \
      --file "$dockerfile" \
      --context "https://github.com/$repository.git#$branch${context:+:$context}" \
      --image "auxilia/$app:{{.Run.Commit}}" \
      --image "auxilia/$app:{{.Run.Branch}}"
    ;;
  *)
    echo "usage: $0 < pat.txt | $0 satellite <openwa|agent-openwa|site> <owner/repo> [branch] [dockerfile] [context] < pat.txt" >&2
    exit 64
    ;;
esac
