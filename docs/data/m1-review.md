# M1 data layer review (commit e5739f9)

Reviewer: database-architect. Scope: Identity `M001_Users`, Courses `M002_Courses`, vs `docs/data/schema-m1-m2.md`.
Legend: **[RAN]** executed by me; **[READ]** code reading only. No source files were modified.

## How it was checked
- [RAN] Throwaway DBs `review_scratch`, `review_scratch2` on the compose postgres (PG 16.15), created and dropped by me. `college_companion_dev` was not touched. There were no pre-existing test DBs. The scratch DDL was hand-written to mirror the migrations; it was not produced by EF.
- [RAN] A copy of `src/api` in the scratchpad ran `dotnet ef migrations add` in `mcr.microsoft.com/dotnet/sdk:10.0`, once against the current snapshot and once from an empty Migrations folder. The repo was not touched.
- [NOT RUN] `dotnet test` (architecture and integration suites). The host has no usable SDK. Test behaviour below is from reading.

## 1. Decision: FK courses.user_id -> identity.users(id)

**Recommend: add the FK, `ON DELETE CASCADE`, in the database only (raw SQL in a Courses migration, no EF navigation).** The architecture doc's "no cross-schema FKs" rule should be amended to a narrow, allow-listed exception.

Conflict evidence:
- Schema doc wants the FK with cascade (`schema-m1-m2.md:6`).
- `backend-foundation.md:33` says "no cross-schema FKs except `user_id` (soft reference)". The doc therefore already carves out `user_id`, but calls it soft.
- Implementation: `CoursesDbContext.cs:25` ("soft reference"). `M002_Courses.cs` has no FK.

Why FK over soft reference:
1. **Account deletion (M1-11, `schema-m1-m2.md:57`).** Soft delete (`users.deleted_at`) followed by a purge. A soft reference makes completeness of the purge depend on every module's cleanup code. One missed module leaves orphaned lecture or course data, which is a privacy defect. With the FK, `DELETE FROM identity.users` removes all student data in one transaction. I [RAN] this: with the FK added, deleting a user removed all of that user's rows (count = 0 after delete, then rolled back).
2. **Orphan prevention.** The ownership interceptor and query filter only check `UserId == current`, so they cannot detect a `user_id` pointing at nothing. The FK is the DB-level backstop. It fits the defense-in-depth list in `schema-m1-m2.md` section 6.
3. **Composite FK target for M2.** `ux_courses_id_user_id` (`CoursesDbContext.cs:36`, `M002_Courses.cs:46`) already exists. Every child's `user_id` then chains back to `users` via the cascade.
4. **Why the module boundary is not broken.** The module rule is about code and assembly coupling, and it is enforced by `ModuleBoundaryTests.cs`: the project-reference allow-list, public-surface and IgnoreQueryFilters checks. A DB-level FK adds no assembly reference. Identity still never learns that Courses exists. Cost: the Identity schema's `users(id)` becomes a published schema contract, and `users` can never be dropped, re-keyed or sharded without a coordinated change.
5. Counter-argument (soft reference preserves later extraction into separate databases) is weak for a modular monolith with one database (`backend-foundation.md:33`). Revisit only if a module is split out.

**How, without breaking the architecture tests** [READ for tests, RAN for SQL]:
- Add a **new** Courses migration (e.g. `M003_CoursesUserFk`). Do not edit applied `M002`. In its `Up`:
  ```
  migrationBuilder.Sql(@"ALTER TABLE courses.courses
    ADD CONSTRAINT fk_courses_user FOREIGN KEY (user_id)
    REFERENCES identity.users (id) ON DELETE CASCADE;");
  ```
  `Down`: `ALTER TABLE courses.courses DROP CONSTRAINT IF EXISTS fk_courses_user;`.
  If any dev DB has orphans, use `NOT VALID` then `VALIDATE CONSTRAINT`, or clean first.
