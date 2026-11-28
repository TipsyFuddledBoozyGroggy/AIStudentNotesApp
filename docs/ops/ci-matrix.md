# CI matrix (refreshed 2026-11-27 from the workflow files and real GitHub runs)

**Status.** CI has run on GitHub many times since 2026-11-01. The first fully green run was 41336514945 on 2026-11-01. `ci.yml` runs on every push to main and on every pull request. The most recent green run on main is 46101097100 (commit 49e33a3, 2026-11-28 03:04 UTC). Dates below are UTC. **Nothing in this file clears release gate G5.** Green runs are evidence that the jobs work. They are not a deployment record, a restore drill, telemetry or a cost review. `docs/RELEASE_GATES.md` is the canonical register.

Files: `.github/workflows/ci.yml`, `codeql.yml`, `deploy-azure-dev.yml`, `.github/dependabot.yml`, `docs/ops/db-log-canary.sh`, `.trivyignore.yaml`.

## Jobs (ci.yml)

Durations are measured job times (start to finish on the runner, queue time excluded). Source: the five most recent successful push runs that ran every job including `infra-scripts`: 46101097100 (2026-11-28), 46099528347 (2026-11-28), 46083395709 (2026-11-27), 46078651462 (2026-11-27, attempt 2 after a rerun), 45979864216 (2026-11-27). Pushes to main always run everything, so none of these was a docs-only skip.

| Job | Runs when | What it does | Measured | Local equivalent |
|---|---|---|---|---|
| `changes` | every PR and push | Classifies the diff into `backend`, `web`, `e2e`, `code` (see Area filters) | 4 to 11 s | n/a |
| `web` | `web` | `npm ci`, `tsc --noEmit`, eslint, a named copy-and-skin lint gate (4 vitest files), full vitest, `next build` (retried up to 3 times for font-fetch failures) | 2.2 to 3.0 min | `cd src/web && npm run typecheck && npm run lint && npm test && npm run build` |
| `api` | `backend` | Compose Postgres (hardened), `dotnet restore`, `build -c Release -warnaserror`, `dotnet test` (all test projects, integration included), DB log canary | 2.9 to 3.0 min | stop the dev API, `docker compose up -d postgres`, `dotnet test` in `src/api` (see DEVELOPMENT.md) |
| `e2e-harness` | `web` | Playwright harness config, own `next dev` on :3100, no API, `--workers=1` | 3.8 to 4.4 min | `cd src/web && npx playwright test -c playwright.harness.config.ts --workers=1` (not beside a dev server in the same folder) |
| `e2e-real` x4 shards | `e2e` (backend or web) | Each shard has its own compose Postgres, API on :5080 (Development env) and `npm run dev:real` on :3000; serial inside a shard. DB log canary at the end; artifacts `e2e-real-output-<shard>` on failure | `flows-a` 2.9 to 3.9 min; `flows-b` 5.6 to 6.3 min; `screens-a` 14.9 to 17.9 min; `screens-b` 18.9 to 22.9 min | start the API and `npm run dev:real`, then `npx playwright test -c playwright.reuse.config.ts --workers=1 <specs>` or `-c playwright.screens.config.ts e2e-screens/<spec> --workers=1` |
| `infra-scripts` | `code` | Case tests for the deploy scripts (`preflight-cases.sh`, `smoke-cases.sh`, `template-cycles-cases.sh`), then installs Bicep v0.49.2, lints and builds `infra/main.bicep`, and runs `infra/scripts/template-cycles.mjs` on the result to catch dependency circles | 1.9 to 2.2 min | `bash infra/scripts/tests/preflight-cases.sh` (and the other two); the Bicep step needs `az bicep` |
| `secret-scan` | always | gitleaks 8.21.2 over full history, tarball checked against a pinned SHA-256 | 7 to 13 s | `gitleaks detect --source . --config .gitleaks.toml --redact` |
| `dependency-audit` | always | `npm audit` (prod: any severity fails; all: high/critical fail, moderate allowed up to the known 14); `dotnet list package --vulnerable --include-transitive`, fails on High/Critical or if the check cannot run | 35 to 41 s | same commands in `src/web` and `src/api` |
| `dependency-review` | pull requests with `code` | GitHub dependency review, fails on high | not measured on a push; ran on PRs | none |
| `image-scan` | pull requests with `code`, never push | Trivy: `postgres:16.15-alpine` (gate, fixable HIGH/CRITICAL), `dotnet/sdk:10.0` (report only), repo config scan. Trivy DB cache off | not measured (skipped in the runs above) | `docker run --rm aquasec/trivy:0.69.3 image --ignorefile .trivyignore.yaml --ignore-unfixed --severity HIGH,CRITICAL postgres:16.15-alpine` |
| `ci-result` | always | The one check for branch protection: fails if any needed job failed or was cancelled; skipped counts as success | 2 to 5 s | none |

