#!/usr/bin/env bash
# Case tests for infra/scripts/preflight.sh (ADR-022 section 2). Pure bash: no Azure, no GitHub, no network.
#   bash infra/scripts/tests/preflight-cases.sh
# Every value below is synthetic (example.invalid addresses, random bytes shaped like a certificate file, documentation
# IP ranges). Exit code 0 when every case behaves as expected, 1 otherwise.
set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
preflight="$here/../preflight.sh"
pass=0
fail=0

# A byte string with the outer shape of a PFX (a DER sequence with a two-byte length, plausible size). Preflight checks
# the shape only; the API does the real check at startup.
fake_pfx() { { printf '\x30\x82\x05\xdc'; head -c "${1:-1500}" /dev/urandom; } | base64 | tr -d '\n'; }

GUID_A="11111111-2222-4333-8444-555555555555"
GUID_B="66666666-7777-4888-9999-aaaaaaaaaaaa"
GUID_C="bbbbbbbb-cccc-4ddd-8eee-ffffffffffff"
GUID_ENTRA="12345678-90ab-4cde-8f01-234567890abc"
GOOD_PFX="$(fake_pfx)"
GOOD_ROOT="$(head -c 48 /dev/urandom | base64 | tr -d '\n')"
SECRET_SENTINEL="sentinel-sentinel-sentinel-sentinel"
ID_SENTINEL="123456789012-abcdefghijklmnopqrstuvwxyz012345.apps.googleusercontent.com"
INVITE_SENTINEL="sentinel.person@example.invalid"

# The complete, valid input set. A case overrides or empties single variables.
base_env() {
  cat <<VARS
TETHER_ACCESS=team
TETHER_GATE_ONLY=false
TETHER_RELEASE_SHA=
AZURE_CLIENT_ID=$GUID_A
AZURE_TENANT_ID=$GUID_B
AZURE_SUBSCRIPTION_ID=$GUID_C
TETHER_DB_ADMIN_PASSWORD=Pg1-aaaaaaaaaaaaaaaaaaaaaaaaaaaa
TETHER_ROOT_SECRET=$GOOD_ROOT
TETHER_GOOGLE_CLIENT_ID=$ID_SENTINEL
TETHER_GOOGLE_CLIENT_SECRET=$SECRET_SENTINEL
TETHER_INVITED_EMAILS=$INVITE_SENTINEL
TETHER_DP_CERT_PFX=$GOOD_PFX
TETHER_DP_CERT_PFX_PREVIOUS=
TETHER_EASYAUTH_CLIENT_ID=$GUID_ENTRA
TETHER_EASYAUTH_CLIENT_SECRET=sentinel-entra-sentinel-entra
TETHER_ALLOWED_IP_RANGES=
VARS
}

# run_case "<name>" <expected exit code> "<text the output must contain, or empty>" [VAR=value ...]
run_case() {
  local name="$1" want_rc="$2" want_text="$3"
  shift 3
  local -a envs=()
  mapfile -t envs < <(base_env)
  local override key i replaced
  for override in "$@"; do
    key="${override%%=*}"
    replaced=0
    for i in "${!envs[@]}"; do
      if [ "${envs[$i]%%=*}" = "$key" ]; then envs[i]="$override"; replaced=1; fi
    done
    [ "$replaced" -eq 1 ] || envs+=("$override")
  done
  local output rc=0
  output="$(env -i PATH="$PATH" "${envs[@]}" bash "$preflight" 2>&1)" || rc=$?
  local problems=""
  [ "$rc" -eq "$want_rc" ] || problems="exit code $rc, expected $want_rc"
  if [ -n "$want_text" ] && ! grep -q -F -- "$want_text" <<< "$output"; then problems="${problems:+$problems; }output lacks '$want_text'"; fi
  # No value may be printed. The ::add-mask:: lines are the exception: the runner consumes and hides them.
  local visible leak
  visible="$(grep -v '^::add-mask::' <<< "$output" || true)"
  for leak in "$SECRET_SENTINEL" "$ID_SENTINEL" "$INVITE_SENTINEL" "second.person@example.invalid" "sentinel-entra-sentinel-entra" "$GUID_ENTRA" "203.0.113." "${GOOD_PFX:0:40}" "${GOOD_ROOT:0:20}"; do
    if grep -q -F -- "$leak" <<< "$visible"; then problems="${problems:+$problems; }a value was printed"; fi
  done
  if [ -z "$problems" ]; then
    pass=$((pass + 1)); echo "ok    $name"
  else
    fail=$((fail + 1)); echo "FAIL  $name: $problems"
    while IFS= read -r line; do echo "        | $line"; done <<< "$visible"
  fi
}

