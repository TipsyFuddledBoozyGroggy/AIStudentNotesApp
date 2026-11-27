#!/usr/bin/env bash
# Case tests for infra/scripts/smoke.sh and infra/scripts/api-signin-mode.sh (ADR-022 section 2).
# Nothing here touches Azure, GitHub or Google: the site is infra/scripts/tests/fake-site.mjs on localhost with a
# throwaway certificate, and `az` is infra/scripts/tests/fake-az.sh reading fixture files.
#
# Needs: bash, jq, curl, openssl, node. On a machine without jq (Git Bash on Windows), run it in a container, for example:
#   docker run --rm --network none -v "<repo>:/repo:ro" -w /repo <an image with bash, jq, curl, openssl and node> \
#     bash infra/scripts/tests/smoke-cases.sh
# Exit code 0 when every case behaves as expected, 1 otherwise. Takes about two minutes (the retry cases wait on purpose).
set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
scripts="$here/.."
for tool in jq curl openssl node; do
  command -v "$tool" > /dev/null || { echo "smoke-cases.sh needs $tool"; exit 1; }
done

port="${SMOKE_TEST_PORT:-18443}"
tmp="$(mktemp -d)"
site_pid=""
stop_site() { if [ -n "$site_pid" ]; then kill "$site_pid" 2>/dev/null; wait "$site_pid" 2>/dev/null; site_pid=""; fi; }
trap 'stop_site; rm -rf "$tmp"' EXIT

openssl req -x509 -newkey rsa:2048 -nodes -days 2 -subj "/CN=localhost" -addext "subjectAltName=DNS:localhost" \
  -keyout "$tmp/key.pem" -out "$tmp/cert.pem" 2>/dev/null || { echo "could not create the throwaway certificate"; exit 1; }
mkdir -p "$tmp/bin"
cp "$here/fake-az.sh" "$tmp/bin/az"
chmod +x "$tmp/bin/az"

API_IMAGE="ghcr.io/example/tether-api:1111111111111111111111111111111111111111"
WEB_IMAGE="ghcr.io/example/tether-web:1111111111111111111111111111111111111111"
ENTRA_ID="12345678-90ab-4cde-8f01-234567890abc"
pass=0
fail=0

