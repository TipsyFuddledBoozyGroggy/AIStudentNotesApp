# Security findings register

Prepared 2026-10-29 by security-engineer under `docs/security/severity-policy.md` (PO-12). Working tree at HEAD `cd3a49a`; local `origin/main` also points at `cd3a49a` (I did not fetch, so I cannot confirm the remote). Nothing in `src/` or `tests/` was changed to produce this file. This register does not clear any gate in `docs/RELEASE_GATES.md`.

How to read it:
- Severity is exactly one of CRITICAL / HIGH / MEDIUM / LOW. Reviews that used other wording were re-rated (stated in each group's note). Where unsure I took the higher level.
- Status: **OPEN**, **FIXED-VERIFIED** (I saw a test, a re-review result or the code), **FIXED-CONTRACT-ONLY** (added 2026-10-29: an independent re-review verified the fix in the contract text, schemas, policy file and contract tests, but the feature is unbuilt, so there is no runtime evidence; implementation tests are still required), **FIXED-UNVERIFIED** (a fix is claimed but I saw no evidence, or the claimed fix has not been re-reviewed), **DEFERRED-WITH-REASON**, **SUPERSEDED**.
- A finding is *resolved* only when it is FIXED-VERIFIED or SUPERSEDED. OPEN, FIXED-CONTRACT-ONLY, FIXED-UNVERIFIED and DEFERRED-WITH-REASON all still block their gate (a DEFERRED row stops blocking only if the Product Owner records a written downgrade; LOW rows block nothing).
- Gate: PUSH for CRITICAL/HIGH, PRODUCTION for MEDIUM, NONE for LOW.
- Evidence tags: **RAN 2026-10-29** means the test is part of the full .NET run I executed that day (see 'What I ran'). **READ** means I read the code or document. A named review means I am relying on that reviewer's recorded result.
- Paths in the Evidence column that start with `tests/` are under `src/api/`; paths starting `src/` or `docs/` are from the repository root.
- IDs: review-local IDs clash across reviews (B1-nn, N-nn), so the register prefixes them (M1, M1R, M2C, M2B1, M2B2, M2B3, M2B3R, P2C, P2R, P2D, P2B1, CV, CVR, GR, NEW). CVR is the calendar.v1 re-review (`docs/security/calendar-v1-contract-rereview.md`), whose new findings N-1..N-8 are CVR-N01..CVR-N08.
- Updated 2026-10-29 from the calendar.v1 re-review: CV-01 and CV-02 are FIXED-VERIFIED at contract level; CV-03..CV-24 are FIXED-CONTRACT-ONLY (or DEFERRED-WITH-REASON for CV-21); CVR-N01..N08 added as LOW. No other row was touched. GR-1 stays OPEN HIGH: only the Product Owner can downgrade it, and no ruling exists.

- Updated 2026-10-29 (batch 1 verification, commit 096ba8e): M1R-N01, M1R-N02, P2B1-02, P2B1-10, P2B1-11, P2B1-14 are FIXED-VERIFIED (evidence in `docs/security/batch1-verification.md`). New rows B1V-01 (MEDIUM) and B1V-02..B1V-08 (LOW) were added. Expected greps and counts below are updated.

# Part I. Gate summary

## 1. Open CRITICAL

**None.** No review recorded a Critical finding, and I found none while confirming evidence (including the A004 migration, the renamed route, LanguageGuard and the web redesign).

## 2. Open HIGH (blocks git push): 1

The policy says no push, and no offer to push, while any HIGH is open. This is the only HIGH row that is not resolved:

- **GR-1** (google-tokens GR-1, status OPEN): Transcript/study-guide copied into an institution-managed Google account becomes visible to the institution.
  - Why it is still open: PO-10j allows Workspace accounts with a warning and PO-10l makes transcript upload per-session opt-in, but legal review (G1) is open and the rating was 'High until PO/legal decide'. The Product Owner has not ruled. No code implements any Docs/Drive write. Only the Product Owner can downgrade (severity-policy.md).

Cleared since the last version of this register:
- **CV-01** and **CV-02** are now FIXED-VERIFIED at contract level (`docs/security/calendar-v1-contract-rereview.md`, 2026-10-29). CV-01: contract sections 15, 9.3, 12, CAL-26, `calendar-policy.json` (`hardDeleteOn`, `contentFreeEventRecord`); tests `Policy_retention_follows_PO_10h_and_the_content_free_record_holds_only_event_id_and_generation_CAL_26` and `Disconnect_remove_events_needs_count_and_digest_and_keep_has_neither_and_detached_no_longer_exists`. CV-02: sections 5.4 and 9.1, CAL-17, policy `disconnecting` and `accountDeletion.blocksOnGoogle=false` (asserted in `CalendarContractTests`). Contract suite: 830 passed, 0 failed (RAN 2026-10-29). This is contract-level evidence only; the behaviour is tests still to write (fake Google: `invalid_grant` during disconnect reaches `not_connected`, account deletion completes with Google failing; extended syllabus-delete scan over the new tables with a 400-day time-shift purge). Those tests are listed in the production recipe (section 6, step 3) and must exist before any Google feature ships.

What this means for the current working tree:
- `docs/API_CONTRACTS_CALENDAR.md` and `src/shared/contracts/calendar.v1/` plus `CalendarContractTests.cs` may still be untracked; leaving them out or adding them changes nothing about the register, because no calendar HIGH remains.
- **To clear GR-1:** either legal/Product Owner evidence that the institution-visibility risk is accepted, or a written downgrade by the Product Owner (PO-10j and PO-10l are mitigations, not a downgrade). No Google write code exists in the tree.
- The uncommitted **Quick recap rename, redesign and language-guide changes** introduced no new CRITICAL or HIGH finding (details in section 7 and Observations RN-01..RN-06). They do not add to the push blockers.
- Observation **RN-07** stays: the Product Owner's personal dev Google address is in committed history (PO-9k, vendor report); mention it to the PO before any push to a public remote.

Everything else rated HIGH in any review is FIXED-VERIFIED (M1-F01, F02, F03; M2C-01, 02, 06, 07, 13, 14, 21, 23, 25; P2C-01, 02, 05, 06, 08, 16, 20, 24; CV-01, CV-02).

## 3. Open MEDIUM (blocks production): 27

| Area | Open MEDIUM | Count |
|---|---|---|
| Authentication, sessions, cookies | M1-F07, M1-F08b, M2C-08, NEW-02 | 4 |
| Edge: proxy, headers, CSRF, rate limits | M1-F04 | 1 |
| Logging | B1V-01 | 1 |
| Web app | M2C-16, M2C-29 | 2 |
| Syllabus | P2C-23 | 1 |
| calendar.v1 contract (unbuilt; FIXED-CONTRACT-ONLY) | CV-03, CV-04, CV-05, CV-06, CV-07, CV-08, CV-09, CV-10, CV-11, CV-12, CV-13 | 11 |
| Google tokens and residency (unbuilt) | GR-2, GR-3, GR-5, GR-6, GR-7 | 5 |
| Governance, deletion, scanning | NEW-01, NEW-03 | 2 |
| **Total** | | **27** |

**Authentication, sessions, cookies (4)**

- M1-F07 [OPEN]: __Host- cookie done; DataProtection keys not encrypted at rest, no compose volume, Production only warns when KeysPath is unset.
- M1-F08b [OPEN]: No audit events for login, logout, session revocation or ownership violations (SEC-028).
- M2C-08 [OPEN]: Session cookie SameSite: contract text says Strict, code is Lax; rationale (OAuth return) is documented only in the uncommitted API_CONTRACTS_CALENDAR.md.
- NEW-02 [DEFERRED-WITH-REASON]: SEC-005 lockout/back-off not built (vacuous until Google sign-in exists).

**Edge: proxy, headers, CSRF, rate limits (1)**

- M1-F04 [OPEN]: Origin-only CSRF has no second layer (Sec-Fetch-Site or custom header). The WebSocket/hub Origin gap is closed.

**Logging (1)**

- B1V-01 [OPEN]: Provider-scoped logging config (Logging:Console:LogLevel:Default and similar) overrides the code filters, so the categories that carry student text can be switched back on. Reproduced in a scratch copy: 9 sentinel-bearing records.

**Web app (2)**

- M2C-16 [FIXED-UNVERIFIED]: Recording indicator must reflect server state AND the local mic track.
- M2C-29 [OPEN]: Web app must send CSP connect-src and Permissions-Policy (microphone=(self)); test that no provider key is in the bundle.

**Syllabus (1)**

- P2C-23 [FIXED-UNVERIFIED]: Syllabus load could starve live-lecture AI budget (per-pool floor).

**calendar.v1 contract (unbuilt) (11)**

- CV-03 [FIXED-CONTRACT-ONLY]: Drift check: events.update replaces whole resource, TOCTOU, fingerprint false positives.
- CV-04 [FIXED-CONTRACT-ONLY]: account_mismatch revoke could destroy another user's grant on a shared computer.
- CV-05 [FIXED-CONTRACT-ONLY]: Token deletion wired to broad error signals; one ops mistake deletes all tokens.
- CV-06 [FIXED-CONTRACT-ONLY]: No per-user daily Google budget; shared-quota DoS.
- CV-07 [FIXED-CONTRACT-ONLY]: Eligibility re-checked only at plan time; event could be written for a withdrawn item.
- CV-08 [FIXED-CONTRACT-ONLY]: Removals/reminder-updates/disconnect bound only to a count.
- CV-09 [FIXED-CONTRACT-ONLY]: Generation counter deleted with the entry row, so ids could be reused.
- CV-10 [FIXED-CONTRACT-ONLY]: include_granted_scopes=true makes scope_unexpected fire; token handling on mismatch undefined.
- CV-11 [FIXED-CONTRACT-ONLY]: No noticeVersion / server confirm record for Google writes.
- CV-12 [FIXED-CONTRACT-ONLY]: previewDigest construction undefined.
- CV-13 [FIXED-CONTRACT-ONLY]: Per-IP sign-in limits lock out a lecture hall behind one NAT.

**Google tokens and residency (unbuilt) (5)**

- GR-2 [FIXED-UNVERIFIED]: Deleting in Tether does not remove Google copies; users may assume it does.
- GR-3 [DEFERRED-WITH-REASON]: Token compromise on a compromised host reaches only Tether-created files/calendar.
- GR-5 [DEFERRED-WITH-REASON]: drive.file capability assumptions wrong, forcing a broader scope.
- GR-6 [DEFERRED-WITH-REASON]: Student shares a Doc containing third-party voices or weakness notes.
- GR-7 [DEFERRED-WITH-REASON]: Unverified OAuth app: test users only, 7-day refresh expiry; unusable for real students.

**Governance, deletion, scanning (2)**

- NEW-01 [DEFERRED-WITH-REASON]: No account/user deletion path exists and evidence-table erasure is undecided (purge_evidence has no production caller).
- NEW-03 [OPEN]: No dependency, secret, container or CodeQL scan evidence; CI workflow has never run on GitHub.

Note: M1R-N01, M1R-N02 and P2B1-02 left this list on 2026-10-29 (FIXED-VERIFIED, batch 1). Note: the 11 calendar.v1 MEDIUM rows were re-reviewed on 2026-10-29 and are FIXED-CONTRACT-ONLY: the contract defect is closed, but nothing is built, so they stay production blockers until their implementation tests exist and pass (contract section 16 'Required'; per-row tests are in Part II and in the re-review). Numbers in CV-06 and CV-13 are PROPOSAL pending PO/devops (PO-10q).

## 4. LOW (noted, no gate): 51 not resolved, 49 resolved

Not resolved (noted; no deadline):

- Authentication, sessions, cookies: M1R-N06 [OPEN] Expired/revoked identity.sessions rows are never deleted
- Edge: proxy, headers, CSRF, rate limits: M1-F09 [OPEN] Dev-login gating is sound; residual: no CI check that the dev compose override is never deployed; M1R-N05 [DEFERRED-WITH-REASON] Authentication runs before the limiter, so revoked-cookie floods cost a DB read each
- Web app: M2B3R-N06 [FIXED-UNVERIFIED] Hub catchup.ready/summary.revision skipped the strict parsers; P2D-R3-07 [DEFERRED-WITH-REASON] Schema patterns only reliable in UTF-16 regex engines
- Lectures / realtime: M2C-11 [FIXED-UNVERIFIED] In-memory partials must be per (user, session) and cleared on stop/pause/delete; M2C-19 [FIXED-UNVERIFIED] Server should derive timecodes; captureOffsetMs advisory; M2C-22 [DEFERRED-WITH-REASON] Export status/ownership semantics for the mock exporter; M2C-27 [FIXED-UNVERIFIED] Replay cap amplification; M2B1-07 [OPEN] Consent text immutability by convention only (no text hash stored); M2B2-10 [OPEN] Transcript source tables mutable by the app role (no append-only triggers); M2B2-11 [OPEN] Vendor-robustness: unbounded _events channel, _rateStrikes never decays, provider write failure leaves socket open, no unique Final (session_id, segment_no); M2B2-12 [OPEN] Lifecycle job: one throwing row stops the batch every tick
- AI gateway: M2B3-05 [OPEN] Rate/duplicate controls: per-user limit, 200-per-session permanent cap, key binding on coalesced answer still unfixed
- Syllabus: P2C-29 [DEFERRED-WITH-REASON] Server-side confirm record for real writes; P2R-R2-09 [DEFERRED-WITH-REASON] Plain false-but-clean date still batch-approvable; P2D-R3-02 [FIXED-UNVERIFIED] Lone surrogate / invalid UTF-8 in JSON must be 400, not 500; P2D-R3-09 [DEFERRED-WITH-REASON] VS15/VS16 invisible channel after non-ASCII base; P2B1-08 [OPEN] Retention purge keeps snippet offsets (contract says remove); P2B1-09 [OPEN] Contract's custom CSRF header does not exist; P2B1-12 [OPEN] Ten slow uploaders can starve the 100 MB upload gate; P2B1-13 [OPEN] Lease fencing done; no heartbeat during long extraction; fault injection keyed on environment name 'Test', P2B1-15 [OPEN] Audit immutability bypassable by the app role (switch via set_config; app owns table)
- Batch 1 (added 2026-10-29): B1V-02 [OPEN] log canary coverage gaps; B1V-03 [OPEN] explicit key overrides skip the strength checks; B1V-04 [OPEN] root-secret check is a denylist (sequential or repeating keys, and the public test key, are accepted); B1V-05 [OPEN] no rotation story and no operator docs for RootSecret/AllowedHosts/ICU/tzdata; B1V-06 [OPEN] Development fallback secret file edge cases; B1V-07 [OPEN] DocumentText.Canonicalize throws on a lone surrogate under ICU (latent); B1V-08 [OPEN] edge validators are lenient and HSTS includeSubDomains is unconditional
- calendar.v1 contract (unbuilt): CV-14, CV-15, CV-16, CV-17, CV-18, CV-19, CV-20, CV-22, CV-23, CV-24 [FIXED-CONTRACT-ONLY] (closed in the contract by the re-review; implementation tests pending); CV-21 [DEFERRED-WITH-REASON] Identity rules (find-or-create by iss+sub, session rotation) are CAL-23 tests for the module; CVR-N01 [OPEN] content-free record pseudonymous and generation protection lapses after disconnect/400 days; CVR-N02 [OPEN] resume take_back write path unspecified; CVR-N03 [OPEN] action previews do not state 'no Google call', eventsAction not in digest binds; CVR-N04 [OPEN] login_hint carries the sub in the URL; CVR-N05 [OPEN] connection state undefined while the circuit breaker is open; CVR-N06 [OPEN] calendar-policy.json can drift from production behaviour; CVR-N07 [OPEN] account deletion does not state its eventsAction; CVR-N08 [OPEN] Syllabus-side 409 is a requirement on frozen syllabus.v1
- Google tokens and residency (unbuilt): GR-4 [DEFERRED-WITH-REASON] KEK loss forces mass reconnect
- Governance, deletion, scanning: M2B1-09 [DEFERRED-WITH-REASON] Retention and erasure of consent/audit evidence undecided

## 5. Counts

By severity and status (all 211 findings; Observations are not counted). FIXED-CONTRACT-ONLY counts as unresolved: it is verified in the contract, not at runtime.

| Severity | FIXED-VERIFIED | FIXED-CONTRACT-ONLY | FIXED-UNVERIFIED | OPEN | DEFERRED-WITH-REASON | SUPERSEDED | Total | Unresolved |
|---|---|---|---|---|---|---|---|---|
| CRITICAL | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| HIGH | 22 | 0 | 0 | 1 | 0 | 0 | 23 | 1 |
| MEDIUM | 58 | 11 | 3 | 7 | 6 | 3 | 88 | 27 |
| LOW | 48 | 10 | 5 | 27 | 9 | 1 | 100 | 51 |
| **Total** | **128** | **21** | **8** | **35** | **15** | **4** | **211** | **79** |

Observations (Info, not findings): 41 entries listed at the end.

## 6. Check recipes for the Lead

### Before-push check (no open CRITICAL or HIGH)

Run from `/Users/jamie/Dev/AIStudentNotesApp`. Every step must pass; then say the result plainly.

1. See what would be pushed: `git status --short` and `git log origin/main..HEAD --oneline`. Untracked files are not pushed until added; the policy still applies to anything you add.
2. No unresolved CRITICAL or HIGH in the register tables (this must print nothing):
   ```
   grep -E '^\| [^|]+ \| [^|]+ \| (CRITICAL|HIGH) \| (OPEN|FIXED-CONTRACT-ONLY|FIXED-UNVERIFIED|DEFERRED-WITH-REASON) \|' docs/security/findings-register.md
   ```
   Today it prints exactly 1 row (GR-1, HIGH, OPEN). The Product Owner ruled on 2026-10-29 (PO-12a) to keep GR-1 HIGH until qualified counsel has reviewed it, so the answer is: **do not push** until that legal evidence exists.
3. No review newer than the register has been left out of it. This must print nothing, or each file it lists must be reflected in the register first:
   ```
   find docs/security -name '*.md' -newer docs/security/findings-register.md ! -name findings-register.md
   ```
4. Any review written after this register uses the same four levels; add its rows to Part II before the check in step 2 is trusted.
5. Backend and contracts still green (5 projects, one command; Docker must be running):
   ```
   docker compose -f docker-compose.yml -f docker-compose.dev.yml run --rm dotnet dotnet test src/api/Tether.slnx
   ```
   Web (`npm test`, typecheck, lint in `src/web`) is a separate step I did not run.
6. Nothing secret in the tree being pushed (hits in `*.cs` test fixtures such as `sk-ant-test` are expected; anything else is a finding):
   ```
   git grep -nI -E 'sk-ant-[A-Za-z0-9]|AKIA[0-9A-Z]{12}|BEGIN (RSA|EC|OPENSSH|PRIVATE) KEY|AIza[0-9A-Za-z_-]{30}|client_secret|refresh_token *[:=]' -- . ':!docs' ':!*.md'; git ls-files --others --exclude-standard | grep -i -E '\.env|\.pem|\.key|secret'
   ```
7. Record the result in the next progress note: 'Register checked at <HEAD>: open CRITICAL n, open HIGH n, push allowed / not allowed'.

### Before-production check (no open CRITICAL, HIGH or MEDIUM)

1. Repeat the push check, then run the same grep with MEDIUM added; it must print nothing:
   ```
   grep -E '^\| [^|]+ \| [^|]+ \| (CRITICAL|HIGH|MEDIUM) \| (OPEN|FIXED-CONTRACT-ONLY|FIXED-UNVERIFIED|DEFERRED-WITH-REASON) \|' docs/security/findings-register.md
   ```
   Today it prints 28 rows (1 HIGH + 27 MEDIUM, of which 11 are calendar.v1 rows FIXED-CONTRACT-ONLY). Expected MEDIUM-only count: `grep -cE '^\| [^|]+ \| [^|]+ \| MEDIUM \| (OPEN|FIXED-CONTRACT-ONLY|FIXED-UNVERIFIED|DEFERRED-WITH-REASON) \|' docs/security/findings-register.md` prints 27.
2. For every FIXED-VERIFIED row you rely on, re-open the evidence (test name still exists and passes in CI, not only locally). `grep -c 'RAN 2026-10-29' docs/security/findings-register.md` shows how many rest on my local run; none rests on a GitHub CI run because CI has never run (NEW-03).
3. Controls that exist only as contract text or design and have no implementation yet. They are not open rows today because the code does not exist, but each becomes a production blocker the moment its feature ships. Confirm none of these features is in the release, or implement and test them first:
   - Syllabus parser sandbox, ingest worker, tmpfs volume, hostile-file fixtures (P2C-01, P2C-02, P2D-R3-03..06; contract 5.1).
   - Real STT vendor controls: daily STT-minutes budget, global kill switch, vendor retention terms (M2C-25, G2).
   - Real Claude adapter: PII redaction decision (M2B3-12 / B3-12), disclosure that an in-flight request cannot be recalled, regex/prompt Cf-character handling (M2B3R-N07), per-user quick-recap limit (M2B3-05), shared-state backplane (single-instance only, ADR-020d).
   - Google: token cipher and KEK custody, OAuth client and verification, Calendar and Docs adapters, quotas (GR-1..GR-7). calendar.v1 is closed only at contract level (CV-01..CV-24, re-review 2026-10-29): before the Calendar module ships, its section 16 'Required' tests must exist and pass, in particular CV-01/CV-02 (extended delete scan, disconnect exits, account deletion with Google failing), CV-03 (fake-Google drift, `If-Match`, normalisation), CV-04 (two-user mismatch), CV-05 (mass `invalid_client` deletes nothing; table-driven match to `calendar-policy.json`, CVR-N06), CV-06/CV-13 (load tests; PO/devops approve the PROPOSAL numbers), CV-07 (withdraw during run), CV-08 (plan-changed zero writes per kind), CV-10/CV-11, CV-12 (cross-user digest replay), the log canary (CV-15), and Google behaviour tests T1-T12 (NOT RUN).
   - Deployment prerequisites added by batch 1 (no Dockerfile or deployment definition exists yet; none of this is enforced by an artifact): `Security__RootSecret` (base64, 32+ bytes, from the secret store; startup refuses it otherwise), `AllowedHosts` (public host names, not `*`), a runtime image with ICU (libicu) and tzdata, `Security__TrustedProxies`/`TrustedProxyNetworks` only for the real proxy. Wrong values stop startup (tested), but B1V-01 and B1V-05 must be closed first.
   - Account deletion and evidence retention (NEW-01), sign-in lockout (NEW-02), multi-instance revocation.
4. Scanning and operations (gate G5): CodeQL, dependency, secret and container scan results from a real CI run; a deployed telemetry sample; a backup restore drill including a KEK restore (NEW-03).
5. Human gates that this policy does not touch: G1 recording consent and campus policy, G2 vendor terms and cost, G3 real-device/screen-reader QA, G4 ADHD suitability validation, G5. All remain as recorded in `docs/RELEASE_GATES.md`.

## 7. New findings from the rename, redesign and language-guide work

Examined: migration `A004_RenameLostTrackToQuickRecap`, the renamed route and its limits, `LanguageGuard` and its log output, `public/prefs-init.js`, the new web shell, command palette and Quick recap panel.

**No new CRITICAL, HIGH or MEDIUM finding.** What I checked and what I saw:
- **A004 migration.** It drops the CHECK, relabels `ai.ai_usage.workflow` and three `prompt_version` columns, and re-adds the CHECK. The ledger amounts and reservation rows are untouched. There are no triggers on the `ai` tables, so the UPDATE is legal. `AiScopeMigrationTests` exercises Up, Down and Up again against legacy rows and passed in my run. Side effect recorded as RN-01 (stored provenance labels are rewritten; accepted by PO-11a).
- **Renamed route.** `POST /api/v1/sessions/{id}/quick-recap` is registered in the same group as before (`AiEndpoints.cs:24`). Authentication, the Origin check and the global limiters are applied by the host to every `/api/v1` route; the per-session recap limiter and Idempotency-Key logic live in `CatchUpService`, not under a route name. No exemption or bucket in `Tether.Host/Program.cs` or `SeparateRateBucket` mentions the old or new name (grep). The web client calls the new path (`lib/api/client.ts:131`); no alias to the old path remains, so the old URL is a 404. `AiRouteTests` (RAN, pass) cover 401, cross-user 404, Origin, content type, idempotency and rate limit on the new route (RN-02).
- **LanguageGuard log canary.** Logs carry rule ids and counts only; the canary test passes (RN-03). Gaps are Info only: it uses a fake logger, default mode is log-only, and quoted transcript text exempts wording (RN-03..RN-05).
- **Web redesign.** No `dangerouslySetInnerHTML`, `innerHTML` or `eval` in non-test web source (grep); `prefs-init.js` sets only whitelisted attributes; localStorage holds preferences and the Quick recap window, not transcript text. The web app still sends no CSP or Permissions-Policy, which is the existing open MEDIUM M2C-29, now with one more same-origin script to allow (RN-06).
- Open items touching the uncommitted code were already in the register (M2C-29, M2B3-05).

## 8. What I ran and what I did not verify

**Ran (2026-10-29, Docker, working tree as found):** `docker compose -f docker-compose.yml -f docker-compose.dev.yml run --rm dotnet dotnet test src/api/Tether.slnx` finished with exit 0: ContractTests 830, AiTests 241, SyllabusTests 95, IntegrationTests 478, ArchitectureTests 19; 0 failed, 0 skipped. This also shows the uncommitted rename, A004 and LanguageGuard code builds and its tests pass. I made no source or test change; the build writes `bin/obj` output, and the test factories create their own `cc_test_*` databases on the dev Postgres (I did not check whether they are dropped afterwards).

**Not verified:**
- Web unit tests, lint, typecheck and Playwright (not run). So M2C-16, M2B3R-N06 and the web parts of M2C-28 rest on reviews and my greps only.
- Any real browser, screen reader, real audio, real STT vendor, real Claude call or real Google call (none exist or are authorized).
- GitHub CI (has never run), CodeQL, dependency, container and secret scanners, deployed telemetry, backup restore.
- Whether the remote `origin/main` really equals `cd3a49a` (no fetch performed).
- Statements made only by earlier reviews that I did not re-derive: for example m1-rereview live probes, m2-b2 socket probes, the sanitizer harness, timing and concurrency behaviour. I relied on the named review, and on the passing regression tests where they exist. Several PROGRESS entries say fixes were 'not shown red first' (M2B3R-N02, M2B3R-N03); I confirmed the tests pass, not that they would fail on the old code.
- Statements about Google, Postgres vacuum/logging, Kestrel and Docker log drivers made in the reviews (all marked UNVERIFIED there).
- The revised `API_CONTRACTS_CALENDAR.md`: re-reviewed on 2026-10-29 (`docs/security/calendar-v1-contract-rereview.md`, 830 contract tests passed). That review is contract-level only; Google behaviour (full-resource `events.update`, `If-Match`, `login_hint` with a sub, `hd` at sign-in, admin-block code, deleted-id reuse, revoke scope) is UNVERIFIED and T1-T12 are NOT RUN.
- Contract-vs-data-design observations P2D-N-04..N-17 and P2R-D-01..D-14 individually.
- Whether any review file under `docs/security/` was modified (several are `M` in git status) in a way that changes a rating. I read the current working-tree versions.

# Part II. The register

Columns: ID, source, severity, status, gate, summary, evidence. IDs starting NEW are added by this register.

## Milestone 1 backend (m1-review.md, m1-rereview.md)

Original reviews used the same four-level scale. F-08 was rated 'Low/Medium'; re-rated MEDIUM (the higher) and split in two (log leakage fixed, audit gap open).

| ID | Source | Sev | Status | Gate | Summary | Evidence |
|---|---|---|---|---|---|---|
| M1-F01 | m1-review F-01 | HIGH | FIXED-VERIFIED | PUSH | Logout/account deletion did not revoke sessions (stolen cookie stayed valid). | m1-rereview F-01 RAN live; ServerSideSessionTests tests/Tether.IntegrationTests/SecurityHardeningTests.cs:21-68 (RAN 2026-10-29, pass) |
| M1-F02 | m1-review F-02 | HIGH | FIXED-VERIFIED | PUSH | 14-day absolute session lifetime not enforced under sliding expiry. | AbsoluteSessionLifetimeTests tests/Tether.IntegrationTests/SecurityHardeningTests.cs:86 (RAN 2026-10-29, pass) |
| M1-F03 | m1-review F-03 | HIGH | FIXED-VERIFIED | PUSH | SEC-036 rate limiting not met (no global/per-user limiter, no forwarded-header config). | src/api/src/Tether.Host/Program.cs:61-120; tests/Tether.IntegrationTests/SecurityHardeningTests.cs:153-285 (RAN 2026-10-29, pass). m1-rereview called it 'partially fixed'; residuals are filed separately: M1R-N01, M1R-N05, NEW-02. |
| M1-F04 | m1-review F-04 | MEDIUM | OPEN | PRODUCTION | Origin-only CSRF has no second layer (Sec-Fetch-Site or custom header). The WebSocket/hub Origin gap is closed. | Closed part: m2-b2-review RAN, Program.cs:190 RealtimeOriginMiddleware. Open part: no 'X-Tether-CSRF' (name chosen in PO-10p) exists anywhere in src/api (grep). |
| M1-F05 | m1-review F-05 | MEDIUM | FIXED-VERIFIED | PRODUCTION | ETag leaked Postgres xmin. | m1-rereview F-05 (RAN EtagTests); tests/Tether.IntegrationTests/SecurityHardeningTests.cs:299,326 (RAN 2026-10-29, pass) |
| M1-F06 | m1-review F-06 | MEDIUM | FIXED-VERIFIED | PRODUCTION | Security headers incomplete and dropped on 500. | Program.cs:141-156; tests/Tether.IntegrationTests/SecurityHardeningTests.cs:352-398 (RAN 2026-10-29, pass). Residuals: M1R-N02 (HSTS/hosts), M2C-29 (web-app CSP). |
| M1-F07 | m1-review F-07 | MEDIUM | OPEN | PRODUCTION | __Host- cookie done; DataProtection keys not encrypted at rest, no compose volume, Production only warns when KeysPath is unset. | Prefix fixed: tests/Tether.IntegrationTests/SecurityHardeningTests.cs:405 (RAN 2026-10-29, pass). Residual still true: Program.cs:131-132 warns only (m1-rereview 'Before any non-dev use' #2). |
| M1-F08 | m1-review F-08 (log leakage) | MEDIUM | FIXED-VERIFIED | PRODUCTION | Exception/log leakage of row values (SEC-027). Re-rated MEDIUM. | Program.cs:29-31,174-179 (type + trace id only); tests/Tether.IntegrationTests/RealtimeLogCanaryTests.cs (RAN 2026-10-29, pass); m2-b2-review 'Logs' RAN. |
| M1-F08b | m1-review F-08 (audit part) | MEDIUM | OPEN | PRODUCTION | No audit events for login, logout, session revocation or ownership violations (SEC-028). | grep 'audit' in src/api/src/Modules/Identity returns nothing. Lectures and Syllabus have audit tables; Identity has none. |
| M1-F09 | m1-review F-09 | LOW | OPEN | NONE | Dev-login gating is sound; residual: no CI check that the dev compose override is never deployed. | Guards verified (DevLoginStartupGuardTests, m1-review RAN). CI check absent; CI has never run on GitHub (PROGRESS Iteration 9). |
| M1R-N01 | m1-rereview N-01 | MEDIUM | FIXED-VERIFIED | PRODUCTION | Forwarded-header config not validated: TrustedProxyNetworks such as 0.0.0.0/0 lets any client spoof X-Forwarded-For and dodge limits. | Program.cs:63 calls EdgeConfig.ParseTrustedProxies (EdgeConfig.cs:17-44); tests/Tether.IntegrationTests/EdgeConfigTests.cs:22,35,42,108 (RAN 2026-10-29, pass). Residual leniency (public /8 and IPv6 /32 accepted; I checked 1.0.0.0/8 and ::/32 in a scratch probe; ::/32 does not match IPv4 clients) is B1V-08. Details: batch1-verification.md |
| M1R-N02 | m1-rereview N-02 | MEDIUM | FIXED-VERIFIED | PRODUCTION | HSTS depends on Request.IsHttps (false behind a TLS terminator); AllowedHosts is '*'. | Program.cs:65,171-172, EdgeConfig.cs:47-53; appsettings.json:9 now 'localhost'; tests/Tether.IntegrationTests/EdgeConfigTests.cs:56,89,98,128 (RAN 2026-10-29, pass). Scratch probe (not in repo): Production Host evil.test gets 400, localhost 200 with HSTS. No repo test sends a wrong Host header. See B1V-08. |
| M1R-N03 | m1-rereview N-03 | LOW | FIXED-VERIFIED | NONE | Re-login did not revoke the session the browser already held. | PROGRESS Iteration 3; tests/Tether.IntegrationTests/PreM2HardeningTests.cs:52 and tests/Tether.IntegrationTests/B2FixTests.cs:79 (RAN 2026-10-29, pass) |
| M1R-N04 | m1-rereview N-04 | LOW | FIXED-VERIFIED | NONE | IPv6 limiter keyed on the full address. | ClientIpKeyTests tests/Tether.IntegrationTests/PreM2HardeningTests.cs:12; tests/Tether.IntegrationTests/B2FixTests.cs:266 (RAN 2026-10-29, pass) |
| M1R-N05 | m1-rereview N-05 | LOW | DEFERRED-WITH-REASON | NONE | Authentication runs before the limiter, so revoked-cookie floods cost a DB read each. | Program.cs:194-195 unchanged. Reviewer: accept for M1; needs proxy/WAF limits at deployment (devops). |
| M1R-N06 | m1-rereview N-06 | LOW | OPEN | NONE | Expired/revoked identity.sessions rows are never deleted. | No identity sweeper exists (hosted services found: Transcript hub reaper, Syllabus jobs, Ai x3, Lectures x2). |

## Milestone 2 contract review (m2-contract-review.md, C-01..C-31)

Design review of API_CONTRACTS.md; code evidence comes from the B1/B2/B3 reviews and the 2026-10-29 test run. C-32 is Info (Observations).

| ID | Source | Sev | Status | Gate | Summary | Evidence |
|---|---|---|---|---|---|---|
| M2C-01 | m2-contract C-01 | HIGH | FIXED-VERIFIED | PUSH | Audio ticket rules: single use, atomic consume, invalidation on pause/stop/logout. | m2-b2-review RAN (replay 4401, concurrent double-consume one winner, post-pause 4401); tests/Tether.IntegrationTests/LectureTicketAndIsolationTests.cs (RAN 2026-10-29, pass) |
| M2C-02 | m2-contract C-02 | HIGH | FIXED-VERIFIED | PUSH | Audio socket must require cookie AND ticket, same user. | m2-b2-review RAN (Bob + Alice's ticket gets 4401, ticket not burned); AudioTicketService.cs:131-136 READ. |
| M2C-03 | m2-contract C-03 | MEDIUM | FIXED-VERIFIED | PRODUCTION | Origin must be checked before upgrade, exact match. | m2-b2-review RAN (403 before upgrade for null/other/suffix/uppercase Origin); Program.cs:189-190. |
| M2C-04 | m2-contract C-04 | MEDIUM | FIXED-VERIFIED | PRODUCTION | No pre-auth limits on the audio socket (hello timeout, frame sizes). | m2-b2-review RAN (1008/1009 closes); tests/Tether.IntegrationTests/RealtimeAudioTests.cs (RAN 2026-10-29, pass) |
| M2C-05 | m2-contract C-05 | MEDIUM | FIXED-VERIFIED | PRODUCTION | Ticket issuance and failed-hello not rate limited. | m2-b1-review RAN P5 (6 x 200 then 429); m2-b2-review RAN (11th upgrade 429). |
| M2C-06 | m2-contract C-06 | HIGH | FIXED-VERIFIED | PUSH | Hub/socket outlive logout, revocation, delete. | m2-b2-review B2-03 then fixed: tests/Tether.IntegrationTests/B2FixTests.cs:56,79,139,162 (RAN 2026-10-29, pass); HubReaper 60 s revalidation. |
| M2C-07 | m2-contract C-07 | HIGH | FIXED-VERIFIED | PUSH | CSRF/CSWSH layers for hub not specified (SSE/long-polling fallbacks, per-call authorization). | m2-b2-review RAN (cross-origin negotiate/upgrade 403; long polling/SSE 404; WebSockets only). |
| M2C-08 | m2-contract C-08 | MEDIUM | OPEN | PRODUCTION | Session cookie SameSite: contract text says Strict, code is Lax; rationale (OAuth return) is documented only in the uncommitted API_CONTRACTS_CALENDAR.md. | docs/API_CONTRACTS.md:110 'SameSite=Strict'; IdentityModule.cs:64 Lax; pinned by tests/Tether.IntegrationTests/CsrfAndConfigTests.cs:108. Fix is documentation/ADR, not code. |
| M2C-09 | m2-contract C-09 | MEDIUM | FIXED-VERIFIED | PRODUCTION | Replay/transcript reads must be keyed by (user, session), not seq alone. | m2-b2-review RAN (Bob vs Alice transcript byte-identical 404; foreign Join = missing Join). |
| M2C-10 | m2-contract C-10 | LOW | FIXED-VERIFIED | NONE | Gap-free seq needs a counter row, not a DB sequence. | m2-b2-review READ SessionPortsService.cs:45-62; ADR-020. |
| M2C-11 | m2-contract C-11 | LOW | FIXED-UNVERIFIED | NONE | In-memory partials must be per (user, session) and cleared on stop/pause/delete. | PartialStore exists; no review or test cited that proves clearing. Not verified by me. |
| M2C-12 | m2-contract C-12 | MEDIUM | FIXED-VERIFIED | PRODUCTION | Model citations must be validated against the same session. | m2-b3-review 'Citations are server-resolved' READ; Tether.AiTests validators (RAN 2026-10-29, pass) |
| M2C-13 | m2-contract C-13 | HIGH | FIXED-VERIFIED | PUSH | Consent not server-authoritative (textVersion, per-course attestation, consent events, no real STT while draft). | m2-b1-review RAN P1 (unregistered version 403) and events in same transaction; LecturesDbContext.cs:61 ck_consent_records_attested; m2-b2-review startup throws for non-replay provider. Consent text is DRAFT; gate G1 unaffected. |
| M2C-14 | m2-contract C-14 | HIGH | FIXED-VERIFIED | PUSH | Pause/stop in-flight audio hole (buffered frames still transcribed). | m2-b2-review RAN (REST pause closed socket 4409 in 44 ms, no finals after); B2-04 race fixed tests/Tether.IntegrationTests/B2FixTests.cs:95 (RAN 2026-10-29, pass) |
| M2C-15 | m2-contract C-15 | MEDIUM | FIXED-VERIFIED | PRODUCTION | Socket must accept only pause/stop (reduce-only). | RelayConnection.cs:220,290; tests/Tether.IntegrationTests/RealtimeAudioTests.cs:257 (RAN 2026-10-29, pass) |
| M2C-16 | m2-contract C-16 | MEDIUM | FIXED-UNVERIFIED | PRODUCTION | Recording indicator must reflect server state AND the local mic track. | Code present (web useLectureSession.ts, lib/lecture/indicator.test.ts). Web unit tests and real-device pass (G3) were NOT run by me. |
| M2C-17 | m2-contract C-17 | MEDIUM | FIXED-VERIFIED | PRODUCTION | Unattended recording limits undefined (heartbeat, auto-pause, auto-stop, hard cap). | SessionLifecycleJob.cs; tests/Tether.IntegrationTests/LectureB2JobTests.cs:31-94 (RAN 2026-10-29, pass). Numeric values still await Product Owner confirmation. |
| M2C-18 | m2-contract C-18 | LOW | FIXED-VERIFIED | NONE | One live recording per user (partial unique index); second audio socket rejected. | m2-b1-review READ (index); RelayConnection registry 4409 (m2-b2-review RAN). |
| M2C-19 | m2-contract C-19 | LOW | FIXED-UNVERIFIED | NONE | Server should derive timecodes; captureOffsetMs advisory. | RelayConnection.cs mentions C-19; no test cited. |
| M2C-20 | m2-contract C-20 | MEDIUM | FIXED-VERIFIED | PRODUCTION | speaker labels capture third-party data. | API_CONTRACTS.md:118,133: speaker never emitted in M2, on G1 legal checklist. Replay provider emits none. |
| M2C-21 | m2-contract C-21 | HIGH | FIXED-VERIFIED | PUSH | Delete must cover all derived data and in-flight work. | m2-b2-review RAN (purge during streaming: socket 4409, transcript rows 0); tests/Tether.IntegrationTests/LectureB15Tests.cs:47,67 (RAN 2026-10-29, pass) |
| M2C-22 | m2-contract C-22 | LOW | DEFERRED-WITH-REASON | NONE | Export status/ownership semantics for the mock exporter. | No exporter exists (notes route is a placeholder). Revisit when Docs export is built. |
| M2C-23 | m2-contract C-23 | HIGH | FIXED-VERIFIED | PUSH | Log canary too weak (sinks, failure paths, Trace level). | m2-b2-review 'Logs' RAN; tests/Tether.IntegrationTests/RealtimeLogCanaryTests.cs (RAN 2026-10-29, pass) |
| M2C-24 | m2-contract C-24 | MEDIUM | FIXED-VERIFIED | PRODUCTION | Hub error detail must come from a fixed vocabulary. | m2-b2-review READ/RAN; ApiProblem.cs, Wire.cs. |
| M2C-25 | m2-contract C-25 | HIGH | FIXED-VERIFIED | PUSH | Ingest limits (frame size, rate, sockets) only implied. | m2-b2-review RAN (22 acks then rate_limited then 1008). Daily STT-minutes budget and kill switch are real-vendor controls not yet built (see Before-production check). |
| M2C-26 | m2-contract C-26 | MEDIUM | FIXED-VERIFIED | PRODUCTION | Hub/REST abuse limits missing (Join, quick-recap, connections). | m2-b2-review RAN (4th hub connection aborted, 4 KB message); B2-02 fix tests/Tether.IntegrationTests/B2FixTests.cs:200,215 (RAN 2026-10-29, pass); quick-recap limiter tests/Tether.IntegrationTests/AiRoutesTests.cs (RAN 2026-10-29, pass) |
| M2C-27 | m2-contract C-27 | LOW | FIXED-UNVERIFIED | NONE | Replay cap amplification. | TranscriptEndpoints.cs references C-27; m2-b2 RAN only limit=501 -> 400. No test of Join replay cap cited. |
| M2C-28 | m2-contract C-28 | MEDIUM | FIXED-VERIFIED | PRODUCTION | AI output is untrusted plain text; render inert. | m2-b3-review/rereview READ (PlainText; no dangerouslySetInnerHTML); my grep of src/web/src non-test files: none. Sanitizer fixpoint RAN in rereview. |
| M2C-29 | m2-contract C-29 | MEDIUM | OPEN | PRODUCTION | Web app must send CSP connect-src and Permissions-Policy (microphone=(self)); test that no provider key is in the bundle. | grep of src/web (excluding node_modules/.next) finds no CSP or Permissions-Policy; next.config.mjs has no headers(). Only the API sets them (Program.cs:148-149). Also m1-review F-06 note and m2-b3-review B3-13(b). |
| M2C-30 | m2-contract C-30 | MEDIUM | FIXED-VERIFIED | PRODUCTION | Audit events for session lifecycle, delete, ticket failures. | m2-b1-review RAN P4 (audit kinds listed); tests/Tether.IntegrationTests/LectureB15Tests.cs:172,244 (RAN 2026-10-29, pass) |
| M2C-31 | m2-contract C-31 | LOW | FIXED-VERIFIED | NONE | Server-relay confirmation / direct-to-vendor needs new review. | ADR-018 (PO confirmed server relay, DECISIONS.md). |

## Lectures module B1 (m2-b1-review.md)

IDs prefixed M2B1 to avoid a clash with the Syllabus P2-B1 review, which also uses B1-nn. B1-11/B1-12 are Info.

| ID | Source | Sev | Status | Gate | Summary | Evidence |
|---|---|---|---|---|---|---|
| M2B1-01 | m2-b1-review B1-01 | MEDIUM | FIXED-VERIFIED | PRODUCTION | Course delete left recordable sessions behind. | ADR-019 FK cascade; tests/Tether.IntegrationTests/LectureB15Tests.cs:121 (resume/ticket 404 once course not owned) and :47 (RAN 2026-10-29, pass) |
| M2B1-02 | m2-b1-review B1-02 | MEDIUM | FIXED-VERIFIED | PRODUCTION | Consent/audit evidence not append-only. | M006 triggers; tests/Tether.IntegrationTests/LectureB15Tests.cs:172 (RAN 2026-10-29, pass). Runtime role is still the table owner: see P2B1-15. |
| M2B1-03 | m2-b1-review B1-03 | MEDIUM | FIXED-VERIFIED | PRODUCTION | No DB tie between child user_id and session user_id. | Composite FKs; tests/Tether.IntegrationTests/LectureB15Tests.cs:141,159 (RAN 2026-10-29, pass) |
| M2B1-04 | m2-b1-review B1-04 | MEDIUM | FIXED-VERIFIED | PRODUCTION | Idempotency fingerprint ambiguous; replay bypassed consent validation. | Length-prefixed fingerprint tests/Tether.IntegrationTests/LectureB15Tests.cs:199,212,228 (RAN 2026-10-29, pass) |
| M2B1-05 | m2-b1-review B1-05 | LOW | FIXED-VERIFIED | NONE | Audit amplification from garbage ticket consumes. | AudioTicketService.cs:62-65 coalescing; tests/Tether.IntegrationTests/LectureB15Tests.cs:244 and tests/Tether.IntegrationTests/LectureB2LimitsTests.cs:27 (RAN 2026-10-29, pass) |
| M2B1-06 | m2-b1-review B1-06 | LOW | FIXED-VERIFIED | NONE | Transition spam grows rows 3x per call. | Resume cap, never Pause/Stop (ADR-019b): tests/Tether.IntegrationTests/LectureB2LimitsTests.cs:52-123 (RAN 2026-10-29, pass) |
| M2B1-07 | m2-b1-review B1-07 | LOW | OPEN | NONE | Consent text immutability by convention only (no text hash stored). | grep for text hash/TextSha in src/api/src/Modules/Lectures: none. |
| M2B1-08 | m2-b1-review B1-08 | LOW | FIXED-VERIFIED | NONE | Resume did not re-validate the consent version. | SessionService.cs:194; tests/Tether.IntegrationTests/LectureB15Tests.cs:316 (RAN 2026-10-29, pass) |
| M2B1-09 | m2-b1-review B1-09 | LOW | DEFERRED-WITH-REASON | NONE | Retention and erasure of consent/audit evidence undecided. | Needs Product Owner + legal-compliance-reviewer; see NEW-01. Test only proves evidence survives session delete (tests/Tether.IntegrationTests/LectureB15Tests.cs:67,188). |
| M2B1-10 | m2-b1-review B1-10 | LOW | FIXED-VERIFIED | NONE | Expired tickets and idempotency rows never swept. | ExpiredRowSweeper.cs; tests/Tether.IntegrationTests/LectureB15Tests.cs:267,308 (RAN 2026-10-29, pass) |

## Realtime B2 (m2-b2-review.md)

B2-13 and B2-14 are Info (Observations).

| ID | Source | Sev | Status | Gate | Summary | Evidence |
|---|---|---|---|---|---|---|
| M2B2-01 | m2-b2-review B2-01 | MEDIUM | FIXED-VERIFIED | PRODUCTION | A student's Pause could be refused with 429. | ADR-019b; tests/Tether.IntegrationTests/LectureB2LimitsTests.cs:79,101 (RAN 2026-10-29, pass) |
| M2B2-02 | m2-b2-review B2-02 | MEDIUM | FIXED-VERIFIED | PRODUCTION | Hub Join unbounded across session ids; limiter memory leak. | LectureHub.cs:191-230 (JoinLimiter, HubInvocationLimiter); tests/Tether.IntegrationTests/B2FixTests.cs:200,215,238,252 (RAN 2026-10-29, pass) |
| M2B2-03 | m2-b2-review B2-03 | LOW | FIXED-VERIFIED | NONE | Logout/revocation polled, not pushed. | IAuthSessionRevocationObserver; tests/Tether.IntegrationTests/B2FixTests.cs:56,79 (RAN 2026-10-29, pass) |
| M2B2-04 | m2-b2-review B2-04 | LOW | FIXED-VERIFIED | NONE | Race between ticket consume and pause. | AudioIngestEndpoint.cs:68-72; tests/Tether.IntegrationTests/B2FixTests.cs:95 (RAN 2026-10-29, pass) |
| M2B2-05 | m2-b2-review B2-05 | LOW | FIXED-VERIFIED | NONE | Fast reconnect could burn its ticket and get 4409. | B2FixTests.cs:118 (RAN 2026-10-29, pass) |
| M2B2-06 | m2-b2-review B2-06 | LOW | FIXED-VERIFIED | NONE | Course purge left incomplete consent evidence. | ADR-020b; B2FixTests.cs:162 (RAN 2026-10-29, pass) |
| M2B2-07 | m2-b2-review B2-07 | LOW | FIXED-VERIFIED | NONE | Archiving a course did not stop its live session. | ADR-020b; B2FixTests.cs:139 (RAN 2026-10-29, pass) |
| M2B2-08 | m2-b2-review B2-08 | LOW | FIXED-VERIFIED | NONE | Demo fault injection/script accepted in any environment. | TranscriptModule.cs:45-48; B2FixTests.cs:290 (RAN 2026-10-29, pass) |
| M2B2-09 | m2-b2-review B2-09 | LOW | FIXED-VERIFIED | NONE | demoSource computed from current provider, not stored per session. | ADR-020c; B2FixTests.cs:325 (RAN 2026-10-29, pass) |
| M2B2-10 | m2-b2-review B2-10 | LOW | OPEN | NONE | Transcript source tables mutable by the app role (no append-only triggers). | No trigger in src/api/src/Modules/Transcript/**/Migrations (grep). |
| M2B2-11 | m2-b2-review B2-11 | LOW | OPEN | NONE | Vendor-robustness: unbounded _events channel, _rateStrikes never decays, provider write failure leaves socket open, no unique Final (session_id, segment_no). | RelayConnection.cs:52 CreateUnbounded; :64,239 strikes never reset; no unique index in Transcript migrations. Matters once a real STT vendor exists. |
| M2B2-12 | m2-b2-review B2-12 | LOW | OPEN | NONE | Lifecycle job: one throwing row stops the batch every tick. | SessionLifecycleJob.cs:42-58 loop has no per-row try/catch; only the per-tick catch at :78. |

## AI gateway B3 (m2-b3-review.md) and re-review (m2-b3-rereview.md)

Re-review IDs N-01..N-10 are prefixed M2B3R to avoid a clash with the M1 re-review. Info items are in Observations.

| ID | Source | Sev | Status | Gate | Summary | Evidence |
|---|---|---|---|---|---|---|
| M2B3-01 | m2-b3-review B3-01 | MEDIUM | FIXED-VERIFIED | PRODUCTION | Output sanitizer single-pass: nested payloads rebuild markup. | m2-b3-rereview RAN harness; Tether.AiTests SanitizerAndPromptTests (RAN 2026-10-29, pass) |
| M2B3-02 | m2-b3-review B3-02 | MEDIUM | FIXED-VERIFIED | PRODUCTION | Budget check-then-spend; ledger fails open. | m2-b3-rereview READ (reservation + advisory lock); tests/Tether.IntegrationTests/AiBudgetTests.cs (RAN 2026-10-29, pass) |
| M2B3-03 | m2-b3-review B3-03 | MEDIUM | FIXED-VERIFIED | PRODUCTION | No HTTP tests for AI route authorization/abuse. | AiRouteTests/AiRouteRateLimitTests/AiRouteInFlightTests tests/Tether.IntegrationTests/AiRoutesTests.cs (RAN 2026-10-29, pass) |
| M2B3-04 | m2-b3-review B3-04 | LOW | FIXED-VERIFIED | NONE | Catch-up could stay Pending forever. | m2-b3-rereview; AiRoutesTests orphan sweep (RAN 2026-10-29, pass) |
| M2B3-05 | m2-b3-review B3-05 | LOW | OPEN | NONE | Rate/duplicate controls: per-user limit, 200-per-session permanent cap, key binding on coalesced answer still unfixed. | m2-b3-rereview 'Partially fixed' (a, c fixed; b, d, e unchanged); PROGRESS Iteration 8 deferred. |
| M2B3-06 | m2-b3-review B3-06 | LOW | FIXED-VERIFIED | NONE | AI work continued after Pause/Stop/Delete. | SessionCutoff; tests/Tether.IntegrationTests/AiSessionCutoffTests.cs (RAN 2026-10-29, pass); m2-b3-rereview. |
| M2B3-07 | m2-b3-review B3-07 | LOW | FIXED-VERIFIED | NONE | Model-derived id placed unescaped in prompt. | m2-b3-rereview READ (CleanId). |
| M2B3-08 | m2-b3-review B3-08 | LOW | FIXED-VERIFIED | NONE | Mock output not marked on the wire. | m2-b3-rereview READ; source field in schemas (ContractTests (RAN 2026-10-29, pass)). |
| M2B3R-N01 | m2-b3-rereview N-01 | LOW | FIXED-VERIFIED | NONE | Resurrected scheduler state loops after Stop/Delete. | PROGRESS Iteration 8 tombstones; tests/Tether.IntegrationTests/AiSessionCutoffTests.cs (RAN 2026-10-29, pass). Not independently re-reviewed. |
| M2B3R-N02 | m2-b3-rereview N-02 | LOW | FIXED-VERIFIED | NONE | Budget reservations can leak (no reaper). | ReservationReaper.cs; tests/Tether.IntegrationTests/AiReservationReaperTests.cs (RAN 2026-10-29, pass). PROGRESS: tests not shown red first. |
| M2B3R-N03 | m2-b3-rereview N-03 | MEDIUM | FIXED-VERIFIED | PRODUCTION | Own Pause/Stop exempt from every limiter (unbounded DB-backed endpoint). | Program.cs:72,104-105,110 (separate 'rev:' bucket, endpoint metadata); tests/Tether.IntegrationTests/SecurityHardeningTests.cs:196-243 (RAN 2026-10-29, pass). PROGRESS: not shown red first. |
| M2B3R-N04 | m2-b3-rereview N-04 | LOW | FIXED-VERIFIED | NONE | Quadratic regex with no match timeout in sanitizer. | PROGRESS Iteration 8 (timeouts, 1 s deadline); SanitizerAndPromptTests (RAN 2026-10-29, pass) |
| M2B3R-N06 | m2-b3-rereview N-06 | LOW | FIXED-UNVERIFIED | NONE | Hub catchup.ready/summary.revision skipped the strict parsers. | PROGRESS Iteration 8 claims fix; web unit tests NOT run by me. |

## Phase 2 contract review (p2-contract-review.md, P2-01..P2-30)

Design findings against syllabus.v1. 'Text' fixes were verified by p2-contract-rereview/p2-contract-delta and by the 830 contract tests I ran. P2-09 was intentionally unused. Controls that need unbuilt components (parser sandbox, ingest worker) are listed in the Before-production check, not hidden here.

| ID | Source | Sev | Status | Gate | Summary | Evidence |
|---|---|---|---|---|---|---|
| P2C-01 | p2-contract P2-01 | HIGH | FIXED-VERIFIED | PUSH | Hostile-file hardening not normative. | API_CONTRACTS_P2.md 5.1; p2-contract-rereview Part 1 'Verified (text)'. Parser is not built (mock-only). |
| P2C-02 | p2-contract P2-02 | HIGH | FIXED-VERIFIED | PUSH | Where upload bytes live was unstated (Postgres/WAL/backups). | Decision changed to tmpfs, never Postgres (API_CONTRACTS_P2.md 5.1 item 9); p2-contract-delta Part 3(a) 'conditionally granted for mock-only'. Upload code hashes and discards bytes (p2-b1-review). |
| P2C-03 | p2-contract P2-03 | MEDIUM | FIXED-VERIFIED | PRODUCTION | Evaluation order and body limits on multipart. | p2-contract-rereview Part 1; p2-b1-review 'Upload limits' OK. |
| P2C-04 | p2-contract P2-04 | LOW | FIXED-VERIFIED | NONE | No AV scan: state that files are never served back. | p2-contract-rereview Part 1. |
| P2C-05 | p2-contract P2-05 | HIGH | FIXED-VERIFIED | PUSH | Nested-id scoping not normative. | p2-b1-review 'no IDOR on any of 16 routes'; tests/Tether.IntegrationTests/Syllabus/SyllabusOwnershipTests.cs (RAN 2026-10-29, pass) |
| P2C-06 | p2-contract P2-06 | HIGH | FIXED-VERIFIED | PUSH | Content-hash extraction cache could leak across users. | API_CONTRACTS_P2.md 5.1 item 10; unique index includes user_id (p2-contract-rereview, S001 index). |
| P2C-07 | p2-contract P2-07 | MEDIUM | FIXED-VERIFIED | PRODUCTION | existingSyllabusId lookup scope. | p2-contract-rereview Part 1. |
| P2C-08 | p2-contract P2-08 | HIGH | FIXED-VERIFIED | PUSH | Hostile-but-verbatim dates; batch approve bypass. | Schema rules + p2-b1-review 'Prompt injection' OK (InjectionSuspect, batch disabled). Accepted residual: P2R-R2-09. |
| P2C-10 | p2-contract P2-10 | MEDIUM | FIXED-VERIFIED | PRODUCTION | Idempotency scope/replay semantics. | p2-contract-delta R2-10 verified; tests/Tether.IntegrationTests/Syllabus/SyllabusB1FixTests.cs (B1-06/07 tests) (RAN 2026-10-29, pass) |
| P2C-11 | p2-contract P2-11 | MEDIUM | FIXED-VERIFIED | PRODUCTION | Revision vs ETag, TOCTOU at sync. | p2-b1-review 'ETag / If-Match' OK; concurrent sync/patch tests listed 'probably effective'. |
| P2C-12 | p2-contract P2-12 | MEDIUM | FIXED-VERIFIED | PRODUCTION | Batch endpoint semantics. | p2-b1-review (per-item not_found, per-item transactions). |
| P2C-13 | p2-contract P2-13 | MEDIUM | FIXED-VERIFIED | PRODUCTION | Contradictory caps. | p2-contract-delta Part 1 R2-13/R2-15 verified. |
| P2C-14 | p2-contract P2-14 | MEDIUM | FIXED-VERIFIED | PRODUCTION | Mass assignment / unknown members. | p2-b1-review 'Mass assignment' OK (Strict binder). |
| P2C-15 | p2-contract P2-15 | LOW | FIXED-VERIFIED | NONE | Id entropy; itemKey not an identifier. | p2-contract-rereview. |
| P2C-16 | p2-contract P2-16 | HIGH | FIXED-VERIFIED | PUSH | No write-boundary text policy (notes, bidi, HTML). | TextPolicy.cs (p2-b1-review READ); patterns verified in p2-contract-delta; residuals R3-07/08/09, P2B1-10 tracked. |
| P2C-17 | p2-contract P2-17 | MEDIUM | FIXED-VERIFIED | PRODUCTION | Title/kind are model wording, not verified. | p2-contract-rereview Part 1. |
| P2C-18 | p2-contract P2-18 | MEDIUM | FIXED-VERIFIED | PRODUCTION | Mock-only guard is config-only; real Google needs a new contract. | Guard tests (p2-b1-review: Startup_refuses_*); calendar.v1 is the new contract (still DRAFT). |
| P2C-19 | p2-contract P2-19 | MEDIUM | FIXED-VERIFIED | PRODUCTION | needs_date enforcement gaps. | p2-contract-rereview R2-07 verified (schema allOf). |
| P2C-20 | p2-contract P2-20 | HIGH | FIXED-VERIFIED | PUSH | Deletion incomplete by design (calendarEvents=keep). | keep dropped; delete scan test (p2-b1-review, SyllabusBatchAndRetentionTests (RAN 2026-10-29, pass)). |
| P2C-21 | p2-contract P2-21 | MEDIUM | FIXED-VERIFIED | PRODUCTION | Export hardening (headers, rate, audit). | p2-b1-review 'Export' implemented (SyllabusEndpoints.cs:277-293). |
| P2C-22 | p2-contract P2-22 | MEDIUM | FIXED-VERIFIED | PRODUCTION | Extraction job lifecycle (lease, reaper, quota). | p2-contract-delta; code Jobs.cs; residual lease fencing P2B1-13. |
| P2C-23 | p2-contract P2-23 | MEDIUM | FIXED-UNVERIFIED | PRODUCTION | Syllabus load could starve live-lecture AI budget (per-pool floor). | A003 pool columns exist (AiScopeMigrationTests (RAN 2026-10-29, pass)); no load test proves lecture AI survives syllabus load (contract section 12 test not written). |
| P2C-24 | p2-contract P2-24 | HIGH | FIXED-VERIFIED | PUSH | Retention/sensitive content, attestation, notice version. | noticeVersion + attestation stored, server receipt time, 180-day purge (p2-b1-review; SyllabusBatchAndRetentionTests (RAN 2026-10-29, pass)). Counsel/PO decisions (FERPA, copyright, accommodation data) stay on the human gate list; no real documents authorized. |
| P2C-25 | p2-contract P2-25 | MEDIUM | SUPERSEDED | PRODUCTION | Syllabus logging rules / canary. | Superseded by P2B1-02, which tracks the implementation gap. |
| P2C-26 | p2-contract P2-26 | MEDIUM | FIXED-VERIFIED | PRODUCTION | CSRF/Origin on multipart and DELETE. | p2-b1-review 'Origin exact match incl. multipart'; Cross_origin_multipart test (SyllabusUploadTests (RAN 2026-10-29, pass)). |
| P2C-27 | p2-contract P2-27 | MEDIUM | FIXED-VERIFIED | PRODUCTION | Date derivation bounds, term, zone, DST. | Schema window 2000-2100 (rereview); server DST-gap check gap is P2B1-14. |
| P2C-28 | p2-contract P2-28 | LOW | FIXED-VERIFIED | NONE | Error leakage / oracles. | p2-b1-review 'Error leakage' OK. |
| P2C-29 | p2-contract P2-29 | LOW | DEFERRED-WITH-REASON | NONE | Server-side confirm record for real writes. | Deferred to calendar.v1 (CV-11 run row stores confirm). |
| P2C-30 | p2-contract P2-30 | LOW | FIXED-VERIFIED | NONE | Companion docs not marked superseded. | p2-contract-rereview Disposition: SUPERSEDED notes appended. |

## Phase 2 contract re-review (p2-contract-rereview.md, R2-01..R2-21)

D-01..D-14 (contract vs data-design divergences) are Observations. Prefix P2R.

| ID | Source | Sev | Status | Gate | Summary | Evidence |
|---|---|---|---|---|---|---|
| P2R-R2-01 | p2-rereview R2-01 | MEDIUM | FIXED-VERIFIED | PRODUCTION | Pattern anchor '$' accepted a trailing LF. | Patterns end in (?![\s\S]); p2-contract-delta RAN; ContractTests (RAN 2026-10-29, pass) |
| P2R-R2-02 | p2-rereview R2-02 | MEDIUM | SUPERSEDED | PRODUCTION | Unicode bypasses in hand-written class. | Superseded by R3-07/R3-08/R3-09 (p2-contract-delta 'Partial'). |
| P2R-R2-03 | p2-rereview R2-03 | LOW | FIXED-VERIFIED | NONE | ZWNJ/ZWJ rejected legitimate scripts. | p2-contract-delta Part 1 Verified (RAN). |
| P2R-R2-04 | p2-rereview R2-04 | MEDIUM | FIXED-VERIFIED | PRODUCTION | snippet.text outlived retention purge; no hard maximum. | Nullable snippet schema rules, 365-day cap (p2-contract-delta Part 1); purge code Jobs.cs:277-289. PO decision on item text surviving purge still open (human). |
| P2R-R2-05 | p2-rereview R2-05 | MEDIUM | FIXED-VERIFIED | PRODUCTION | Alias forms of one id; duplicate JSON keys. | Strict binder (AllowDuplicateProperties=false) per p2-b1-review; Non_canonical_ids test. |
| P2R-R2-06 | p2-rereview R2-06 | MEDIUM | FIXED-VERIFIED | PRODUCTION | hiddenContentRemoved not tied to dropped/batch flags. | Schema both directions (p2-contract-delta Part 1 Verified). |
| P2R-R2-07 | p2-rereview R2-07 | MEDIUM | FIXED-VERIFIED | PRODUCTION | Edit of resolved AI item keeps looks_right. | Schema allOf rule (p2-contract-delta). |
| P2R-R2-08 | p2-rereview R2-08 | MEDIUM | FIXED-VERIFIED | PRODUCTION | Supersession cue list narrow/undefined. | Contract text verified (p2-contract-delta). Calibration on synthetic syllabi not done: before real documents. |
| P2R-R2-09 | p2-rereview R2-09 | LOW | DEFERRED-WITH-REASON | NONE | Plain false-but-clean date still batch-approvable. | Accepted residual recorded (API_CONTRACTS_P2.md section 8(e)); re-rate before real documents. |
| P2R-R2-10 | p2-rereview R2-10 | MEDIUM | FIXED-VERIFIED | PRODUCTION | Idempotency lookup order; replay after delete. | Tombstone design in S001:325-335; p2-contract-delta verified. |
| P2R-R2-11 | p2-rereview R2-11 | MEDIUM | FIXED-VERIFIED | PRODUCTION | sync_failed unrepresentable for never-synced item. | Schema three-way rule (p2-contract-delta Verified). |
| P2R-R2-12 | p2-rereview R2-12 | MEDIUM | SUPERSEDED | PRODUCTION | Orchestrator vs parser trust split. | Superseded by R3-03/R3-04 (p2-contract-delta 'Partial'). |
| P2R-R2-13 | p2-rereview R2-13 | MEDIUM | FIXED-VERIFIED | PRODUCTION | UNLOGGED bytes table residual risks. | Withdrawn: bytes never in Postgres (p2-contract-delta (a)). |
| P2R-R2-14 | p2-rereview R2-14 | MEDIUM | FIXED-VERIFIED | PRODUCTION | Shared write bucket with lecture traffic; pool dimension. | SeparateRateBucket (Program.cs:101-103); A003 pool; test 'Syllabus_writes_have_their_own_bucket' (p2-b1-review). Load test: see P2C-23. |
| P2R-R2-15 | p2-rereview R2-15 | LOW | FIXED-VERIFIED | NONE | Cap race and processing order. | p2-contract-delta Verified. |
| P2R-R2-16 | p2-rereview R2-16 | LOW | SUPERSEDED | NONE | Code points vs UTF-16 unit mismatches. | Superseded by delta N-01, fixed in S001: ck_canonical_texts_len (S001.cs:107) READ. |
| P2R-R2-17 | p2-rereview R2-17 | LOW | FIXED-VERIFIED | NONE | No body-size caps on JSON routes; NUL ambiguity. | p2-contract-delta Verified. |
| P2R-R2-18 | p2-rereview R2-18 | LOW | FIXED-VERIFIED | NONE | Doc tests passed vacuously. | p2-contract-delta Verified; ContractTests 830 pass (RAN 2026-10-29, pass) |
| P2R-R2-19 | p2-rereview R2-19 | LOW | FIXED-VERIFIED | NONE | Creation quota unenforceable in mock mode. | p2-contract-delta (counter via audit rows, N-08 noted). |
| P2R-R2-20 | p2-rereview R2-20 | LOW | FIXED-VERIFIED | NONE | errors key pattern lacked '-'. | p2-contract-delta Verified (fixture). |
| P2R-R2-21 | p2-rereview R2-21 | LOW | FIXED-VERIFIED | NONE | Real-model guard was config-only. | API_CONTRACTS_P2.md 3.1; Startup_refuses_* tests (p2-b1-review). Env-name 'Test' escape is P2B1-13. |

## Phase 2 contract delta check (p2-contract-delta.md, R3-01..R3-14)

N-01..N-17 (contract vs data design) are Observations. Prefix P2D.

| ID | Source | Sev | Status | Gate | Summary | Evidence |
|---|---|---|---|---|---|---|
| P2D-R3-01 | p2-delta R3-01 | MEDIUM | FIXED-VERIFIED | PRODUCTION | Purged syllabus remained batch-approvable (blind approve). | Schema rule present; fixture syllabus--text-purged has batchApproveAllowed false and invalid twin syllabus--purged-batch-allowed.json exists (READ); ContractTests (RAN 2026-10-29, pass) |
| P2D-R3-02 | p2-delta R3-02 | LOW | FIXED-UNVERIFIED | NONE | Lone surrogate / invalid UTF-8 in JSON must be 400, not 500. | Contract text 2.2 READ; the required server tests (section 12) were not found in tests/. |
| P2D-R3-03 | p2-delta R3-03 | MEDIUM | FIXED-VERIFIED | PRODUCTION | Shared long-lived parser sandbox; process roles. | API_CONTRACTS_P2.md 5.1 item 8 READ (one fresh sandbox per job; ingest worker mounts volume). Sandbox not built. |
| P2D-R3-04 | p2-delta R3-04 | MEDIUM | FIXED-VERIFIED | PRODUCTION | Parser stdout/container log driver would log document text. | 5.1 item 8 READ (result file/pipe, log driver none, canary test required). Not built. |
| P2D-R3-05 | p2-delta R3-05 | LOW | FIXED-VERIFIED | NONE | In-flight byte cap lacked per-user concurrency. | 5.1 item 2 READ; p2-b1-review 'one upload per user'. Starvation nuance: P2B1-12. |
| P2D-R3-06 | p2-delta R3-06 | LOW | FIXED-VERIFIED | NONE | tmpfs vs node-local disk equivalence. | 5.1 item 9 READ (memory-backed required). |
| P2D-R3-07 | p2-delta R3-07 | LOW | DEFERRED-WITH-REASON | NONE | Schema patterns only reliable in UTF-16 regex engines. | Contract 2.2 states requirement; ajv astral test is required before the web client consumes the schemas. Not run. |
| P2D-R3-08 | p2-delta R3-08 | LOW | FIXED-VERIFIED | NONE | Stripped variation selectors etc. not counted as hidden content. | API_CONTRACTS_P2.md 2.2 line 59 READ. |
| P2D-R3-09 | p2-delta R3-09 | LOW | DEFERRED-WITH-REASON | NONE | VS15/VS16 invisible channel after non-ASCII base. | Accepted residual for mock (contract line 59). |
| P2D-R3-10 | p2-delta R3-10 | LOW | FIXED-VERIFIED | NONE | Multipart fingerprint must be HMAC, not plain hash. | Contract line 45 READ; p2-b1-review 'HMAC fingerprint'. |
| P2D-R3-11 | p2-delta R3-11 | LOW | FIXED-VERIFIED | NONE | attestedAt client-supplied. | p2-b1-review: Notice_and_attestation_are_stamped_by_the_server_not_the_client. |
| P2D-R3-12 | p2-delta R3-12 | LOW | FIXED-VERIFIED | NONE | Deletion test vs allowed survivors. | Contract line 166 READ; audit contentHmac (p2-b1-review). |
| P2D-R3-13 | p2-delta R3-13 | LOW | FIXED-VERIFIED | NONE | needs_text hand entry vs 7-day debris purge. | Contract line 87 READ. |
| P2D-R3-14 | p2-delta R3-14 | LOW | FIXED-VERIFIED | NONE | Id-form check ordering; restore repeats row_version. | Contract line 39 READ (id check at step 3). |

## Syllabus module P2-B1 (p2-b1-review.md incl. 'B1 fixes')

Prefix P2B1. B1-16..B1-20 are Info. 'B1 fixes' covered B1-01,03,04,05,06,07,13; the 14 red-first tests in SyllabusB1FixTests.cs ran green in the 2026-10-29 run.

| ID | Source | Sev | Status | Gate | Summary | Evidence |
|---|---|---|---|---|---|---|
| P2B1-01 | p2-b1-review B1-01 | MEDIUM | FIXED-VERIFIED | PRODUCTION | Syllabus delete: no deadlock retry; reaper lock order reversed. | Jobs.cs:203-205 lock order; tests/Tether.IntegrationTests/Syllabus/SyllabusB1FixTests.cs B1_01_* (RAN 2026-10-29, pass) |
| P2B1-02 | p2-b1-review B1-02 | MEDIUM | FIXED-VERIFIED | PRODUCTION | No log-canary through the real log pipeline; Npgsql and EF Query log categories unfiltered. | Program.cs:38-46; tests/Tether.IntegrationTests/LogPipelineCanaryTests.cs:97,155 (RAN 2026-10-29, pass). Mutation in a scratch copy: removing all filters, or Hosting.Diagnostics, or ExceptionHandlerMiddleware+EF Update makes the canary FAIL. Removing the EF Query/Npgsql.* filters does NOT fail it (B1V-02). Provider-scoped config bypass is B1V-01 (MEDIUM, open). |
| P2B1-03 | p2-b1-review B1-03 | MEDIUM | FIXED-VERIFIED | PRODUCTION | Batch approvals not individually audited. | B1_03_* in tests/Tether.IntegrationTests/Syllabus/SyllabusB1FixTests.cs (RAN 2026-10-29, pass) |
| P2B1-04 | p2-b1-review B1-04 | MEDIUM | FIXED-VERIFIED | PRODUCTION | Unbounded growth of sync_runs, items, audit. | B1_04_* in tests/Tether.IntegrationTests/Syllabus/SyllabusB1FixTests.cs (RAN 2026-10-29, pass). Residual (purge_evidence has no production caller) is NEW-01. |
| P2B1-05 | p2-b1-review B1-05 | LOW | FIXED-VERIFIED | NONE | Foreign DELETE cancelled another user's job. | B1_05_* in tests/Tether.IntegrationTests/Syllabus/SyllabusB1FixTests.cs (RAN 2026-10-29, pass) |
| P2B1-06 | p2-b1-review B1-06 | LOW | FIXED-VERIFIED | NONE | Item-create idempotency race returns 500. | B1_06_* in tests/Tether.IntegrationTests/Syllabus/SyllabusB1FixTests.cs (RAN 2026-10-29, pass) |
| P2B1-07 | p2-b1-review B1-07 | LOW | FIXED-VERIFIED | NONE | Fingerprint material delimiter-joined. | B1_07_* in tests/Tether.IntegrationTests/Syllabus/SyllabusB1FixTests.cs (RAN 2026-10-29, pass) |
| P2B1-08 | p2-b1-review B1-08 | LOW | OPEN | NONE | Retention purge keeps snippet offsets (contract says remove). | Not in the 'B1 fixes' list; Jobs.cs:277-289 still nulls text and context but keeps snippet_start/end. |
| P2B1-09 | p2-b1-review B1-09 | LOW | OPEN | NONE | Contract's custom CSRF header does not exist. | Same evidence as M1-F04: no X-Tether-CSRF in src/api. |
| P2B1-10 | p2-b1-review B1-10 | LOW | FIXED-VERIFIED | NONE | NFC is a no-op under InvariantGlobalization; the test is a tautology. | Directory.Build.props InvariantGlobalization=false; TextAndIdTests.cs:171 now asserts 'Caf\u00E9' and not a mirror; NormalizationGuardTests (TextAndIdTests.cs:319); module guard SyllabusModule.cs:29 (RAN 2026-10-29, Syllabus 99 pass). No culture-sensitive string calls found in src (grep). Latent issue B1V-07. |
| P2B1-11 | p2-b1-review B1-11 | LOW | FIXED-VERIFIED | NONE | Idempotency/audit HMAC keys fall back to random per-process keys; only ETagKey warned. | RootSecret.cs; RootSecretTests.cs:37,54,71,81,90,105,119,126,145,154,178,188,214 (RAN 2026-10-29, pass; RFC 5869 check at :37). Fallback to random per-process keys is gone outside Development. Residuals: B1V-03, B1V-04, B1V-05, B1V-06. |
| P2B1-12 | p2-b1-review B1-12 | LOW | OPEN | NONE | Ten slow uploaders can starve the 100 MB upload gate. | Not in 'B1 fixes'. |
| P2B1-13 | p2-b1-review B1-13 | LOW | OPEN | NONE | Lease fencing done; no heartbeat during long extraction; fault injection keyed on environment name 'Test'. | B1_13_* tests/Tether.IntegrationTests/Syllabus/SyllabusB1FixTests.cs (RAN 2026-10-29, pass) covers fencing; 'B1 fixes' lists heartbeat and env-name as not done. |
| P2B1-14 | p2-b1-review B1-14 | LOW | FIXED-VERIFIED | NONE | DST-gap check silently passes when host tzdata is missing. | Support.cs IsInDstGap throws and ZoneAllowList.EnsureHostSupportsAll; SyllabusModule.cs:30; TextAndIdTests.cs:335,342,351 (RAN 2026-10-29, pass). The 'host lacks tzdata' case is tested by injecting the resolver, not on a real image. |
| P2B1-15 | p2-b1-review B1-15 | LOW | OPEN | NONE | Audit immutability bypassable by the app role (switch via set_config; app owns table). | Not in 'B1 fixes'. Same owner-role gap applies to M2B1-02 evidence tables. |

## calendar.v1 contract review (calendar-v1-contract-review.md) and re-review (calendar-v1-contract-rereview.md)

Updated 2026-10-29 from `docs/security/calendar-v1-contract-rereview.md` (independent re-review of the revised contract; 830 contract tests passed). CV-01 and CV-02 are FIXED-VERIFIED at contract level. CV-03..CV-24 are FIXED-CONTRACT-ONLY (CV-21 DEFERRED-WITH-REASON): the contract defect is closed, nothing is implemented, no Google behaviour is verified (T1-T12 NOT RUN), so MEDIUM rows remain production blockers until the module's tests exist. CVR-N01..CVR-N08 are new LOW findings from the re-review. CV-25..CV-28 are Observations. Contract files may be untracked in the working tree.

| ID | Source | Sev | Status | Gate | Summary | Evidence |
|---|---|---|---|---|---|---|
| CV-01 | calendar-v1 CV-01 | HIGH | FIXED-VERIFIED | PUSH | Syllabus delete / disconnect-keep left 'detached' entries with titles and dates (re-opens P2-20). | Verified at contract level: docs/security/calendar-v1-contract-rereview.md. Contract 15, 9.3 (no `detached` state), 12, CAL-26, calendar-policy.json hardDeleteOn/contentFreeEventRecord; tests Policy_retention_follows_PO_10h_and_the_content_free_record_holds_only_event_id_and_generation_CAL_26 and Disconnect_remove_events_needs_count_and_digest_and_keep_has_neither_and_detached_no_longer_exists (RAN 2026-10-29, pass). Not runtime-verified: extended delete scan and 400-day purge test are required for the module (production recipe). Open PO items X14/X15/Q1/Q14 are decisions, not defects (re-review I-1). |
| CV-02 | calendar-v1 CV-02 | HIGH | FIXED-VERIFIED | PUSH | 'disconnecting' could never be left after invalid_grant; account deletion could hang. | Verified at contract level: docs/security/calendar-v1-contract-rereview.md. Contract 5.4 exits (terminal token failure, keep, 24 h deadline), 9.1, CAL-17, policy disconnecting.deadlineHours=24 and accountDeletion.blocksOnGoogle=false (asserted in CalendarContractTests, RAN 2026-10-29, pass). Fake-Google behaviour test still to write. |
| CV-03 | calendar-v1 CV-03 | MEDIUM | FIXED-CONTRACT-ONLY | PRODUCTION | Drift check: events.update replaces whole resource, TOCTOU, fingerprint false positives. | Re-reviewed docs/security/calendar-v1-contract-rereview.md. Contract 9.3/13.1 verified (full-resource update, If-Match, fingerprint from Google's response). Google behaviour UNVERIFIED (T7 NOT RUN). Required: fake-Google normalisation, student-added location, edit between get and update, 412 path. |
| CV-04 | calendar-v1 CV-04 | MEDIUM | FIXED-CONTRACT-ONLY | PRODUCTION | account_mismatch revoke could destroy another user's grant on a shared computer. | Re-reviewed docs/security/calendar-v1-contract-rereview.md. Contract 5.1, CAL-06 verified; test Auth_start_urls_request_only_allowlisted_scopes_params_and_the_exact_redirect_uri pins prompt=select_account consent. Required: fake OIDC with two Tether users. |
| CV-05 | calendar-v1 CV-05 | MEDIUM | FIXED-CONTRACT-ONLY | PRODUCTION | Token deletion wired to broad error signals; one ops mistake deletes all tokens. | Re-reviewed docs/security/calendar-v1-contract-rereview.md. Contract 5.3, 13.3, calendar-policy.json verified; test Policy_error_map_deletes_the_token_only_on_a_confirmed_invalid_grant_CAL_24. Required: production mapping equals the policy table; mass invalid_client deletes nothing. See CVR-N05. |
| CV-06 | calendar-v1 CV-06 | MEDIUM | FIXED-CONTRACT-ONLY | PRODUCTION | No per-user daily Google budget; shared-quota DoS. | Re-reviewed docs/security/calendar-v1-contract-rereview.md. Contract 13.2, 14, CAL-25 verified; test Policy_limits_have_a_per_user_daily_budget_fair_share_and_no_global_outage_CAL_25_and_CV_13. Numbers are PROPOSAL (PO-10q); PO/devops must approve. Required: load test. |
| CV-07 | calendar-v1 CV-07 | MEDIUM | FIXED-CONTRACT-ONLY | PRODUCTION | Eligibility re-checked only at plan time; event could be written for a withdrawn item. | Re-reviewed docs/security/calendar-v1-contract-rereview.md. Contract 8.2 (re-check before each Google write), CAL-05 verified. Required: withdraw/edit/delete during a run with the fake-Google request counter. Syllabus-side 409 tracked as CVR-N08. |
| CV-08 | calendar-v1 CV-08 | MEDIUM | FIXED-CONTRACT-ONLY | PRODUCTION | Removals/reminder-updates/disconnect bound only to a count. | Re-reviewed docs/security/calendar-v1-contract-rereview.md. Schemas require previewDigest and count; tests Every_state_changing_request_that_touches_events_is_bound_to_a_plan_digest_CV_08, Disconnect_remove_events_needs_count_and_digest_..., Action_previews_cover_... Required: plan_changed with zero writes per kind. |
| CV-09 | calendar-v1 CV-09 | MEDIUM | FIXED-CONTRACT-ONLY | PRODUCTION | Generation counter deleted with the entry row, so ids could be reused. | Re-reviewed docs/security/calendar-v1-contract-rereview.md. Resolved via PO-9o content-free record; contract 10 item 3; test Add_remove_add_bumps_the_generation_using_the_content_free_record_CV_09. See CVR-N01. Required: fake-Google id reuse case. |
| CV-10 | calendar-v1 CV-10 | MEDIUM | FIXED-CONTRACT-ONLY | PRODUCTION | include_granted_scopes=true makes scope_unexpected fire; token handling on mismatch undefined. | Re-reviewed docs/security/calendar-v1-contract-rereview.md. Parameter removed from the pinned set; scope_missing/unexpected store nothing (5.1); URL test asserts it is absent. Required: fake OIDC extra/missing scope. |
| CV-11 | calendar-v1 CV-11 | MEDIUM | FIXED-CONTRACT-ONLY | PRODUCTION | No noticeVersion / server confirm record for Google writes. | Re-reviewed docs/security/calendar-v1-contract-rereview.md. noticeVersion required in calendar-google-connect-start-request; 5.1 and 8.2 (grant version, run row digest/confirm/server time). Required: callback test. Notice text DRAFT pending legal (Q11). |
| CV-12 | calendar-v1 CV-12 | MEDIUM | FIXED-CONTRACT-ONLY | PRODUCTION | previewDigest construction undefined. | Re-reviewed docs/security/calendar-v1-contract-rereview.md. Contract 8.2 and policy previewDigest verified; test Preview_digest_policy_names_key_derivation_canonical_form_and_every_binding. Required: cross-user/syllabus/kind replay gives plan_changed or 404. See CVR-N03. |
| CV-13 | calendar-v1 CV-13 | MEDIUM | FIXED-CONTRACT-ONLY | PRODUCTION | Per-IP sign-in limits lock out a lecture hall behind one NAT. | Re-reviewed docs/security/calendar-v1-contract-rereview.md. Contract 14 and policy limits.auth verified (per-IP 300/min, per-binder 10/min, 20 un-consumed states). Numbers PROPOSAL. Required: NAT load test. |
| CV-14 | calendar-v1 CV-14 | LOW | FIXED-CONTRACT-ONLY | NONE | Adoption rule trusts forgeable markers. | Re-reviewed docs/security/calendar-v1-contract-rereview.md. Contract 10 items 4 and 6 verified. Required: grep test that no code reads extendedProperties; adoption fake case. |
| CV-15 | calendar-v1 CV-15 | LOW | FIXED-CONTRACT-ONLY | NONE | Callback URL secrets in logs/Referer; canary scope too narrow. | Re-reviewed docs/security/calendar-v1-contract-rereview.md. Contract 5.1 headers/redaction, CAL-10 verified. Required: canary across all sinks. |
| CV-16 | calendar-v1 CV-16 | LOW | FIXED-CONTRACT-ONLY | NONE | Id-bearing inputs (courseOverrides, cursors) without uniform 404/400. | Re-reviewed docs/security/calendar-v1-contract-rereview.md. Contract section 2, CAL-11 verified. Required: two-user route table. |
| CV-17 | calendar-v1 CV-17 | LOW | FIXED-CONTRACT-ONLY | NONE | Composed title / description link / body allowlist. | Re-reviewed docs/security/calendar-v1-contract-rereview.md. Contract 8.1, 11, policy eventBodyAllowlist; tests Policy_event_body_is_a_positive_allowlist_..., Titles_have_no_type_word_prefix_... Required: adapter body test. |
| CV-18 | calendar-v1 CV-18 | LOW | FIXED-CONTRACT-ONLY | NONE | calendars.insert crash creates duplicate calendars. | Re-reviewed docs/security/calendar-v1-contract-rereview.md. Contract 10 item 7 verified. Required: fake-Google crash after calendars.insert. |
| CV-19 | calendar-v1 CV-19 | LOW | FIXED-CONTRACT-ONLY | NONE | Disconnect revocation result not reported; stale idempotent replay. | Re-reviewed docs/security/calendar-v1-contract-rereview.md. Revocation field on the connection; test Connection_resource_reports_unconfirmed_revocation_...; replay returns current resource (5.4). |
| CV-20 | calendar-v1 CV-20 | LOW | FIXED-CONTRACT-ONLY | NONE | Destination-switching rules incomplete. | Re-reviewed docs/security/calendar-v1-contract-rereview.md. Contract 6, eventsInGoogle in settings (test). Required: PUT during run is 409. |
| CV-21 | calendar-v1 CV-21 | LOW | DEFERRED-WITH-REASON | NONE | Identity rules (find-or-create by iss+sub, session rotation) untested. | CAL-23 written in the contract (re-review accepts it as a LOW design rule); implementation tests required: email vs sub, double callback, session id rotation, binder cleared, unique sub, bad iss/aud/exp/nonce. |
| CV-22 | calendar-v1 CV-22 | LOW | FIXED-CONTRACT-ONLY | NONE | hd claim availability / admin_blocked unreachable. | Re-reviewed docs/security/calendar-v1-contract-rereview.md. Contract 5.2 verified; Google behaviour UNVERIFIED (T9 NOT RUN). |
| CV-23 | calendar-v1 CV-23 | LOW | FIXED-CONTRACT-ONLY | NONE | edited_in_google is a trap door. | Re-reviewed docs/security/calendar-v1-contract-rereview.md. Resolved by PO-10a/e/o and contract 8.6 resume run; see CVR-N02. |
| CV-24 | calendar-v1 CV-24 | LOW | FIXED-CONTRACT-ONLY | NONE | Contract test adequacy. | Re-reviewed docs/security/calendar-v1-contract-rereview.md. Case-insensitive forbidden-name matcher, rule rows CAL-01..CAL-26 anchored by id/phrases, policy files executed (CalendarContractTests 830 pass, RAN 2026-10-29). Phrase tests prove text exists, not behaviour; section 16 Required list is the module merge gate. |
| CVR-N01 | calendar-v1 re-review | LOW | OPEN | NONE | content-free event record is pseudonymous (event id hashes itemKey, linkable via audit rows); generation protection lapses after disconnect or at 400 days (safe failure: remote_conflict). | Re-review N-1. Fix: day-precision lastUsedAt, plain failure message, time-shift purge test. See docs/security/calendar-v1-contract-rereview.md. |
| CVR-N02 | calendar-v1 re-review | LOW | OPEN | NONE | Resume `take_back` write procedure unspecified (must use get + full-resource update + If-Match; preview cannot show the student's edit). | Re-review N-2. Fix: one sentence in 8.6 plus fake-Google cases. See docs/security/calendar-v1-contract-rereview.md. |
| CVR-N03 | calendar-v1 re-review | LOW | OPEN | NONE | Action previews do not state 'no Google call'; eventsAction not in the digest binding list. | Re-review N-3. Fix: text plus policy binds. See docs/security/calendar-v1-contract-rereview.md. |
| CVR-N04 | calendar-v1 re-review | LOW | OPEN | NONE | login_hint carries the Google sub in the authorization URL returned to the browser; contradicts 'never carries the sub' text; Google honouring a sub is UNVERIFIED. | Re-review N-4. Fix: state the exception, redact where URLs are logged, verify in a T-test. See docs/security/calendar-v1-contract-rereview.md. |
| CVR-N05 | calendar-v1 re-review | LOW | OPEN | NONE | Connection state undefined while the fleet circuit breaker is open; Testing-status 7-day expiry can trip the 20-connection threshold in a pilot. | Re-review N-5. Fix: set needs_reauth without deleting, no further calls, operator alert. See docs/security/calendar-v1-contract-rereview.md. |
| CVR-N06 | calendar-v1 re-review | LOW | OPEN | NONE | calendar-policy.json is not tied to production behaviour; drift passes the contract tests. | Re-review N-6. Fix: table-driven implementation test; security review of policy changes. See docs/security/calendar-v1-contract-rereview.md. |
| CVR-N07 | calendar-v1 re-review | LOW | OPEN | NONE | Account deletion does not state which eventsAction it uses (PO-10m says copies stay). | Re-review N-7. Fix: state keep unless the student chose removal in the dialog. See docs/security/calendar-v1-contract-rereview.md. |
| CVR-N08 | calendar-v1 re-review | LOW | OPEN | NONE | Syllabus-side 409 run_in_progress on items with an active intent is a requirement on frozen syllabus.v1 (no such code there); the worker recheck already covers the invariant. | Re-review N-8. Fix: decide under Q8: drop it or make a reviewed additive change. See docs/security/calendar-v1-contract-rereview.md. |

## Google token and data-residency design (google-tokens-and-data-residency.md, GR-1..GR-7)

Reviewer ratings were: GR-1 'High until PO/legal decide', GR-2 Medium, GR-3 Medium, GR-4 Low, GR-5 Medium, GR-6 Medium, GR-7 'blocking for real use' (rated MEDIUM here). Design only; no token code, Docs adapter or Google call exists.

| ID | Source | Sev | Status | Gate | Summary | Evidence |
|---|---|---|---|---|---|---|
| GR-1 | google-tokens GR-1 | HIGH | OPEN | PUSH | Transcript/study-guide copied into an institution-managed Google account becomes visible to the institution. | PO-10j allows Workspace accounts with a warning and PO-10l makes transcript upload per-session opt-in, but legal review (G1) is open and the rating was 'High until PO/legal decide'. No code implements any Docs/Drive write. Only the Product Owner can downgrade (severity-policy.md). |
| GR-2 | google-tokens GR-2 | MEDIUM | FIXED-UNVERIFIED | PRODUCTION | Deleting in Tether does not remove Google copies; users may assume it does. | PO-10m: dialogs must say so; calendar.v1 5.4 claims it. Docs delete UX not designed. Unreviewed. |
| GR-3 | google-tokens GR-3 | MEDIUM | DEFERRED-WITH-REASON | PRODUCTION | Token compromise on a compromised host reaches only Tether-created files/calendar. | ITokenCipher, KEK custody and scope allowlist test are not built (token doc section 5 items 3-4, 13). |
| GR-4 | google-tokens GR-4 | LOW | DEFERRED-WITH-REASON | NONE | KEK loss forces mass reconnect. | Rotation/restore drill not built. |
| GR-5 | google-tokens GR-5 | MEDIUM | DEFERRED-WITH-REASON | PRODUCTION | drive.file capability assumptions wrong, forcing a broader scope. | Vendor tests T1-T12 NOT RUN (calendar contract CV-28). |
| GR-6 | google-tokens GR-6 | MEDIUM | DEFERRED-WITH-REASON | PRODUCTION | Student shares a Doc containing third-party voices or weakness notes. | Student-controlled; warning copy not written (adhd-ux/legal). |
| GR-7 | google-tokens GR-7 | MEDIUM | DEFERRED-WITH-REASON | PRODUCTION | Unverified OAuth app: test users only, 7-day refresh expiry; unusable for real students. | Blocking for real use; Google Cloud project/OAuth client not created (PO-9g). |

## Batch 1 verification (batch1-verification.md, commit 096ba8e)

| ID | Source | Sev | Status | Gate | Summary | Evidence |
|---|---|---|---|---|---|---|
| B1V-01 | batch 1 verification | MEDIUM | OPEN | PRODUCTION | Code filters in Program.cs:38-46 have no provider name. A provider-scoped rule such as Logging:Console:LogLevel:Default=Trace wins over them (.NET selects provider-specific rules first), so Hosting.Diagnostics (full URL with query), ExceptionHandlerMiddleware and EF Update (exception with row values) log again. The comment and commit say config cannot re-enable them. | Scratch probe (not in repo) with a provider-scoped Trace rule: 9 sentinel records, incl. 'Request starting ... ?token=CANARY-02-QUERY-TOKEN' and EF Update. Fix: add rules per registered provider or post-configure LoggerFilterOptions; add a canary case with a provider-scoped rule. |
| B1V-02 | batch 1 verification | LOW | OPEN | NONE | Canary gaps: TestServer (no Kestrel categories or bad-request logs), new canary does not drive hub, audio socket or AI/LanguageGuard paths, sentinels are plain ASCII (no spaces or Unicode), EF Query and Npgsql.* filters are not proven (removing them still passes). | Scratch mutation runs; LogPipelineCanaryTests.cs:18-24. Older RealtimeLogCanaryTests covers hub and audio with fewer categories. |
| B1V-03 | batch 1 verification | LOW | OPEN | NONE | Explicit overrides Security:ETagKey, IdempotencyHmacKey(+Previous), SyllabusAuditHmacKey accept any 32+ bytes (zeros included) with no sample check or warning, in any environment. | READ Support.cs SyllabusKeys.Override, CoursesModule.cs, LecturesModule.cs. Not run. |
| B1V-04 | batch 1 verification | LOW | OPEN | NONE | Root-secret sample detection is a denylist. Bytes 0..31, 'abcdefghijklmnopqrstuvwxyz012345' and a repeating 4-byte pattern are ACCEPTED in Production; the public test key in ApiFactory.cs is accepted too (design asked for test-key refusal and key ids in readiness). | Scratch probe: 3 of 4 weak keys accepted. READ RootSecret.cs:90-96. |
| B1V-05 | batch 1 verification | LOW | OPEN | NONE | No root-secret rotation procedure: no RootSecretPrevious, IdempotencyHmacKeyPrevious takes a raw key (no tool prints the derived one), no previous ring for the audit HMAC. Docs (DEVELOPMENT.md, compose) do not mention RootSecret, AllowedHosts, ICU or tzdata; no Dockerfile exists to carry them. | grep of docs and compose; READ. Startup guards fail closed, so the effect is an operator trap, not a leak. |
| B1V-06 | batch 1 verification | LOW | OPEN | NONE | Development fallback file: if no home folder resolves the path becomes relative to the working directory (not run); an existing file's permissions are not checked on load; a reader can hit a half-written file and report it invalid. File mode 0600 on create is tested. | READ RootSecret.cs:71-75,98-120; RootSecretTests.cs:139 (pass). |
| B1V-07 | batch 1 verification | LOW | OPEN | NONE | With ICU, DocumentText.Canonicalize (DocumentText.cs:24) throws ArgumentException on a lone surrogate; TextPolicy catches it, Canonicalize does not. Not reachable over HTTP today (escaped lone surrogates in JSON gave 400 in a probe). | Scratch probe: 'canonicalize THROWS ArgumentException'. |
| B1V-08 | batch 1 verification | LOW | OPEN | NONE | Edge validators are lenient: any /8 IPv4 (public too) or /32 IPv6 network is accepted as a trusted proxy; HSTS always sends includeSubDomains for a year and cannot be configured (no preload token; browsers ignore it over plain http). On a shared parent domain it pins sibling subdomains to HTTPS. AllowedHosts defaults to 'localhost', so probes with another Host header get 400. | Scratch probe: 1.0.0.0/8 accepted. READ Program.cs:171-172. |

## Findings added by this register

Gaps implied by the reviews and decisions that no review gave an ID. Rated by the policy; where unsure, the higher level.

| ID | Source | Sev | Status | Gate | Summary | Evidence |
|---|---|---|---|---|---|---|
| NEW-01 | register (ADR-020b; p2-b1-review B1-04; m2-b1 B1-09) | MEDIUM | DEFERRED-WITH-REASON | PRODUCTION | No account/user deletion path exists and evidence-table erasure is undecided (purge_evidence has no production caller). | DECISIONS.md ADR-020b 'User deletion has no endpoint yet'; p2-b1-review fix notes. Invariant 8 / SEC-014 not met for accounts. Needs PO + legal for evidence retention. |
| NEW-02 | register (m1-rereview 'Before any non-dev use' #4) | MEDIUM | DEFERRED-WITH-REASON | PRODUCTION | SEC-005 lockout/back-off not built (vacuous until Google sign-in exists). | grep 'lockout' in src/api: none. Arrives with Google OIDC (PO-6). |
| NEW-03 | register (m1-review 'What I did not check') | MEDIUM | OPEN | PRODUCTION | No dependency, secret, container or CodeQL scan evidence; CI workflow has never run on GitHub. | PROGRESS Iterations 4 and 9; .github/workflows/ci.yml exists but no run record. Also gate G5 (RELEASE_GATES.md). |

## Observations (Info; not findings)

| ID | Source | Observation |
|---|---|---|
| M1-F10 | m1-review | Anonymous unknown path 401 vs signed-in 404 (verified OK). |
| M1-F11 | m1-review | IDOR/ownership verified OK. Limit 1 (ExecuteUpdate/Delete) now covered by ArchitectureTests ModuleBoundaryTests.cs:190 (RAN pass). |
| M1-F12 | m1-review | Not-yet-implemented requirements at M1 (CORS, hubs, outbound fetches, OAuth review). |
| M1R-N07 | m1-rereview | Weak ETag accepted in If-Match; '*' skips the check. |
| M1R-N08 | m1-rereview | ETagKey and DataProtection keys from plain config; Production only warns (Program.cs:131-134). Related to M1-F07. |
| M1R-N09 | m1-rereview | ExecuteUpdate/ExecuteDelete ban: DONE (ArchitectureTests, PROGRESS Iteration 3). |
| M1R-N10 | m1-rereview | Logout vs in-flight request window of one request (documented, accepted). |
| M1R-N11 | m1-rereview | Origin matching ignores case/trailing slash; hub path now exact. |
| M2C-32 | m2-contract C-32 | Idempotency-Key on session creation: implemented (M2B1-04). |
| M2B1-11 | m2-b1-review | Misleading comment, weak stopped_at CHECK (fixed: LectureB15Tests.cs:329), two limiter registrations, title rendered as text only. |
| M2B1-12 | m2-b1-review | Items correctly deferred to B2 (all since covered by B2 findings). |
| M2B2-13 | m2-b2-review | Wire details differ from section 15 (HubException text, 1006 on hello timeout, Ping counts as activity). PO decision on Ping still open. |
| M2B2-14 | m2-b2-review | ADR-020 missing from DECISIONS.md: now present. |
| M2B3-09 | m2-b3-review | ai tables lack append-only triggers; ai.global_spend writable by app role (and A002 updates ai_usage in place). |
| M2B3-10 | m2-b3-review | AiHealth breaker and global monthly budget are shared across users; in-process only. |
| M2B3-11 | m2-b3-review | SummaryJoinSource ignores the userId argument (EF filter protects). |
| M2B3-12 | m2-b3-review | Redactor is narrow; names/health details go to the vendor. Disclosure and G2 matter before any real model. |
| M2B3-13 | m2-b3-review | Web: Next WebSocket rewrite, CSP connect-src, eventGuard schema match (CSP is M2C-29). |
| M2B3R-N05 | m2-b3-rereview | Raised dev rate limits live only in docker-compose.dev.yml; startup warning added (RateLimitConfigTests). |
| M2B3R-N07 | m2-b3-rereview | Prompt-side Clean keeps Unicode Cf/tag characters (deferred, PROGRESS Iteration 8). |
| M2B3R-N08 | m2-b3-rereview | AiSource.Of labels any non-'mock' model id as 'model' (deferred). |
| M2B3R-N09 | m2-b3-rereview | Migration notes A001/A002; A002.Down fails once reserved rows exist. |
| M2B3R-N10 | m2-b3-rereview | B3-05 leftovers (b, d, e); tracked as M2B3-05. |
| P2B1-16 | p2-b1-review | Rate limiting state is per process. |
| P2B1-17 | p2-b1-review | Syllabus DELETE is subject to the 20/min write bucket (consider exempting like Pause/Stop). |
| P2B1-18 | p2-b1-review | Approval persists across a student edit of a synced item (matches reconciliation row 17). |
| P2B1-19 | p2-b1-review | Export covers stated scope; excludes canonical text, sync history, audit rows. |
| P2B1-20 | p2-b1-review | content_sha256 of the original survives text purge until syllabus delete (counsel note). |
| P2R-D-01..D-14 | p2-contract-rereview | Contract vs data-design divergences; p2-contract-delta: 11 verified, 3 partial (D-06, D-13, D-14). |
| P2D-N-01..N-17 | p2-contract-delta | New divergences. N-01 (canonical text CHECK), N-02 (notice_version varchar 64) and N-03 (tombstone keeps no syllabus id) READ as fixed in S001 (S001.cs:41,107,325-335). N-04..N-17 not individually checked by me. |
| CV-25 | calendar-v1 | (re-review: verified, 5.3/PO-10n) Sessions outlive a closed Google account until their 14-day limit (accepted by PO-10n). |
| CV-26 | calendar-v1 | (re-review: PARTIAL, text in section 2, SameSite/CSRF-header pin still a Required test) State both Origin and X-Tether-CSRF apply; Content-Type 415; pin SameSite by test. |
| CV-27 | calendar-v1 | (re-review: verified in 5.1; see CVR-N04 for login_hint) Egress rules: AllowAutoRedirect=false, no env proxy, pinned discovery URLs, login_hint = stored sub. |
| CV-28 | calendar-v1 | (unchanged) Everything is mock/fake; vendor tests T1-T12 NOT RUN. |
| S1-S13 | calendar-v1 review | Re-review dispositions (2026-10-29): verified at contract level S1, S4, S5, S6, S9, S10, S11 (numbers PROPOSAL), S13; verified in text with behaviour tests pending S2, S3, S7, S12; deferred to implementation S8 (CAL-23; closed-account purge Q4 is a PO decision). No S item is a separate finding; each maps to a CV row above. |
| RN-01 | register (rename work) | A004 relabels stored prompt_version in ai.summary_revisions and ai.catch_ups, rewriting provenance labels. Accepted by PO-11a; migration test AiScopeMigrationTests (RAN pass, up/down/repeat). |
| RN-02 | register (rename work) | Renamed route POST /sessions/{id}/quick-recap sits in the same MapGroup; no limiter, Origin check or authorization is keyed on the route name (no 'lost-track' left in Host/Program.cs; grep). Limits are per session in CatchUpService and per user in the host. AiRouteTests (RAN pass) use the new route. |
| RN-03 | register (LanguageGuard) | LanguageGuard logs rule ids and counts only; canary test The_log_carries_rule_ids_and_counts_and_never_the_text_log_canary (LanguageGuardTests.cs:241, RAN pass). It uses a fake ListLogger, not the real logging pipeline, so it shares the gap in P2B1-02. |
| RN-04 | register (LanguageGuard) | Default is log-only (Ai:LanguageGuard:Enforce false, AiOptions.cs:99): student-facing wording rules are not enforced until the PO/UX turn it on. Product, not security. |
| RN-05 | register (LanguageGuard) | A phrase quoted in the cited transcript is exempt, so lecture content can exempt its own wording from the guard. Wording-only effect; no security impact. Regexes have 100 ms timeouts, scan cap 20,000 characters; failure is 'incomplete' (fail closed only in enforce mode). |
| RN-06 | register (web redesign) | public/prefs-init.js is a same-origin synchronous script that reads two localStorage keys and sets whitelisted data-* attributes. It needs 'script-src self' once a CSP exists (M2C-29). next/font/google fetches fonts at build time (no runtime request), a build-time egress. |

RN-07 (register): `docs/DECISIONS.md` PO-9k and `docs/vendors/google-verification-report.md` name the Product Owner's personal dev Google address. It is in the committed history already. Mention to the Product Owner before any push to a public remote.

# Summary (5 lines)

1. Open CRITICAL: 0. Open HIGH: 1 (GR-1, awaiting a Product Owner ruling), so a git push is still blocked. CV-01 and CV-02 are FIXED-VERIFIED at contract level.
2. Open MEDIUM: 27 (blocks production), of which 11 are calendar.v1 items FIXED-CONTRACT-ONLY (need implementation tests); LOW not resolved: 51 (noted, includes CVR-N01..N08 and B1V-02..B1V-08).
3. 211 findings (14 review documents, 3 added by this register, 8 from batch 1 verification); 132 resolved (FIXED-VERIFIED or SUPERSEDED), 79 unresolved; 41 observations (S1-S13 added as one row).
4. Full .NET suite passed on 2026-10-29 (830 contract, 241 AI, 99 syllabus, 534 integration, 22 architecture; rerun after commit 096ba8e); web tests, CI, scanners, Google behaviour (T1-T12) and all human gates were not verified.
5. The rename, A004 migration, renamed route and LanguageGuard introduced no new CRITICAL, HIGH or MEDIUM finding; this register clears no release gate. RN-07 (PO's personal address in committed history) stays.