many_invites() { local n="$1" i out=""; for i in $(seq 1 "$n"); do out="${out}${out:+,}person${i}@example.invalid"; done; printf '%s' "$out"; }

# --- Gates for each access mode ---
run_case "team with Entra -> entra"                      0 "gate=entra"
run_case "team writes access=team"                       0 "access=team"
run_case "team with Entra and IP layer -> entra+ip"      0 "gate=entra+ip"   TETHER_ALLOWED_IP_RANGES=203.0.113.7,203.0.113.0/24
run_case "invited without Entra -> google"               0 "gate=google"     TETHER_ACCESS=invited TETHER_EASYAUTH_CLIENT_ID= TETHER_EASYAUTH_CLIENT_SECRET=
run_case "invited with Entra values kept -> google"      0 "gate=google"     TETHER_ACCESS=invited
run_case "invited with IP layer -> google+ip"            0 "gate=google+ip"  TETHER_ACCESS=invited TETHER_ALLOWED_IP_RANGES=203.0.113.7
run_case "closed -> closed"                              0 "gate=closed"     TETHER_ACCESS=closed
run_case "closed without Entra -> closed"                0 "gate=closed"     TETHER_ACCESS=closed TETHER_EASYAUTH_CLIENT_ID= TETHER_EASYAUTH_CLIENT_SECRET=
run_case "closed with IP list stays closed"              0 "gate=closed"     TETHER_ACCESS=closed TETHER_ALLOWED_IP_RANGES=203.0.113.7
run_case "gate_only true is accepted"                    0 "gate=google"     TETHER_ACCESS=invited TETHER_GATE_ONLY=true
run_case "release_sha in full form is accepted"          0 "gate=entra"      TETHER_RELEASE_SHA=0123456789abcdef0123456789abcdef01234567

# --- Access mode and run shape ---
run_case "access missing fails"                          1 "access mode is missing or unknown"   TETHER_ACCESS=
run_case "access unknown fails"                          1 "access mode is missing or unknown"   TETHER_ACCESS=public
run_case "access in capitals fails"                      1 "access mode is missing or unknown"   TETHER_ACCESS=TEAM
run_case "gate_only not a boolean fails"                 1 "TETHER_GATE_ONLY must be true or false" TETHER_GATE_ONLY=yes
run_case "release_sha short fails"                       1 "release_sha must be a full commit id" TETHER_RELEASE_SHA=0123abc
run_case "release_sha with a shell character fails"      1 "release_sha must be a full commit id" "TETHER_RELEASE_SHA=0123456789abcdef0123456789abcdef0123456;"

# --- Entra: required for team, both or neither otherwise ---
run_case "team without Entra fails"                      1 "access=team puts Microsoft sign-in in front" TETHER_EASYAUTH_CLIENT_ID= TETHER_EASYAUTH_CLIENT_SECRET=
run_case "team with only the Entra id fails"             1 "Only one of the secrets EASYAUTH_CLIENT_ID" TETHER_EASYAUTH_CLIENT_SECRET=
run_case "invited with only the Entra secret fails"      1 "Only one of the secrets EASYAUTH_CLIENT_ID" TETHER_ACCESS=invited TETHER_EASYAUTH_CLIENT_ID=
run_case "Entra id not a GUID fails"                     1 "EASYAUTH_CLIENT_ID is not a GUID"     TETHER_EASYAUTH_CLIENT_ID=not-a-guid

