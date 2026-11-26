#!/usr/bin/env bash
# Post-deploy check for the Tether proof of concept. Used by .github/workflows/deploy-azure-dev.yml.
#
# The GitHub runner is NOT a signed-in team member and is not on any allowlist, so it cannot (and must not be able to)
# load the app. The check is therefore in two parts:
#   1. Health, read from Azure: for the API and the web app, the newest revision is the ready one, it is provisioned and
#      it is not unhealthy. The API's readiness probe is /health/ready, which opens a database connection, so a healthy
#      API revision also proves the database is reachable and the migrations ran.
#   2. The gate, tested from outside: an anonymous request to https://<web>/signin must be REFUSED.
#        web not public   -> anything except a 2xx (normally 404)
#        IP allowlist on  -> 403
#        Entra sign-in    -> 302 to the sign-in endpoint, or 401
#      A 2xx answer means the app is open to the internet. That is reported as breach=true (exit code 2) and the
#      workflow then closes the web ingress again.
# What this does NOT prove: that a signed-in person can use the app end to end (web proxy to API, WebSockets). A person
# has to try that.
#
# Reads (environment): RESOURCE_GROUP, API_APP_NAME, WEB_APP_NAME, WEB_FQDN, WEB_PUBLIC (true|false),
#                      GATE (none|entra|ip|entra+ip), SMOKE_WAIT_SECONDS (default 300), SMOKE_GATE_WAIT_SECONDS (default 180),
#                      SMOKE_POLL_SECONDS (default 15)
# Needs: az (signed in), jq, curl.
set -euo pipefail

: "${RESOURCE_GROUP:?}" "${API_APP_NAME:?}" "${WEB_APP_NAME:?}" "${WEB_FQDN:?}" "${WEB_PUBLIC:?}" "${GATE:?}"
wait_seconds="${SMOKE_WAIT_SECONDS:-300}"
gate_wait_seconds="${SMOKE_GATE_WAIT_SECONDS:-180}"
poll_seconds="${SMOKE_POLL_SECONDS:-15}"
api_version="2025-01-01"

out() { if [ -n "${GITHUB_OUTPUT:-}" ]; then echo "$1" >> "$GITHUB_OUTPUT"; fi; }

# Prints one line "ok ..." or "wait ..." or "bad ..." for a container app.
app_state() {
  local app="$1" app_json rev_json latest ready min_replicas app_id prov health running replicas
  app_json="$(az resource show --resource-group "$RESOURCE_GROUP" --name "$app" --resource-type Microsoft.App/containerApps --api-version "$api_version" --output json)"
  app_id="$(jq -r '.id' <<< "$app_json")"
  latest="$(jq -r '.properties.latestRevisionName // ""' <<< "$app_json")"
  ready="$(jq -r '.properties.latestReadyRevisionName // ""' <<< "$app_json")"
  min_replicas="$(jq -r '.properties.template.scale.minReplicas // 0' <<< "$app_json")"
  if [ -z "$latest" ]; then echo "wait no revision yet"; return; fi
  rev_json="$(az resource show --ids "${app_id}/revisions/${latest}" --api-version "$api_version" --output json)"
  prov="$(jq -r '.properties.provisioningState // ""' <<< "$rev_json")"
  health="$(jq -r '.properties.healthState // ""' <<< "$rev_json")"
  running="$(jq -r '.properties.runningState // ""' <<< "$rev_json")"
  replicas="$(jq -r '.properties.replicas // 0' <<< "$rev_json")"
  local detail="revision=$latest provisioning=$prov health=$health running=$running replicas=$replicas minReplicas=$min_replicas"
  if [ "$prov" = "Failed" ] || [ "$running" = "Failed" ]; then echo "bad $detail"; return; fi
  if [ "$latest" != "$ready" ] || [ "$prov" != "Provisioned" ]; then echo "wait $detail"; return; fi
  if [ "$health" = "Unhealthy" ] || [ "$running" = "Degraded" ] || [ "$running" = "Stopped" ]; then echo "wait $detail"; return; fi
  # An always-on app must actually have a healthy replica; a min-0 app may legitimately be scaled to zero.
  if [ "$min_replicas" -ge 1 ] && { [ "$health" != "Healthy" ] || [ "$replicas" -lt 1 ]; }; then echo "wait $detail"; return; fi
  echo "ok $detail"
}

