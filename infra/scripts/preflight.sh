#!/usr/bin/env bash
# Checks the deploy inputs BEFORE anything touches Azure, and names the access gate this run will have.
# Used by .github/workflows/deploy-azure-dev.yml. Prints names and verdicts only, never a value: the repository is
# public, so its workflow logs are public too. Case tests: infra/scripts/tests/preflight-cases.sh.
#
# ADR-022 (docs/architecture/go-live-signin-adr.md) section 2: the API always runs as Production with Google sign-in
# limited to the Tether invite list, so the Google, invite-list and key-ring values are REQUIRED WHENEVER SOMETHING IS
# DEPLOYED (team and invited). A missing one stops the run here; nothing ever falls back to another sign-in.
#
# access=closed is the emergency stop and must always be able to run (Lead ruling F1, 2026-11-27): it deploys nothing
# and only makes the web ingress internal (infra/scripts/close-web.sh), so it needs ONLY the three Azure sign-in ids.
# Every other value, and the gate_only and release_sha inputs, may be missing or malformed and is not looked at.
#
# Reads (environment):
#   TETHER_ACCESS                                             team | invited | closed (the workflow input `access`)
#   TETHER_GATE_ONLY                                          true | false (default false): change the access mode only, no image build
#   TETHER_RELEASE_SHA                                        optional: full commit id of the release to deploy instead of the newest commit
#   AZURE_CLIENT_ID, AZURE_TENANT_ID, AZURE_SUBSCRIPTION_ID   identifiers, stored as GitHub environment secrets
#   TETHER_DB_ADMIN_PASSWORD, TETHER_ROOT_SECRET              GitHub environment secrets (required)
#   TETHER_GOOGLE_CLIENT_ID, TETHER_GOOGLE_CLIENT_SECRET      Google sign-in (required)
#   TETHER_INVITED_EMAILS                                     the invite list: comma-separated, 1 to 100 addresses (required)
#   TETHER_DP_CERT_PFX                                        key-ring certificate, base64 PFX (required)
#   TETHER_DP_CERT_PFX_PREVIOUS                               the certificate before the last rotation (optional)
#   TETHER_EASYAUTH_CLIENT_ID, TETHER_EASYAUTH_CLIENT_SECRET  Entra sign-in in front: required for team, otherwise both or neither
#   TETHER_ALLOWED_IP_RANGES                                  optional extra layer on a public mode (never a gate alone): IPv4 or CIDR (/16 to /32)
# Writes to $GITHUB_OUTPUT (when set) and to stdout:
#   access=team|invited|closed
#   gate=closed|entra|entra+ip|google|google+ip
# Exit code 1 when any input this access mode needs is missing or malformed.
set -euo pipefail

failed=0
bad() { echo "::error::$*"; failed=1; }
mask() { if [ -n "$1" ]; then echo "::add-mask::$1"; fi; }
guid_re='^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$'

# --- Access mode and run shape ---
access="${TETHER_ACCESS:-}"
case "$access" in
  team|invited|closed) ;;
  *) bad "The access mode is missing or unknown. Choose team, invited or closed when starting the workflow."; access="" ;;
esac
for name in AZURE_CLIENT_ID AZURE_TENANT_ID AZURE_SUBSCRIPTION_ID; do
  value="${!name:-}"
  if [[ ! "$value" =~ $guid_re ]]; then
    bad "$name is missing or is not a GUID. Set it as a secret on the GitHub environment azure-dev (output of scripts/azure/bootstrap-tether-dev.ps1)."
  fi
done

# --- access=closed: the emergency stop. Nothing below this block applies to it. ---
if [ "$access" = "closed" ]; then
  # Still hide anything personal or identifying that happens to be set, in case a later step prints it.
  mask "$(printf '%s' "${TETHER_GOOGLE_CLIENT_ID:-}" | tr -d '[:space:]')"
  IFS=',' read -r -a closed_masks <<< "$(printf '%s' "${TETHER_INVITED_EMAILS:-},${TETHER_ALLOWED_IP_RANGES:-}" | tr -d '\r\n')"
  for raw in "${closed_masks[@]}"; do mask "$(printf '%s' "$raw" | tr -d '[:space:]')"; done
  if [ "$failed" -ne 0 ]; then
    echo "Preflight failed. Nothing was changed."
    exit 1
  fi
  echo "access=closed"
  echo "gate=closed"
  if [ -n "${GITHUB_OUTPUT:-}" ]; then
    echo "access=closed" >> "$GITHUB_OUTPUT"
    echo "gate=closed" >> "$GITHUB_OUTPUT"
  fi
  echo "Access for this run: closed. The web app will be made INTERNAL only (not reachable from the internet). Nothing is deployed and the API is not touched; only the Azure sign-in ids were checked."
  exit 0
