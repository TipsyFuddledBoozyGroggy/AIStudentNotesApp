#!/usr/bin/env bash
# Case tests for infra/scripts/template-cycles.mjs. Node only: no Azure, no Bicep, no network.
#   bash infra/scripts/tests/template-cycles-cases.sh
# The templates below are tiny, synthetic stand-ins for compiled Bicep (languageVersion 2.0, symbolic names). Exit code
# 0 when every case behaves as expected, 1 otherwise.
set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
checker="$here/../template-cycles.mjs"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
pass=0
fail=0

# run_case <name> <expected exit code> <text the output must contain> <template json>
run_case() {
  local name="$1" want="$2" text="$3" json="$4" out rc
  printf '%s' "$json" > "$work/t.json"
  out="$(node "$checker" "$work/t.json" 2>&1)"
  rc=$?
  if [ "$rc" -eq "$want" ] && printf '%s' "$out" | grep -q -F -- "$text"; then
    pass=$((pass + 1))
  else
    fail=$((fail + 1))
    echo "FAIL: $name (exit $rc, wanted $want; output: $out)"
  fi
}

head='{"languageVersion":"2.0","resources":{'
tail='}}'
app() { printf '"%s":{"type":"Microsoft.App/containerApps","name":"%s"%s}' "$1" "$2" "${3:-}"; }

# The shape Azure refused in Run A: an existing declaration of the web app that the template also deploys, read by a
# step that the web app itself waits for (through the API).
run_case "existing copy of a deployed resource closes a circle" 1 "dependency circle: web -> api -> rules -> seen -> web" \
  "$head$(app web tether-web ',"dependsOn":["api"]'),$(app api tether-api ',"dependsOn":["rules"]'),\"rules\":{\"type\":\"Microsoft.Resources/deployments\",\"name\":\"fw\",\"dependsOn\":[\"seen\"]},$(app seen tether-web ',"existing":true')$tail"

run_case "plain dependsOn circle" 1 "dependency circle" \
  "$head$(app a one ',"dependsOn":["b"]'),$(app b two ',"dependsOn":["a"]')$tail"

run_case "circle through a reference() call" 1 "dependency circle" \
  "$head$(app a one ',"properties":{"x":"[reference('"'"'b'"'"').fqdn]"}'),$(app b two ',"dependsOn":["a"]')$tail"

run_case "existing copy that nothing deployed waits on is fine" 0 "no dependency circle (3 resources)" \
  "$head$(app web tether-web ',"dependsOn":["api"]'),$(app api tether-api),$(app seen tether-web ',"existing":true')$tail"

run_case "existing reference to something not deployed here is fine" 0 "no dependency circle (2 resources)" \
  "$head$(app api tether-api ',"dependsOn":["server"]'),\"server\":{\"type\":\"Microsoft.DBforPostgreSQL/flexibleServers\",\"name\":\"db\",\"existing\":true}$tail"

run_case "a template without symbolic names is refused" 2 "languageVersion 2.0" '{"resources":[]}'

echo ""
echo "template-cycles cases: $pass passed, $fail failed"
[ "$fail" -eq 0 ]
