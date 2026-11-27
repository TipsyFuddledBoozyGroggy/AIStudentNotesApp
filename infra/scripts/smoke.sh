#!/usr/bin/env bash
# Post-deploy check for Tether in Azure. Used by .github/workflows/deploy-azure-dev.yml.
# The expectations are the tables in ADR-022 section 2 (docs/architecture/go-live-signin-adr.md); change them there first.
# Case tests: infra/scripts/tests/smoke-cases.sh.
#
# Three parts:
#   1. Health, read from Azure: for the API and the web app, the newest revision is the ready one, it is provisioned and
#      it is not unhealthy. The API's readiness probe is /health/ready, which opens a database connection, so a healthy
#      API revision also proves the database is reachable and the migrations ran.
#      Skipped for closed: the emergency stop deploys nothing, and whether the apps are healthy must not decide
#      whether closing the site counts as done.
#   2. Sign-in mode, read from Azure (team and invited): the live API runs as Production with no dev-login variable
#      (infra/scripts/api-signin-mode.sh), its image is the one this run was asked to deploy, and built-in (Entra)
#      authentication on the web app is ON for team and OFF for invited.
#   3. The gate, tested from OUTSIDE (the GitHub runner is not a signed-in person and is on no allowlist):
#        closed             GET /signin must not answer 2xx
#        any mode with +ip  GET /signin answers 403 (the runner is not on the allowlist)
#        team (entra)       GET /signin answers 302 to the Microsoft sign-in, or 401
#        invited (google)   six probes, in this order; the page is MEANT to be public here, so "a 2xx is a breach" is
#                           replaced by proof that dev login does not exist and that Google sign-in is wired correctly:
#          1  POST /api/v1/dev/login {"email":"smoke@example.invalid"}      404, or 401 with problem code
#             "unauthenticated" and no Set-Cookie (the API answers 401 for EVERY unmapped route; a mapped dev login is
#             anonymous and never answers 401)        breach: any 2xx, or 400, or a 401 with another code or a Set-Cookie;
#             a 401 with no problem code at all is not the API's answer: "try again"
#          2  GET  /api/v1/auth/methods                                     200 {"google":true,"devLogin":false}   breach: devLogin true
#          3  GET  /api/v1/me ; POST /hubs/lecture/negotiate                401 both   breach: 2xx
#          4  POST /api/v1/auth/google/signin/start {"landing":"home"}      200, Google's authorize URL with the expected
#             redirect_uri, response_type=code, S256, scope "openid email profile"; binder cookie __Host-cc.signin with
#             Secure, HttpOnly, SameSite=Lax, Path=/, Max-Age=600, no Domain                 breach: any other URL host
#          5  GET  /api/v1/auth/google/callback?state=<random>&code=x with that binder     302, Location exactly
#             /signin?purpose=signin&outcome=state_invalid, binder expired                   breach: 2xx
#          6  GET  /signin                                                  200 with Strict-Transport-Security
#      Unsafe methods carry `Origin: https://<web host>` (without it the API answers 403 and the probe proves nothing).
#      A 429, a timeout or a 5xx means "try again" until the deadline; after the deadline the check FAILS, it never passes.
#      No probe follows a redirect or calls Google.
#
# `gate_verified=true` is written to $GITHUB_OUTPUT only when the site ended up PUBLIC and part 2 and part 3 both passed
# (for closed, `closed_verified=true` is written instead: nothing is public, so no gate is verified). The workflow's fail-closed step
# makes the web app internal again whenever a run that asked for public ingress ends without it.
# Exit codes: 0 all passed; 1 a check failed or timed out; 2 breach (breach=true is written too).
# What this does NOT prove: that an invited person can sign in with Google and use the app end to end. A person does that.
#
# Nothing here prints the web address, a URL, a cookie, a header value or an identifier: the workflow log is public.
#
# Reads (environment): RESOURCE_GROUP, API_APP_NAME, WEB_APP_NAME, WEB_FQDN, WEB_PUBLIC (true|false),
#                      GATE (closed|entra|entra+ip|google|google+ip),
#                      EXPECTED_API_IMAGE (team and invited), TETHER_EASYAUTH_CLIENT_ID (team),
#                      SMOKE_WAIT_SECONDS (default 300), SMOKE_GATE_WAIT_SECONDS (default 240), SMOKE_POLL_SECONDS (default 15),
#                      SMOKE_ENTRA_CLEAR_ANSWERS (default 3: answers in a row without the Microsoft layer before the invited probes start)
#   SMOKE_LOCAL_REHEARSAL=1  local rehearsal against a stand-in or the local Production stack: parts 1 and 2 are skipped,
#                            only the six invited probes run, and NOTHING is written to $GITHUB_OUTPUT (so it can never
#                            verify a gate). Needs only WEB_FQDN and GATE=google; trust a local certificate with CURL_CA_BUNDLE.
# Needs: az (signed in), jq, curl.
set -euo pipefail

