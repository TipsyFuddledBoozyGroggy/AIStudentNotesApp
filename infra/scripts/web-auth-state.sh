#!/usr/bin/env bash
# Says whether built-in (Entra, "Easy Auth") authentication is switched on for the web container app, whatever app
# registration it uses. Read-only: one ARM GET. Used by infra/scripts/live-rules.sh and infra/scripts/smoke.sh.
#
# Prints exactly one word:
#   on       the settings exist and platform.enabled is true
#   off      the settings exist with platform.enabled false, OR Azure answered "not found" (HTTP 404): no settings at all
#   unknown  anything else: the read failed for another reason (throttling, a 5xx, no sign-in, an answer that is not JSON)
#
# Why three answers: a failed read must never be taken as "off". A caller that needs "off" (taking the Microsoft gate
# off, or confirming it is off) refuses on `unknown`; only a real 404 counts as "there is nothing to switch off".
# Never prints Azure's error text (it can name the environment domain).
#
# Usage: web-auth-state.sh <resource-group> <web-app-name>
# Needs: az (signed in), jq.
set -uo pipefail

resource_group="${1:?resource group}"
app_name="${2:?web app name}"
api_version="2025-01-01"

subscription="$(az account show --query id --output tsv 2>/dev/null | tr -d '\r[:space:]')" || subscription=""
if [ -z "$subscription" ]; then echo unknown; exit 0; fi
url="https://management.azure.com/subscriptions/${subscription}/resourceGroups/${resource_group}/providers/Microsoft.App/containerApps/${app_name}/authConfigs/current?api-version=${api_version}"

error_text="$(mktemp)"
trap 'rm -f "$error_text"' EXIT
if json="$(az rest --method get --url "$url" --output json 2> "$error_text")"; then
  enabled="$(jq -r 'if type == "object" then (.properties.platform.enabled // false | tostring) else "invalid" end' <<< "$json" 2>/dev/null)" || enabled="invalid"
  case "$enabled" in
    true) echo on ;;
    false) echo off ;;
    *) echo unknown ;;
  esac
  exit 0
fi

# The read failed. Only "not found" means there are no settings. The Azure CLI reports an HTTP 404 from `az rest` as
# "Not Found(...)" with the service's error code inside (ResourceNotFound and similar); match those and nothing else.
failure="$(tr -d '\r' < "$error_text")"
if [[ "$failure" =~ (^|[^A-Za-z])(Not[[:space:]]Found|NotFound|ResourceNotFound|AuthConfigNotFound)([^A-Za-z]|$) ]] || [[ "$failure" =~ (^|[^0-9])404([^0-9]|$) ]]; then
  echo off
else
  echo unknown
fi