# write_azure <dir> [key=value ...]   Fixture set for the fake az. Keys and defaults:
#   env=Production  devlogin=no|yes|lower  active=1|2  ready=yes|no  auth=off|on|missing|other-client  image=<ref>  api=yes|no
#   web=external|internal|no-ingress|missing (the web app's ingress, for close-web.sh)
#   mark=<file>[,<file>...]  empty marker files the fake az reacts to: az-down, auth-unreadable, patch-refused, patch-ignored, cli-refused, cli-ignored
write_azure() {
  local dir="$1"; shift
  local env=Production devlogin=no active=1 ready=yes auth=off image="$API_IMAGE" api=yes web=external mark="" kv
  for kv in "$@"; do
    case "${kv%%=*}" in
      env) env="${kv#*=}" ;; devlogin) devlogin="${kv#*=}" ;; active) active="${kv#*=}" ;; ready) ready="${kv#*=}" ;;
      auth) auth="${kv#*=}" ;; image) image="${kv#*=}" ;; api) api="${kv#*=}" ;; web) web="${kv#*=}" ;; mark) mark="${kv#*=}" ;;
    esac
  done
  rm -rf "$dir"; mkdir -p "$dir"
  local sub="/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg/providers/Microsoft.App/containerApps"
  local extra_env='[]'
  case "$devlogin" in
    yes) extra_env='[{"name":"Auth__DevLogin__Enabled","value":"false"}]' ;;
    lower) extra_env='[{"name":"auth__devlogin__enabled","value":"true"}]' ;;
  esac
  local ready_name="tether-api--r2"
  [ "$ready" = "yes" ] || ready_name="tether-api--r1"
  : > "$dir/names.txt"
  if [ "$api" = "yes" ]; then
    printf 'tether-api\n' >> "$dir/names.txt"
    jq -n --arg id "$sub/tether-api" --arg ready "$ready_name" \
      '{id:$id, properties:{latestRevisionName:"tether-api--r2", latestReadyRevisionName:$ready, template:{scale:{minReplicas:1}}}}' > "$dir/app-tether-api.json"
    jq -n --arg env "$env" --arg image "$image" --argjson extra "$extra_env" --argjson two "$([ "$active" = "2" ] && echo true || echo false)" '
      def rev($name; $active): {name:$name, properties:{active:$active, provisioningState:"Provisioned", healthState:"Healthy", runningState:"Running", replicas:1,
        template:{containers:[{name:"api", image:$image, env:([{name:"ASPNETCORE_ENVIRONMENT", value:$env}, {name:"ConnectionStrings__Default", secretRef:"db-connection"}] + $extra)}]}}};
      {value:[rev("tether-api--r1"; $two), rev("tether-api--r2"; true)]}' > "$dir/revisions-tether-api.json"
  fi
  if [ "$web" != "missing" ]; then
    printf 'tether-web\n' >> "$dir/names.txt"
    local ingress='{"external":true,"targetPort":3000}'
    case "$web" in internal) ingress='{"external":false,"targetPort":3000}' ;; no-ingress) ingress='null' ;; esac
    jq -n --arg id "$sub/tether-web" --argjson ingress "$ingress" \
      '{id:$id, properties:{latestRevisionName:"tether-web--r1", latestReadyRevisionName:"tether-web--r1", configuration:{ingress:$ingress},
        environmentId:"/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg/providers/Microsoft.App/managedEnvironments/env",
        template:{scale:{minReplicas:0}}}}' > "$dir/app-tether-web.json"
    jq -n --arg image "$WEB_IMAGE" '{value:[{name:"tether-web--r1", properties:{active:true, provisioningState:"Provisioned", healthState:"Healthy", runningState:"Running", replicas:0,
        template:{containers:[{name:"web", image:$image, env:[]}]}}}]}' > "$dir/revisions-tether-web.json"
  fi
  local marker
  local -a markers=()
  IFS=',' read -r -a markers <<< "$mark"
  for marker in "${markers[@]}"; do [ -z "$marker" ] || touch "$dir/$marker"; done
  case "$auth" in
    on) jq -n --arg id "$ENTRA_ID" '{properties:{platform:{enabled:true}, globalValidation:{unauthenticatedClientAction:"RedirectToLoginPage"}, identityProviders:{azureActiveDirectory:{enabled:true, registration:{clientId:$id}}}}}' > "$dir/auth.json" ;;
    other-client) jq -n '{properties:{platform:{enabled:true}, globalValidation:{unauthenticatedClientAction:"RedirectToLoginPage"}, identityProviders:{azureActiveDirectory:{enabled:true, registration:{clientId:"99999999-9999-4999-8999-999999999999"}}}}}' > "$dir/auth.json" ;;
    off) jq -n '{properties:{platform:{enabled:false}, globalValidation:{unauthenticatedClientAction:"AllowAnonymous"}, identityProviders:{}}}' > "$dir/auth.json" ;;
    missing) ;;
  esac
}