- Do **not** model it in EF (`HasOne<User>`). That needs a Courses -> Identity reference, which fails `Module_implementations_only_reference_shared_kernel_and_contracts_from_this_solution` (`ModuleBoundaryTests.cs:38-51`). Raw SQL means the snapshot is unchanged and there is no drift. Confirmed: a re-scaffold produced an empty migration (section 4).
- `migrationBuilder.Sql` does not trip `No_raw_sql_concatenation_in_source` (it searches for `FromSqlRaw`/`ExecuteSqlRaw`, and the Migrations folder is excluded anyway, `ModuleBoundaryTests.cs:105-116`).
- Ordering: `Program.cs:95-96` migrates Identity before Courses, which is correct. Keep it. Document "Identity migrates first" as a rule, because the FK makes it load-bearing.
- Add a test that queries `pg_constraint` and allow-lists cross-schema FKs (`courses.courses.user_id -> identity.users.id`, and later the lectures ones). That replaces the unenforced prose rule.
- Amend `backend-foundation.md:33` to: "cross-schema FKs only to `identity.users(id)` and to `<owner>.<table>(id, user_id)` composite targets, declared in migration SQL only, never as EF navigations; allow-listed by test."
- Risk to check: `ApiFactory`/test-auth fixtures that create courses for fabricated user ids would start failing with FK violations. I did not run the integration suite [NOT RUN]. Seed users first.
- Hard-delete path: `users` is only soft-deleted today. `User` has `DeletedAt` but no `purge_after` (`User.cs:12`; doc `schema-m1-m2.md:14`), and no purge job exists. The cascade only helps once that job is written. The job must be the only code that hard-deletes users and needs an elevated role (the interceptor and filters do not apply).
- Recommend `ON DELETE CASCADE` for the M2 composite FK `lecture_sessions(course_id,user_id) -> courses` too. Keep the "409 if live session" check (`backend-foundation.md:86`) in the service. A RESTRICT FK would turn the `?purge=true` path (`CourseEndpoints.cs:123-131`) into an unhandled FK 500.

## 2. Paging query and indexes
- Query: `CourseEndpoints.cs:71-74` (user filter via global query filter, `archived_at IS NULL`, `id < cursor`, `ORDER BY id DESC LIMIT n+1`).
- [RAN] Scratch table with 200 users x 2,500 courses (30% archived), same indexes. Plan: `Index Scan Backward using ix_courses_user_id_id`, `Index Cond: user_id [and id < cursor]`, `Filter: archived_at IS NULL`, 26 rows, 0.06 ms / 0.30 ms. No sort node, so the index serves both order and keyset. Good.
- The archived filter is a post-index filter. Cost grows only with a user's archived rows interleaved in the first page, so it is fine for the course counts a student has. If it ever matters, add a partial index `(user_id, id DESC) WHERE archived_at IS NULL`. Not needed now.
- `ix_courses_user_id_id` (`CoursesDbContext.cs:37`) is needed. `ux_courses_id_user_id` (`:36`) has the wrong leading column for this query but is required as the composite FK target. `PK(id)` makes three indexes on a small table; acceptable.
- Keyset correctness depends on UUIDv7 ordering (`CourseEndpoints.cs:70` comment). `Guid.CompareTo` is translated to a SQL `<` on uuid, so it is evaluated by PostgreSQL bytewise, which matches UUIDv7 time order. Ids are generated in `Ids.New()` (SharedKernel `ICurrentUser.cs:16`) [READ]; I did not verify it emits v7. Use `Guid.CreateVersion7` and keep a test.
- Missing vs doc: `UNIQUE(user_id, semester_id, code)` does not exist because `semester_id` and `semesters` do not exist (see section 7).

