# CI matrix (authored 2026-11-01; area filters and e2e sharding added 2026-11-01, not yet run on GitHub)

**Status: NOT_RUN.** These workflows were written and statically checked on 2026-10-31/11-01. No GitHub Actions run record exists, so nothing here is evidence for RELEASE_GATES G5. The gate stays `NOT_RUN` until a real run exists and an accountable reviewer has read it.

Files: `.github/workflows/ci.yml`, `.github/workflows/codeql.yml`, `.github/dependabot.yml`, `docs/ops/db-log-canary.sh`.

## Jobs

Runtime figures are **estimates** from local runs of the same commands (marked L) or guesses (marked G). They include no runner start-up, cache restore or Playwright browser install.

| Job (workflow) | Runs on | What it does | Estimate | Local equivalent |
|---|---|---|---|---|
| `changes` (ci) | every PR/push | `git diff` against the base commit, classified into outputs `backend`, `web`, `e2e` (backend or web), `code` (see Area filters) | < 1 min G | n/a |
| `web` (ci) | `web` changed | `npm ci`, `tsc --noEmit`, eslint, **named copy and skin lint gate** (4 vitest files, 186 tests, 2.6 s L), full vitest (about 20 s L), `next build` (14 s L); the `npm audit` steps moved to `dependency-audit` so they always run | 4 to 6 min G | `cd src/web && npm run typecheck && npm run lint && npm test && npm run build` |
| `api` (ci) | `backend` changed | compose Postgres (hardened), `dotnet restore`, `build -c Release -warnaserror`, `dotnet test` (all 5 test projects, integration tests included, no dev API needed), then the **DB log canary** | 10 to 20 min G | stop the dev API first, then `docker compose up -d postgres` and `dotnet test` (see DEVELOPMENT.md) |
| `e2e-harness` (ci) | `web` changed | Playwright harness config, own `next dev` on :3100, no API, `--workers=1` | 5 to 8 min G (1.1 min L for the run itself) | `cd src/web && npx playwright test -c playwright.harness.config.ts --workers=1` (never beside a dev server in the same folder) |
| `e2e-real` x4 shards (ci) | `e2e` (backend or web) changed | Matrix of 4 runners, each with its own compose Postgres, API on :5080 (Development env) and `npm run dev:real` on :3000; serial inside a shard. Shards: `flows-a` (courses, syllabus, syllabus-utc, syllabus-unclaimed), `flows-b` (lecture, notes, reopen, stop-move), `screens-a` (screens + axe, skins), `screens-b` (geometry, gamer-glass, lecture-looks). Each runs the DB log canary and uploads artifacts on failure (`e2e-real-output-<shard>`) | 10 to 18 min G per shard; wall time is the slowest shard (was 25 to 40 min G serial; the first real CI runs took 35 to 55 min) | start the API and `npm run dev:real`, then the same `npx playwright test -c playwright.reuse.config.ts --workers=1 <specs>` / `-c playwright.screens.config.ts e2e-screens/<spec> --workers=1` |
| `secret-scan` (ci) | **always**, full history | gitleaks v8.21.2, tarball verified against a pinned SHA-256 (SBR-03) | 1 min G | `gitleaks detect --source . --redact` |
| `dependency-audit` (ci) | **always** | `npm audit` (prod: any severity fails; all: high/critical fail, moderate allowed up to the known 14), then `dotnet list package --vulnerable --include-transitive`, fails on High/Critical, and also fails if the check cannot run (nonzero exit, NU1900/feed errors, or no per-project result) (SBR-02) | 2 to 3 min G | same commands in `src/web` / `src/api` |
| `dependency-review` (ci) | PRs with code changes | GitHub dependency review, fails on high | 1 min G | none |
| `image-scan` (ci) | PRs with code changes only, never push to main; Trivy DB cache off (SBR-01) | Trivy: `postgres:16.15-alpine` (gates on fixable HIGH/CRITICAL; 26 gosu findings ignored by `.trivyignore.yaml` until 2026-12-01), `dotnet/sdk:10.0` (report only, dev tooling), repo config scan (compose, workflows) | 3 to 6 min G | `docker run --rm aquasec/trivy:0.69.3 image --ignorefile .trivyignore.yaml --ignore-unfixed --severity HIGH,CRITICAL postgres:16.15-alpine` |
| `ci-result` (ci) | always | One stable check for branch protection: fails if any needed job failed or was cancelled; skipped counts as success | < 1 min G | none |
| `analyze` x2 (codeql) | code PRs, pushes to main, weekly | CodeQL `security-extended` for `csharp` and `javascript-typescript`, no-build mode | 10 to 25 min G | none |

