# CI matrix (authored 2026-11-01, never run on GitHub)

**Status: NOT_RUN.** These workflows were written and statically checked on 2026-10-31/11-01. No GitHub Actions run record exists, so nothing here is evidence for RELEASE_GATES G5. The gate stays `NOT_RUN` until a real run exists and an accountable reviewer has read it.

Files: `.github/workflows/ci.yml`, `.github/workflows/codeql.yml`, `.github/dependabot.yml`, `docs/ops/db-log-canary.sh`.

## Jobs

Runtime figures are **estimates** from local runs of the same commands (marked L) or guesses (marked G). They include no runner start-up, cache restore or Playwright browser install.

| Job (workflow) | Runs on | What it does | Estimate | Local equivalent |
|---|---|---|---|---|
| `changes` (ci) | every PR/push | `git diff` against the base commit; docs-only (`docs/**`, `.claude/**`, `*.md`) sets `code=false` so the heavy jobs skip | < 1 min G | n/a |
| `web` (ci) | code changes | `npm ci`, `tsc --noEmit`, eslint, **named copy and skin lint gate** (4 vitest files, 186 tests, 2.6 s L), full vitest (about 20 s L), `npm audit` (prod: any severity; all: high/critical fail, moderate allowed up to the known 14), `next build` (14 s L) | 4 to 6 min G | `cd src/web && npm run typecheck && npm run lint && npm test && npm run build` |
| `api` (ci) | code changes | compose Postgres (hardened), `dotnet restore`, `build -c Release -warnaserror`, `dotnet test` (all 5 test projects, integration tests included, no dev API needed), then the **DB log canary** | 10 to 20 min G | stop the dev API first, then `docker compose up -d postgres` and `dotnet test` (see DEVELOPMENT.md) |
| `e2e-harness` (ci) | code changes | Playwright harness config, own `next dev` on :3100, no API, `--workers=1` | 5 to 8 min G (1.1 min L for the run itself) | `cd src/web && npx playwright test -c playwright.harness.config.ts --workers=1` (never beside a dev server in the same folder) |
| `e2e-real` (ci) | code changes | compose Postgres, API on :5080 (Development env), `npm run dev:real` on :3000, then serial: real-stack e2e (reuse config), geometry, screens + axe, skins, gamer-glass, lecture-looks; DB log canary; artifacts on failure | 25 to 40 min G (screens alone 3 to 5 min L) | start the API and `npm run dev:real`, then the same `npx playwright test -c playwright.reuse.config.ts --workers=1` / `-c playwright.screens.config.ts e2e-screens/<spec> --workers=1` |
| `secret-scan` (ci) | always, full history | gitleaks v8.21.2, tarball verified against a pinned SHA-256 (SBR-03) | 1 min G | `gitleaks detect --source . --redact` |
| `dependency-audit` (ci) | code changes | `dotnet list package --vulnerable --include-transitive`, fails on High/Critical, and also fails if the check cannot run (nonzero exit, NU1900/feed errors, or no per-project result) (SBR-02) | 2 min G | same command in `src/api` |
| `dependency-review` (ci) | PRs with code changes | GitHub dependency review, fails on high | 1 min G | none |
| `image-scan` (ci) | code PRs only, never push to main; Trivy DB cache off (SBR-01) | Trivy: `postgres:16-alpine` (gates on fixable HIGH/CRITICAL), `dotnet/sdk:10.0` (report only, dev tooling), repo config scan (compose, workflows) | 3 to 6 min G | `docker run --rm aquasec/trivy image postgres:16-alpine` |
| `analyze` x2 (codeql) | code PRs, pushes to main, weekly | CodeQL `security-extended` for `csharp` and `javascript-typescript`, no-build mode | 10 to 25 min G | none |

All jobs: `permissions: contents: read` (CodeQL adds `security-events: write` and `actions: read`), a timeout, and a concurrency group (PR runs are cancelled by newer pushes; main runs are not). Every action is pinned to a full commit SHA.

## Secrets needed