fi

gate_only="${TETHER_GATE_ONLY:-false}"
case "$gate_only" in
  true|false) ;;
  *) bad "TETHER_GATE_ONLY must be true or false." ;;
esac
release_sha="${TETHER_RELEASE_SHA:-}"
if [ -n "$release_sha" ] && [[ ! "$release_sha" =~ ^[0-9a-f]{40}$ ]]; then
  bad "release_sha must be a full commit id (40 lower-case hex characters), or empty for the newest commit on main."
fi

# --- Database administrator password: goes into a connection string, so the character set is restricted. ---
password="${TETHER_DB_ADMIN_PASSWORD:-}"
if [ -z "$password" ]; then
  bad "Secret TETHER_DB_ADMIN_PASSWORD is not set on the GitHub environment azure-dev. Run scripts/azure/set-deploy-secrets.ps1 once."
elif [[ ! "$password" =~ ^[A-Za-z0-9_-]{24,128}$ ]]; then
  bad "Secret TETHER_DB_ADMIN_PASSWORD must be 24 to 128 characters from letters, digits, hyphen and underscore. Run scripts/azure/set-deploy-secrets.ps1 -Rotate DbPassword."
else
  classes=0
  [[ "$password" =~ [A-Z] ]] && classes=$((classes + 1))
  [[ "$password" =~ [a-z] ]] && classes=$((classes + 1))
  [[ "$password" =~ [0-9] ]] && classes=$((classes + 1))
  [[ "$password" =~ [_-] ]] && classes=$((classes + 1))
  if [ "$classes" -lt 3 ]; then
    bad "Secret TETHER_DB_ADMIN_PASSWORD needs three of: upper case, lower case, digit, hyphen or underscore (Azure PostgreSQL rule)."
  fi
fi

# --- Security:RootSecret: base64 of at least 32 bytes (the API refuses anything else). ---
root_secret="${TETHER_ROOT_SECRET:-}"
if [ -z "$root_secret" ]; then
  bad "Secret TETHER_ROOT_SECRET is not set on the GitHub environment azure-dev. Run scripts/azure/set-deploy-secrets.ps1 once."
elif [[ ! "$root_secret" =~ ^[A-Za-z0-9+/]+={0,2}$ ]]; then
  bad "Secret TETHER_ROOT_SECRET is not base64."
else
  decoded_bytes="$(printf '%s' "$root_secret" | base64 -d 2>/dev/null | wc -c | tr -d '[:space:]')" || decoded_bytes=0
  if [ "${decoded_bytes:-0}" -lt 32 ]; then
    bad "Secret TETHER_ROOT_SECRET must decode to at least 32 bytes."
  fi
fi

# --- Google sign-in (required in every access mode). The pattern is the one the API checks at startup. ---
google_id="${TETHER_GOOGLE_CLIENT_ID:-}"
google_secret="${TETHER_GOOGLE_CLIENT_SECRET:-}"
mask "$google_id"
if [ -z "$google_id" ]; then
  bad "Secret GOOGLE_CLIENT_ID is not set on the GitHub environment azure-dev. Run scripts/azure/set-google-signin.ps1."
elif [[ ! "$google_id" =~ ^[0-9]+-[a-z0-9]+\.apps\.googleusercontent\.com$ ]]; then
  bad "Secret GOOGLE_CLIENT_ID does not look like a Google OAuth client id (digits, a hyphen, letters and digits, then .apps.googleusercontent.com). Run scripts/azure/set-google-signin.ps1 again."
fi
if [ -z "$google_secret" ]; then
  bad "Secret GOOGLE_CLIENT_SECRET is not set on the GitHub environment azure-dev. Run scripts/azure/set-google-signin.ps1."
elif [ "${#google_secret}" -lt 16 ] || [ "${#google_secret}" -gt 256 ] || [[ "$google_secret" =~ [[:space:][:cntrl:]] ]]; then
  bad "Secret GOOGLE_CLIENT_SECRET is too short or holds a space or line break (a paste slip). Run scripts/azure/set-google-signin.ps1 again."