health_failed=0
for app in "$API_APP_NAME" "$WEB_APP_NAME"; do
  deadline=$(( $(date +%s) + wait_seconds ))
  while :; do
    state="$(app_state "$app")"
    case "$state" in
      ok*) echo "PASS  $app: ${state#ok }"; break ;;
      bad*) echo "::error::$app revision failed: ${state#bad }"; health_failed=1; break ;;
    esac
    if [ "$(date +%s)" -ge "$deadline" ]; then
      echo "::error::$app is not healthy after ${wait_seconds}s: ${state#wait }"
      health_failed=1
      break
    fi
    echo "      $app: ${state#wait } (waiting)"
    sleep "$poll_seconds"
  done
done

# --- The gate, from outside. Redirects are NOT followed; only the first answer matters. ---
probe() {
  local headers code location
  headers="$(curl --silent --show-error --max-time 20 --output /dev/null --dump-header - "https://${WEB_FQDN}/signin" 2>/dev/null)" || true
  code="$(printf '%s\n' "$headers" | awk 'toupper($1) ~ /^HTTP\// { c = $2 } END { print c + 0 }')"
  location="$(printf '%s\n' "$headers" | tr -d '\r' | awk 'tolower($1) == "location:" { print $2 }' | tail -n 1)"
  echo "${code:-0} ${location:-}"
}

if [ "$WEB_PUBLIC" != "true" ]; then expected="not-2xx"
elif [[ "$GATE" == *ip* ]]; then expected="403"
elif [ "$GATE" = "entra" ]; then expected="302-or-401"
else
  # Public with no gate must be impossible (infra/main.bicep refuses it). Treat it as a breach if it ever happens.
  expected="never"
fi

gate_failed=0
deadline=$(( $(date +%s) + gate_wait_seconds ))
while :; do
  read -r code location <<< "$(probe)"
  verdict="retry"
  if [ "$code" -ge 200 ] && [ "$code" -lt 300 ]; then
    verdict="breach"
  else
    case "$expected" in
      not-2xx) verdict="pass" ;;
      403) [ "$code" = "403" ] && verdict="pass" ;;
      302-or-401)
        if [ "$code" = "401" ]; then verdict="pass"
        elif [ "$code" = "302" ] && [[ "$location" == *"/.auth/login/"* || "$location" == *"login.microsoftonline.com"* ]]; then verdict="pass"
        fi ;;
      never) verdict="breach" ;;
    esac
  fi
  case "$verdict" in
    pass)
      echo "PASS  gate: anonymous request to /signin answered HTTP $code (expected $expected)"
      out "gate_verified=true" # the workflow's fail-closed step keeps the ingress only when this is written
      break ;;
    breach)
      echo "::error::GATE NOT ENFORCED: an anonymous request to the web app answered HTTP $code (expected $expected). The web ingress will be closed."
      out "breach=true"
      exit 2 ;;
  esac
  if [ "$(date +%s)" -ge "$deadline" ]; then
    echo "::error::gate check: anonymous request to /signin answered HTTP $code, expected $expected."
    gate_failed=1
    break
  fi
  echo "      gate: HTTP $code so far, expected $expected (waiting for the ingress to settle)"
  sleep "$poll_seconds"
done

out "breach=false"
if [ "$health_failed" -ne 0 ] || [ "$gate_failed" -ne 0 ]; then exit 1; fi
echo "Smoke check passed. Not covered: a signed-in person using the app end to end."