rehearsal="${SMOKE_LOCAL_REHEARSAL:-0}"
: "${WEB_FQDN:?}" "${GATE:?}"
if [ "$rehearsal" = "1" ]; then
  if [ "$GATE" != "google" ]; then echo "::error::SMOKE_LOCAL_REHEARSAL only runs the invited probes: set GATE=google."; exit 1; fi
  WEB_PUBLIC=true
else
  : "${RESOURCE_GROUP:?}" "${API_APP_NAME:?}" "${WEB_APP_NAME:?}" "${WEB_PUBLIC:?}"
fi
case "$GATE" in
  closed|entra|entra+ip|google|google+ip) ;;
  *) echo "::error::smoke.sh: unknown gate. Nothing was verified."; exit 1 ;;
esac
# team and invited ask for a PUBLIC site behind a gate. If the deployment did not end up public, the run did not do what
# was asked: that is a failure, and no gate is reported as verified (the outside check below still runs).
asked_public=false
case "$GATE" in entra*|google*) asked_public=true ;; esac
not_public_failed=0
if [ "$asked_public" = "true" ] && [ "$WEB_PUBLIC" != "true" ]; then
  not_public_failed=1
  echo "::error::A public access mode was asked for (gate '$GATE') but the web app did not end up public. Nothing is verified."
fi
wait_seconds="${SMOKE_WAIT_SECONDS:-300}"
gate_wait_seconds="${SMOKE_GATE_WAIT_SECONDS:-240}"
poll_seconds="${SMOKE_POLL_SECONDS:-15}"
api_version="2025-01-01"
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
base="https://${WEB_FQDN}"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

out() { if [ "$rehearsal" != "1" ] && [ -n "${GITHUB_OUTPUT:-}" ]; then echo "$1" >> "$GITHUB_OUTPUT"; fi; }
breach() { echo "::error::$*"; out "breach=true"; exit 2; }

# ---------------------------------------------------------------------------------------------------------------------
# Part 1: health, from Azure
# ---------------------------------------------------------------------------------------------------------------------

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
if [ "$rehearsal" != "1" ] && [ "$GATE" != "closed" ]; then
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
fi

# ---------------------------------------------------------------------------------------------------------------------
# Part 2: sign-in mode, from Azure (team and invited)
# ---------------------------------------------------------------------------------------------------------------------

