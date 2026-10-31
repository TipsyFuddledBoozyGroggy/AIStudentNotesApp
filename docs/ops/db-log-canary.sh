#!/usr/bin/env bash
# SB-01 canary: proves the PostgreSQL SERVER log does not echo row or statement text when a CHECK constraint rejects a row.
#
# Usage:  docs/ops/db-log-canary.sh <container-name> [canary]        active probe (creates and drops a scratch database)
#         docs/ops/db-log-canary.sh <container-name> --passive     scan the WHOLE container log for any CANARY-/CANARY_ marker
#                                                                   (the test suites' canaries, e.g. CANARY-02-SYLLABUS-TEXT-77ab); run after the tests
#   Uses `docker exec` against a running postgres container and `docker logs` to read its log.
#   Env: PGUSER (default cc_dev), PGDATABASE_ADMIN (default postgres).
#   Exit 0 = canary absent from the log (hardened). Exit 1 = canary present (LEAK). Exit 2 = test could not run.
#
# Method: create a throwaway database, a table with a CHECK constraint, INSERT a row that violates the check and
# whose text column holds the canary (also once through a bound-parameter-style statement), then grep the
# container log for the canary. A control run proves the harness can fail: on default settings it MUST exit 1.
# The scratch database is dropped at the end. Touches no application database.
set -u
C="${1:?container name}"
MODE="active"; [ "${2:-}" = "--passive" ] && { MODE="passive"; shift; }
CANARY="${2:-CANARY_DB_LOG_7f3a}"
U="${PGUSER:-cc_dev}"
DB="canary_scratch_$$"
psql_() { docker exec -i "$C" psql -U "$U" -v ON_ERROR_STOP=0 -q "$@"; }

if [ "$MODE" = passive ]; then
  HITS="$(docker logs "$C" 2>&1 | grep -E "CANARY[-_]" || true)"
  if [ -n "$HITS" ]; then echo "LEAK: canary marker(s) in $C server log:"; echo "$HITS" | sed "s/^/  /" | head -20; exit 1; fi
  echo "OK: no CANARY marker anywhere in the $C log ($(docker logs "$C" 2>&1 | wc -l) lines scanned)."; exit 0
fi
SINCE="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
psql_ -d "${PGDATABASE_ADMIN:-postgres}" -c "CREATE DATABASE $DB" >/dev/null 2>&1 || { echo "cannot create scratch db" >&2; exit 2; }
trap 'psql_ -d "${PGDATABASE_ADMIN:-postgres}" -c "DROP DATABASE IF EXISTS $DB" >/dev/null 2>&1' EXIT

psql_ -d "$DB" >/dev/null 2>&1 <<SQL
CREATE TABLE t (id int, snippet_text text, CONSTRAINT ck_len CHECK (char_length(snippet_text) < 5));
SQL
# 1) plain statement, 2) prepared statement with a bound parameter (the shape the app/Npgsql uses).
psql_ -d "$DB" >/dev/null 2>&1 <<SQL
INSERT INTO t VALUES (1, '$CANARY plain statement text');
PREPARE p(int, text) AS INSERT INTO t VALUES (\$1, \$2);
EXECUTE p(2, '$CANARY bound parameter text');
SQL
sleep 1
LOG="$(docker logs --since "$SINCE" "$C" 2>&1)"
if ! grep -q 'ck_len' <<<"$LOG"; then
  echo "TEST INVALID: the CHECK violation was not seen in the log (ERROR line missing), so absence proves nothing." >&2
  echo "$LOG" | tail -5 >&2
  exit 2
fi
if grep -q "$CANARY" <<<"$LOG"; then
  echo "LEAK: canary '$CANARY' found in $C server log:"
  grep "$CANARY" <<<"$LOG" | sed 's/^/  /'
  exit 1
fi
echo "OK: ERROR for ck_len is logged, canary '$CANARY' is absent from $C log."
exit 0
