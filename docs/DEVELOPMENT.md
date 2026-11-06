# Local Development

## First 10 minutes (mock and synthetic data only)

Needs Docker Desktop (running) and Node.js 22 or newer. Nothing else: no .NET SDK, no accounts, no keys.

1. Start everything with one command from the repo root.
   - PowerShell: `.scriptsstart-local.ps1`
   - Git Bash or any shell with Node: `node scripts/start-local.mjs`
2. It starts Postgres and the API (docker compose, dev overlay), waits for `http://localhost:5080/health/live`, starts the web
   server (`npm run dev:real` in `src/web`), waits for `http://localhost:3000`, seeds the demo data and prints the URL. The very first
   run builds the .NET project inside the API container and can take several minutes; later starts take seconds.
3. Open http://localhost:3000, choose sign in, type `demo@dev.local` (any email works in dev; there is no password).
4. Look around: two "Demo:" courses; in "Demo: Biology 101" a finished "Demo lecture (synthetic replay)" with its transcript,
   TL;DR and notes (the text says "Demo replay segment ..." and nothing was spoken by anyone), and a "Demo syllabus (synthetic)"
   waiting for your review (nothing is approved and nothing is written to any calendar until you decide).
5. Start your own lecture in "Demo: Statistics 101": the "Demo source" replays a fixed script, so no microphone or real speech
   service is used.
6. Stop: Ctrl+C in the window ends a web server the script started, then `node scripts/start-local.mjs stop` stops the containers
   (data is kept). `node scripts/start-local.mjs status` shows what is up.

What the script will and will not do:
- It never starts a second `next dev` (that corrupts `.next`, lesson L-DEVSERVER) and never runs `next build`. If a dev server is already
  on :3000 and forwards `/api` to the real API, it is reused; if :3000 is taken by something else it stops and says so.
- It starts the web server inside its own window (output in `%TEMP%	ether-localweb-dev.log`), so closing the window ends it. `--detach`
  tries to leave it running in the background; that mode is best effort and was not verified on a plain Windows terminal.
- Seeding (`node scripts/seed-demo.mjs`, also `start-local.mjs --seed`, `--no-seed` to skip) uses the public API and dev login only,
  refuses any host but localhost, and looks each item up by title first, so running it again adds nothing. If you delete a demo
  item it comes back on the next run. It sends the same DRAFT acknowledgement version ids the web app sends, on behalf of the
  synthetic demo user only; it does not change any consent, notice or Demo-source wording.
- Everything is mock: the speech source is the replay "Demo source", AI is the mock client, the calendar is the "Demo calendar".
  Compose passwords are dev-only defaults; no secret is needed. Real providers need Product Owner approval (see "Providers and cost").

## Prerequisites
- Docker Desktop (PostgreSQL, the API container and the .NET build and test image)
- Node.js 22 or newer (`src/web`, the scripts)
- .NET SDK: **not installed on the host and not needed.** The API runs, builds and tests inside `mcr.microsoft.com/dotnet/sdk:10.0`.
  Installing a host SDK is optional and not part of the supported setup.

## Running the tests
- Web (from `src/web`): `npx.cmd tsc --noEmit -p .`, `npx.cmd eslint .`, `npx.cmd vitest run` (PowerShell uses `npm.cmd`/`npx.cmd`).
- API: stop the dev API first (integration tests share the dev database), then run the suite in the SDK image on the compose network.
  PowerShell: `docker stop aistudentnotesapp-api-1`, then
  `docker run --rm --network aistudentnotesapp_default -e "ConnectionStrings__Default=Host=cc-postgres-dev;Port=5432;Database=college_companion_dev;Username=cc_dev;Password=cc_dev_only_password" -v "${PWD}:/repo" -w /repo/src/api mcr.microsoft.com/dotnet/sdk:10.0 dotnet test Tether.slnx`
  (Git Bash: prefix `MSYS_NO_PATHCONV=1` and use `$(pwd -W)` for the mount). Afterwards `docker start aistudentnotesapp-api-1` and
  check `:5080/health/live`. After backend changes recreate the API container:
  `docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d --force-recreate api`.
- Playwright against the running dev server: only the `*.reuse.config.ts` configs, `--workers=1` (see `docs/agents/project-playbook.md`
  section 2). The demo data is ordinary data in the dev database; wiping the database
  (`down -v`) removes it until the next start.

## Setup details (what the script does for you)
1. Optional: `cp .env.example .env` to change ports or the dev password. Without it, compose uses its dev-only defaults. `.env` is gitignored; never commit real values.
2. Start the database and API by hand: `docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d postgres api`, then `npm.cmd run dev:real` in `src/web`
3. Check health: `docker compose ps` (status should become `healthy`).
4. Connection (dev only): `Host=127.0.0.1;Port=5432;Database=college_companion_dev;Username=cc_dev;Password=<POSTGRES_PASSWORD>`
5. Stop: `docker compose -f docker-compose.yml -f docker-compose.dev.yml stop api postgres` or `down` (keeps data). Wipe data: `down -v`.

## Secrets
- API: provider keys (none are used today; all providers are mock) would go in user-secrets or environment variables, never in files; web: `.env.local` (gitignored).
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
Workflows (first GitHub runs 2026-11-01; read run results with `gh run list`, and the gate status in `docs/RELEASE_GATES.md`, not here): `.github/workflows/ci.yml`
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
