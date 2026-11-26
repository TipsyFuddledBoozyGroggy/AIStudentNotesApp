#!/usr/bin/env bash
# Exit 0 when a ghcr.io image can be pulled WITHOUT credentials, 1 when it cannot.
# Azure Container Apps pulls the Tether images anonymously (the deploy identity cannot grant a registry role and no
# registry password is stored), so a private package would only show up later as a failed revision.
#
# Usage: check-public-image.sh ghcr.io/<owner>/<name>:<tag>
# Needs: curl, jq. Sends no credentials.
set -euo pipefail

image="${1:?image reference, for example ghcr.io/owner/name:tag}"
case "$image" in
  ghcr.io/*:*) ;;
  *) echo "::error::Not a ghcr.io image reference with a tag: $image"; exit 1 ;;
esac
path="${image#ghcr.io/}"
repository="${path%:*}"
tag="${path##*:}"

token="$(curl --silent --max-time 20 "https://ghcr.io/token?service=ghcr.io&scope=repository:${repository}:pull" | jq -r '.token // empty' 2>/dev/null)" || token=""
if [ -z "$token" ]; then
  echo "No anonymous pull token for ${repository}: the package is private or does not exist."
  exit 1
fi

accept='application/vnd.oci.image.index.v1+json, application/vnd.oci.image.manifest.v1+json, application/vnd.docker.distribution.manifest.list.v2+json, application/vnd.docker.distribution.manifest.v2+json'
code="$(curl --silent --max-time 20 --output /dev/null --write-out '%{http_code}' \
  --header "Authorization: Bearer ${token}" --header "Accept: ${accept}" \
  "https://ghcr.io/v2/${repository}/manifests/${tag}")" || code=000

if [ "$code" = "200" ]; then
  echo "Anonymous pull works: ${image}"
  exit 0
fi
echo "Anonymous pull of ${image} answered HTTP ${code}: the package is private or the tag does not exist."
exit 1