fi

# --- Invite list. Every entry is masked in the log before anything else could print it; only the count is reported. ---
invite_count=0
invite_raw="${TETHER_INVITED_EMAILS:-}"
if [ -z "$(printf '%s' "$invite_raw" | tr -d '[:space:]')" ]; then
  bad "Secret TETHER_INVITED_EMAILS is not set on the GitHub environment azure-dev. Run scripts/azure/set-invites.ps1 -Add <address>."
else
  IFS=',' read -r -a invites <<< "$(printf '%s' "$invite_raw" | tr -d '\r\n')"
  for raw in "${invites[@]}"; do mask "$(printf '%s' "$raw" | tr -d '[:space:]')"; mask "$(printf '%s' "$raw" | tr -d '[:space:]' | tr '[:upper:]' '[:lower:]')"; done
  declare -A seen=()
  invite_bad=0
  invite_duplicate=0
  for raw in "${invites[@]}"; do
    # The API trims and lower-cases each entry (ADR-022 integration point 5); judge the same normalised form.
    entry="$(printf '%s' "$raw" | sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//' | tr '[:upper:]' '[:lower:]')"
    invite_count=$((invite_count + 1))
    if [ "${#entry}" -gt 320 ] || [[ ! "$entry" =~ ^[^[:space:]@,]+@[^[:space:]@,]+\.[^[:space:]@,]+$ ]]; then
      invite_bad=$((invite_bad + 1))
    elif [ -n "${seen[$entry]:-}" ]; then
      invite_duplicate=$((invite_duplicate + 1))
    else
      seen[$entry]=1
    fi
  done
  if [ "$invite_bad" -gt 0 ]; then
    bad "Secret TETHER_INVITED_EMAILS has $invite_bad entry(ies) that are empty or not an email address. Run scripts/azure/set-invites.ps1 -List, fix the list, then -Add or -Remove to write it again."
  fi
  if [ "$invite_duplicate" -gt 0 ]; then
    bad "Secret TETHER_INVITED_EMAILS lists $invite_duplicate address(es) twice. The API refuses a list with a duplicate. Run scripts/azure/set-invites.ps1 to write it again."
  fi
  if [ "$invite_count" -gt 100 ]; then
    bad "Secret TETHER_INVITED_EMAILS has $invite_count entries; the limit is 100 (Google's test-user cap)."
  fi
fi

# --- Key-ring certificate(s): base64 of a PFX. Shape only (a DER sequence of a plausible size); the API does the real
#     check at startup (private key present, RSA of at least 2048 bits). ---
check_pfx() { # $1 = secret name for messages, $2 = value, $3 = the script hint
  local name="$1" value bytes first
  value="$(printf '%s' "$2" | tr -d '[:space:]')"
  if [[ ! "$value" =~ ^[A-Za-z0-9+/]+={0,2}$ ]]; then
    bad "Secret $name is not base64. $3"
    return
  fi
  bytes="$(printf '%s' "$value" | base64 -d 2>/dev/null | wc -c | tr -d '[:space:]')" || bytes=0
  # A PFX is a DER sequence with a two-byte length: bytes 0x30 0x82, which in base64 is "MI" followed by I, J, K or L.
  # Read from the text itself, not by decoding into `head -c 1`: a reader that stops early can make the pipeline fail
  # under pipefail and turn a good certificate into a failed check now and then.
  first=no
  if [[ "$value" =~ ^MI[I-L] ]]; then first=yes; fi
  if [ "${bytes:-0}" -lt 1000 ] || [ "${bytes:-0}" -gt 20000 ] || [ "$first" != "yes" ]; then
    bad "Secret $name does not hold a certificate file (PFX). $3"
  fi
}
if [ -z "${TETHER_DP_CERT_PFX:-}" ]; then
  bad "Secret DP_CERT_PFX is not set on the GitHub environment azure-dev. Run scripts/azure/set-google-signin.ps1 (it creates the key-ring certificate once)."
else
  check_pfx DP_CERT_PFX "$TETHER_DP_CERT_PFX" "Run scripts/azure/set-google-signin.ps1 -RotateKeyRingCertificate (this signs everyone out once)."