start_site() { # $1 = scenario
  stop_site
  PORT="$port" SCENARIO="$1" TLS_KEY="$tmp/key.pem" TLS_CERT="$tmp/cert.pem" node "$here/fake-site.mjs" > "$tmp/site.log" 2>&1 &
  site_pid=$!
  for _ in $(seq 1 50); do
    if [ "$(curl --silent --cacert "$tmp/cert.pem" --output /dev/null --write-out '%{http_code}' "https://localhost:${port}/ready" 2>/dev/null)" != "000" ]; then return 0; fi
    sleep 0.1
  done
  echo "the fake site did not start"; cat "$tmp/site.log"; return 1
}

report() { # $1 name, $2 problems, $3 output
  if [ -z "$2" ]; then pass=$((pass + 1)); echo "ok    $1"
  else
    fail=$((fail + 1)); echo "FAIL  $1: $2"
    local line
    while IFS= read -r line; do echo "        | $line"; done <<< "$3"
  fi
}

# smoke_case "<name>" <scenario> <gate> <web_public> <want exit> <want gate_verified yes|no|closed> <want breach yes|no> [azure key=value ...] [-- VAR=value ...]
#   The wait for the Microsoft layer needs ONE clear answer here (fast); the cases about that wait ask for the real three.
smoke_case() {
  local name="$1" scenario="$2" gate="$3" web_public="$4" want_rc="$5" want_verified="$6" want_breach="$7"
  shift 7
  local -a azure=() extra=()
  while [ "$#" -gt 0 ] && [ "$1" != "--" ]; do azure+=("$1"); shift; done
  [ "$#" -gt 0 ] && shift
  extra=("$@")
  write_azure "$tmp/az" "${azure[@]}"
  start_site "$scenario" || { report "$name" "fake site failed to start" ""; return; }
  : > "$tmp/github-output"
  local output rc=0
  output="$(env PATH="$tmp/bin:$PATH" FAKE_AZ_DIR="$tmp/az" CURL_CA_BUNDLE="$tmp/cert.pem" GITHUB_OUTPUT="$tmp/github-output" \
    RESOURCE_GROUP=rg API_APP_NAME=tether-api WEB_APP_NAME=tether-web WEB_FQDN="localhost:${port}" WEB_PUBLIC="$web_public" GATE="$gate" \
    EXPECTED_API_IMAGE="$API_IMAGE" TETHER_EASYAUTH_CLIENT_ID="$ENTRA_ID" \
    SMOKE_WAIT_SECONDS=2 SMOKE_GATE_WAIT_SECONDS=4 SMOKE_POLL_SECONDS=1 SMOKE_ENTRA_CLEAR_ANSWERS=1 "${extra[@]}" bash "$scripts/smoke.sh" 2>&1)" || rc=$?
  stop_site
  local problems="" verified=no breached=no
  grep -q -x 'gate_verified=true' "$tmp/github-output" && verified=yes
  # closed: nothing is public, so no gate is verified; smoke.sh says closed_verified instead.
  if [ "$verified" = "no" ] && grep -q -x 'closed_verified=true' "$tmp/github-output"; then verified=closed; fi
  grep -q -x 'breach=true' "$tmp/github-output" && breached=yes
  [ "$rc" -eq "$want_rc" ] || problems="exit code $rc, expected $want_rc"
  [ "$verified" = "$want_verified" ] || problems="${problems:+$problems; }gate_verified written: $verified, expected $want_verified"
  [ "$breached" = "$want_breach" ] || problems="${problems:+$problems; }breach written: $breached, expected $want_breach"
  # The log is public: no web address, URL, cookie or identifier may be printed.
  if grep -q -E 'localhost|https?://|__Host-cc\.signin=|state=|'"$ENTRA_ID" <<< "$output"; then problems="${problems:+$problems; }an address, URL, cookie or identifier was printed"; fi
  report "$name" "$problems" "$output"
}

# mode_case "<name>" "<expected output>" <query> <app> [azure key=value ...] [-- down]
mode_case() {
  local name="$1" want="$2" query="$3" app="$4"
  shift 4
  local -a azure=()
  while [ "$#" -gt 0 ] && [ "$1" != "--" ]; do azure+=("$1"); shift; done
  write_azure "$tmp/az" "${azure[@]}"
  if [ "${2-}" = "down" ]; then touch "$tmp/az/az-down"; fi
  local got rc=0
  got="$(env PATH="$tmp/bin:$PATH" FAKE_AZ_DIR="$tmp/az" bash "$scripts/api-signin-mode.sh" rg "$app" "$query" 2>&1)" || rc=$?
  local problems=""
  [ "$rc" -eq 0 ] || problems="exit code $rc"
  [ "$got" = "$want" ] || problems="${problems:+$problems; }printed '$got', expected '$want'"
  report "$name" "$problems" "$got"
}

# close_case "<name>" <want exit> "<text the output must contain>" <calls: changed|unchanged> [azure key=value ...]
#   changed   = it must have tried to change the web ingress;  unchanged = it must not have sent any change at all.
close_case() {
  local name="$1" want_rc="$2" want_text="$3" want_calls="$4"
  shift 4
  write_azure "$tmp/az" "$@"
  local output rc=0
  output="$(env PATH="$tmp/bin:$PATH" FAKE_AZ_DIR="$tmp/az" CLOSE_WAIT_SECONDS=2 CLOSE_POLL_SECONDS=1 \
    bash "$scripts/close-web.sh" rg tether-web "$tmp/close.err" 2>&1)" || rc=$?
  local problems="" calls=""
  [ -f "$tmp/az/calls.log" ] && calls="$(cat "$tmp/az/calls.log")"
  [ "$rc" -eq "$want_rc" ] || problems="exit code $rc, expected $want_rc"
  grep -q -F -- "$want_text" <<< "$output" || problems="${problems:+$problems; }output lacks '$want_text'"
  # Azure's own error text goes to the file the caller masks, never straight to the (public) log.
  if grep -q -F 'leaky-domain' <<< "$output"; then problems="${problems:+$problems; }Azure error text was printed"; fi
  # The emergency stop touches the web app only: never the API, never a deployment.
  if grep -q -E 'tether-api|deployment' <<< "$calls"; then problems="${problems:+$problems; }it called something about the API or a deployment"; fi
  local changed=unchanged
  if grep -q -E ' patch |containerapp ingress update' <<< "$calls"; then changed=changed; fi
  [ "$changed" = "$want_calls" ] || problems="${problems:+$problems; }change calls: $changed, expected $want_calls"
  report "$name" "$problems" "$output"
}

echo "== close-web.sh (the emergency stop and the first move of fail-closed) =="
close_case "public ingress -> made internal by the one-field PATCH"        0 "no public address"           changed   web=external
close_case "already internal -> nothing is changed"                        0 "already internal"            unchanged web=internal
close_case "no ingress at all -> nothing is changed"                       0 "has no ingress"              unchanged web=no-ingress
close_case "the web app does not exist -> nothing to close"                0 "does not exist"              unchanged web=missing
close_case "works with no API app at all"                                  0 "no public address"           changed   web=external api=no
close_case "PATCH refused -> the second way closes it"                     0 "no public address"           changed   web=external mark=patch-refused
close_case "PATCH accepted but nothing changed -> the second way closes it" 0 "no public address"          changed   web=external mark=patch-ignored
close_case "both ways refused -> fails loudly with the portal step"        1 "COULD NOT BE CONFIRMED"      changed   web=external mark=patch-refused,cli-refused
close_case "both ways accepted but nothing changed -> fails loudly"        1 "COULD NOT BE CONFIRMED"      changed   web=external mark=patch-ignored,cli-ignored
close_case "Azure cannot be read -> fails loudly"                          1 "COULD NOT BE CONFIRMED"      changed   web=external mark=az-down

# live_case "<name>" <want exit> "<text the output must contain>" <images: as-given|adopted-live|-> ACCESS GATE_ONLY RELEASE_SHA <commit: live|ahead> [azure key=value ...]
#   commit=live: the run's images are the ones that are live. commit=ahead: main has moved on, the run's images are newer.
live_case() {
  local name="$1" want_rc="$2" want_text="$3" want_images="$4" access="$5" gate_only="$6" release="$7" commit="$8"
  shift 8
  write_azure "$tmp/az" "$@"
  local api_image="$API_IMAGE" web_image="$WEB_IMAGE"
  if [ "$commit" = "ahead" ]; then
    api_image="ghcr.io/example/tether-api:3333333333333333333333333333333333333333"
    web_image="ghcr.io/example/tether-web:3333333333333333333333333333333333333333"
  fi
  : > "$tmp/github-output"; : > "$tmp/github-env"
  local output rc=0
  output="$(env PATH="$tmp/bin:$PATH" FAKE_AZ_DIR="$tmp/az" GITHUB_OUTPUT="$tmp/github-output" GITHUB_ENV="$tmp/github-env" \
    ACCESS="$access" GATE_ONLY="$gate_only" RELEASE_SHA="$release" BUILT_IN_THIS_RUN=false RESOURCE_GROUP=rg \
    TETHER_API_APP_NAME=tether-api TETHER_WEB_APP_NAME=tether-web TETHER_API_IMAGE="$api_image" TETHER_WEB_IMAGE="$web_image" \
    TETHER_EASYAUTH_CLIENT_ID="$ENTRA_ID" bash "$scripts/live-rules.sh" 2>&1)" || rc=$?
  local problems="" images="-"
  grep -q -x 'images=as-given' "$tmp/github-output" && images=as-given
  grep -q -x 'images=adopted-live' "$tmp/github-output" && images=adopted-live
  [ "$rc" -eq "$want_rc" ] || problems="exit code $rc, expected $want_rc"
  grep -q -F -- "$want_text" <<< "$output" || problems="${problems:+$problems; }output lacks '$want_text'"
  [ "$images" = "$want_images" ] || problems="${problems:+$problems; }images: $images, expected $want_images"
  if [ "$want_images" = "adopted-live" ]; then
    # The later steps must deploy exactly what is live, for both apps.
    grep -q -x "TETHER_API_IMAGE=$API_IMAGE" "$tmp/github-env" && grep -q -x "TETHER_WEB_IMAGE=$WEB_IMAGE" "$tmp/github-env" \
      || problems="${problems:+$problems; }the live images were not handed to the later steps"
  elif [ -s "$tmp/github-env" ]; then
    problems="${problems:+$problems; }images were changed although nothing was adopted"
  fi
  if grep -q -E 'ghcr\.io|leaky-domain|'"$ENTRA_ID" <<< "$output"; then problems="${problems:+$problems; }an image reference, Azure error text or identifier was printed"; fi
  report "$name" "$problems" "$output"
}

echo "== live-rules.sh (what a run may do, given what is live) =="
live_case "invited: Production API, Entra off -> allowed"                         0 "Live API sign-in mode: google" as-given     invited false "" live auth=off
live_case "invited: no auth settings at all (404) counts as off -> allowed"       0 "Microsoft sign-in on the web app: off" as-given invited false "" live auth=missing
live_case "invited: the live API is Development -> refused"                       1 "not a Production API without dev login" -   invited true  "" live env=Development auth=on
live_case "invited: Entra on and not gate_only -> refused"                        1 "take the Microsoft gate off AND deploy code" - invited false "" live auth=on
live_case "invited: Entra on, gate_only, live images are this commit -> allowed"  0 "Only the access mode changes" as-given       invited true  "" live auth=on
live_case "invited: the auth settings cannot be read -> unknown -> refused"       1 "could not say whether the Microsoft gate is on or off" - invited true "" live auth=on mark=auth-unreadable
live_case "invited: unreadable settings refuse even without gate_only"            1 "could not say whether the Microsoft gate is on or off" - invited false "" live auth=off mark=auth-unreadable
live_case "invited: gate_only but main has moved on -> refused (strict)"          1 "gate_only was refused" -                      invited true  "" ahead auth=on
live_case "team rollback: gate_only, main has moved on, live API Production -> keeps the live images" 0 "the images that are LIVE are kept" adopted-live team true "" ahead auth=off
live_case "team rollback: same, with Entra already on -> keeps the live images"   0 "the images that are LIVE are kept" adopted-live team true "" ahead auth=on
live_case "team rollback: live API is Development -> refused, nothing adopted"    1 "gate_only was refused" -                      team    true  "" ahead env=Development auth=off
live_case "team rollback: a dev-login variable on the live API -> refused"        1 "gate_only was refused" -                      team    true  "" ahead devlogin=yes auth=off
live_case "team rollback: a release_sha was given and is not what is live -> refused" 1 "gate_only was refused" -                  team    true  "0123456789abcdef0123456789abcdef01234567" ahead auth=off
live_case "team rollback: two revisions active (live image unknown) -> refused"   1 "gate_only was refused" -                      team    true  "" ahead active=2 auth=off
live_case "team: gate_only and the live images are this commit -> nothing adopted" 0 "Only the access mode changes" as-given       team    true  "" live auth=off
live_case "team: an ordinary run is not held to the live images"                  0 "Live API sign-in mode: google" as-given       team    false "" ahead auth=on
live_case "team: unreadable auth settings do not block putting the gate back"     0 "Microsoft sign-in on the web app: unknown" as-given team false "" live auth=on mark=auth-unreadable

# guard_case "<name>" <want exit> "<text>" <file>
guard_case() {
  local name="$1" want_rc="$2" want_text="$3" file="$4" output rc=0 problems=""
  output="$(bash "$scripts/release-guard.sh" "$file" "the template" 2>&1)" || rc=$?
  [ "$rc" -eq "$want_rc" ] || problems="exit code $rc, expected $want_rc"
  grep -q -F -- "$want_text" <<< "$output" || problems="${problems:+$problems; }output lacks '$want_text'"
  report "$name" "$problems" "$output"
}
env_line="{ name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }"
printf '%s\n' "// never sets Auth__DevLogin__Enabled" "  // 'Auth__DevLogin__Enabled' and 'Development' in a comment are fine" "env: [" "  $env_line" "]" > "$tmp/good.bicep"
printf '%s\n' "env: [" "  $env_line" "  { name: 'Auth__DevLogin__Enabled', value: 'true' }" "]" > "$tmp/devlogin.bicep"
printf '%s\n' "env: [" "  $env_line" "  { name: 'AUTH__DEVLOGIN__ENABLED', value: 'false' }" "]" > "$tmp/devlogin-upper.bicep"
printf '%s\n' "env: [" "  { name: 'ASPNETCORE_ENVIRONMENT', value: 'Development' }" "]" > "$tmp/development.bicep"
printf '%s\n' "var e = googleOn ? 'Production' : 'Development'" "env: [" "  { name: 'ASPNETCORE_ENVIRONMENT', value: e }" "]" > "$tmp/branch.bicep"
printf '%s\n' "env: [" "  $env_line" "  $env_line" "]" > "$tmp/twice.bicep"
: > "$tmp/empty.bicep"

echo "== release-guard.sh (the template itself must be free of dev login) =="
guard_case "this repository's infra/main.bicep passes"                   0 "runs the API as Production" "$scripts/../main.bicep"
guard_case "a minimal Production template passes; comments do not count" 0 "runs the API as Production" "$tmp/good.bicep"
guard_case "a dev-login variable fails"                                  1 "declares a dev-login variable" "$tmp/devlogin.bicep"
guard_case "a dev-login variable in another letter case fails"           1 "declares a dev-login variable" "$tmp/devlogin-upper.bicep"
guard_case "Development as the environment fails"                        1 "does not set ASPNETCORE_ENVIRONMENT to the literal 'Production'" "$tmp/development.bicep"
guard_case "an environment chosen by a condition fails"                  1 "does not set ASPNETCORE_ENVIRONMENT to the literal 'Production'" "$tmp/branch.bicep"
guard_case "the environment set twice fails"                             1 "does not set ASPNETCORE_ENVIRONMENT to the literal 'Production'" "$tmp/twice.bicep"
guard_case "an empty or unreadable template fails"                       1 "could not be read" "$tmp/empty.bicep"
guard_case "a missing file fails"                                        1 "could not be read" "$tmp/no-such-file.bicep"

echo "== api-signin-mode.sh =="
mode_case "Production, no dev-login variable, one ready revision -> google"   google mode tether-api
mode_case "Development -> other"                                              other  mode tether-api env=Development
mode_case "lower-case production -> other"                                    other  mode tether-api env=production
mode_case "a dev-login variable, even set to false -> other"                  other  mode tether-api devlogin=yes
mode_case "a dev-login variable in another letter case -> other"              other  mode tether-api devlogin=lower
mode_case "two active revisions -> other"                                     other  mode tether-api active=2
mode_case "newest revision not ready yet -> other"                            other  mode tether-api ready=no
mode_case "the app does not exist -> none"                                    none   mode tether-api api=no
mode_case "Azure cannot be read -> other"                                     other  mode tether-api -- down
mode_case "image of the live revision"                                        "$API_IMAGE" image tether-api
mode_case "image is empty while two revisions are active"                     ""     image tether-api active=2
mode_case "image is empty when the app does not exist"                        ""     image tether-api api=no

echo "== smoke.sh: invited (google), the six outside probes =="
smoke_case "all six probes pass"                                 ok                     google true 0 yes no
smoke_case "scope encoded with plus signs passes"                ok-plus-encoding       google true 0 yes no
smoke_case "probe 1: dev login answers 204 -> breach"            devlogin-204           google true 2 no yes
smoke_case "probe 1: 401 unauthenticated, no cookie -> pass"     devlogin-401           google true 0 yes no
smoke_case "probe 1: 401 with another problem code -> breach"    devlogin-401-other-code google true 2 no yes
smoke_case "probe 1: 401 with no problem code -> tried again, then fails closed (not a breach)" devlogin-401-not-json google true 1 no no
smoke_case "probe 1: 401 with no problem code twice, then the API's 401 -> pass" devlogin-401-nobody-then-ok google true 0 yes no
smoke_case "probe 1: 401 that sets a cookie -> breach"           devlogin-401-set-cookie google true 2 no yes
smoke_case "probe 1: dev login answers 400 -> breach"            devlogin-400           google true 2 no yes
smoke_case "probe 1: 429 twice, then 404 -> pass after retry"    devlogin-429-then-404  google true 0 yes no
smoke_case "probe 1: 503 once, then 404 -> pass after retry"     devlogin-503-then-404  google true 0 yes no
smoke_case "probe 1: 429 until the deadline -> fails closed"     devlogin-429-forever   google true 1 no no
smoke_case "probe 1: 403 (proves nothing) -> fails closed"       devlogin-403           google true 1 no no
smoke_case "probe 2: devLogin true -> breach"                    methods-devlogin-true  google true 2 no yes
smoke_case "probe 2: google false -> fails"                      methods-google-false   google true 1 no no
smoke_case "probe 2: not JSON -> fails"                          methods-not-json       google true 1 no no
smoke_case "probe 2: route missing -> fails closed"              methods-404            google true 1 no no
smoke_case "probe 3: /me answers 200 -> breach"                  me-200                 google true 2 no yes
smoke_case "probe 3: hub negotiate answers 200 -> breach"        negotiate-200          google true 2 no yes
smoke_case "probe 4: URL on another host -> breach"              start-other-host       google true 2 no yes
smoke_case "probe 4: redirect_uri of another site -> fails"      start-wrong-redirect   google true 1 no no
smoke_case "probe 4: response_type not code -> fails"            start-token-response   google true 1 no no
smoke_case "probe 4: challenge method plain -> fails"            start-plain-challenge  google true 1 no no
smoke_case "probe 4: an extra scope -> fails"                    start-extra-scope      google true 1 no no
smoke_case "probe 4: no authorizationUrl -> fails"               start-no-url           google true 1 no no
smoke_case "probe 4: no binder cookie -> fails"                  start-no-cookie        google true 1 no no
smoke_case "probe 4: binder without the __Host- name -> fails"   start-cookie-wrong-name google true 1 no no
smoke_case "probe 4: binder without Secure -> fails"             start-cookie-no-secure google true 1 no no
smoke_case "probe 4: binder without HttpOnly -> fails"           start-cookie-no-httponly google true 1 no no
smoke_case "probe 4: binder SameSite=None -> fails"              start-cookie-samesite-none google true 1 no no
smoke_case "probe 4: binder with a Domain -> fails"              start-cookie-domain    google true 1 no no
smoke_case "probe 4: 429 once, then ok -> pass after retry"      start-429-then-ok      google true 0 yes no
smoke_case "probe 5: callback answers 200 -> breach"             callback-200           google true 2 no yes
smoke_case "probe 5: absolute Location -> fails"                 callback-absolute-location google true 1 no no
smoke_case "probe 5: another outcome -> fails"                   callback-other-outcome google true 1 no no
smoke_case "probe 5: binder not expired -> fails"                callback-binder-kept   google true 1 no no
smoke_case "probe 5: rate_limited once, then ok -> pass"         callback-rate-limited-then-ok google true 0 yes no
smoke_case "probe 6: page without HSTS -> fails"                 signin-no-hsts         google true 1 no no
smoke_case "probe 6: page still redirects -> fails closed"       signin-302             google true 1 no no
smoke_case "Microsoft sign-in still answering -> fails closed"   gate-entra-302         google true 1 no no
smoke_case "Microsoft sign-in layer answers 401 -> waits, then fails closed (not a breach)" gate-entra-401 google true 1 no no

echo "== smoke.sh: invited, the wait for the Microsoft layer to go (three clear answers in a row) =="
smoke_case "three clear answers in a row -> the probes run and pass"            ok                    google true 0 yes no -- SMOKE_ENTRA_CLEAR_ANSWERS=3 SMOKE_GATE_WAIT_SECONDS=8
smoke_case "one answer from the layer in between -> the count starts again, then passes" entra-flap-then-clear google true 0 yes no -- SMOKE_ENTRA_CLEAR_ANSWERS=3 SMOKE_GATE_WAIT_SECONDS=10
smoke_case "the layer keeps answering every other time -> never three in a row, fails closed" entra-flap-forever google true 1 no no -- SMOKE_ENTRA_CLEAR_ANSWERS=3 SMOKE_GATE_WAIT_SECONDS=6

echo "== smoke.sh: invited, the reads from Azure =="
smoke_case "probes pass but the live API is Development -> not verified"   ok google true 1 no no env=Development
smoke_case "probes pass but a dev-login variable exists -> not verified"   ok google true 1 no no devlogin=yes
smoke_case "probes pass but the live image is another one -> not verified" ok google true 1 no no image=ghcr.io/example/tether-api:2222222222222222222222222222222222222222
smoke_case "probes pass but Entra is still reported ON -> not verified"    ok google true 1 no no auth=on
smoke_case "no built-in authentication settings at all (404) counts as off" ok google true 0 yes no auth=missing
smoke_case "the settings cannot be read (not a 404) -> unknown, not verified" ok google true 1 no no auth=off mark=auth-unreadable
smoke_case "invited asked for but the web app is not public -> not verified" gate-404 google false 1 no no
smoke_case "team asked for but the web app is not public -> not verified"    gate-404 entra false 1 no no auth=on

echo "== smoke.sh: team (entra), closed and the IP layer =="
smoke_case "team: 302 to the Microsoft sign-in passes"           gate-entra-302  entra true 0 yes no auth=on
smoke_case "team: 401 passes"                                    gate-entra-401  entra true 0 yes no auth=on
smoke_case "team: page still public at the deadline -> breach"   ok              entra true 2 no yes auth=on
smoke_case "team: public page and API not Production -> breach"  ok              entra true 2 no yes auth=on env=Development
smoke_case "team: Azure reports Entra off -> not verified"       gate-entra-302  entra true 1 no no auth=off
smoke_case "team: Entra on with another app registration -> not verified" gate-entra-302 entra true 1 no no auth=other-client
smoke_case "team + IP layer: 403 passes"                         gate-403        entra+ip true 0 yes no auth=on
smoke_case "team + IP layer: page reachable -> breach"           gate-open-200   entra+ip true 2 no yes auth=on
smoke_case "invited + IP layer: 403 passes"                      gate-403        google+ip true 0 yes no
smoke_case "closed: 404 passes (closed_verified, never gate_verified)" gate-404  closed false 0 closed no
smoke_case "closed: page still reachable -> breach"              gate-open-200   closed false 2 no yes
smoke_case "closed: passes with no API app at all (health is not looked at)" gate-404 closed false 0 closed no api=no
smoke_case "closed: passes when Azure cannot be read (only the outside check counts)" gate-404 closed false 0 closed no mark=az-down
smoke_case "closed: page reachable and Azure unreadable -> still a breach" gate-open-200 closed false 2 no yes mark=az-down
smoke_case "an unknown gate verifies nothing"                    ok              public true 1 no no

echo "== smoke.sh: local rehearsal mode =="
smoke_case "rehearsal passes the six probes and writes NO output"   ok           google true 0 no no -- SMOKE_LOCAL_REHEARSAL=1
smoke_case "rehearsal reports a breach but writes NO output"        devlogin-204 google true 2 no no -- SMOKE_LOCAL_REHEARSAL=1
smoke_case "rehearsal refuses any gate but google"                  gate-404     closed false 1 no no -- SMOKE_LOCAL_REHEARSAL=1

echo ""
echo "smoke cases: $pass passed, $fail failed"
[ "$fail" -eq 0 ]