Wall time of a full push run, from the same five runs: 19 min 9 s, 19 min 13 s, 20 min 34 s, 22 min 41 s, 27 min 23 s (the last includes a rerun). Typical: 19 to 23 minutes. `screens-b` sets it. Slowest shards vary by about 4 minutes between runs, so treat the figures as a range.

All jobs: `permissions: contents: read` (least privilege), a timeout, and a concurrency group (PR runs are cancelled by newer pushes; main runs are not, but a newer push can cancel a queued main run: several runs show `cancelled`). Every action is pinned to a full commit SHA.

## Area filters (the `changes` job)

A plain `git diff --name-only <base> HEAD` and a shell `case`. No third-party action. **The diff filter applies to pull requests only.** A push to main has no base and always runs everything (so a docs-only push to main still runs the heavy jobs). For a PR, first match wins per changed file:

| Path | Effect |
|---|---|
| `.github/*`, `.gitleaks.toml`, `docs/ops/*`, any `*.sh *.bash *.ps1 *.cmd *.bat` | backend and web |
| `docs/*`, `.claude/*`, any `*.md` | nothing (docs-only) |
| `src/shared/contracts/*` | backend and web |
| `src/api/*`, `docker-compose*.yml`, `global.json`, `Directory.*` (any folder) | backend |
| `src/web/*` | web |
| anything else (for example `infra/*` non-markdown, `scripts/*` non-shell files, `.gitattributes`, `.trivyignore.yaml`) | backend and web (conservative) |

There is no `infra/` or `scripts/` rule. `infra/` files and `scripts/*.mjs` hit the "anything else" line and run everything. Any `.sh` or `.ps1` file runs everything wherever it lives (SBR-04). A markdown file such as `infra/README.md` counts as docs-only.

Outputs: `e2e` and `code` are each "backend or web". A missing or unfetchable base runs everything. Gating: `api` needs backend; `web` and `e2e-harness` need web; `e2e-real` needs e2e; `infra-scripts` needs code; `dependency-review` and `image-scan` need code and a PR; `secret-scan` and `dependency-audit` always run.

**The classifier has no tests of its own.** There is no test file in the repo for it. It was exercised by hand on 2026-11-01 with 16 synthetic file lists, and the PR run 45979328130 ran it for real. The case tests under `infra/scripts/tests/` cover the deploy scripts, not this `case`.

## Job graph

```text
changes ─┬─> web            (web)                       ─┐
         ├─> e2e-harness    (web)                        │
         ├─> api            (backend)                    │
         ├─> e2e-real       (backend or web)             │
         │     ├ flows-a    courses, syllabus, syllabus-utc, syllabus-unclaimed, signin
         │     ├ flows-b    lecture, notes, reopen, stop-move, delete, notes-export, docs-export
         │     ├ screens-a  screens + axe, skins          ├─> ci-result (always; skipped = ok)
         │     └ screens-b  geometry, gamer-glass, lecture-looks
         ├─> infra-scripts  (backend or web)             │
         └─> dependency-review, image-scan   (PR and code)
secret-scan, dependency-audit   (no dependencies, always)  ─┘
```

All jobs run in parallel after `changes`. Wall time is the slowest job.

### Sharding rules

- A shard is a runner: its own IP (the dev-login limit is per shard), Postgres container, API process and dev users. Specs create their own courses and sessions with unique names.
- `e2e/global-setup.ts` honors `E2E_USERS` (comma-separated `e2e/users.ts` keys) and logs in only those users. Unset means all users (local runs unchanged); an unknown key throws. The lists live in the `ci.yml` matrix. A spec that calls `stateFor()` with a key missing from its shard's list fails with a missing storage-state file. When adding a user to a spec, add it to that shard's `users`.
- The first step of every shard fails the job if any `e2e/*.spec.ts` or `e2e-screens/*.spec.ts` file is named in zero or in more than one shard. This caught a real gap on 2026-11-27 (PR run 45979328130: `signin.spec.ts` in 0 shards); fixed by f9502c2.
- Screens specs log in their own throwaway users. They run as one Playwright process per spec file; a failure in one does not stop the next.
- Rebalance by moving a spec name between shards (and its users, for `flows-*`). Do not split a spec file across shards: several specs are serial and share state.

### Caching