mode_failed=0
api_is_production=unknown
if [ "$rehearsal" != "1" ] && [ "$GATE" != "closed" ]; then
  : "${EXPECTED_API_IMAGE:?}"
  # The API revision may still be switching over; give it the same patience as the health check.
  deadline=$(( $(date +%s) + wait_seconds ))
  while :; do
    api_mode="$(bash "$script_dir/api-signin-mode.sh" "$RESOURCE_GROUP" "$API_APP_NAME" mode)"
    live_image="$(bash "$script_dir/api-signin-mode.sh" "$RESOURCE_GROUP" "$API_APP_NAME" image)"
    if [ "$api_mode" = "google" ] && [ "$live_image" = "$EXPECTED_API_IMAGE" ]; then break; fi
    if [ "$(date +%s)" -ge "$deadline" ]; then break; fi
    echo "      API: sign-in mode '$api_mode' so far (waiting for the new revision to be the only active one)"
    sleep "$poll_seconds"
  done
  if [ "$api_mode" = "google" ]; then
    api_is_production=true
    echo "PASS  API: one active revision, ASPNETCORE_ENVIRONMENT=Production, no Auth__DevLogin__Enabled variable"
  else
    api_is_production=false
    mode_failed=1
    echo "::error::The live API is not confirmed as Production without dev login (api-signin-mode.sh says '$api_mode'). The gate is NOT verified."
  fi
  if [ "$live_image" = "$EXPECTED_API_IMAGE" ]; then
    echo "PASS  API: the live image is the one this run was asked to deploy"
  else
    mode_failed=1
    echo "::error::The live API image is not the one this run was asked to deploy. The gate is NOT verified."
  fi
  if [[ "$GATE" == entra* ]]; then
    : "${TETHER_EASYAUTH_CLIENT_ID:?}"
    if [ "$(bash "$script_dir/web-auth-active.sh" "$RESOURCE_GROUP" "$WEB_APP_NAME" "$TETHER_EASYAUTH_CLIENT_ID")" = "true" ]; then
      echo "PASS  web: Microsoft (Entra) sign-in is ON with the expected app registration"
    else
      mode_failed=1
      echo "::error::Azure does not report Microsoft (Entra) sign-in as ON for the web app, but access=team was asked for. The gate is NOT verified."
    fi
  else
    # on | off | unknown. Only settings that say "off", or a real 404 (no settings), count as off. A read that failed
    # for any other reason is unknown, and unknown is not a pass (web-auth-state.sh).
    case "$(bash "$script_dir/web-auth-state.sh" "$RESOURCE_GROUP" "$WEB_APP_NAME")" in
      off)
        echo "PASS  web: built-in (Entra) authentication is OFF, as access=invited asks" ;;
      on)
        mode_failed=1
        echo "::error::Azure still reports built-in (Entra) authentication as ON for the web app, but access=invited was asked for. The gate is NOT verified." ;;
      *)
        mode_failed=1
        echo "::error::Azure could not say whether built-in (Entra) authentication is on or off for the web app (the read failed; that is not taken as off). The gate is NOT verified." ;;
    esac
  fi
fi

# ---------------------------------------------------------------------------------------------------------------------
# Part 3: the gate, from outside. Redirects are never followed; only the first answer matters.
# ---------------------------------------------------------------------------------------------------------------------

origin="$base"
code=000
# request METHOD PATH [JSON-BODY] [COOKIE]. Sets $code; leaves the response in $work/headers and $work/body.
request() {
  local method="$1" path="$2" body="${3-}" cookie="${4-}"
  local args=(--silent --max-time 20 --request "$method" --dump-header "$work/headers" --output "$work/body" --write-out '%{http_code}')
  case "$method" in
    GET|HEAD) ;;
    *) args+=(--header "Origin: ${origin}")
       if [ -n "$body" ]; then args+=(--header 'Content-Type: application/json' --data "$body"); else args+=(--data ''); fi ;;
  esac
  if [ -n "$cookie" ]; then args+=(--header "Cookie: ${cookie}"); fi
  : > "$work/headers"; : > "$work/body"
  code="$(curl "${args[@]}" "${base}${path}" 2>/dev/null)" || code=000
  [[ "$code" =~ ^[0-9]{3}$ ]] || code=000
}
# Every value of one response header, one per line (names are case-insensitive; the last header block only).
header_values() {
  tr -d '\r' < "$work/headers" | awk -v want="$(printf '%s' "$1" | tr '[:upper:]' '[:lower:]')" '
    toupper($1) ~ /^HTTP\// { n = 0 }
    { i = index($0, ":"); if (i > 0 && tolower(substr($0, 1, i - 1)) == want) { v = substr($0, i + 1); sub(/^[ \t]+/, "", v); lines[++n] = v } }
    END { for (j = 1; j <= n; j++) print lines[j] }'
}
is_2xx() { [ "$code" -ge 200 ] && [ "$code" -lt 300 ]; }
urldecode() { local s="${1//+/ }"; printf '%b' "${s//%/\\x}"; }
# The Set-Cookie line for one cookie name, or nothing.
set_cookie_line() { header_values set-cookie | awk -v name="$1" 'index($0, name "=") == 1 { line = $0 } END { if (line != "") print line }'; }
# has_attribute "<set-cookie line>" "<attribute, lower case>": exact match of one attribute, ignoring case and spaces.
# The list is built first and searched from a here-string, NOT at the end of a pipeline: under `pipefail` a `grep -q`
# that stops at its first match can make the pipeline report failure (the writer gets SIGPIPE), which would turn a
# correct cookie into a failed check now and then (seen in the case tests, 2026-11-27).
has_attribute() {
  local attributes
  attributes="$(printf '%s' "$1" | tr ';' '\n' | sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//' | tr '[:upper:]' '[:lower:]')"
  grep -q -x -F -- "$2" <<< "$attributes"
}

