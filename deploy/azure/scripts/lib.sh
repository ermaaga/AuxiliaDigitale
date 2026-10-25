# Helpers shared by the scripts of deploy/azure/scripts (sourced, never run). ADR 0022, ADR 0024.
# shellcheck shell=bash

azure_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
location="italynorth"
shared_rg="rg-auxilia-next-shared"
shared_vault="kv-auxilia-next-shared"

# Every environment: one file environments/<name>.bicepparam.
environment_names() {
  local file
  for file in "$azure_dir"/environments/*.bicepparam; do
    [ -e "$file" ] && basename "$file" .bicepparam
  done
  return 0
}

# Stops unless <name> is a valid environment name with its parameter file. 9 characters at most: kv-auxilia-next-<name>
# must fit the 24 characters of a Key Vault name; "shared" would collide with kv-auxilia-next-shared.
require_environment() {
  local env="$1"
  if ! [[ "$env" =~ ^[a-z][a-z0-9]{1,8}$ ]] || [ "$env" = "shared" ]; then
    echo "invalid environment name: '$env' (2-9 lowercase letters or digits, starting with a letter, not 'shared')" >&2
    exit 64
  fi
  if [ ! -f "$azure_dir/environments/$env.bicepparam" ]; then
    echo "unknown environment $env: add deploy/azure/environments/$env.bicepparam first" >&2
    exit 64
  fi
}

# Literal value of `param <name> = <value>` in environments/<env>.bicepparam (numbers and quoted strings).
environment_param() {
  sed -n -E "s/^param $2 = '?([^']*)'?[[:space:]]*$/\1/p" "$azure_dir/environments/$1.bicepparam"
}

# JSON array of plain strings (names and object ids only: no quoting needed).
json_array() {
  local out="" item
  for item in "$@"; do
    out="$out${out:+,}\"$item\""
  done
  printf '[%s]' "$out"
}

# Value of a secret, empty when the vault or the secret does not exist. Any other error (typically a role assignment
# that is not active yet) is retried and then fails with status 2, so a missing permission never looks like a missing
# secret: that would generate a new password over the existing one. Assign the result to a variable (`v="$(…)"`) so
# that `set -e` stops the script on a failure.
secret_value() {
  local vault="$1" name="$2" err value attempt
  if ! az keyvault show --name "$vault" --output none 2>/dev/null; then
    return 0
  fi
  err="$(mktemp)"
  for attempt in 1 2 3 4 5 6 7 8 9 10; do
    if value="$(az keyvault secret show --vault-name "$vault" --name "$name" --query value -o tsv 2>"$err")"; then
      rm -f "$err"
      printf '%s' "$value"
      return 0
    fi
    if grep -q "SecretNotFound" "$err"; then
      rm -f "$err"
      return 0
    fi
    echo "waiting for read access to $vault ($attempt/10)..." >&2
    sleep 30
  done
  cat "$err" >&2
  rm -f "$err"
  echo "cannot read $vault/$name" >&2
  return 2
}

# Writes the secret read from standard input, only when its value changed (no new version, no restart for nothing).
# Sets secret_changed=true|false; refuses an empty value. Use with a redirection, not a pipe (`put_secret v n < <(…)`
# or `<<<`), so that secret_changed reaches the caller. The value never appears on a command line.
put_secret() {
  local vault="$1" name="$2" value current file attempt
  value="$(cat)"
  secret_changed=false
  if [ -z "$value" ]; then
    echo "refusing to store an empty value in $vault/$name" >&2
    return 2
  fi
  current="$(secret_value "$vault" "$name")"
  [ "$current" = "$value" ] && return 0
  file="$(mktemp)"
  printf '%s' "$value" > "$file"
  # The Secrets Officer role of a new vault may take a few minutes to apply.
  for attempt in 1 2 3 4 5 6 7 8 9 10; do
    if az keyvault secret set --vault-name "$vault" --name "$name" --file "$file" --encoding utf-8 --output none "${@:3}" 2>/dev/null; then
      rm -f "$file"
      secret_changed=true
      echo "written $vault/$name" >&2
      return 0
    fi
    echo "waiting for write access to $vault ($attempt/10)..." >&2
    sleep 30
  done
  rm -f "$file"
  echo "cannot write $vault/$name" >&2
  return 2
}

# Tag of the image a container app runs now (empty when the app does not exist).
running_tag() {
  az containerapp show -g "$1" -n "$2" --query 'properties.template.containers[0].image' -o tsv 2>/dev/null \
    | sed 's/.*://' || true
}

# Restarts the active revisions of a container app, if it exists (Key Vault references and secret volumes are read
# when a revision starts).
restart_app() {
  local rg="$1" app="$2" revision
  az containerapp show -g "$rg" -n "$app" --output none 2>/dev/null || return 0
  for revision in $(az containerapp revision list -g "$rg" -n "$app" --query '[?properties.active].name' -o tsv); do
    az containerapp revision restart -g "$rg" -n "$app" --revision "$revision" --output none
    echo "restarted $app ($revision)" >&2
  done
}

# Starts a Container Apps job with its configured command and waits for the end of the execution.
run_job() {
  local rg="$1" job="$2" execution status
  execution="$(az containerapp job start -g "$rg" -n "$job" --query name -o tsv)"
  echo "$job → execution $execution" >&2
  while :; do
    status="$(az containerapp job execution show -g "$rg" -n "$job" --job-execution-name "$execution" --query properties.status -o tsv)"
    case "$status" in
      Succeeded) return 0 ;;
      Failed|Stopped|Degraded)
        echo "$job ended with $status (az containerapp job logs show -g $rg -n $job --execution $execution)" >&2
        return 1
        ;;
    esac
    sleep 15
  done
}