All jobs: `permissions: contents: read` (CodeQL adds `security-events: write` and `actions: read`), a timeout, and a concurrency group (PR runs are cancelled by newer pushes; main runs are not). Every action is pinned to a full commit SHA.

## Area filters (the `changes` job)

Plain `git diff --name-only <base> HEAD` and a shell `case`, no third-party action. First match wins, per changed file:

| Path | Effect |
|---|---|
| `.github/*` (includes `ci.yml`, `codeql.yml`), `.gitleaks.toml`, `docs/ops/*`, any `.sh .bash .ps1 .cmd .bat` (SBR-04) | everything (backend and web) |
| `docs/*`, `.claude/*`, any `*.md` | nothing (docs-only) |
| `src/shared/contracts/*` | backend and web |
| `src/api/*`, `docker-compose*.yml`, `global.json`, `Directory.*` (any folder) | backend |
| `src/web/*` | web |
| anything else (unknown path) | everything (conservative) |

Outputs: `backend`, `web`, `e2e` = backend or web, `code` = backend or web. A missing or unfetchable base commit runs everything. Job gating: `api` needs backend; `web` and `e2e-harness` need web; `e2e-real` needs e2e (a contract or API change can break the UI); `dependency-review` and `image-scan` need code and a PR; `secret-scan` and `dependency-audit` always run. Editing this file counts as `docs/ops/*` and runs everything (SBR-04, pre-existing).

The classifier was exercised locally with synthetic file lists (16 cases: docs-only, each area, contracts, scripts, workflow, gitleaks, dependabot, unknown path, README under src/). It has not run on GitHub.

## Job graph

```text
changes ─┬─> web            (web)                       ─┐
         ├─> e2e-harness    (web)                        │
         ├─> api            (backend)                    │
         ├─> e2e-real       (backend or web)             ├─> ci-result (always; skipped = ok)
         │     ├ flows-a    courses, syllabus, syllabus-utc
         │     ├ flows-b    lecture, notes, reopen, stop-move
         │     ├ screens-a  screens + axe, skins
         │     └ screens-b  geometry, gamer-glass, lecture-looks
         └─> dependency-review, image-scan   (PR and code)
secret-scan, dependency-audit   (no dependencies, always)  ─┘
```

All jobs run in parallel after `changes`. Wall time is the slowest job, now one e2e shard rather than the whole serial e2e.

### Sharding rules

- A shard is a runner, so it has its own IP (the dev-login limit is per shard), its own Postgres container and API process, and its own dev users. No data is shared between shards. The specs were already independent of each other: each creates its own courses and sessions with unique names, and per-user state uses dedicated users.
- `e2e/global-setup.ts` honours `E2E_USERS` (comma-separated `e2e/users.ts` keys) and logs in only those users. Unset means every user, as before (local runs unchanged); an unknown key throws. The lists live in the `ci.yml` matrix and were derived from the `stateFor()` / `asUser()` calls per spec (checked by grep, all 17 keys are covered). A spec that calls `stateFor()` with a key missing from its shard's list fails with a missing storage-state file. When adding a user to a spec, add it to that shard's `users`.
- The first step of every shard fails the job if any `e2e/*.spec.ts` or `e2e-screens/*.spec.ts` file is named in zero or in more than one shard, so a new spec cannot silently stop running.
- Screens specs log in their own throwaway users, so they need no `E2E_USERS`. They still run as one Playwright process per spec file, as before, and a failure in one does not stop the next.
- Rebalance by moving a spec name between shards (and its users, for `flows-*`). Do not split a single spec file across shards: several specs are serial and share state inside the file.