# Probe functions return: 0 pass, 1 try again (until the deadline), 2 breach, 3 failed for good. They set $detail to a
# short reason that never holds a URL, a host name, a cookie or a header value.
detail=""
binder_cookie=""

probe_simple_gate() { # closed, +ip and entra: one request to /signin
  request GET /signin
  case "$expected" in
    not-2xx)
      if is_2xx; then detail="HTTP $code although the web app should not be reachable from the internet"; return 1; fi
      detail="HTTP $code"; return 0 ;;
    403)
      if [ "$code" = "403" ]; then detail="HTTP 403"; return 0; fi
      detail="HTTP $code, expected 403 from the IP allowlist"; return 1 ;;
    302-or-401)
      if [ "$code" = "401" ]; then detail="HTTP 401"; return 0; fi
      if [ "$code" = "302" ]; then
        local location; location="$(header_values location | tail -n 1)"
        if [[ "$location" == *"/.auth/login/"* || "$location" == *"login.microsoftonline.com"* ]]; then detail="HTTP 302 to the Microsoft sign-in"; return 0; fi
        detail="HTTP 302, but not to the Microsoft sign-in"; return 1
      fi
      if is_2xx && [ "$api_is_production" != "true" ]; then
        detail="HTTP $code with no Microsoft sign-in in front, and the API is not confirmed as Production"; return 2
      fi
      detail="HTTP $code, expected 302 to the Microsoft sign-in or 401"; return 1 ;;
  esac
  return 3
}

# Before the six probes: wait until the Microsoft sign-in layer has stopped answering. Right after an invited deployment
# the platform can still have it in front for a while, and not on every replica or edge at once: one clear answer can be
# followed by one more from the old layer. So the wait passes only after SMOKE_ENTRA_CLEAR_ANSWERS answers IN A ROW
# (default 3, one per poll interval) that did not come from that layer; an answer from it starts the count again. This
# is a readiness wait, not a verdict on what the page answers. If the layer never goes away the check fails closed at
# the deadline like any other. A timeout or a 5xx is no answer either way: it also starts the count again.
clear_needed="${SMOKE_ENTRA_CLEAR_ANSWERS:-3}"
[[ "$clear_needed" =~ ^[1-9]$ ]] || clear_needed=3
if [ "$rehearsal" = "1" ]; then clear_needed=1; fi   # the local stack has no such layer
clear_streak=0
probe_entra_layer_gone() {
  local location
  request GET /signin
  location="$(header_values location | tail -n 1)"
  if [ "$code" = "401" ]; then clear_streak=0; detail="the Microsoft sign-in layer still answers (HTTP 401)"; return 1; fi
  if [ "$code" = "302" ] && [[ "$location" == *"/.auth/login/"* || "$location" == *"login.microsoftonline.com"* ]]; then
    clear_streak=0; detail="the Microsoft sign-in layer still answers (HTTP 302 to its sign-in)"; return 1
  fi
  if [ "$code" = "000" ] || [ "$code" -ge 500 ]; then clear_streak=0; detail="no clear answer yet (HTTP $code)"; return 1; fi
  clear_streak=$((clear_streak + 1))
  if [ "$clear_streak" -lt "$clear_needed" ]; then
    detail="no Microsoft sign-in layer in $clear_streak of $clear_needed answers in a row"; return 1
  fi
  detail="no Microsoft sign-in layer in front ($clear_needed answers in a row)"; return 0
}

