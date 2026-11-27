#!/usr/bin/env bash
# Checks, from its CONTENT, that an infra/main.bicep cannot give the API a dev login (ADR-022 R1/R2).
# Used by the plan job of .github/workflows/deploy-azure-dev.yml on the template of the commit being deployed and on the
# template at the go-live tag, so the rule "no dev-login release is ever deployed again" rests on what the template
# says, not only on where a tag was put. Case tests: infra/scripts/tests/smoke-cases.sh.
#
# Passes (exit 0) only when, outside whole-line comments:
#   - the template sets the environment variable ASPNETCORE_ENVIRONMENT to the literal 'Production', exactly once;
#   - it nowhere names a variable 'Auth__DevLogin__Enabled' (any letter case);
#   - it nowhere holds the literal 'Development'.
# Anything else, including a file that cannot be read, fails (exit 1) with one line saying which rule.
#
# Usage: release-guard.sh <path to main.bicep> [label for messages]
set -uo pipefail

file="${1:?path to a main.bicep}"
label="${2:-the template}"
if [ ! -r "$file" ] || [ ! -s "$file" ]; then
  echo "::error::$label: infra/main.bicep could not be read, so it cannot be shown to be free of dev login."
  exit 1
fi

# Whole-line comments are dropped (they explain the rule and name the variable). Nothing else is removed: text after a
# `//` inside a line is kept, so a setting can never hide behind one.
body="$(grep -v -E '^[[:space:]]*//' "$file" | tr -d '\r')"
lower="$(printf '%s' "$body" | tr '[:upper:]' '[:lower:]')"

production="$(grep -c -E "name:[[:space:]]*'ASPNETCORE_ENVIRONMENT'[[:space:]]*,?[[:space:]]*value:[[:space:]]*'Production'" <<< "$body" || true)"
any_environment="$(grep -c -E "'ASPNETCORE_ENVIRONMENT'" <<< "$body" || true)"
if [ "$production" != "1" ] || [ "$any_environment" != "1" ]; then
  echo "::error::$label: infra/main.bicep does not set ASPNETCORE_ENVIRONMENT to the literal 'Production' exactly once. It may be a release from before go-live."
  exit 1
fi
if [[ "$lower" == *"'auth__devlogin__enabled'"* ]]; then
  echo "::error::$label: infra/main.bicep declares a dev-login variable. Releases with dev login are never deployed again (ADR-022)."
  exit 1
fi
if [[ "$lower" == *"'development'"* ]]; then
  echo "::error::$label: infra/main.bicep can run the API as Development. Releases like that are never deployed again (ADR-022)."
  exit 1
fi
echo "$label: the template runs the API as Production and declares no dev-login variable."