### Caching

| Cache | Key | Policy |
|---|---|---|
| NuGet (`~/.nuget/packages`) in `api` and each `e2e-real` shard | hash of `src/api/**/*.csproj`, `Directory.Build.props`, `global.json` (the repo has no NuGet lock file) | restore on every run, **save only on push to main** |
| Playwright browsers (`~/.cache/ms-playwright`) | Playwright version + hash of `src/web/package-lock.json` | restore on every run, save only on push to main; OS libraries are still installed each run |
| npm (`setup-node` `cache: npm`) | hash of `package-lock.json` (action default) | action default (also saves on PRs, scoped to the PR ref) |
| Trivy DB | none | off by design (SBR-01), unchanged |
| Docker images / buildx layers | none | not added: no image is built (no API Dockerfile) and only `postgres:16.15-alpine` is pulled (small); a layer cache would add a poisoning surface for little gain. Revisit when an API image exists |
| `.next/cache` of the dev server | none | deliberately not cached (L-DEVSERVER: a stale or poisoned `.next` corrupts the dev server) |

The restore/save split uses the `actions/cache/restore` and `actions/cache/save` sub-actions at the same pinned SHA as `actions/cache` (v4.2.0; both sub-actions exist at that commit, checked with `gh api`). Because PRs never save NuGet or Playwright caches, a PR's first run after a lock or csproj change is cold until a push to main has populated the cache (restore-keys fall back to the newest older cache with the same prefix).

## Expected timings (ESTIMATES, not measured)

Marked G (guess); nothing here has run on GitHub. The 35 to 55 minute serial `e2e real stack` figure is from the first CI runs.

| Scenario | Jobs that run | Estimated wall time |
|---|---|---|
| Docs-only change | `changes`, `secret-scan`, `dependency-audit`, `ci-result` | 2 to 4 min G |
| Backend-only change | `api` (10 to 20 min), 4 e2e shards, audits, scans | 15 to 22 min G |
| Web-only change | `web`, `e2e-harness`, 4 e2e shards, audits | 12 to 20 min G |
| Both / CI config / contracts | everything | 15 to 22 min G, set by the slower of `api` and the slowest shard |
| Per shard (setup 4 to 6 min + tests) | flows-a ~12, flows-b ~14, screens-a ~12, screens-b ~15 min G | slowest shard sets the e2e wall time |

The shard split is by spec count and local experience, not by measured durations; expect to rebalance after the first runs. Cost: four runners in parallel use the same test minutes as before plus about 4 x 5 min of setup, roughly 15 to 20 extra runner minutes per full run. The repo is public, so standard runners are free; if it goes private this becomes billable and needs Product Owner approval.

## Planned, NOT built: nightly full-matrix workflow

The slice 9 text asks for a PR subset of looks plus a nightly full matrix. Neither exists. Today every code PR runs the screens and skins specs in full (now sharded). A scheduled workflow (`schedule`, full looks matrix via `SCREENS`/`LOOKS`, plus the same shards) is planned only once real run times are known. Do not describe it as existing.

## Secrets needed

**None.** The CI database uses the compose dev-only credentials (`cc_dev` / `cc_dev_only_password`) on a throwaway runner container. No provider key is used; STT, Claude and Google stay mock. The e2e API is the Development environment (dev login enabled), with the same relaxed dev-only rate limits as `docker-compose.dev.yml`. That is acceptable on an ephemeral runner and must never be copied to a deployed environment.

## SB-01 DB log canary