# ADR-022 section 2, probe 1 (Lead ruling 2026-11-27). The API's fallback policy answers 401 "unauthenticated" for every
# unmapped route, so that answer, with no cookie set, proves the route is absent just as a 404 does. A MAPPED dev login
# is anonymous: it answers 204, 400 or 429, never 401.
probe_dev_login() {
  local problem_code
  request POST /api/v1/dev/login '{"email":"smoke@example.invalid"}'
  if [ "$code" = "404" ]; then detail="HTTP 404: the dev-login route does not exist"; return 0; fi
  if [ "$code" = "401" ]; then
    problem_code="$(jq -r 'if type == "object" then (.code // "") else "" end' "$work/body" 2>/dev/null)" || problem_code=""
    if [ -n "$(header_values set-cookie)" ]; then
      detail="POST /api/v1/dev/login answered HTTP 401 AND set a cookie"; return 2
    fi
    if [ "$problem_code" = "unauthenticated" ]; then
      detail="HTTP 401 unauthenticated with no cookie: the dev-login route does not exist"; return 0
    fi
    if [ -z "$problem_code" ]; then
      # No problem code at all: this is not the API talking (the API always answers with one). Most likely a sign-in
      # layer that has not quite gone yet. Not proof of anything, so: try again; at the deadline the check fails closed.
      detail="POST /api/v1/dev/login answered HTTP 401 with no problem code (not the API's own answer)"; return 1
    fi
    detail="POST /api/v1/dev/login answered HTTP 401 with another problem code than 'unauthenticated'"; return 2
  fi
  if is_2xx || [ "$code" = "400" ]; then detail="POST /api/v1/dev/login answered HTTP $code: the dev-login route EXISTS"; return 2; fi
  detail="POST /api/v1/dev/login answered HTTP $code, expected 404 or 401 unauthenticated"; return 1
}

probe_methods() {
  request GET /api/v1/auth/methods
  if [ "$code" != "200" ]; then detail="GET /api/v1/auth/methods answered HTTP $code, expected 200"; return 1; fi
  if [ "$(jq -r 'if type == "object" then (.devLogin == true) else false end' "$work/body" 2>/dev/null)" = "true" ]; then
    detail="GET /api/v1/auth/methods says devLogin is true"; return 2
  fi
  if [ "$(jq -r 'if type == "object" then (.google == true and .devLogin == false) else false end' "$work/body" 2>/dev/null)" = "true" ]; then
    detail="google true, devLogin false"; return 0
  fi
  detail="GET /api/v1/auth/methods did not answer google true and devLogin false"; return 3
}

probe_anonymous_refused() {
  request GET /api/v1/me
  if is_2xx; then detail="GET /api/v1/me answered HTTP $code to an anonymous request"; return 2; fi
  if [ "$code" != "401" ]; then detail="GET /api/v1/me answered HTTP $code, expected 401"; return 1; fi
  request POST '/hubs/lecture/negotiate?negotiateVersion=1'
  if is_2xx; then detail="POST /hubs/lecture/negotiate answered HTTP $code to an anonymous request"; return 2; fi
  if [ "$code" != "401" ]; then detail="POST /hubs/lecture/negotiate answered HTTP $code, expected 401"; return 1; fi
  detail="HTTP 401 for both"; return 0
}

