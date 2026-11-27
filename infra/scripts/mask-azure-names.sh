#!/usr/bin/env bash
# Registers GitHub log masks for the Azure names that would reveal where the POC lives: the Container Apps environment's
# default domain (it is part of the web and internal API host names) and the PostgreSQL server name (part of the public
# database host name). Used by .github/workflows/deploy-azure-dev.yml BEFORE any Azure error text is printed, because a
# failed deployment's message can name these resources and the repository's workflow logs are public.
# Read-only. Prints only ::add-mask:: commands (the runner hides those lines). Never fails the caller: a name that does
# not exist yet simply has nothing to mask.
#
# Usage: mask-azure-names.sh <resource-group> [file-to-scan]...
#   Each file given is also scanned for names that look like this deployment's database server or environment domain,
#   which matters when the resource was REFUSED: it then never existed, so the lookups below cannot find its name, but the
#   error text still contains it (deploy run 1, 2026-11-26, printed a refused server's name this way).
# Needs: az (signed in).
set -uo pipefail

resource_group="${1:?resource group}"
shift

mask() { if [ -n "$1" ]; then echo "::add-mask::$1"; fi; }

for file in "$@"; do
  [ -f "$file" ] || continue
  while IFS= read -r name; do
    mask "$name"
  done < <(grep -o -E 'tether-dev-pg-[a-z0-9]+|[a-z0-9-]+\.[a-z0-9]+\.azurecontainerapps\.io' "$file" 2>/dev/null | sort -u || true)
done

while IFS= read -r domain; do
  mask "$(printf '%s' "$domain" | tr -d '\r[:space:]')"
done < <(az resource list --resource-group "$resource_group" --resource-type Microsoft.App/managedEnvironments \
  --query "[].properties.defaultDomain" --output tsv 2>/dev/null || true)

# `az resource list` does not return properties for every type, so read each environment directly as well.
while IFS= read -r env_id; do
  [ -n "$env_id" ] || continue
  mask "$(az resource show --ids "$env_id" --query properties.defaultDomain --output tsv 2>/dev/null | tr -d '\r[:space:]')"
done < <(az resource list --resource-group "$resource_group" --resource-type Microsoft.App/managedEnvironments \
  --query "[].id" --output tsv 2>/dev/null || true)

while IFS= read -r server; do
  mask "$(printf '%s' "$server" | tr -d '\r[:space:]')"
done < <(az resource list --resource-group "$resource_group" --resource-type Microsoft.DBforPostgreSQL/flexibleServers \
  --query "[].name" --output tsv 2>/dev/null || true)

exit 0
