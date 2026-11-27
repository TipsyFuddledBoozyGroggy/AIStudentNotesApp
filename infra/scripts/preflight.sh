#!/usr/bin/env bash
# Checks the deploy inputs BEFORE anything touches Azure, and decides which access gate this run will have.
# Used by .github/workflows/deploy-azure-dev.yml. Prints names and verdicts only, never a value: the repository is
# public, so its workflow logs are public too.
#
# Reads (environment):
#   AZURE_CLIENT_ID, AZURE_TENANT_ID, AZURE_SUBSCRIPTION_ID   identifiers, stored as GitHub environment secrets
#   TETHER_DB_ADMIN_PASSWORD, TETHER_ROOT_SECRET              GitHub environment secrets (required)
#   TETHER_EASYAUTH_CLIENT_ID, TETHER_EASYAUTH_CLIENT_SECRET  sign-in gate (optional, but both or neither)
#   TETHER_ALLOWED_IP_RANGES                                  optional extra layer on top of Entra (never a gate alone): IPv4 or CIDR (/16 to /32)
# Writes to $GITHUB_OUTPUT (when set) and to stdout:
#   gate=none|entra|entra+ip   (an allowlist without Entra gives gate=none: the web app stays internal)
# Exit code 1 when any input is missing or malformed.
set -euo pipefail

failed=0
bad() { echo "::error::$*"; failed=1; }
guid_re='^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$'

for name in AZURE_CLIENT_ID AZURE_TENANT_ID AZURE_SUBSCRIPTION_ID; do
  value="${!name:-}"
  if [[ ! "$value" =~ $guid_re ]]; then
    bad "$name is missing or is not a GUID. Set it as a secret on the GitHub environment azure-dev (output of scripts/azure/bootstrap-tether-dev.ps1)."
  fi
done

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

# --- Sign-in gate (primary): client id and client secret, both or neither. ---
auth=false
client_id="${TETHER_EASYAUTH_CLIENT_ID:-}"
client_secret="${TETHER_EASYAUTH_CLIENT_SECRET:-}"
if [ -n "$client_id" ] && [ -n "$client_secret" ]; then
  if [[ "$client_id" =~ $guid_re ]]; then
    auth=true
  else
    bad "Secret EASYAUTH_CLIENT_ID is not a GUID. Re-run scripts/azure/create-signin-app.ps1."
  fi
elif [ -n "$client_id" ] || [ -n "$client_secret" ]; then
  bad "Only one of the secrets EASYAUTH_CLIENT_ID and EASYAUTH_CLIENT_SECRET is set. Re-run scripts/azure/create-signin-app.ps1 (it sets both)."
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

if [ "$auth" = true ] && [ "$ip" = true ]; then gate="entra+ip"
elif [ "$auth" = true ]; then gate="entra"
else
  # PO-20cf: an IP allowlist is only an extra layer on top of Entra sign-in, never a gate on its own.
  if [ "$ip" = true ]; then
    echo "::warning::ALLOWED_IP_RANGES is set but Entra sign-in is not: the allowlist alone never makes the web app public. Run scripts/azure/create-signin-app.ps1 first."
  fi
  gate="none"
fi

echo "gate=$gate"
if [ -n "${GITHUB_OUTPUT:-}" ]; then echo "gate=$gate" >> "$GITHUB_OUTPUT"; fi
case "$gate" in
  none) echo "No access gate is configured: the web app will be deployed with INTERNAL ingress only (not reachable from the internet)." ;;
  *) echo "Access gate for this run: $gate. The web app will be public behind it." ;;
esac
