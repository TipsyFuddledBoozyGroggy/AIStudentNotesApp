#!/usr/bin/env bash
# Prints the PostgreSQL regions a deploy may use, one per line, in the order to try them.
#  - A database server already exists in the resource group (any state but Failed): print ONLY its region. A later run
#    must never put a second, empty server in another region and point the API at it.
#  - None exists: print the candidate regions given as arguments (the first one that accepts a Burstable server wins).
# Used by .github/workflows/deploy-azure-dev.yml. Read-only. Never prints a server name (the logs are public).
#
# Usage: postgres-location.sh <resource-group> <candidate-region>...
# Needs: az (signed in).
set -uo pipefail

resource_group="${1:?resource group}"
shift
[ "$#" -gt 0 ] || { echo "::error::postgres-location.sh: no candidate region given" >&2; exit 1; }

existing="$(az resource list --resource-group "$resource_group" --resource-type Microsoft.DBforPostgreSQL/flexibleServers \
  --query "[?provisioningState!='Failed'].location" --output tsv 2>/dev/null | tr -d '\r' | tr '[:upper:]' '[:lower:]' | tr -d ' ' | sort -u)"
count="$(printf '%s' "$existing" | grep -c . || true)"

if [ "$count" -ge 1 ]; then
  if [ "$count" -gt 1 ]; then
    echo "::warning::More than one database server region found in the resource group; using the first. Remove the one that is not in use." >&2
  fi
  printf '%s\n' "$existing" | head -n 1
  exit 0
fi

for region in "$@"; do printf '%s\n' "$region"; done
