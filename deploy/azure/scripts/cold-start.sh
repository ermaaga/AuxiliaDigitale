#!/usr/bin/env bash
# Measures the cold start of an external container app that scales to zero (ADR 0022 §11), e.g. the showcase site.
#
#   deploy/azure/scripts/cold-start.sh [app] [path] [resource group]
#       defaults: ca-auxilia-site-01, /api/health, rg-auxilia-next-shared
#
# Waits until the app has no replica (scale to zero after the 300 s cooldown of Container Apps), then times one
# request that wakes it and a second, warm one, and prints the system events of the wake-up (replica scheduled,
# image pulled, container started, probes) so the time can be split into scheduling, image pull and app start.
set -euo pipefail

app="${1:-ca-auxilia-site-01}"
path="${2:-/api/health}"
group="${3:-rg-auxilia-next-shared}"

fqdn="$(az containerapp show --resource-group "$group" --name "$app" \
  --query properties.configuration.ingress.fqdn --output tsv)"
[ -z "$fqdn" ] && { echo "$app has no ingress" >&2; exit 1; }
url="https://$fqdn$path"

replicas() {
  az containerapp replica list --resource-group "$group" --name "$app" --query 'length(@)' --output tsv 2>/dev/null || echo 0
}

waited=0
while [ "$(replicas)" != "0" ]; do
  [ "$waited" -ge 900 ] && { echo "still running after 15 minutes (minReplicas > 0 or traffic?)" >&2; exit 1; }
  [ "$waited" -eq 0 ] && echo "waiting for $app to scale to zero (no request to it meanwhile)…"
  sleep 30
  waited=$((waited + 30))
done

format='status %{http_code}  dns %{time_namelookup}s  tls %{time_appconnect}s  first byte %{time_starttransfer}s  total %{time_total}s\n'
started="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
echo "cold  $url"
curl --silent --output /dev/null --max-time 240 --write-out "      $format" "$url"
echo "warm  $url"
curl --silent --output /dev/null --max-time 30 --write-out "      $format" "$url"

echo "system events of the wake-up (request at $started):"
# System logs are JSON only; they reach the log stream a few seconds late.
sleep 20
az containerapp logs show --resource-group "$group" --name "$app" --type system --tail 60 2>/dev/null \
  | python3 -I -c '
import json, sys
since = sys.argv[1]
for line in sys.stdin:
    try:
        event = json.loads(line)
    except ValueError:
        continue
    stamp = str(event.get("TimeStamp", ""))
    if stamp[:19].replace(" ", "T") < since[:19]:
        continue
    if event.get("Reason") in ("AssigningReplica", "PullingImage", "PulledImage", "ContainerCreated",
                               "ContainerStarted", "ProbeFailed", "ContainerTerminated", "ReplicaUnhealthy"):
        print("  " + stamp[11:23] + "  " + event["Reason"] + "  " + str(event.get("Msg", ""))[:120])
' "$started" || true
