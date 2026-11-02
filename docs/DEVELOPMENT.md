# Local Development

## Prerequisites
- Docker Desktop (for local PostgreSQL)
- Node.js 22 LTS (for `src/web`, once it exists)
- .NET SDK: **NOT yet installed on the dev machine** (decision PO-1). Either
  `winget install Microsoft.DotNet.SDK.10` (fallback .NET 8) or build/test inside a Docker SDK image,
  e.g. `docker run --rm -v "${PWD}/src/api:/src" -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet test`.
  The compose `dotnet` service (docker-compose.dev.yml) builds and tests without a host SDK.

## Setup
1. `cp .env.example .env` and adjust. `.env` is gitignored; never commit real values.
2. Start the database: `docker compose up -d postgres`
3. Check health: `docker compose ps` (status should become `healthy`).
4. Connection (dev only): `Host=127.0.0.1;Port=5432;Database=college_companion_dev;Username=cc_dev;Password=<POSTGRES_PASSWORD>`
5. Stop: `docker compose down` (keeps data). Wipe data: `docker compose down -v`.

## Secrets
- API: use `dotnet user-secrets` for provider keys; web: `.env.local` (gitignored).
- Compose credentials are DEV ONLY. Postgres is bound to loopback.

## Providers and cost
`.env.example` defaults STT, Claude and Google Docs to `mock`. Switching to a real provider incurs cost
and needs Product Owner approval. Do not put lecture content in logs.

## Database logging (SB-01)
`docker-compose.yml` starts Postgres with logging settings that keep row and statement text out of the server log
(`log_error_verbosity=terse`, `log_min_error_statement=panic`, `log_statement=none`, `log_duration=off`, parameter
logging 0). Without them a CHECK violation writes `DETAIL: Failing row contains (...)` with lecture or syllabus text.
Recreate the container after editing the file (`docker compose up -d postgres`; the `pgdata` volume is kept). Verify with
`bash docs/ops/db-log-canary.sh cc-postgres-dev` (exit 0 = clean) or `--passive` to scan the whole log. A deployed
database needs the same settings in its parameter group.

## CI
Workflows (authored 2026-11-01, **never run on GitHub; no run record exists, G5 stays NOT_RUN**): `.github/workflows/ci.yml`
(web, api, e2e-harness, e2e-real, secret-scan, dependency-audit, dependency-review, image-scan; docs-only changes skip the
heavy jobs), `.github/workflows/codeql.yml` (C# and TypeScript), `.github/dependabot.yml`. Job table, estimates,
local commands and known limits: `docs/ops/ci-matrix.md`. No secrets are needed.

How to read results: open the run's Checks tab; a red job names the step that failed (`Copy and skin lint gate`,
`Geometry`, `DB log canary (SB-01)` and so on). Playwright output, axe reports and API/web server logs are attached as
artifacts only when a job fails (kept 7 days). The e2e jobs run serially because the dev login is limited to 10 per
minute per IP. Branch protection must be configured in GitHub settings by the repo owner (SEC-040). Not implemented:
nightly full look matrix, bundle budget, AI evals, deployment, backup/restore drill.

Before pushing a workflow change, lint it:
`docker run --rm -v "$PWD:/repo" -w /repo rhysd/actionlint:latest`

## Running integration tests next to the dev API
`PersistenceTests.Rolling_back_every_migration_leaves_no_module_tables_behind` (and the migrate-down/up test) roll migrations back on the **shared dev database**. If the dev API container (`aistudentnotesapp-api-1`) or a Playwright run is using the same database, the rollback test can fail (seen 2026-10-30). Stop the API first (`docker stop aistudentnotesapp-api-1`), run `dotnet test`, then start it again for E2E. A separate test database per run would remove this coupling (not done).

### Load-sensitive integration tests (seen 2026-11-01)
`AiRouteTests.Concurrent_requests_with_the_same_key_make_exactly_one_catch_up_and_all_get_it` (expects 5 `Idempotency-Replayed` headers, got 4) and a one-off `ObjectDisposedException` in `RealtimeLogCanaryTests` (the latter is FIXED 2026-11-01: the test sent audio frames past the close it expected; it now sends exactly 10) failed when the dotnet suite ran while another agent was running Playwright against the same machine and Postgres. Three clean runs with nothing else running: 600/600 each. Run the dotnet integration suite on an otherwise idle machine. A real defect is not ruled out for the catch-up test; if it fails on an idle machine, treat it as a product race (same-key catch-up replay) and write a deterministic reproduction.