probe_signin_start() {
  local url query pair key value redirect_uri="" response_type="" method="" scope="" line lower_line
  request POST /api/v1/auth/google/signin/start '{"landing":"home"}'
  if [ "$code" != "200" ]; then detail="POST /api/v1/auth/google/signin/start answered HTTP $code, expected 200"; return 1; fi
  url="$(jq -r 'if type == "object" then (.authorizationUrl // "") else "" end' "$work/body" 2>/dev/null)" || url=""
  if [ -z "$url" ]; then detail="the start response has no authorizationUrl"; return 3; fi
  if [[ "$url" != "https://accounts.google.com/o/oauth2/v2/auth?"* ]]; then
    detail="the authorization URL does not point at Google's authorize endpoint"; return 2
  fi
  query="${url#*\?}"
  while IFS= read -r -d '&' pair || [ -n "$pair" ]; do
    key="${pair%%=*}"; value="$(urldecode "${pair#*=}")"
    case "$key" in
      redirect_uri) redirect_uri="$value" ;;
      response_type) response_type="$value" ;;
      code_challenge_method) method="$value" ;;
      scope) scope="$value" ;;
    esac
  done <<< "$query"
  if [ "$redirect_uri" != "${base}/api/v1/auth/google/callback" ]; then detail="redirect_uri in the authorization URL is not this site's callback address"; return 3; fi
  if [ "$response_type" != "code" ]; then detail="response_type is not code"; return 3; fi
  if [ "$method" != "S256" ]; then detail="code_challenge_method is not S256"; return 3; fi
  if [ "$scope" != "openid email profile" ]; then detail="scope is not exactly 'openid email profile'"; return 3; fi
  line="$(set_cookie_line '__Host-cc.signin')"
  if [ -z "$line" ]; then detail="the start response did not set the binder cookie __Host-cc.signin"; return 3; fi
  for attribute in secure httponly samesite=lax path=/ max-age=600; do
    if ! has_attribute "$line" "$attribute"; then detail="the binder cookie lacks the attribute '$attribute'"; return 3; fi
  done
  lower_line="$(printf '%s' "$line" | tr '[:upper:]' '[:lower:]')"
  if [[ "$lower_line" =~ \;[[:space:]]*domain[[:space:]]*= ]]; then detail="the binder cookie has a Domain attribute"; return 3; fi
  binder_cookie="${line%%;*}"
  if [ "${#binder_cookie}" -le "${#binder_name_eq}" ]; then detail="the binder cookie is empty"; return 3; fi
  detail="Google authorize URL with this site's callback, code, S256, sign-in scopes; binder cookie attributes correct"; return 0
}
binder_name_eq='__Host-cc.signin='

