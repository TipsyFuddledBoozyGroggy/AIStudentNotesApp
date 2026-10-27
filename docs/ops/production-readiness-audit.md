# Production Readiness Audit (self-audit)

**Authored by devops-engineer acting as substitute for production-readiness-auditor (agent not loaded). Self-audit; independent review still required.**

Date: 2026-10-27. Repo HEAD 39a026f (5 commits ahead of origin/main). Method: reading files plus `git remote -v`, `git ls-remote`, `git status -sb`. No command was run that executes CI, builds, scans or touches a cloud account. Nothing here is a deployment-readiness claim.

## Gate status

**Production CI, scanning, observability, backups: BLOCKED / NOT VERIFIED (matches CLAUDE.md:101).** No item in this document is `PASS`. Deployment remains blocked until a human produces the records in the last section and the Product Owner approves release.

## Fact that governs everything else

- `git ls-remote origin` returns a single commit `afcb951 "Initial commit"` on `main`. `git status -sb` reports `main...origin/main [ahead 5]`. Our five commits (including `.github/workflows/ci.yml` history) are not on GitHub, so **the workflow has never executed anywhere**. There are zero CI run URLs. Every "CI" claim below is configuration review only.
- Whether GitHub Actions is enabled, whether branch protection exists, and whether CodeQL/Dependabot/secret scanning are enabled in repo settings is unknown. I did not and cannot check repository settings from here (NOT RUN).

## Claimed vs evidenced

| Area | Claim (where) | What the files show | Status |
|---|---|---|---|
| CI runs | docs/DEVELOPMENT.md:26-28, test-strategy.md section 6 | `.github/workflows/ci.yml` exists (98 lines). Never run (see above). | NOT RUN |
| API CI job works | ci.yml:45-59 | Job runs `dotnet test` with no Postgres service and no `ConnectionStrings__Default`. `ApiFactory.cs:23-24` throws if that variable is missing. Integration tests will almost certainly fail on first real run. Local runs used the compose `dotnet` service (docker-compose.dev.yml:37). **Defect in my own earlier work.** | LIKELY BROKEN, unverified |
| Web CI | ci.yml:26-43 | lint, `npm test --if-present`, build. Playwright E2E (`src/web/e2e`, `playwright.config.ts`) is not in CI. test-strategy section 6 lists E2E in the PR gate. | PARTIAL |
| Security regression suite as required gate (SEC-041) | SECURITY.md:70 | No separate job; no required-check config. Tests live inside `dotnet test` only. | NOT MET |
| CodeQL / SAST (SEC-039) | SECURITY.md:68 | No `codeql` workflow, no `.github/` content other than `workflows/ci.yml`. | ABSENT |
| Dependabot/Renovate (SEC-039) | SECURITY.md:68 | No `.github/dependabot.yml`. | ABSENT |
| Dependency scan | ci.yml:72-95 | `npm audit --audit-level=high` and `dotnet list package --vulnerable` with a grep. The grep `! grep -qE "High\|Critical"` also matches any package name or text containing those words (false positives) and exit status of `dotnet list` is ignored by `tee` pipeline (no pipefail override; default bash `-e` without pipefail). No seeded-vulnerable-package test exists as SEC-039 Verify requires. No waiver mechanism. | WEAK, unproven |
| Container image scan (SEC-039) | SECURITY.md:68 | No Dockerfile exists in `src/` (find returned none); compose uses stock `mcr.microsoft.com/dotnet/sdk:10.0` with the repo bind-mounted (docker-compose.dev.yml:44-51). Nothing to scan, no Trivy/Grype step. No SBOM. | ABSENT |
| Secret scanning (SEC-019) | ci.yml:61-70 | gitleaks 8.21.2 fetched by `curl` with no checksum verification; full history (`fetch-depth: 0`). Never run. No `.gitleaks.toml`, no local pre-commit hook, GitHub native push protection unknown. `.gitignore` covers `.env*`, keys (`.gitignore` lines 1-9 of file); `.env.example` has placeholders only. | UNPROVEN |
| Action pinning / supply chain (SEC-040) | SECURITY.md:69 | Actions pinned to major tags (`@v4`), not SHAs. Permissions `contents: read` at top (ci.yml:6-7) is good. | PARTIAL |
| Lockfiles (SEC-040) | SECURITY.md:69 | `src/web/package-lock.json` present. **No NuGet lock files** (`packet.lock.json` absent; `RestorePackagesWithLockFile` not set in `Directory.Build.props:1-12`). CI restores without `--locked-mode`. `dotnet-tools.json` exists. | PARTIAL |
| Branch protection / required review (SEC-040) | SECURITY.md:69 | Not in repo; settings unknown. | NOT RUN |
| .NET version | ci.yml:56 comment PO-1 | Pinned `10.0.x` floating patch; PO decision on .NET 10 vs 8 not evidenced as closed. | OPEN |
| Observability | CLAUDE.md "structured telemetry" | Only: console `ILogger` with default providers (`appsettings.json:2-8`), `/health/live` and `/health/ready` (Program.cs:151-152, `PostgresReadyCheck` HostServices.cs:36-50, DB probe only), an unhandled-exception logger (Program.cs:133). No OpenTelemetry, no metrics, no request IDs/correlation, no alerting, no dashboards, no log shipping, no STT/Claude uptime signal (those providers do not exist yet). No log-redaction test or lecture-content log guard. | MINIMAL; no deployed telemetry |
| Cost guardrails (SEC-037) | SECURITY.md:64 | Only request rate limits (Program.cs:49-80, SEC-036 partial: per-user/IP fixed windows, in-process memory, not shared across instances, no hub/AI policies yet). No token/audio-minute budgets, kill switch, spend alert, session cap, or provider usage dashboard. Providers are mock (`.env.example` `*_PROVIDER=mock`), so spend is currently nil by absence, not by control. | NOT IMPLEMENTED |
| Backup/restore drill | Required deliverable | No backup script, schedule, encryption, RPO/RTO, or restore procedure in repo. Compose uses a single local named volume `pgdata` (docker-compose.yml:13-14, 23-24), `restart: unless-stopped`, no replication. Zero drills. | ABSENT; drill NOT RUN |
| Secrets management | SEC-019 | Dev: env defaults labelled DEV ONLY (docker-compose.yml:8-9) and `.env.example`; startup fail-fast on missing required secret is not shown in the host for non-DB secrets (no secrets exist yet). No vault/managed store integration, no rotation procedure, no IaC. Dev defaults for the DB password will silently apply if `.env` is missing; nothing prevents reuse of them outside dev. | DEV-ONLY |
| Cookie (DataProtection) key persistence | m1-review.md:123 "Fixed, with residuals" | Code: `PersistKeysToFileSystem` only when `DataProtection:KeysPath` set (IdentityModule.cs:50-52); warning only, not failure, when unset in non-Development (Program.cs:87-92); a test writes a key file (SecurityHardeningTests.cs:365-368). Residuals the review itself notes: keys unencrypted at rest, **compose has no volume/path configured** (the api service in docker-compose.dev.yml:43-62 sets no `DataProtection__KeysPath`, so local restarts sign everyone out). No KMS/cert protection (`ProtectKeysWith...`), no key rotation or backup plan. Files sit on an ephemeral container FS in any containerised deploy. | PARTIAL; not deployable |
| ETag key persistence | src/api/README.md:20 | `Security:ETagKey` optional; random per process if unset (CoursesModule.cs:30-34), warning only (Program.cs:90-92). Not set in compose. In multi-instance or after restart, If-Match returns 412 for every client. Not stored in any secret manager. Should fail startup in Production, not warn. | PARTIAL |
| Session-row retention | m1-review.md:126 "Deferred" | `identity.sessions` rows (SessionPolicy.cs:18,34) are never deleted. Table grows unbounded and retains user_id/timestamps beyond need; no purge job; not coordinated with Security retention policy. | NOT IMPLEMENTED |
| Deployment config | CLAUDE.md "Ops" | No Dockerfile for api or web, no IaC, no staging/prod environment config, no TLS/domain definition, no migration-run procedure (`Database:MigrateOnStartup=false`, appsettings.json:19-21; no migration job), no rollback or incident runbook. `AllowedHosts: "*"` (appsettings.json:9) is unsuitable for production unless overridden. docker-compose files are labelled "Local development only" (docker-compose.yml:1). That label is accurate. | ABSENT (by design so far) |
| docs accuracy | docs/DEVELOPMENT.md:6-9 | Still says .NET SDK not installed and "CI skips the api job while src/api has no .csproj"; stale since the API scaffold. | STALE |