# --- Google, invite list and certificate are required whenever something is deployed (ADR-022 R1) ---
for mode in team invited; do
  run_case "$mode: Google client id missing fails"       1 "GOOGLE_CLIENT_ID is not set"          "TETHER_ACCESS=$mode" TETHER_GOOGLE_CLIENT_ID=
  run_case "$mode: Google client secret missing fails"   1 "GOOGLE_CLIENT_SECRET is not set"      "TETHER_ACCESS=$mode" TETHER_GOOGLE_CLIENT_SECRET=
  run_case "$mode: invite list missing fails"            1 "TETHER_INVITED_EMAILS is not set"     "TETHER_ACCESS=$mode" TETHER_INVITED_EMAILS=
  run_case "$mode: certificate missing fails"            1 "DP_CERT_PFX is not set"               "TETHER_ACCESS=$mode" TETHER_DP_CERT_PFX=
done

# --- access=closed is the emergency stop: it must run whatever else is missing or broken (Lead ruling F1) ---
run_case "closed: Google client id missing still passes"       0 "gate=closed" TETHER_ACCESS=closed TETHER_GOOGLE_CLIENT_ID=
run_case "closed: Google client secret missing still passes"   0 "gate=closed" TETHER_ACCESS=closed TETHER_GOOGLE_CLIENT_SECRET=
run_case "closed: invite list missing still passes"            0 "gate=closed" TETHER_ACCESS=closed TETHER_INVITED_EMAILS=
run_case "closed: certificate missing still passes"            0 "gate=closed" TETHER_ACCESS=closed TETHER_DP_CERT_PFX=
run_case "closed: every other secret missing still passes"     0 "gate=closed" TETHER_ACCESS=closed TETHER_GOOGLE_CLIENT_ID= TETHER_GOOGLE_CLIENT_SECRET= TETHER_INVITED_EMAILS= TETHER_DP_CERT_PFX= TETHER_DB_ADMIN_PASSWORD= TETHER_ROOT_SECRET= TETHER_EASYAUTH_CLIENT_ID= TETHER_EASYAUTH_CLIENT_SECRET=
run_case "closed: every other secret malformed still passes"   0 "gate=closed" TETHER_ACCESS=closed TETHER_GOOGLE_CLIENT_ID=nonsense TETHER_GOOGLE_CLIENT_SECRET=x "TETHER_INVITED_EMAILS=$INVITE_SENTINEL,not-an-address,$INVITE_SENTINEL" TETHER_DP_CERT_PFX=AAAA TETHER_DP_CERT_PFX_PREVIOUS=AAAA TETHER_DB_ADMIN_PASSWORD=short TETHER_ROOT_SECRET=AAAA TETHER_EASYAUTH_CLIENT_ID=not-a-guid TETHER_ALLOWED_IP_RANGES=203.0.0.0/8
run_case "closed: half-set Entra values still pass"            0 "gate=closed" TETHER_ACCESS=closed TETHER_EASYAUTH_CLIENT_SECRET=
run_case "closed: odd gate_only and release_sha are ignored"   0 "gate=closed" TETHER_ACCESS=closed TETHER_GATE_ONLY=yes TETHER_RELEASE_SHA=0123abc
run_case "closed: says nothing is deployed"                    0 "Nothing is deployed and the API is not touched" TETHER_ACCESS=closed
run_case "closed: an Azure id that is not a GUID still fails"  1 "AZURE_CLIENT_ID is missing or is not a GUID" TETHER_ACCESS=closed AZURE_CLIENT_ID=nope
run_case "closed: a missing Azure id still fails"              1 "AZURE_SUBSCRIPTION_ID is missing or is not a GUID" TETHER_ACCESS=closed AZURE_SUBSCRIPTION_ID=

# --- Google values ---
run_case "client id with capitals fails"                 1 "does not look like a Google OAuth client id" TETHER_GOOGLE_CLIENT_ID=123456789012-ABCDEF.apps.googleusercontent.com
run_case "client id without the digit prefix fails"      1 "does not look like a Google OAuth client id" TETHER_GOOGLE_CLIENT_ID=abcdef.apps.googleusercontent.com
run_case "client id of another domain fails"             1 "does not look like a Google OAuth client id" TETHER_GOOGLE_CLIENT_ID=123456789012-abcdef.apps.example.invalid
run_case "client secret with a space fails"              1 "GOOGLE_CLIENT_SECRET is too short or holds a space" "TETHER_GOOGLE_CLIENT_SECRET=sentinel sentinel-sentinel"
run_case "client secret too short fails"                 1 "GOOGLE_CLIENT_SECRET is too short or holds a space" TETHER_GOOGLE_CLIENT_SECRET=short

