#!/usr/bin/env bash
# Says what the LIVE container app is actually running, read from Azure (read-only: ARM GETs).
# Used by .github/workflows/deploy-azure-dev.yml and infra/scripts/smoke.sh (ADR-022 section 2).
#
# Usage: api-signin-mode.sh <resource-group> <container-app-name> [mode|image]
#
#   mode  (default) prints exactly one word:
#     google   the app has ONE active revision, it is the newest one and it is ready, and that revision's container runs
#              with ASPNETCORE_ENVIRONMENT=Production and has NO variable named Auth__DevLogin__Enabled (whatever its value).
#              Those two facts are the whole test (ADR-022 integration point 6): Production cannot start with dev login on,
#              and this template gives a Production API no sign-in but Google.
#     other    the app exists but any of the above does not hold (Development, a dev-login variable, a revision still
#              rolling out, more than one active revision, an answer that could not be read).
#     none     the app does not exist.
#     The deploy workflow drops the Microsoft gate (access=invited) ONLY when this prints google.
#
#   image  prints the container image of that single active, newest, ready revision; prints nothing when there is no
#          such revision. Gate-only runs compare it with the image of the commit they were asked to keep.
#
# Fails closed: every doubt is `other` (or an empty image), never `google`. Prints no host name, id or secret.
# Needs: az (signed in), jq.
set -uo pipefail

resource_group="${1:?resource group}"
app_name="${2:?container app name}"
query="${3:-mode}"
api_version="2025-01-01"

case "$query" in
  mode|image) ;;
  *) echo "api-signin-mode.sh: the third argument must be mode or image" >&2; exit 64 ;;
esac

answer() { # $1 = what to print for `mode`; `image` prints nothing unless the caller got as far as the image
  if [ "$query" = "mode" ]; then echo "$1"; fi
  exit 0
}

app_json="$(az resource show --resource-group "$resource_group" --name "$app_name" \
  --resource-type Microsoft.App/containerApps --api-version "$api_version" --output json 2>/dev/null)" || app_json=""
if [ -z "$app_json" ]; then
  # No answer: either the app does not exist or Azure could not be read. Tell them apart with a listing, which
  # succeeds with an empty result when the app is simply not there.
  names="$(az resource list --resource-group "$resource_group" --resource-type Microsoft.App/containerApps \
    --query "[].name" --output tsv 2>/dev/null)" || answer other
  # Searched from a here-string, not at the end of a pipeline: `grep -q` under pipefail can report a false failure.
  names="$(printf '%s\n' "$names" | tr -d '\r')"
  if grep -q -x -F -- "$app_name" <<< "$names"; then answer other; fi
  answer none
fi

app_id="$(jq -r '.id // ""' <<< "$app_json" 2>/dev/null)" || app_id=""
latest="$(jq -r '.properties.latestRevisionName // ""' <<< "$app_json" 2>/dev/null)" || latest=""
ready="$(jq -r '.properties.latestReadyRevisionName // ""' <<< "$app_json" 2>/dev/null)" || ready=""
if [ -z "$app_id" ] || [ -z "$latest" ] || [ "$latest" != "$ready" ]; then answer other; fi

# Every ACTIVE revision. Exactly one may exist, and it must be the newest: an older revision that is still active could
# be serving requests with other settings.
revisions_json="$(az rest --method get --url "https://management.azure.com${app_id}/revisions?api-version=${api_version}" --output json 2>/dev/null)" || answer other
active="$(jq -r '[.value[]? | select(.properties.active == true) | .name] | join(" ")' <<< "$revisions_json" 2>/dev/null)" || answer other
if [ "$active" != "$latest" ]; then answer other; fi

revision="$(jq -c --arg name "$latest" '[.value[]? | select(.name == $name)] | first // empty' <<< "$revisions_json" 2>/dev/null)" || answer other
[ -n "$revision" ] || answer other
if [ "$(jq -r '.properties.provisioningState // ""' <<< "$revision")" != "Provisioned" ]; then answer other; fi
containers="$(jq -r '.properties.template.containers | length' <<< "$revision" 2>/dev/null)" || answer other
if [ "$containers" != "1" ]; then answer other; fi

if [ "$query" = "image" ]; then
  jq -r '.properties.template.containers[0].image // ""' <<< "$revision"
  exit 0
fi

# Case-insensitive on the NAME: .NET reads these variable names without regard to case. Two variables that differ only in
# case join to "a,b", which is not "Production", so that is `other` too.
environment="$(jq -r '[.properties.template.containers[0].env[]? | select((.name // "") | ascii_downcase == "aspnetcore_environment") | .value // ""] | join(",")' <<< "$revision" 2>/dev/null)" || answer other
dev_login_vars="$(jq -r '[.properties.template.containers[0].env[]? | select((.name // "") | ascii_downcase == "auth__devlogin__enabled")] | length' <<< "$revision" 2>/dev/null)" || answer other

if [ "$environment" = "Production" ] && [ "$dev_login_vars" = "0" ]; then answer google; fi
answer other