`docker-compose.yml` starts Postgres with `log_error_verbosity=terse`, `log_min_error_statement=panic`, `log_statement=none`, `log_duration=off`, `log_parameter_max_length(_on_error)=0`, no `auto_explain`. `docs/ops/db-log-canary.sh <container> [--passive]` proves it: active mode forces a CHECK violation in a scratch database with a canary string and greps `docker logs`; passive mode greps the whole log for any `CANARY-`/`CANARY_` marker the test suites wrote. Exit 0 clean, 1 leak, 2 test could not run (so a broken probe cannot pass silently). Both modes run after the tests in `api` and `e2e-real`. Evidence of red and green runs is in the 2026-10-31 hand-off, not in a CI record.

A deployed database needs the same settings in its parameter group; that is a deployment prerequisite (SB-01), not covered by CI.

## Known limits

- Never run. First run will probably need fixes (path, timing, a Trivy finding on the base image, Playwright timing on a slower runner). Treat the first run as the test of this file.
- `ci.yml` job names changed from the old file (`api`, `web` keep their ids). Branch protection is not configured and cannot be from here (SEC-040); when it is, require the final job ids, not names from the old file. Skipped (docs-only) jobs report success. `codeql.yml` uses `paths-ignore`, so a CodeQL check does not report on docs-only PRs: do not make it required without revisiting that.
- CodeQL and dependency review need code scanning / the dependency graph enabled (free on public repos, GitHub Advanced Security on private). The repo is public (since 2026-11-01), CodeQL upload works (run 41321316201); dependency review runs on pull requests and has not run yet.
- No nightly full-matrix run and no PR-subset-of-looks split yet (the slice 9 text asks for both): the screens/skins specs run in full on every code PR. Add a `schedule` and a `SCREENS` subset once real run times are known. No bundle budget step exists.
- No API Dockerfile exists, so no application image is built or scanned. Trivy covers the images compose pulls and the config files only.
- gitleaks tarball is checked against a SHA-256 pinned in `ci.yml`. On 2026-10-31 the pin was compared with the release's own `checksums.txt` and with a fresh download, and all three matched. This is trust on first use (same origin as the asset), so it catches a later replaced asset, not a release that was bad when pinned. No custom gitleaks config.
- SBR-04: the docs-only filter treats `docs/ops/` and any `.sh/.bash/.ps1/.cmd/.bat` file as code, so editing the DB log canary runs the heavy jobs.
- SBR-01 (cache half): `image-scan` runs on pull requests only, so a compromised scan action can only write PR-scoped caches. Cost: main is not scanned automatically after merge. Residual: the runtime cache token is still exposed to the action until it is replaced by a checksum-pinned binary (follow-up, not done).
- None of the SBR-01..04 changes has run on GitHub yet.
- Action SHAs were verified on 2026-10-31 per `docs/security/action-pin-verification.md` (Trivy v0.35.0, CodeQL v3.28.3). Verified is not exercised: none of them has run on GitHub yet. Some tags are older than the newest releases; Dependabot will propose bumps.
- **CodeQL v3 deprecation (follow-up, not done):** `github/codeql-action` v3 is slated for deprecation in Dec 2026. The move to v4 (SHA `9b3254085494b55a0f7c4c2b3d2d52cc712ce370`, v4.39.1) is a follow-up that needs its release notes read first (behaviour, runner and input changes) and its own pin verification entry before any SHA in `ci.yml` is changed. The current v3.28.3 pins stay until then.
- Postgres service in CI is the repo compose file (needs Docker Compose v2 with `--wait`, present on ubuntu-24.04 runners per GitHub's docs; not verified here).
- Playwright browser cache is keyed by Playwright version and the lock file hash; OS libraries are re-installed each run. A Playwright container image as the job container would remove that step but changes the environment (and compose would run Docker-in-container); not done.
- **Sharding, path filters, the cache split and `ci-result` are unproven**: authored and actionlint-clean 2026-11-01, no GitHub run. Branch protection should require `ci result (all required jobs)`, not individual shard names (matrix names change with the matrix).
- Not parallelised yet: the `api` job (5 test projects, serial, 10 to 20 min G) is now the likely critical path for backend changes; splitting it by test project is the next lever.
- Windows line endings: `docs/ops/db-log-canary.sh` must stay LF (there is no `.gitattributes` rule for it; CI checks out on Linux, a CRLF checkout on Windows would break it under Git Bash).
- Backups, restore drill, telemetry, cost dashboards and deployment are outside CI and not done.


## Measured timings (2026-11-01)

| Run | Pipeline | Result | Wall time |
|---|---|---|---|
| 41328040245 | serial real-stack e2e | failed (lecture-looks, font timing) | 55 min |
| 41331066937 | first sharded run | failed (geometry, font timing) | 42 min |
| 41336514945 | sharded, after the layout fix | **green, all jobs** | **19 min** |

The first green run is the measured baseline: the sharded pipeline ran about three times faster than the serial one. Trivy and dependency review are skipped on pushes by design and have not yet run on a pull request. The estimates earlier in this file are superseded by these measurements.

## Trivy and the postgres image (2026-11-01)

Dependabot PR run 41331403769: the `postgres:16-alpine` gate failed with 26 findings (25 HIGH, 1 CRITICAL, e.g. CVE-2025-68121 crypto/tls), all in the Go 1.24.6 stdlib of `/usr/local/bin/gosu` (gosu 1.19). The OS packages are clean. The gate was working.

Local check, Trivy 0.69.3 (the default of trivy-action v0.35.0), `--severity HIGH,CRITICAL --ignore-unfixed --scanners vuln`, images pulled 2026-11-01: `postgres:16`, `16.15`, `16-alpine`, `16.15-alpine`, `16.15-alpine3.23`, `16.15-alpine3.24`, `16-bookworm`, `16.15-bookworm` all show the same 26 gosu findings and nothing else. **No postgres 16 tag is clean today**; the cause is upstream (the official image's gosu build), not the tag. `postgres:18` and others were not checked.

What was done (smallest honest option): compose and the CI scan now pin `postgres:16.15-alpine` (a minor pin, so Dependabot proposes bumps), and `.trivyignore.yaml` ignores those 26 IDs for the path `usr/local/bin/gosu` only, with `expired_at: 2026-12-01`. Reasoning: gosu runs once to drop root and then exec()s postgres; it opens no listeners, so the stdlib net/http, tls, url and xml issues are not reachable; the DB is a localhost dev container or throwaway runner database. Checked locally: with the file the scan exits 0; with the expiry set in the past it exits 1 (26 findings); with a wrong path it exits 1. So a new finding elsewhere, or the expiry date, re-fails the gate. Used by the `image-scan` postgres step only (not the SDK or config steps).

On or before 2026-12-01: re-scan the newest `postgres:16.x-alpine`; if gosu is rebuilt with Go >= 1.25.13, delete `.trivyignore.yaml`; otherwise renew with a fresh justification or switch to a different base. Alternatives not taken: building our own image that drops gosu (adds an image to maintain), or a broader `--skip-files` (hides more). Never carry this to a deployed database: that uses a managed Postgres.

Other steps checked the same day: the repo config scan on a `git archive HEAD` copy is clean (exit 0). The `dotnet/sdk:10.0` report-only step shows 1 HIGH (libbrotli1 CVE-2026-111497, fixed in 1.1.0-2ubuntu0.1); it does not gate, and is dev tooling, so it is only reported.

**Unproven until a PR run:** that trivy-action v0.35.0 honours `trivyignores: .trivyignore.yaml` on the runner (its entrypoint code supports a single YAML file, read from the pinned SHA), that the runner pulls the same gosu, . The running `cc-postgres-dev` image id (sha256:0cefaabd...) equals the `postgres:16.15-alpine` digest pulled today, so recreating it on the pinned tag should change nothing (read from `docker inspect`; recreate not done).