probe_callback_bogus_state() {
  local state location line
  state="$(head -c 32 /dev/urandom | base64 | tr '+/' '-_' | tr -d '=\n')"
  request GET "/api/v1/auth/google/callback?state=${state}&code=x" "" "$binder_cookie"
  if is_2xx; then detail="the callback answered HTTP $code to an unknown state"; return 2; fi
  if [ "$code" != "302" ]; then detail="the callback answered HTTP $code, expected 302"; return 1; fi
  location="$(header_values location | tail -n 1)"
  if [ "$location" = "/signin?purpose=signin&outcome=rate_limited" ]; then detail="the callback is rate limited"; return 1; fi
  if [[ "$location" != /* ]]; then detail="the callback's Location is not a relative path"; return 3; fi
  if [ "$location" != "/signin?purpose=signin&outcome=state_invalid" ]; then detail="the callback's Location is not /signin?purpose=signin&outcome=state_invalid"; return 3; fi
  line="$(set_cookie_line '__Host-cc.signin')"
  local lower; lower="$(printf '%s' "$line" | tr '[:upper:]' '[:lower:]')"
  if [ -z "$line" ] || { [ "${line%%;*}" != "$binder_name_eq" ] && [[ "$lower" != *"max-age=0"* ]] && [[ "$lower" != *"expires=thu, 01 jan 1970"* ]]; }; then
    detail="the callback did not expire the binder cookie"; return 3
  fi
  detail="HTTP 302 to /signin?purpose=signin&outcome=state_invalid; binder expired"; return 0
}

probe_signin_page() {
  request GET /signin
  if [ "$code" != "200" ]; then detail="GET /signin answered HTTP $code, expected 200"; return 1; fi
  if [ -z "$(header_values strict-transport-security)" ]; then detail="GET /signin has no Strict-Transport-Security header"; return 3; fi
  detail="HTTP 200 with Strict-Transport-Security"; return 0
}

gate_deadline=$(( $(date +%s) + gate_wait_seconds ))
gate_failed=0
# run_probe "<label>" <function>: repeats a probe that says "try again" until the shared deadline.
run_probe() {
  local label="$1" fn="$2" rc
  while :; do
    rc=0; "$fn" || rc=$?
    case "$rc" in
      0) echo "PASS  $label: $detail"; return 0 ;;
      2) breach "GATE NOT ENFORCED ($label): $detail. The web ingress will be closed." ;;
      3) echo "::error::$label: $detail."; gate_failed=1; return 1 ;;
    esac
    if [ "$(date +%s)" -ge "$gate_deadline" ]; then
      # Fail closed (ADR-022 R11): a probe that never got a clear answer (429, timeout, 5xx, a sign-in layer still
      # settling) is a FAILED check, not a pass.
      echo "::error::$label: $detail. Still so after ${gate_wait_seconds}s; the check fails."
      gate_failed=1
      return 1
    fi
    echo "      $label: $detail (trying again)"
    sleep "$poll_seconds"
  done
}

if [ "$WEB_PUBLIC" != "true" ] || [ "$GATE" = "closed" ]; then
  expected="not-2xx"
  run_probe "gate, web not public" probe_simple_gate || true
  # A page that is still served although the ingress should be internal is a breach, not a slow start.
  if [ "$gate_failed" -ne 0 ] && is_2xx; then breach "The web app still answers HTTP $code from the internet although it should be internal."; fi
elif [[ "$GATE" == *+ip ]]; then
  expected="403"
  run_probe "gate, IP allowlist" probe_simple_gate || true
  if [ "$gate_failed" -ne 0 ] && is_2xx; then breach "GATE NOT ENFORCED: the IP allowlist let an outside request through (HTTP $code). The web ingress will be closed."; fi
elif [ "$GATE" = "entra" ]; then
  expected="302-or-401"
  run_probe "gate, Microsoft sign-in" probe_simple_gate || true
  if [ "$gate_failed" -ne 0 ] && is_2xx; then breach "GATE NOT ENFORCED: access=team was asked for, but an anonymous request still gets the page (HTTP $code) with no Microsoft sign-in. The web ingress will be closed."; fi
else
  # invited: a readiness wait (no verdict on what the page answers), then the six probes, in order. Each must pass
  # before the next runs, and a 2xx page counts as correct only in probe 6, after probes 1 to 5 have passed.
  # The wait has the whole window to itself; the six probes then get a fresh one.
  if run_probe "wait, Microsoft sign-in layer gone" probe_entra_layer_gone; then
    gate_deadline=$(( $(date +%s) + gate_wait_seconds ))
    run_probe "probe 1, dev login absent" probe_dev_login \
      && run_probe "probe 2, sign-in methods" probe_methods \
      && run_probe "probe 3, anonymous refused" probe_anonymous_refused \
      && run_probe "probe 4, Google sign-in start" probe_signin_start \
      && run_probe "probe 5, callback with unknown state" probe_callback_bogus_state \
      && run_probe "probe 6, sign-in page" probe_signin_page \
      || true
  fi
fi

out "breach=false"
if [ "$mode_failed" -eq 0 ] && [ "$gate_failed" -eq 0 ] && [ "$not_public_failed" -eq 0 ]; then
  if [ "$asked_public" = "true" ]; then
    # gate_verified means exactly: the site IS public and its gate was proven. Written BEFORE the health verdict on
    # purpose: a slow or unhealthy revision behind a proven gate must not close the ingress; an unproven gate always does.
    out "gate_verified=true"
  else
    # closed: nothing is public, so there is no gate to verify. Say what was shown instead.
    out "closed_verified=true"
  fi
  if [ "$rehearsal" = "1" ]; then echo "Rehearsal: all six invited probes passed. Nothing was written to the workflow outputs."; fi
fi
if [ "$health_failed" -ne 0 ] || [ "$mode_failed" -ne 0 ] || [ "$gate_failed" -ne 0 ] || [ "$not_public_failed" -ne 0 ]; then exit 1; fi
echo "Smoke check passed. Not covered: an invited person signing in with Google and using the app end to end."