**None.** The CI database uses the compose dev-only credentials (`cc_dev` / `cc_dev_only_password`) on a throwaway runner container. No provider key is used; STT, Claude and Google stay mock. The e2e API is the Development environment (dev login enabled), with the same relaxed dev-only rate limits as `docker-compose.dev.yml`. That is acceptable on an ephemeral runner and must never be copied to a deployed environment.

## SB-01 DB log canary

`docker-compose.yml` starts Postgres with `log_error_verbosity=terse`, `log_min_error_statement=panic`, `log_statement=none`, `log_duration=off`, `log_parameter_max_length(_on_error)=0`, no `auto_explain`. `docs/ops/db-log-canary.sh <container> [--passive]` proves it: active mode forces a CHECK violation in a scratch database with a canary string and greps `docker logs`; passive mode greps the whole log for any `CANARY-`/`CANARY_` marker the test suites wrote. Exit 0 clean, 1 leak, 2 test could not run (so a broken probe cannot pass silently). Both modes run after the tests in `api` and `e2e-real`. Evidence of red and green runs is in the 2026-10-31 hand-off, not in a CI record.

A deployed database needs the same settings in its parameter group; that is a deployment prerequisite (SB-01), not covered by CI.

## Known limits

- Never run. First run will probably need fixes (path, timing, a Trivy finding on the base image, Playwright timing on a slower runner). Treat the first run as the test of this file.
- `ci.yml` job names changed from the old file (`api`, `web` keep their ids). Branch protection is not configured and cannot be from here (SEC-040); when it is, require the final job ids, not names from the old file. Skipped (docs-only) jobs report success. `codeql.yml` uses `paths-ignore`, so a CodeQL check does not report on docs-only PRs: do not make it required without revisiting that.
- CodeQL and dependency review need code scanning / the dependency graph enabled (free on public repos, GitHub Advanced Security on private). Unconfirmed for this repo.
- No nightly full-matrix run and no PR-subset-of-looks split yet (the slice 9 text asks for both): the screens/skins specs run in full on every code PR. Add a `schedule` and a `SCREENS` subset once real run times are known. No bundle budget step exists.
- No API Dockerfile exists, so no application image is built or scanned. Trivy covers the images compose pulls and the config files only.
- gitleaks tarball is checked against a SHA-256 pinned in `ci.yml`. On 2026-10-31 the pin was compared with the release's own `checksums.txt` and with a fresh download, and all three matched. This is trust on first use (same origin as the asset), so it catches a later replaced asset, not a release that was bad when pinned. No custom gitleaks config.
- SBR-04: the docs-only filter treats `docs/ops/` and any `.sh/.bash/.ps1/.cmd/.bat` file as code, so editing the DB log canary runs the heavy jobs.
- SBR-01 (cache half): `image-scan` runs on pull requests only, so a compromised scan action can only write PR-scoped caches. Cost: main is not scanned automatically after merge. Residual: the runtime cache token is still exposed to the action until it is replaced by a checksum-pinned binary (follow-up, not done).
- None of the SBR-01..04 changes has run on GitHub yet.
- Action SHAs were verified on 2026-10-31 per `docs/security/action-pin-verification.md` (Trivy v0.35.0, CodeQL v3.28.3). Verified is not exercised: none of them has run on GitHub yet. Some tags are older than the newest releases; Dependabot will propose bumps.
- **CodeQL v3 deprecation (follow-up, not done):** `github/codeql-action` v3 is slated for deprecation in Dec 2026. The move to v4 (SHA `9b3254085494b55a0f7c4c2b3d2d52cc712ce370`, v4.39.1) is a follow-up that needs its release notes read first (behaviour, runner and input changes) and its own pin verification entry before any SHA in `ci.yml` is changed. The current v3.28.3 pins stay until then.
- Postgres service in CI is the repo compose file (needs Docker Compose v2 with `--wait`, present on ubuntu-24.04 runners per GitHub's docs; not verified here).
- Playwright browser cache is keyed by Playwright version; OS libraries are re-installed each run.
- Windows line endings: `docs/ops/db-log-canary.sh` must stay LF (there is no `.gitattributes` rule for it; CI checks out on Linux, a CRLF checkout on Windows would break it under Git Bash).
- Backups, restore drill, telemetry, cost dashboards and deployment are outside CI and not done.