## Top gaps (ordered)

1. CI never executed; remote lacks our commits. Push is a Product Owner / Lead action; I did not push.
2. API CI job has no Postgres and no connection string, so it likely fails; the CI "gate" cannot be trusted until a green run exists.
3. SEC-039 incomplete: no CodeQL, no Dependabot, no image scan, no SBOM, no seeded-vulnerability proof; dependency step is fragile; gitleaks unverified download; no NuGet lockfiles.
4. No backup/restore, no secret manager, no deployment artifacts (Dockerfiles, IaC, TLS, migrations job), so no environment exists to evidence.
5. Keys and retention: DataProtection and ETag keys warn instead of failing in Production and are not persisted in compose; session rows never purged; no cost/budget controls (SEC-037) or telemetry.

## Records and drills a human must produce (none exist today)

Each needs an accountable reviewer, date and link, then a row in `docs/RELEASE_GATES.md` (file not present in the repo at this audit).

1. Push the branch; capture the GitHub Actions run URL for web, api, secret-scan, dependency-audit with conclusion and commit SHA. Fix api job (Postgres service container plus `ConnectionStrings__Default`) first.
2. Screenshot or `gh api` export of repo settings: branch protection with required checks and review, Actions permissions, secret scanning with push protection, Dependabot alerts, code scanning.
3. A CodeQL run result (C# and JS/TS), and a Dependabot config merged and producing a first alert/PR list.
4. Seeded-vulnerable-package PR showing the dependency job fails (SEC-039 Verify), then reverted.
5. Container image build and scan report once Dockerfiles exist; SBOM artifact.
6. Secret-scan evidence including a deliberate canary secret that is detected.
7. Witnessed backup and restore drill: stated RPO/RTO, encrypted backup, restore into a clean instance, row-count and migration-version check, measured elapsed time, operator name.
8. Staging deployment record with Product Owner approval: telemetry visible, alert fired and received, rollback exercised, key-rotation exercised (cookie keys, ETag key), DataProtection keys surviving a restart.
9. SEC-037 simulated overrun test and a provider spend alert configured on a real account (needs Product Owner spend approval; STT and Claude pricing are UNVERIFIED per CLAUDE.md:98).
10. Session-retention job decision from Security, implemented and verified by row counts.
11. Product Owner go/no-go review. Not obtainable from any AI agent.

## Assumptions and limits

- I assumed the remote on GitHub is the intended CI host; I did not check whether the repo is private or whether Actions minutes or features differ by plan.
- Workflow behaviour (e.g. grep pipeline and the Postgres failure) is inferred from reading, not executed.
- This is a self-audit of my own scaffolding; an independent reviewer should re-verify every row.