fi
if [ -n "${TETHER_DP_CERT_PFX_PREVIOUS:-}" ]; then
  check_pfx DP_CERT_PFX_PREVIOUS "$TETHER_DP_CERT_PFX_PREVIOUS" "Delete that secret on the GitHub environment azure-dev (people who signed in before the last rotation sign in again)."
fi

# --- Entra sign-in in front: client id and client secret, both or neither; required for access=team. ---
entra=false
client_id="${TETHER_EASYAUTH_CLIENT_ID:-}"
client_secret="${TETHER_EASYAUTH_CLIENT_SECRET:-}"
if [ -n "$client_id" ] && [ -n "$client_secret" ]; then
  if [[ "$client_id" =~ $guid_re ]]; then
    entra=true
  else
    bad "Secret EASYAUTH_CLIENT_ID is not a GUID. Re-run scripts/azure/create-signin-app.ps1."
  fi
elif [ -n "$client_id" ] || [ -n "$client_secret" ]; then
  bad "Only one of the secrets EASYAUTH_CLIENT_ID and EASYAUTH_CLIENT_SECRET is set. Re-run scripts/azure/create-signin-app.ps1 (it sets both)."
fi
if [ "$access" = "team" ] && [ "$entra" != true ] && [ -z "$client_id" ] && [ -z "$client_secret" ]; then
  bad "access=team puts Microsoft sign-in in front of the site, but the secrets EASYAUTH_CLIENT_ID and EASYAUTH_CLIENT_SECRET are not set. Run scripts/azure/create-signin-app.ps1 first, or choose invited or closed."
fi

# --- Optional IP allowlist. Each entry is masked in the log before anything else could print it. ---
ip=false
entries=0
IFS=',' read -r -a ranges <<< "${TETHER_ALLOWED_IP_RANGES:-}"
for raw in "${ranges[@]}"; do
  entry="$(printf '%s' "$raw" | tr -d '[:space:]')"
  [ -n "$entry" ] || continue
  echo "::add-mask::$entry"
  entries=$((entries + 1))
  if [[ "$entry" =~ ^([0-9]{1,3})\.([0-9]{1,3})\.([0-9]{1,3})\.([0-9]{1,3})(/([0-9]{1,2}))?$ ]]; then
    octets_ok=true
    for octet in "${BASH_REMATCH[1]}" "${BASH_REMATCH[2]}" "${BASH_REMATCH[3]}" "${BASH_REMATCH[4]}"; do
      [ "$((10#$octet))" -le 255 ] || octets_ok=false
    done
    prefix="${BASH_REMATCH[6]:-32}"
    if [ "$octets_ok" != true ]; then
      bad "ALLOWED_IP_RANGES entry $entries is not a valid IPv4 address."
    elif [ "$((10#$prefix))" -lt 16 ] || [ "$((10#$prefix))" -gt 32 ]; then
      bad "ALLOWED_IP_RANGES entry $entries has a prefix outside /16 to /32. A wider range is not an allowlist."
    fi
  else
    bad "ALLOWED_IP_RANGES entry $entries is not an IPv4 address or CIDR range (for example 203.0.113.7 or 203.0.113.0/24)."
  fi
done
[ "$entries" -gt 0 ] && ip=true

if [ "$failed" -ne 0 ]; then
  echo "Preflight failed. Nothing was deployed."
  exit 1
fi

# Only team and invited reach this point (closed left above).
case "$access" in
  team) gate="entra" ;;
  invited) gate="google" ;;
esac
if [ "$ip" = true ]; then gate="${gate}+ip"; fi

echo "access=$access"
echo "gate=$gate"
if [ -n "${GITHUB_OUTPUT:-}" ]; then
  echo "access=$access" >> "$GITHUB_OUTPUT"
  echo "gate=$gate" >> "$GITHUB_OUTPUT"
fi
echo "Invite list: $invite_count address(es)."
if [ "$invite_count" -gt 1 ]; then
  # PO-20ax: 'download my data' and 'delete my account' must be built and tested before anyone but the Product Owner uses the site.
  echo "::notice::The invite list has more than one address. Export-all and delete-account (PO-20ax) must be in place before a second person is told to sign in."
fi
case "$access" in
  team) echo "Access for this run: team. Microsoft (Entra) sign-in in front of the site; Google sign-in for invited accounts behind it." ;;
  invited) echo "Access for this run: invited. No Microsoft sign-in; Google sign-in limited to the invite list is the gate." ;;
esac
