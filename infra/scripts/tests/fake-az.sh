#!/usr/bin/env bash
# A stand-in for the Azure CLI, used ONLY by infra/scripts/tests/smoke-cases.sh (installed there as `az` at the front
# of PATH). It answers the few calls infra/scripts/{smoke,api-signin-mode,web-auth-active,close-web}.sh make, from
# fixture files in $FAKE_AZ_DIR. It never touches a network.
#   app-<name>.json         answer of `az resource show ... --name <name>` (missing file = the app does not exist)
#   revisions-<name>.json   answer of the revisions listing of that app; a single revision is cut out of it for --ids
#   auth.json               answer of the authConfigs/current GET (missing file = no settings: the call fails with a 404)
#   auth-unreadable         when this file exists that GET fails for another reason (throttling), not with a 404
#   names.txt               container app names, one per line
#   az-down                 when this file exists every call fails (Azure unreachable)
# For close-web.sh (the two ways of making the web ingress internal):
#   patch-refused / cli-refused   that way fails, printing a line that must never reach the public log
#   patch-ignored / cli-ignored   that way "succeeds" but changes nothing
#   web-internal                  written by a way that worked; from then on the web app reads as internal
#   calls.log                     one line per call (command words and the app or URL it named), for the tests to inspect
set -euo pipefail
dir="${FAKE_AZ_DIR:?}"

value_after() { # value_after --flag "$@"
  local flag="$1"; shift
  while [ "$#" -gt 0 ]; do
    if [ "$1" = "$flag" ]; then printf '%s' "${2-}"; return 0; fi
    shift
  done
  return 1
}

echo "${1-} ${2-} ${3-} $(value_after --method "$@" || true) $(value_after --name "$@" || true) $(value_after --url "$@" || true)" >> "$dir/calls.log"
if [ -f "$dir/az-down" ]; then echo "fake az: unreachable" >&2; exit 1; fi
refuse() { echo "fake az: refused for leaky-domain.example.invalid" >&2; exit 1; }

case "${1-} ${2-}" in
  "account show")
    echo "00000000-0000-0000-0000-000000000000" ;;
  "resource list")
    if [ -f "$dir/names.txt" ]; then cat "$dir/names.txt"; fi ;;
  "resource show")
    if ids="$(value_after --ids "$@")"; then
      app_id="${ids%%/revisions/*}"
      revision="${ids##*/revisions/}"
      jq -e --arg name "$revision" '.value[] | select(.name == $name)' "$dir/revisions-${app_id##*/}.json"
    else
      name="$(value_after --name "$@")"
      [ -f "$dir/app-${name}.json" ] || { echo "fake az: ResourceNotFound" >&2; exit 3; }
      if [ -f "$dir/web-internal" ] && [ "$name" = "tether-web" ]; then
        jq '.properties.configuration.ingress.external = false' "$dir/app-${name}.json"
      else
        cat "$dir/app-${name}.json"
      fi
    fi ;;
  "rest --method")
    url="$(value_after --url "$@")"
    if [ "$(value_after --method "$@")" = "patch" ]; then
      [ ! -f "$dir/patch-refused" ] || refuse
      # Only the one-field body close-web.sh sends, and only for the web app, is understood.
      case "$url" in */containerApps/tether-web\?*) ;; *) echo "fake az: unexpected patch target" >&2; exit 2 ;; esac
      [ "$(value_after --body "$@")" = '{"properties":{"configuration":{"ingress":{"external":false}}}}' ] || { echo "fake az: unexpected patch body" >&2; exit 2; }
      [ -f "$dir/patch-ignored" ] || touch "$dir/web-internal"
      exit 0
    fi
    case "$url" in
      */revisions\?*) app_path="${url%%/revisions\?*}"; cat "$dir/revisions-${app_path##*/}.json" ;;
      */authConfigs/current\?*)
        # A failure that is NOT "not found" (throttling here): the caller must read it as unknown, never as off.
        [ ! -f "$dir/auth-unreadable" ] || { echo 'ERROR: Too Many Requests({"error":{"code":"TooManyRequests","message":"leaky-domain.example.invalid"}})' >&2; exit 1; }
        # No settings at all: the 404 the Azure CLI reports for a missing resource.
        [ -f "$dir/auth.json" ] || { echo 'ERROR: Not Found({"error":{"code":"ResourceNotFound","message":"The auth config was not found."}})' >&2; exit 1; }
        cat "$dir/auth.json" ;;
      *) echo "fake az: unexpected url" >&2; exit 2 ;;
    esac ;;
  "containerapp ingress")
    [ "${3-}" = "update" ] && [ "$(value_after --type "$@")" = "internal" ] && [ "$(value_after --name "$@")" = "tether-web" ] \
      || { echo "fake az: unexpected containerapp ingress call" >&2; exit 2; }
    [ ! -f "$dir/cli-refused" ] || refuse
    [ -f "$dir/cli-ignored" ] || touch "$dir/web-internal"
    ;;
  *)
    echo "fake az: unexpected command: ${1-} ${2-}" >&2; exit 2 ;;
esac