# --- Invite list ---
run_case "two addresses pass and are counted"            0 "Invite list: 2 address(es)."          "TETHER_INVITED_EMAILS=$INVITE_SENTINEL,second.person@example.invalid"
run_case "more than one address gives the PO-20ax note"  0 "PO-20ax"                              "TETHER_INVITED_EMAILS=$INVITE_SENTINEL,second.person@example.invalid"
run_case "spaces and capitals are normalised like the API" 0 "Invite list: 2 address(es)."        "TETHER_INVITED_EMAILS= Sentinel.Person@Example.invalid , second.person@example.invalid "
run_case "100 addresses pass"                            0 "Invite list: 100 address(es)."        "TETHER_INVITED_EMAILS=$(many_invites 100)"
run_case "101 addresses fail"                            1 "the limit is 100"                     "TETHER_INVITED_EMAILS=$(many_invites 101)"
run_case "an entry that is not an address fails"         1 "empty or not an email address"        "TETHER_INVITED_EMAILS=$INVITE_SENTINEL,not-an-address"
run_case "an empty entry in the middle fails"            1 "empty or not an email address"        "TETHER_INVITED_EMAILS=$INVITE_SENTINEL,,second.person@example.invalid"
run_case "a duplicate fails"                             1 "twice"                                "TETHER_INVITED_EMAILS=$INVITE_SENTINEL,$INVITE_SENTINEL"
run_case "a duplicate differing only in case fails"      1 "twice"                                "TETHER_INVITED_EMAILS=$INVITE_SENTINEL,SENTINEL.PERSON@example.invalid"
run_case "only spaces counts as missing"                 1 "TETHER_INVITED_EMAILS is not set"     "TETHER_INVITED_EMAILS=   "

# --- Certificate shape ---
run_case "certificate that is not base64 fails"          1 "DP_CERT_PFX is not base64"            "TETHER_DP_CERT_PFX=not base64 at all!"
run_case "certificate too small fails"                   1 "does not hold a certificate file"     "TETHER_DP_CERT_PFX=$(fake_pfx 100)"
run_case "certificate with the wrong first byte fails"   1 "does not hold a certificate file"     "TETHER_DP_CERT_PFX=$(head -c 1500 /dev/zero | base64 | tr -d '\n')"
run_case "certificate with line breaks in the base64 passes" 0 "gate=entra"                       "TETHER_DP_CERT_PFX=$(printf '%s' "$GOOD_PFX" | fold -w 76)"
run_case "a good previous certificate passes"            0 "gate=entra"                           "TETHER_DP_CERT_PFX_PREVIOUS=$(fake_pfx)"
run_case "a bad previous certificate fails"              1 "DP_CERT_PFX_PREVIOUS"                 TETHER_DP_CERT_PFX_PREVIOUS=AAAA

# --- Unchanged rules ---
run_case "Azure id not a GUID fails"                     1 "AZURE_TENANT_ID is missing or is not a GUID" AZURE_TENANT_ID=nope
run_case "database password with a semicolon fails"      1 "TETHER_DB_ADMIN_PASSWORD must be 24 to 128" "TETHER_DB_ADMIN_PASSWORD=Pg1-aaaaaaaaaaaaaaaa;aaaaaaaaa"
run_case "root secret too short fails"                   1 "TETHER_ROOT_SECRET must decode to at least 32 bytes" TETHER_ROOT_SECRET=AAAAAAAA
run_case "IP range wider than /16 fails"                 1 "prefix outside /16 to /32"            TETHER_ALLOWED_IP_RANGES=203.0.0.0/8
run_case "several problems are all reported"             1 "GOOGLE_CLIENT_SECRET is not set"      TETHER_GOOGLE_CLIENT_SECRET= TETHER_DP_CERT_PFX=

echo ""
echo "preflight cases: $pass passed, $fail failed"
[ "$fail" -eq 0 ]
