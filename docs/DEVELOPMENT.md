# Local Development

## Prerequisites
- Docker Desktop (for local PostgreSQL)
- Node.js 22 LTS (for `src/web`, once it exists)
- .NET SDK: **NOT yet installed on the dev machine** (decision PO-1). Either
  `winget install Microsoft.DotNet.SDK.10` (fallback .NET 8) or build/test inside a Docker SDK image,
  e.g. `docker run --rm -v "${PWD}/src/api:/src" -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet test`.
  Until then `src/api` cannot be built locally; CI skips the api job while `src/api` has no .csproj.

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

## CI
`.github/workflows/ci.yml`: web lint/test/build, api restore/build/test (skipped until src/api has a
project), gitleaks full-history secret scan, npm audit / dotnet vulnerable-package audit.
Not yet implemented (SEC-039/041, test-strategy gate): CodeQL, container image scan,
Dependabot, security regression suite, Playwright, AI evals. Branch protection must be configured
in GitHub settings by the repo owner (SEC-040).

## Running integration tests next to the dev API
`PersistenceTests.Rolling_back_every_migration_leaves_no_module_tables_behind` (and the migrate-down/up test) roll migrations back on the **shared dev database**. If the dev API container (`aistudentnotesapp-api-1`) or a Playwright run is using the same database, the rollback test can fail (seen 2026-10-30). Stop the API first (`docker stop aistudentnotesapp-api-1`), run `dotnet test`, then start it again for E2E. A separate test database per run would remove this coupling (not done).
