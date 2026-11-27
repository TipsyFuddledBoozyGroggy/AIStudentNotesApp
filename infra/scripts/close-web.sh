#!/usr/bin/env bash
# Makes the web container app's ingress INTERNAL (no public address) and proves Azure reports it so.
# The emergency stop (workflow input access=closed) and the first move of the workflow's fail-closed step.
#
# It changes ONE setting of ONE resource: properties.configuration.ingress.external of the web app. It deploys no
# template, never touches the API, the database or any secret, and needs none of the Google, invite-list, key-ring,
# database or Entra values. So it runs even when those are missing or broken (ADR-022 follow-up F1).
#
# How: a merge PATCH of the container app with only that field. If Azure does not then report the ingress as internal,
# it tries the Azure CLI's own command for the same change. Either way the verdict comes from reading the app back.
# UNVERIFIED against Azure as of 2026-11-27 (both ways are documented, neither has been run here); the case tests
# cover the logic with a stand-in az (infra/scripts/tests/smoke-cases.sh).
#
# Usage: close-web.sh <resource-group> <web-app-name> [file-for-azure-error-text]
#   The third argument, when given, receives Azure's error text instead of the log: the caller masks it before
#   printing (it can name the environment domain, and the workflow log is public).
# Exit code 0: Azure reports the web ingress as internal (or the app has no ingress, or does not exist).
# Exit code 1: it could not be made internal or could not be confirmed. The caller must say so loudly.
# Reads (environment): CLOSE_WAIT_SECONDS (default 90), CLOSE_POLL_SECONDS (default 5).
# Needs: az (signed in), jq.
set -uo pipefail

resource_group="${1:?resource group}"
app_name="${2:?web container app name}"
error_file="${3:-/dev/null}"
api_version="2025-01-01"
wait_seconds="${CLOSE_WAIT_SECONDS:-90}"
poll_seconds="${CLOSE_POLL_SECONDS:-5}"
: > "$error_file" 2>/dev/null || error_file=/dev/null

# Prints: internal | external | none (no ingress at all) | missing (no such app) | unknown (Azure could not be read).
ingress_state() {
  local json names
  if ! json="$(az resource show --resource-group "$resource_group" --name "$app_name" \
    --resource-type Microsoft.App/containerApps --api-version "$api_version" --output json 2>> "$error_file")"; then
    # No answer: the app does not exist, or Azure could not be read. A listing tells them apart.
    if names="$(az resource list --resource-group "$resource_group" --resource-type Microsoft.App/containerApps --query "[].name" --output tsv 2>> "$error_file")"; then
      names="$(printf '%s\n' "$names" | tr -d '\r')"
      if ! grep -q -x -F -- "$app_name" <<< "$names"; then echo missing; return; fi
    fi
    echo unknown; return
  fi
  jq -r 'if (.properties.configuration.ingress // null) == null then "none"
         elif .properties.configuration.ingress.external == false then "internal"
         elif .properties.configuration.ingress.external == true then "external"
         else "unknown" end' <<< "$json" 2>/dev/null || echo unknown
}

# Waits until Azure reports the ingress as internal. Returns 0 when it does, 1 at the deadline.
wait_until_internal() {
  local deadline state
  deadline=$(( $(date +%s) + wait_seconds ))
  while :; do
    state="$(ingress_state)"
    case "$state" in internal|none) return 0 ;; esac
    if [ "$(date +%s)" -ge "$deadline" ]; then return 1; fi
    sleep "$poll_seconds"
  done
}

state="$(ingress_state)"
case "$state" in
  missing) echo "The web app does not exist, so nothing is reachable. Nothing to close."; exit 0 ;;
  none) echo "The web app has no ingress, so it is not reachable. Nothing to close."; exit 0 ;;
  internal) echo "The web ingress is already internal. Nothing to close."; exit 0 ;;
esac
# external or unknown: act.

subscription="$(az account show --query id --output tsv 2>> "$error_file" | tr -d '\r[:space:]')" || subscription=""
if [ -n "$subscription" ]; then
  url="https://management.azure.com/subscriptions/${subscription}/resourceGroups/${resource_group}/providers/Microsoft.App/containerApps/${app_name}?api-version=${api_version}"
  echo "Closing the web ingress (PATCH of the one field ingress.external) ..."
  if az rest --method patch --url "$url" --headers "Content-Type=application/json" \
    --body '{"properties":{"configuration":{"ingress":{"external":false}}}}' --output none 2>> "$error_file"; then
    if wait_until_internal; then echo "The web ingress is internal: the site has no public address."; exit 0; fi
    echo "::warning::Azure accepted the change but does not report the web ingress as internal after ${wait_seconds}s. Trying the second way."
  else
    echo "::warning::The PATCH was refused. Trying the second way."
  fi
else
  echo "::warning::The subscription could not be read. Trying the second way."
fi

echo "Closing the web ingress (az containerapp ingress update --type internal) ..."
if az containerapp ingress update --resource-group "$resource_group" --name "$app_name" --type internal --output none 2>> "$error_file"; then
  if wait_until_internal; then echo "The web ingress is internal: the site has no public address."; exit 0; fi
fi

echo "::error::THE WEB INGRESS COULD NOT BE CONFIRMED AS INTERNAL. Make it internal by hand now: Azure portal, container app ${app_name}, Ingress, untick 'Accepting traffic from anywhere' (or disable ingress)."
exit 1