## 3. xmin concurrency
- Mapping: `Course.Version` is a `uint` (`Course.cs:18`), `IsRowVersion()` (`CoursesDbContext.cs:34`). The snapshot has `xmin`/`xid`/`IsConcurrencyToken`/`ValueGeneratedOnAddOrUpdate` (`CoursesDbContextModelSnapshot.cs:74-78`). Correct for Npgsql.
- [RAN] `xmin` is readable (`select xmin` returned 1213) and `create table (... xmin xid)` fails with `column name "xmin" conflicts with a system column name`, so the hand edit that removed the column was required.
- Update path is right [READ]: If-Match is required (428), `OriginalValue` is set to the expected version so the UPDATE is conditional (`CourseEndpoints.cs:109`), and `DbUpdateConcurrencyException` maps to 409 (`:114`). `TryParseIfMatch` handles `*`, weak and quoted tags (`:142-149`).
- **Gap:** the archive and purge branches of `Delete` (`CourseEndpoints.cs:125-138`) call `SaveChangesAsync` with the tracked xmin and no `DbUpdateConcurrencyException` handler. A concurrent update or delete between the read and the write yields a 500. Map it to 409 or 204 (idempotent delete).
- Caveats, not defects: xmin changes on every UPDATE (including no-op updates) and is reassigned when rows are reloaded from a dump, so ETags are not stable across a restore (not tested by me); clients just get 412 and refetch. 32-bit wraparound only risks a false match after ~4 billion transactions between read and write. For M2+ tables that need a durable version across restore (summary revisions, note sections), use an explicit integer `row_version` as the doc's `row_version` implies. The doc (`schema-m1-m2.md:6`) names an "xmin-based `row_version`" column; the code uses a property named `Version` with no column, so the docs and code differ in naming.

## 4. Hand-edited migrations and re-scaffolding
- M002 edits: `internal partial class` (`M002_Courses.cs:9`) and no `xmin` column (`:32` note). M001 is `internal` too (`M001_Users.cs:9`). Designers use `partial class` with no modifier, so they are internal. Both edits are purposeful: they satisfy `Module_implementations_expose_only_their_module_registration_class_publicly` (`ModuleBoundaryTests.cs:64-68`) and PostgreSQL.
- [RAN] Re-scaffolding against the **current snapshot is safe**: `dotnet ef migrations add Probe` generated an empty `Up`/`Down` (model equals snapshot, no drift), but the class is emitted as `public partial class Probe`. That leaks a public type and would fail `AssertPublicSurface` until changed to `internal`.
- [RAN] **Re-scaffolding from scratch (deleting Migrations and running `add`) is NOT safe**: it emits `xmin = table.Column<uint>(type: "xid", rowVersion: true, ...)` (would fail on apply, error above) and `public partial class`. Do not regenerate M002 from empty. The hand-edits are undocumented, so they will be lost by anyone who does.
- Recommend: a short `docs/data/migrations.md` checklist (change to `internal`; strip `xmin` AddColumn/CreateTable column), plus a test that fails if any migration source contains `"xmin"`, and set `AssertPublicSurface` to also report the offending type. Better: a design-time `IMigrationsCodeGenerator` or a post-scaffold script so this is not manual. Also pin `ProductVersion 10.0.14` (snapshot `:21`), matching `dotnet-tools.json`.
- Naming inconsistency: M001 uses EF default index names `IX_users_email` (`M001_Users.cs:36`) and `PK_users`/`PK_courses`; M002 uses `ix_/ux_`. Pick one convention before M003 or it will be expensive to change.

## 5. Down correctness
- [READ] M001 `Down` drops `identity.users` (`M001_Users.cs:51`), M002 `Down` drops `courses.courses` (`M002_Courses.cs:55`). Neither drops its schema (`EnsureSchema` has no inverse). That leaves empty `identity`/`courses` schemas and is harmless, but not a full rollback.
- Rollback order matters once the FK exists: Courses must roll back before Identity. `PersistenceTests.cs:124-140` already does Courses -> Identity down and Identity -> Courses up [READ, NOT RUN]. With the FK in a later migration whose `Down` drops the constraint, M001's `DropTable` still succeeds, but only if Courses has been rolled back first.
- Per `schema-m1-m2.md:70` the policy is Down until production data exists, then expand/contract. Say so in the migrations doc.

