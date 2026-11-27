#!/usr/bin/env bash
# LOCAL REHEARSAL ONLY. Creates the throwaway values docker-compose.poc-prod.yml needs, in a folder OUTSIDE the
# repository (default: ~/.tether/poc-prod):
#   tls-key.pem, tls-cert.pem   a self-signed certificate for https://localhost (30 days)
#   poc-prod.env                TETHER_POC_ROOT_SECRET, TETHER_POC_DP_CERT_PFX (a local key-ring certificate, RSA 3072, base64
#                               PFX without a password, the same shape scripts/azure/set-google-signin.ps1 stores) and
#                               TETHER_POC_TLS_DIR
# Nothing here is used in Azure and nothing is printed except file names. Safe to run again: existing files are kept.
# Needs: bash, openssl, base64. Works in Git Bash on Windows.
#
# Usage: bash infra/local/make-local-prod-env.sh [folder]
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
dir="${1:-$HOME/.tether/poc-prod}"
case "$dir" in /* | [A-Za-z]:[\\/]*) ;; *) dir="$PWD/$dir" ;; esac   # absolute as given (also C:\... in Git Bash), else under the current folder
# Resolve the folder WITHOUT creating it: the nearest existing parent decides where it really is.
existing="$dir"
while [ ! -d "$existing" ]; do existing="$(dirname "$existing")"; done
resolved="$(cd "$existing" && pwd -P)${dir#"$existing"}"
case "$resolved/" in
  "$repo_root"/*) echo "Refusing: $resolved is inside the repository. These files hold private keys; keep them outside it." >&2; exit 1 ;;
esac
umask 077
mkdir -p "$dir"
dir="$(cd "$dir" && pwd -P)"
cd "$dir"

# MSYS_NO_PATHCONV: Git Bash would otherwise rewrite the -subj value ("/CN=...") into a Windows path. File names are
# relative on purpose, so they need no conversion.
if [ ! -f tls-key.pem ] || [ ! -f tls-cert.pem ]; then
  MSYS_NO_PATHCONV=1 openssl req -x509 -newkey rsa:2048 -nodes -days 30 -subj "/CN=localhost" \
    -addext "subjectAltName=DNS:localhost" -keyout tls-key.pem -out tls-cert.pem 2>/dev/null
  echo "created tls-key.pem and tls-cert.pem (https://localhost, 30 days)"
fi

if [ ! -f poc-prod.env ]; then
  root_secret="$(openssl rand -base64 48 | tr -d '\r\n')"
  MSYS_NO_PATHCONV=1 openssl req -x509 -newkey rsa:3072 -nodes -days 3650 -subj "/CN=tether-dp-keyring-local" \
    -keyout dp-key.pem -out dp-cert.pem 2>/dev/null
  pfx="$(openssl pkcs12 -export -inkey dp-key.pem -in dp-cert.pem -passout pass: | base64 | tr -d '\r\n')"
  rm -f dp-key.pem dp-cert.pem
  # A path Docker Desktop can mount: C:/... on Windows (cygpath exists in Git Bash), unchanged elsewhere.
  mount_dir="$dir"
  if command -v cygpath > /dev/null 2>&1; then mount_dir="$(cygpath -m "$dir")"; fi
  {
    echo "TETHER_POC_ROOT_SECRET=$root_secret"
    echo "TETHER_POC_DP_CERT_PFX=$pfx"
    echo "TETHER_POC_TLS_DIR=$mount_dir"
  } > poc-prod.env
  echo "created poc-prod.env (root secret, local key-ring certificate, TLS folder)"
fi

echo ""
echo "Files are in: $dir"
echo "Start the local Production stack from the repository folder:"
echo "  docker compose --env-file \"$dir/poc-prod.env\" -f docker-compose.poc-prod.yml up -d --build"
echo "Rehearse the outside smoke probes against it:"
echo "  docker compose --env-file \"$dir/poc-prod.env\" -f docker-compose.poc-prod.yml run --rm rehearsal"
