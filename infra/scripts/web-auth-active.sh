#!/usr/bin/env bash
# Prints "true" when built-in sign-in is ALREADY enabled on the web container app with the expected client id,
# otherwise "false" (also when the app or its auth settings do not exist yet). Read-only: one ARM GET.
# The deploy workflow uses the answer to decide whether the web app may be made public in a single pass, or must
# first get its sign-in settings while it is still internal.
#
# Usage: web-auth-active.sh <resource-group> <web-app-name> <expected-client-id>
# Needs: az (signed in), jq.
set -euo pipefail

resource_group="${1:?resource group}"
app_name="${2:?web app name}"
expected_client_id="${3:?expected client id}"

subscription="$(az account show --query id --output tsv)"
url="https://management.azure.com/subscriptions/${subscription}/resourceGroups/${resource_group}/providers/Microsoft.App/containerApps/${app_name}/authConfigs/current?api-version=2025-01-01"

if ! json="$(az rest --method get --url "$url" --output json 2>/dev/null)"; then
  echo false
  exit 0
fi

enabled="$(jq -r '.properties.platform.enabled // false' <<< "$json")"
client_id="$(jq -r '.properties.identityProviders.azureActiveDirectory.registration.clientId // ""' <<< "$json")"
provider_enabled="$(jq -r '.properties.identityProviders.azureActiveDirectory.enabled // true' <<< "$json")"
action="$(jq -r '.properties.globalValidation.unauthenticatedClientAction // ""' <<< "$json")"

# Case-insensitive GUID comparison.
if [ "$enabled" = "true" ] && [ "$provider_enabled" = "true" ] && [ "$action" = "RedirectToLoginPage" ] \
  && [ "${client_id,,}" = "${expected_client_id,,}" ]; then
  echo true
else
  echo false
fi