## 6. Timestamp types
- All `timestamp with time zone` (`M001_Users.cs:34-35`, `M002_Courses.cs:51-53`), mapped from `DateTimeOffset`. Matches the doc.
- Npgsql 6+ rejects non-UTC offsets on write and returns UTC on read. `Now()` truncates to microseconds (`CourseEndpoints.cs:35-39`) to match PostgreSQL precision. Good.
- No DB defaults (`now()`), so `created_at`/`updated_at` rely on the app clock (`TimeProvider`). Acceptable for testability, but M003+ background writers and manual SQL must also set them. `updated_at` is set by hand on update (`CourseEndpoints.cs:114`); there is no trigger.
- `users.email` is `varchar(320)` with `CHECK (email = lower(email))` (`IdentityDbContext.cs:17`), not `citext` as in the doc (`schema-m1-m2.md:11`). Fine, and avoids the extension. Update the doc.

## 7. Drift from schema-m1-m2.md and M003+ blockers
Drift (doc is stale or code is incomplete; decide which):
- `courses`: code has `name`, `term` (free text) and no `semester_id`, no `semesters` table, no `UNIQUE(user_id, semester_id, code)`, no `transcript_retention_days` (`schema-m1-m2.md:16`, `:56`). Code has extra `color`.
- `users`: no `quiet_mode`, no `purge_after` (`User.cs`). Migration names differ from the doc plan (`schema-m1-m2.md:63-70`: `M001_Identity_Courses`, `M002_Consent_Sessions`).
- Query filter and interceptor are on Courses only; Identity has no user filter (correct for `users`).

Blockers or decisions for M003 (consent and lecture sessions):
1. **Where `consent_records` lives.** `lecture_sessions.consent_id` is a composite FK `(consent_id, user_id)` (`schema-m1-m2.md:18-20`). Consent could be its own module or part of Lectures. It needs its own schema and a `UNIQUE(id, user_id)`. It must be settled before M003; the module list in `backend-foundation.md:33` has `lectures` but no `consent`. Escalate to Lead Architect.
2. **Cross-module composite FK course -> session** needs the same allow-listed raw-SQL FK approach; the Lectures module cannot reference Courses (only `Courses.Contracts`).
3. **`CHECK (audio_retained = false)`, one-active-session partial UNIQUE** (`WHERE state IN ('recording','paused')`) need `HasFilter` / raw SQL and a test; EF does not scaffold `CREATE INDEX CONCURRENTLY` (`suppressTransaction`).
4. **Query-filter + interceptor duplication.** `CoursesDbContext` hand-wires the filter (`:15`, `:38`). Each new module will copy it. Provide a SharedKernel helper (convention applying the filter to all `IUserOwned`) and an architecture test asserting every `IUserOwned` entity has a filter.
5. **Migration hygiene (section 4)** will recur on every module (internal + xmin). Automate before adding a third context.
6. **Architecture test constants are hand-maintained** (`ModuleBoundaryTests.cs:15, 38-47`). Add each new module to them in the same change.
7. **RLS (doc section 6.4)** needs a per-connection `set_config('app.user_id', ...)` and a non-owner app role. Compose connects as `cc_dev` (owner-like). Decide before M003 tables stabilize or RLS will be retrofitted across modules.
8. Retention and cascade (`schema-m1-m2.md:56`): with `ON DELETE CASCADE` the retention purge must be written as explicit deletes by segment age. A user-triggered course purge relies on the FK cascade and must be tested for row counts across all child tables.

## 8. Validation scenarios to add (not yet written)
- Account purge: hard-delete a user with courses (and later sessions and segments) and assert zero rows remain in every schema.
- FK allow-list test over `pg_constraint`.
- Concurrent update-vs-delete on a course returns 409/204, not 500.
- Re-scaffold probe: `migrations add` yields an empty migration (guards snapshot drift), then `remove`.
- Two-user isolation per endpoint (the existing `CourseTests` and `AuthBoundaryTests` presumably cover Courses; not run).