| Cache | Key | Policy |
|---|---|---|
| NuGet in `api` and each `e2e-real` shard | hash of `src/api/**/*.csproj`, `Directory.Build.props`, `global.json` (no lock file) | restore on every run, **save only on push to main** |
| Playwright browsers | Playwright version + hash of `package-lock.json` | restore on every run, save only on push to main; OS libraries installed each run |
| npm (`setup-node`) | hash of `package-lock.json` | action default |
| Trivy DB | none | off by design (SBR-01) |
| Docker layers, `.next/cache` | none | not cached on purpose (no layer cache; a stale `.next` corrupts the dev server) |

A PR's first run after a lock or csproj change is cold until a push to main has populated the cache.

## CodeQL (codeql.yml)

Triggers: pull requests and pushes to main (both skip docs-only changes via `paths-ignore: docs/**, **/*.md, .claude/**`) and a weekly schedule, Mondays 04:17 UTC. Two matrix jobs, query suite `security-extended`:

- `analyze (csharp)`: **manual build** (`dotnet build src/api/Tether.slnx -c Release`) under the CodeQL tracer, with the SDK from `src/api/global.json`. Buildless mode broke on .NET 10.
- `analyze (javascript-typescript)`: no build.

`github/codeql-action` is pinned at v4.38.3. Permissions: `contents: read`, plus `security-events: write` and `actions: read` on the job. Latest push run 46083395664 (2026-11-27) passed: csharp 152 s, javascript-typescript 86 s. The weekly schedule has run and passed (for example 45198634817 on 2026-11-23). Because `paths-ignore` means no check reports on docs-only changes, do not make a CodeQL check required in branch protection without revisiting that.

## Dependabot (.github/dependabot.yml)

Weekly updates for four ecosystems: `github-actions` (`/`, one group `actions`, max 5 open PRs), `npm` (`/src/web`, group `minor-and-patch`, max 5), `nuget` (`/src/api`, group `minor-and-patch`, max 5) and `docker-compose` (`/`, max 3, no group). Its PRs run the same CI as any PR.

## Deploy workflow (deploy-azure-dev.yml)

A manual `workflow_dispatch`, from main only. It never runs on a push or a pull request and is not part of `ci result`. **Every run waits for Product Owner approval** on the GitHub environment `azure-dev` before it signs in to Azure (OIDC, no stored Azure secret). Procedure, secrets, run order and the Product Owner steps are in [`infra/README.md`](../../infra/README.md); do not copy them here.

Inputs:

| Input | Meaning |
|---|---|
| `access` | `team` (default): Microsoft sign-in in front of the site, Google sign-in behind it; also the rollback. `invited`: Google sign-in and the invite list only. `closed`: emergency stop; makes the web ingress internal and deploys nothing |
| `gate_only` | Change the access mode only. No new build; the live release stays. Required when taking the Microsoft gate off |
| `release_sha` | Optional full commit id of an earlier release to deploy or keep. Must be on main and at or after the tag `go-live-base` |

Before the deploy: a plan job (picks the commit, refuses a template that has a dev login), a build job that pushes both images to ghcr.io (skipped for `gate_only`, `release_sha` and `closed`) and a preflight (`infra/scripts/preflight.sh`) that refuses missing values. A live-state check then refuses unsafe moves, such as taking the Microsoft gate off before the live API is proven to be Production. After the deploy: a smoke check (`infra/scripts/smoke.sh`) of health, the live sign-in mode and, from outside, the gate. **Fail closed:** if a run that asked for public ingress does not end with the gate proven (deploy failed, probe failed or unclear, run cancelled), the last step makes the web ingress internal again.

Runs so far: 5 (`gh run list --workflow deploy-azure-dev.yml`), all on 2026-11-27 UTC.

| Run | Commit | Result |
|---|---|---|
| 45924207613 | 54ddb99 | failure |
| 45928522029 | 76c55d2 | success |
| 45957101380 | 34a71f4 | success |
| 46079398123 | b9a6367 | failure |
| 46083406628 | c6078a1 | **success (latest)** |

The infra-scripts CI job exists because Azure refused the template in a run for a dependency circle that `bicep build` does not report.

## Secrets needed in CI

**None for `ci.yml`.** The CI database uses the compose dev-only credentials on a throwaway runner container. No provider key is used; STT, Claude and Google stay mock. The e2e API is the Development environment (dev login on, relaxed dev-only rate limits). Never copy that to a deployed environment. The deploy workflow uses GitHub environment secrets (see `infra/README.md`).

## SB-01 DB log canary

