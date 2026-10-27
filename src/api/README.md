# College Companion API (src/api)

ASP.NET Core modular monolith. Design: `docs/architecture/backend-foundation.md`. Decisions: `docs/DECISIONS.md`.

## Build and test (Docker, no host SDK needed)

```
docker compose -f docker-compose.yml -f docker-compose.dev.yml run --rm dotnet dotnet build src/api/CollegeCompanion.slnx
docker compose -f docker-compose.yml -f docker-compose.dev.yml run --rm dotnet dotnet test  src/api/CollegeCompanion.slnx
```

Integration tests create a throwaway `cc_test_*` database on the compose Postgres per test class.

## Configuration worth knowing

| Key | Default | Purpose |
|---|---|---|
| `Security:AllowedOrigins` | empty (fail closed) | CSRF Origin allow-list for unsafe methods |
| `Security:TrustedProxies` / `Security:TrustedProxyNetworks` | empty | Only these peers may set `X-Forwarded-For/Proto`. Empty means the headers are ignored entirely. |
| `Security:ETagKey` | random per process | Base64, at least 32 bytes. Set it in any multi-instance or restart-sensitive deployment, otherwise clients get 412 after a restart. |
| `DataProtection:KeysPath` | unset (ephemeral keys, warning in non-Development) | Directory for persisted cookie-encryption keys. Mount a volume or secret; never commit keys. Keys are not encrypted at rest here. |
| `RateLimiting:ApiPermitPerMinute` | 120 | Per authenticated user, all routes |
| `RateLimiting:WritePermitPerMinute` | 30 | Per user, POST/PUT/PATCH/DELETE (additional bucket) |
| `RateLimiting:AnonymousPermitPerMinute` | 60 | Per client IP for unauthenticated requests, including `/health/*` |
| `RateLimiting:AuthPermitPerMinute` | 10 | Per client IP on login |

Sessions: the cookie holds only a session id; `identity.sessions` is authoritative (revocation, 14-day absolute expiry, soft-deleted users). The cookie is `__Host-cc.session` outside Development and `cc.session` in Development (plain http cannot carry a `Secure` cookie).

## Migrations

Applied in this order: Identity, then Courses (the Courses FK to `identity.users` needs it). Roll back in the reverse order. Down methods drop their tables and constraints but not the schemas: each schema hosts its own `__ef_migrations_history` table that EF needs after `Down` runs, so `DROP SCHEMA` fails (verified, SQLSTATE 2BP01).

Policy: `Down` is maintained until production data exists; after that, expand/contract.

### Re-scaffolding migrations (hand edits you must repeat)

`dotnet ef migrations add` output needs two edits for this repo. The tests fail if you skip them.

1. Change the generated class from `public partial class X` to `internal partial class X`. The module assemblies expose only their `*Module` class (architecture test `Module_implementations_expose_only_their_module_registration_class_publicly`). The `.Designer.cs` is already internal.
2. Remove any `xmin = table.Column<uint>(type: "xid", rowVersion: true, ...)` line (`CreateTable` or `AddColumn`). `xmin` is a PostgreSQL system column and cannot be created; `Course.Version` maps to it with `IsRowVersion()`. Test `Migrations_never_try_to_create_the_xmin_system_column` guards this.

Never regenerate `M002_Courses` from an empty Migrations folder: it would reintroduce both problems. Adding a migration against the current snapshot is safe and yields an empty `Up`/`Down` when the model has not changed.

Generate (example):

```
docker compose -f docker-compose.yml -f docker-compose.dev.yml run --rm dotnet sh -c \
  "cd src/api && dotnet tool restore && dotnet ef migrations add M00X_Name \
   --project src/Modules/Identity/CollegeCompanion.Identity --context IdentityDbContext --output-dir Persistence/Migrations"
```

Cross-schema constraints are raw SQL by design (`M004_CoursesUserFk`) and must be added to the allow-list in `backend-foundation.md` and the `pg_constraint` test.
