#!/usr/bin/env bash
# Compares what a deploy run asks for with what is LIVE in Azure and refuses the run, before anything is deployed, when
# a rule of .github/workflows/deploy-azure-dev.yml is not met. Read-only (ARM GETs). Not used for access=closed.
# Case tests: infra/scripts/tests/smoke-cases.sh.
#
# Rules (ADR-022, docs/architecture/go-live-signin-adr.md; findings F1 and F2 of the 2026-11-27 cross-review):
#   invited  - refused unless the live API is a Production API with no dev-login variable (api-signin-mode.sh = google);
#            - refused unless the Microsoft (Entra) gate is KNOWN to be on or off. A read that failed is "unknown", never
#              "off" (web-auth-state.sh): only a 404 means there is nothing to switch off;
#            - taking the Microsoft gate off (it is on) must be a gate_only run: no new code rides along.
#   gate_only - keeps the release that is live: refused unless the live images are the ones this run would deploy.
#            - EXCEPT access=team with no release_sha, the rollback: when main has moved past the live release, the run
#              ADOPTS the live images instead of refusing, provided the live API checks as Production with no dev login
#              (so the live image is a post-go-live image and no dev-login release can come back). The rollback stays one
#              step even right after someone merged a fix. The newest infra/ folder is applied with the live images.
#   release_sha without gate_only - the images of that release must still exist; the caller checks that (it needs the
#              network): this script only says so with check_images=true.
#
# Reads (environment): ACCESS (team|invited), GATE_ONLY (true|false), RELEASE_SHA (the workflow input, may be empty),
#   BUILT_IN_THIS_RUN (true|false), RESOURCE_GROUP, TETHER_API_APP_NAME, TETHER_WEB_APP_NAME, TETHER_API_IMAGE,
#   TETHER_WEB_IMAGE, TETHER_EASYAUTH_CLIENT_ID (may be empty).
# Writes to $GITHUB_OUTPUT: api_mode, entra_state (on|off|unknown), entra_active (true|false: on with THIS app
#   registration), images (as-given|adopted-live), check_images (true|false).
# Writes to $GITHUB_ENV when it adopts the live images: TETHER_API_IMAGE, TETHER_WEB_IMAGE.
# Exit code 1 = the run is refused (one ::error:: line says why). Prints no image reference, address or identifier.
# Needs: az (signed in), jq.
set -euo pipefail

: "${ACCESS:?}" "${RESOURCE_GROUP:?}" "${TETHER_API_APP_NAME:?}" "${TETHER_WEB_APP_NAME:?}" "${TETHER_API_IMAGE:?}" "${TETHER_WEB_IMAGE:?}"
gate_only="${GATE_ONLY:-false}"
release="$(printf '%s' "${RELEASE_SHA:-}" | tr -d '[:space:]')"
built="${BUILT_IN_THIS_RUN:-false}"
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
refuse() { echo "::error::$*"; exit 1; }
out() { if [ -n "${GITHUB_OUTPUT:-}" ]; then echo "$1" >> "$GITHUB_OUTPUT"; fi; }

case "$ACCESS" in
  team|invited) ;;
  *) refuse "live-rules.sh handles access=team and access=invited only." ;;
esac

api_mode="$(bash "$script_dir/api-signin-mode.sh" "$RESOURCE_GROUP" "$TETHER_API_APP_NAME" mode)"
live_api_image="$(bash "$script_dir/api-signin-mode.sh" "$RESOURCE_GROUP" "$TETHER_API_APP_NAME" image)"
live_web_image="$(bash "$script_dir/api-signin-mode.sh" "$RESOURCE_GROUP" "$TETHER_WEB_APP_NAME" image)"
# Is built-in (Entra) authentication switched on at all, whatever its app registration?
entra_state="$(bash "$script_dir/web-auth-state.sh" "$RESOURCE_GROUP" "$TETHER_WEB_APP_NAME")"
# ...and on with THIS app registration and "redirect to sign-in" (what access=team needs before a single pass).
entra_active=false
if [ -n "${TETHER_EASYAUTH_CLIENT_ID:-}" ]; then
  entra_active="$(bash "$script_dir/web-auth-active.sh" "$RESOURCE_GROUP" "$TETHER_WEB_APP_NAME" "$TETHER_EASYAUTH_CLIENT_ID")"
fi
echo "Live API sign-in mode: $api_mode. Microsoft sign-in on the web app: $entra_state."

if [ "$ACCESS" = "invited" ]; then
  if [ "$api_mode" != "google" ]; then
    refuse "access=invited was refused: the live API is not a Production API without dev login (api-signin-mode.sh says '$api_mode'). Run with access=team first, try Google sign-in behind the Microsoft gate, then run invited with gate_only."
  fi
  if [ "$entra_state" = "unknown" ]; then
    refuse "access=invited was refused: Azure could not say whether the Microsoft gate is on or off (the read failed; that is not the same as off). Nothing was changed. Run the workflow again."
  fi
  if [ "$entra_state" = "on" ] && [ "$gate_only" != "true" ]; then
    refuse "access=invited was refused: this run would take the Microsoft gate off AND deploy code in one go. Take the gate off with gate_only ticked (the release that is live stays)."
  fi
fi

images="as-given"
check_images=false
if [ "$gate_only" = "true" ]; then
  if [ "$live_api_image" = "$TETHER_API_IMAGE" ] && [ "$live_web_image" = "$TETHER_WEB_IMAGE" ]; then
    echo "gate_only: the live images are this run's commit. Only the access mode changes."
  elif [ "$ACCESS" = "team" ] && [ -z "$release" ] && [ "$api_mode" = "google" ] && [ -n "$live_api_image" ] && [ -n "$live_web_image" ]; then
    # The rollback, with main ahead of what is live. Keep exactly the images that are running.
    if [ -n "${GITHUB_ENV:-}" ]; then
      echo "TETHER_API_IMAGE=$live_api_image" >> "$GITHUB_ENV"
      echo "TETHER_WEB_IMAGE=$live_web_image" >> "$GITHUB_ENV"
    fi
    images="adopted-live"
    echo "::notice::gate_only with access=team: the newest commit is not what is live, so the images that are LIVE are kept (the live API is a Production API with no dev login). Only the Microsoft gate changes."
  else
    refuse "gate_only was refused: the release that is live is not the commit this run would deploy. Give the live commit as release_sha (Azure portal, container app, Revisions, the image tag), or run without gate_only to deploy new code (not together with taking the Microsoft gate off). Putting the Microsoft gate back (access=team, gate_only, no release_sha) keeps whatever is live by itself, as long as the live API is a Production API."
  fi
elif [ "$built" != "true" ]; then
  # release_sha without gate_only: an earlier release is deployed again; its images must still exist.
  check_images=true
fi

out "api_mode=$api_mode"
out "entra_state=$entra_state"
out "entra_active=$entra_active"
out "images=$images"
out "check_images=$check_images"