`docker-compose.yml` starts Postgres with `log_error_verbosity=terse`, `log_min_error_statement=panic`, `log_statement=none`, `log_duration=off`, `log_parameter_max_length(_on_error)=0`, no `auto_explain`. `docs/ops/db-log-canary.sh <container> [--passive]` proves it: active mode forces a CHECK violation with a canary string and greps `docker logs`; passive mode greps the whole log for any `CANARY-`/`CANARY_` marker the suites wrote. Exit 0 clean, 1 leak, 2 the probe could not run. Both modes run after the tests in `api` and in every `e2e-real` shard. A deployed database needs the same settings in its parameter group (deployment prerequisite, not covered by CI). The script must stay LF; `.gitattributes` forces LF for `*.sh`.

## Trivy and the postgres image

`image-scan` gates on `postgres:16.15-alpine`. Every postgres 16 tag checked on 2026-11-01 showed the same 26 HIGH findings, all in the Go stdlib of `/usr/local/bin/gosu` (upstream, not the tag). `.trivyignore.yaml` ignores those IDs for that path only, with `expired_at: 2026-12-01`, on the reasoning that gosu runs once to drop root and opens no listener. On or before 2026-12-01: re-scan the newest `postgres:16.x-alpine`; delete the file if gosu is rebuilt, otherwise renew with a fresh justification or change the base. This applies to local and CI containers only; a deployed database is managed Postgres.

## Reading a failed run

- `gh run view <id> --json jobs` and look for the first job with `conclusion: failure`. `ci result` only repeats it.
- e2e failure: download the artifact `e2e-real-output-<shard>` (Playwright output, `api.log`, `web.log`; kept 7 days). Harness: `e2e-harness-output`. API tests: `api-test-results` (trx).
- Shard coverage failure ("listed in 0 shards"): a spec file is not named in the `ci.yml` matrix.
- Font fetch failure in `next build`: retried 3 times already; three failures in a row is real.
- Canary exit 1 is a leak (real). Exit 2 is a broken probe (also a failure).
- For flaky versus real, see Known limits. Diagnosis of the first red runs: `docs/ops/ci-first-run-e2e-diagnosis.md`.

## Known limits

- **PR-only jobs have run, on Dependabot PRs only.** All 25 PRs ever opened are Dependabot PRs (10 open, 15 closed, none merged). `dependency-review` passed on them (for example 45979328130, 45083059394). `image-scan` (Trivy) has **failed** on every PR run I read (45979328130 on 2026-11-27, 45083059394 on 2026-11-22, 41331403769 on 2026-11-01). In 45979328130 it reported 26 HIGH findings (the gosu ones) even though `.trivyignore.yaml` was present; I did not establish why. So the gate has never been seen green on GitHub, and the ignore file is unproven on the runner. Neither job has run on a PR from the maintainers.
- Branch protection on `main` (read 2026-11-27 with `gh api .../branches/main/protection`): the required check is `ci result (all required jobs)`, not shard names; no required reviews; force pushes off; not enforced for admins (SEC-040).
- **Flaky tests.**
  - Run 46078651462 (2026-11-27, commit b9a6367) failed once in `RealtimeAudioTests.Timecodes_come_from_accepted_sample_counts_not_from_the_client_supplied_offset` with "The remote end closed the connection" and passed on rerun. Not fixed.
  - `RealtimeAudioTests.Deleting_a_streaming_session_closes_the_socket...` once closed 1000 instead of 4409 (see `docs/PROGRESS.md`). Not fixed.
  - `docs/DEVELOPMENT.md` lists two older load-sensitive tests seen 2026-11-01: `AiRouteTests.Concurrent_requests_with_the_same_key_make_exactly_one_catch_up_and_all_get_it` (expects 5 `Idempotency-Replayed` headers, got 4; a real race is not ruled out) and a `RealtimeLogCanaryTests` `ObjectDisposedException` (fixed). Run the .NET integration suite on an idle machine.
  - Font-fetch failures in `next build` are retried in the workflow.
- **Not implemented:** nightly or scheduled full matrix (CI has no `schedule`; only CodeQL does); PR-subset of looks; AI evals as a CI job; bundle budget; an image scan of the built API and web images (the Dockerfiles exist and the deploy workflow builds them, but `image-scan` still scans only the postgres image, the SDK image and the config files); backups, restore drill, telemetry and cost dashboards.
- gitleaks tarball is checked against a pinned SHA-256 (trust on first use: it catches a later replaced asset, not a release that was bad when pinned).
- `image-scan` runs on PRs only, so main is not scanned after merge (SBR-01). The runtime cache token is still exposed to the Trivy action until it is replaced by a checksum-pinned binary (follow-up).
- Editing `docs/ops/` (including this file) runs every heavy job on a PR (SBR-04).
- Not parallelized: the `api` job runs all test projects serially (about 3 minutes, not the critical path today).
- Fork or Dependabot PRs get no repository secrets; CI needs none.
