# Google token key management and "Google as store of record": design and threat model

Date: 2026-10-28. Author: security-engineer. Status: **PROPOSAL / design only.** No code, cloud resource, OAuth client or external call was made. Nothing here is verified against current Google documentation (every Google behaviour is tagged **[UNVERIFIED]** or **[VERIFY]**; `vendor-verification-researcher` must confirm with dated sources). **No gate is recorded as passed.** The Product Owner (PO) said to plan as if legal clearance exists; that is a planning assumption only. G1 (recording/consent/university policy) stays `BLOCKED`, and the Google-as-recipient review stays open (docs/legal/phase2-uploads-and-vendors.md line 126).

Inputs read: CLAUDE.md invariants, docs/SECURITY.md (SEC-013..017, 020, 027, 028), docs/DECISIONS.md (PO-5, PO-6, ADR-012), docs/architecture/google-calendar-integration.md, docs/security/p2-b1-review.md (B1-11), docs/architecture/backend-foundation.md (identity and Integrations sections). Not read: source code (no code was inspected for this document), docs/legal/*.

PO decisions designed for: (a) one Google account per user; (b) use the Google suite (Calendar now, Docs/Drive for notes and study guides); (c) a Tether event the student deletes in Google is left alone; (d) all created content is stored in Google so it survives a Tether outage; (e) legal clearance assumed for planning only.

Dependent teams: `google-integration` (scopes, flows), `backend-engineer` (port, outbox, startup guards), `devops-engineer` (secret store, backup, per-environment config), `database-architect` (schema), `legal-compliance-reviewer` (section 3.7), `vendor-verification-researcher` (all [UNVERIFIED]). Lead Architect decides contract and ADR changes.

---

## 1. Token key management

### 1.1 Options compared

| Option | For | Against |
|---|---|---|
| A. ASP.NET Data Protection keyring (as used for cookies) | Already in the stack; automatic key creation and 90-day rolling; no new vendor | Keyring is a set of files/rows that must itself be protected and backed up; default at-rest protection in containers is weak (plain XML unless a cert or cloud key protects it); no AAD (only purpose strings); opaque failure when a key is missing; sharing one keyring with cookies couples cookie and token blast radius; `ApplicationName` mismatch silently makes everything undecryptable |
| B. Envelope encryption: per-row data key (DEK), wrapped by a versioned key-encryption key (KEK) | Explicit key ids and versions in the row; AAD binds ciphertext to user and connection; rotation by re-wrapping only; KEK custody can move from a secret store to a KMS without changing the row format | We own a small crypto wrapper (must use a vetted primitive, AES-256-GCM, never custom crypto) and a re-wrap job |
| C. Cloud KMS/vault doing the encrypt/decrypt calls directly (no local DEK) | Strongest custody, audit trail per call | Cost, latency on every refresh, vendor choice, region (PO-7 hosting region undecided); needs PO approval |

### 1.2 Recommendation (proposal)

**Default for the first pilot: option B behind one port, `ITokenCipher`, with the KEK supplied by the host's managed secret store (no new vendor, no new spend).** **Production: the same ciphertext format with the KEK held in a cloud KMS/vault (option B with a KMS-wrapped KEK)**, chosen by the PO together with the hosting provider (cost and vendor decision, see section 5). Data Protection stays in the product for session cookies and antiforgery only; it is not used for Google tokens.

Why B over A for tokens: explicit versioning and AAD are the controls SEC-020 needs; migration pilot to production is a re-wrap, not a re-encrypt of a different format; key loss and rotation behaviour is testable and legible. If the PO prefers fewer moving parts for a very small pilot, A with a dedicated protector (`CreateProtector("tether.google.refresh.v1").CreateProtector(userId)`), a separate keyring path from the cookie keyring, and keys encrypted by an X.509 certificate from the secret store is acceptable **for the pilot only**, with the same backup and loss rules below.

Ciphertext format (proposal): `v1 | kek_id | wrapped_dek | nonce | ciphertext | tag`, stored as `bytea` (or base64 text) with `kek_id` also in its own column for queries. AES-256-GCM, 96-bit random nonce per encryption, AAD = `"tether.google.refresh.v1" | user_id | connection_id | google_sub`. A row copied to another user or connection fails authentication.

### 1.3 Replacing random per-process keys (B1-11)

B1-11: `Security:IdempotencyHmacKey` and `Security:SyllabusAuditHmacKey` silently fall back to random bytes; only `ETagKey` warns. The same failure mode applied to tokens would be worse: a restart would lose every connection.

Proposal:
1. One **root secret per environment** (32 random bytes) in the secret store, plus the token KEK set. Derive purpose keys with HKDF-SHA256 and fixed labels (`etag.v1`, `idempotency.v1`, `syllabus-audit.v1`, `oauth-state.v1`). Rotation of one label does not require the others.
2. Outside `Development`/`Test`, **startup fails** if the root secret, the token KEK, or any derived key is absent, shorter than 32 bytes, or equal to a documented sample value. Development generates a key into user-secrets (never committed). Test uses a fixed key declared only in test projects and the host refuses a test key when the environment is not `Test`.
3. The fault-injection/`Test` environment-name escape (B1-13) must not unlock key fallbacks: key presence is checked regardless of environment name when `Security:RequireKeys=true` (default true except Development).
4. Idempotency and ETag keys keep the existing ring (current plus previous). The audit HMAC row records a `key_id`, so historical audit values stay verifiable after rotation. `/health/ready` reports key ids (not values) and fails when the token KEK is unavailable.

### 1.4 Rotation

- **Scheduled:** KEK rotated at most every 12 months or on staff change with access. Add a new `kek_id`, new writes use it, a background job re-wraps DEKs in batches (cheap: no refresh-token decryption needed beyond unwrap/rewrap), old KEK is retired only after a count query shows zero rows reference it and one backup cycle has passed.
- **On suspected compromise:** treat as an incident. Mint a new KEK, then bulk-revoke tokens at Google using the revocation endpoint [VERIFY endpoint and bulk behaviour], mark all connections `needs_reauth`, and let students reconnect. Revocation cannot be undone, so it needs PO sign-off in the runbook but is pre-authorized for a confirmed compromise.
- **Google refresh tokens:** Google generally returns the same refresh token and may not rotate it [UNVERIFIED]; if a new one is returned, store it in the same transaction and discard the old one.
- Rotation drills run in staging before pilot (control C-10).

### 1.5 Backup and key loss

- Database backups contain ciphertext only; they are useless without the KEK. That separation is intentional: **KEK backups are held apart from database backups** (different store, different credentials, at least two named custodians, sealed offline copy for the pilot). A restore drill must include restoring a KEK and decrypting a test row (production-readiness-auditor evidence, not yet run).
- **Loss of a KEK (or restore without it):** all affected refresh tokens are unrecoverable. This is a **degradation, not a data loss**: no student content lived in the token table. Behaviour: connections show `needs_reauth` with the calm reconnect prompt; sync and export stop; students sign in with Google and reconnect; content already written to Google is untouched. Reconnect should re-adopt prior Tether-created files and calendars via stored remote ids and `appProperties` markers if Google lets the same client regain access after re-consent **[VERIFY: drive.file and calendar.app.created behaviour after revoke and re-consent]**. Loss of the HMAC root secret: idempotency replays within 24 h return 400, ETags invalidate (clients refetch), old audit HMACs become unverifiable (mitigated by keeping the previous key in the backup).
- Never log, print or return key material or `kek_id` plus ciphertext together in error bodies.

### 1.6 Per-environment configuration

| Env | Token KEK | Root secret | OAuth client | Notes |
|---|---|---|---|---|
| Development | generated locally, user-secrets | generated locally | none, or a dev client in Testing status with the PO's throwaway account | `ITokenCipher` fake allowed only with `Providers:Google:Mode=Fake` and a startup warning |
| Test/CI | fixed key in test code only | fixed in test code | none; fake Google server | Host refuses test keys when env is not `Test` |
| Pilot/staging | secret-store KEK (option B), distinct from prod | distinct | separate Google Cloud project/client, Testing status, listed test users | 7-day refresh expiry in Testing is expected [per google-calendar-integration.md section 3] |
| Production | KMS/vault KEK, rotation enabled, access limited to the runtime identity | distinct | separate verified client | KEK and database credentials held by different principals |

Secret-store access is by managed identity (no static credential in config or CI variables). CI has no production secrets.

---

## 2. Token storage, scopes, consent and connection handling

### 2.1 Schema requirements (extends section 6 of the calendar proposal; coordinate with `database-architect`)

`integrations.google_connections`: `id`, `user_id` (FK, cascade on user purge), `google_sub` (**unique across all users**, so one Google account cannot be linked to two Tether users), `google_email_masked` (display only), `refresh_token_ct` (format above), `kek_id`, `status` (`connected|needs_reauth|revoked|admin_blocked|disconnecting`), `connected_at`, `last_refresh_ok_at`, `last_error_code`, `revoked_at`. **Unique (user_id)** enforces decision (a). No access token column; access tokens live in memory with an expiry margin and are never written to disk, logs or caches shared across users.

`integrations.google_grants`: `connection_id`, `product` (`calendar|drive_files`), `scope`, `granted_at`, `withdrawn_at`, `consent_text_version`. One row per product so consent is recorded separately and shows evidence of what the student agreed to, in the same spirit as ADR-008 (append-only consent events; add `grant|withdraw` rows to the existing consent evidence stream rather than a new mechanism, proposal).

`integrations.oauth_states`: `state_hash`, `user_id`, `session_id`, `purpose` (`connect_calendar|connect_drive`), `requested_scopes`, `pkce_verifier_ct`, `nonce_hash`, `expires_at` (10 min), `consumed_at` (single use), cleanup job. Never store the raw state.

`integrations.remote_objects` (or per-module mapping tables): `user_id`, `kind` (`calendar|event|folder|doc`), `remote_id`, `local_ref`, `content_fingerprint`, `remote_revision`, `status` (`active|deleted_remotely|edited_remotely|detached`), timestamps. This is the idempotency and ownership record (stays in Postgres, section 3.2).

Token columns are excluded from export, backups of exports, ProblemDetails, logs, traces and metrics (SEC-027). Add them to the log-canary test (B1-02 is still open and applies here).

### 2.2 Scope minimisation (proposal; classification of each scope is [UNVERIFIED], check the Cloud Console picker)

| Product | Request | Rationale | Reject |
|---|---|---|---|
| Sign-in | `openid email profile` only | PO-6, backend-foundation | any Drive/Calendar scope at sign-in |
| Calendar | `calendar.app.created` | Tether can only touch calendars it created; supports decision (c) because we never see the student's other events | `calendar`, `calendar.events`, any `*.readonly` |
| Docs/Drive | `drive.file` | Access only to files the app created (or the user explicitly opens with it); enough to create a Tether folder and Docs and to edit/delete only those files | `drive`, `drive.readonly`, `documents` (full Docs scope reaches all Docs), `drive.metadata*` |

Open technical points **[VERIFY]**: whether `drive.file` alone allows `documents.batchUpdate` on files the app created via the Docs API or Drive API; whether it can create a folder and place files in it; whether files stay accessible to the same client after revoke and re-consent; whether `files.generateIds` gives idempotent create; how `appProperties` (visible only to our client) behave. If `drive.file` proves insufficient, escalate to the PO rather than widening silently.

A scope allowlist test (backend) fails the build if any other scope string appears in code or config. Changing a scope list is a security-reviewed change.

### 2.3 Incremental consent and separate consent per product

- Sign-in is its own authorization request. Calendar and Drive are **separate, student-initiated actions** ("Connect Google Calendar", "Save notes to Google Docs"), each with its own plain-language pre-screen, each recorded as its own grant. Requesting both at once is not allowed even though Google could batch them.
- Google issues one refresh token per client and account, covering all granted scopes **[UNVERIFIED]**. Consequence: the revocation endpoint revokes all products together. A "disconnect only Docs" action therefore does one of two things, which must be explicit in the UI: (1) local withdrawal (Tether stops using the Docs scope and records `withdrawn_at`, but Google still lists the grant until the student removes it in their Google account), or (2) full revoke then re-authorize the remaining product. Recommend (1) for the pilot plus a link to the Google permissions page, and say so honestly.
- `include_granted_scopes=true`, `access_type=offline`; `prompt=consent` only when no refresh token is held. On callback, compare the returned `scope` to the requested set; treat missing scope as "not connected for that product", and treat any scope outside the allowlist as an error (log scope names, never tokens).

### 2.4 One Google account per user, and sign-in vs data connection

- Decisions (a) and PO-9d (Google OIDC sign-in is the identity; the data account must be the same account) change calendar proposal section 2 ("account may differ from sign-in") and Q7. New rule: **one `google_sub` per user, unique across users, and it is the user's identity key.** The data connection must return the same `sub` as the sign-in (verified from the ID token; `login_hint` is a convenience, not a control). See 2.7 for the full flow.
- Sign-in and data connection use separate request flows and separate stored rows. A sign-in session never carries Google API tokens; signing out of Tether does not revoke the connection (the student disconnects deliberately) and revoking the connection does not sign them out. Using a separate OAuth client for data scopes is optional; separate flows and consent screens are mandatory.
- A university-managed Google Workspace account (.edu) is a special case: see 3.6.

### 2.7 Google OIDC sign-in as identity (PO-9d; proposal)

Flow:
1. **Sign-in** (authorization code + PKCE S256, `openid email profile`, state and nonce as in 2.5). Identity key is `iss=https://accounts.google.com` + `sub`, **never the email** (emails change and can be reused). `identity.external_logins(provider, subject)` unique; users are created or found by `sub`. Verify ID token `iss`, `aud`, `exp`, `nonce`; use `email_verified` only for display. No data scopes at sign-in, so a student can use Tether (and fully review/delete) without ever granting Calendar or Drive.
2. **Data connect** (later, per product, 2.3): request carries the session; the callback's ID token `sub` must equal the session user's `sub`. Mismatch: abort before storing any token, call Google revoke on the just-issued token (best effort), create nothing, show "That is a different Google account than the one you signed in with. Choose the same account to continue." A user cannot use another account's Drive, and an attacker's account cannot be linked into a victim's session (state is session-bound; connect is POST).
3. **Different account wanted:** that is a different Tether identity. Offer sign out, then sign in with the other account; never merge two Google accounts into one user (merging would enable account takeover and break unique `google_sub`). Content already written to the first account stays there.
4. **Re-authentication:** `needs_reauth` is resolved by running the Google sign-in again (login hint = stored email, `sub` re-checked), which also refreshes the data grants incrementally.

Account closed, suspended, deleted, or institution-blocked:
- Google provides no push signal [UNVERIFIED]; Tether learns on refresh or sign-in failure (`invalid_grant`, `admin_policy_enforced`, `access_denied`). Tether: sets the connection `needs_reauth`/`admin_blocked`, stops writes, keeps the working copy under normal retention (PO-5), and shows a plain notice that content in Google may be inaccessible and that Tether still holds the working copy until retention ends.
- **The student can no longer sign in to Tether at all**, because Google is the only identity. This is the real cost of PO-9d. Required before pilot: an **account-recovery/export path that does not depend on Google sign-in**, for example a time-limited export requested via support after identity verification handled by the PO, or a documented decision that the data is inaccessible. Needs PO decision (Q7). Do not add email/password or magic-link as a fallback without a security review (new credential surface).
- Deleted Google account: `sub` is not reused by Google [UNVERIFIED]; the Tether user remains orphaned; a purge job (retention clock, PO-5) deletes it with notice impossible. Document that deletion in this state is by retention expiry or support request. Institution accounts removed at graduation: same outcome, and content in that Drive may also be removed (see 3.6, GR-1), so a student leaving school loses both copies once retention expires. Warn Workspace users at connect time.
- Suspended/blocked sessions already in Tether are ended by the existing server-side session revoke (ADR-016) when the user is deactivated by support.

Replacing dev login:
- Production, staging and pilot: Google OIDC only. The dev-auth scheme must be unreachable outside `Development`: registration compiled under a startup guard that throws if enabled when the environment is not `Development` and a test asserts `dev auth disabled outside Development` (already listed in backend-foundation test plan; keep it as a release gate). Setting the environment name to `Test` must not enable it (compare B1-13).
- Existing dev users: dev-login users have no `sub`. Proposal: dev users are never migrated; production database starts with Google identities only. For a pilot database that already has dev users, a one-time, PO-approved claim step is needed (sign in with Google, claim by an operator-issued single-use code) rather than matching by email.
- Development without internet: a Fake `IGoogleIdentityProvider` (`Providers:Google:Mode=Fake`) that issues ID tokens signed by a local test key, with the startup warning and `/health/ready` `mode: fake`; contract tests run against it. A real Google OIDC login in dev needs a Cloud OAuth client in Testing status (PO approval, section 5 #15).
- Identity module today uses `UnconfiguredGoogleIdentityProvider`; real sign-in is blocked on PO-6 (client creation, project ownership) and domain/redirect URIs per environment.
- Cookie session remains the first-party `__Host-cc.session` with server-side sessions; Google tokens are not the session. Short access-token lifetimes do not log the student out.

Added tests: `sub` is the identity key (email change does not change user); email collision does not link users; mismatch aborts and revokes; two users cannot hold one `sub`; sign-in works with zero data grants; dev auth fails startup outside Development; fake provider refuses non-Development; deactivated user's sessions rejected.

### 2.5 CSRF, state, PKCE, redirect

- Connect starts with **POST** (antiforgery header plus Origin check; closes B1-09-style gaps for this route), never a GET link, to prevent forced linking.
- `state`: 256-bit random, hashed at rest, bound to session id, user id, purpose and requested scopes, single use, 10 minute expiry. Callback requires the authenticated Tether session cookie and a matching state row; otherwise 400 with no detail and no token exchange.
- PKCE S256 with a server-held verifier; `nonce` in the ID token verified; ID token verified for `iss`, `aud`, `exp`, `sub`. Exact-match redirect URIs per environment over HTTPS; no wildcard or open redirect (post-callback redirect target is a server-side allowlisted path, not a request parameter).
- Callback performs the code exchange and stores the token only. **No Google write happens at callback time.** The browser receives only a success/failure redirect.
- Outbound Google calls go only to a fixed host allowlist (`accounts.google.com`, `oauth2.googleapis.com`, `www.googleapis.com`, `docs.googleapis.com` etc.); no user-supplied URLs reach the HTTP client (SSRF). `Authorization` headers are redacted in the HttpClient handler.

### 2.6 Token theft, revocation and disconnect

| Threat | Control |
|---|---|
| DB dump or backup theft | Ciphertext only; KEK held elsewhere; AAD binding |
| Compromised app host | Residual risk: runtime can decrypt. Limit blast radius with `drive.file` and `calendar.app.created` (token can only reach what Tether made), short-lived access tokens, per-user rate limits, alerting on refresh-failure spikes and on unusual per-user Google call volume |
| Tokens in logs/AI | SEC-027 canary tests; `Tether.Ai` has no reference to the Integrations module; no method returns a token outside the HTTP handler (invariant 3) |
| Cross-user use | Every Google call resolves the connection by `user_id` from the session, never from request input; test that user B cannot trigger writes with A's connection (404) |
| Stale/forged callback | State and PKCE above |
| Admin or Google-side revocation | `invalid_grant`/`admin_policy_enforced` moves status to `needs_reauth`/`admin_blocked`; no retry loops; calm UI |

Disconnect (student-initiated, Settings): (1) ask what to do with content Tether created in Google (default keep, matching decision (d); options to remove calendar and/or the Tether Drive folder), executing removals **before** revocation because they need a valid token; (2) call Google revocation with the refresh token; (3) delete the ciphertext row regardless of the revocation result; (4) set mapping rows to `detached`, record audit entry (ids, counts, no secrets). Account deletion runs the same path and, if the token is lost, still deletes the row. Failures are visible to the student, not silent.

---

## 3. Threat model: Google as "store of record"

### 3.1 Framing

Making Google the *only* store is not workable: live transcription, TL;DR revisions, "I Lost Track" retrieval (<5 s), idempotency, ownership checks and AI grounding all need low-latency server-side data and cannot depend on Docs API latency, quotas or student edits. Proposal: **Postgres is the operational system of record; Google is the student's durable, student-owned copy of everything Tether creates for them, written with their approval.** This satisfies (d) (content survives a Tether outage) without making Tether's correctness depend on Google. Needs PO confirmation, and CLAUDE.md wording (section 4).

### 3.2 What lives where (proposal)

| Data | Postgres | Google |
|---|---|---|
| OAuth refresh token ciphertext, state | yes | no |
| Ownership, course/session ids, authorization | yes | no |
| Idempotency keys, sync runs/intents, remote ids, fingerprints, `appProperties` mapping | yes | markers only (no PII) |
| Consent and grant evidence, audit log (ids/counts, no secrets, no content) | yes | no |
| Raw lecture transcript segments (source) | yes, short retention per PO-5 | optional, see 3.3 |
| Summaries, TL;DR, catch-ups, notes, study guides (derived) | working copy, revision history | final or checkpointed Docs |
| Syllabus-derived events | `calendar_events` mapping rows | events on the Tether calendar |
| Knowledge estimates, mastery signals, quiz answers | yes | **not by default** (PO question Q3) |
| Audio | none (PO-5 proposal) | never |

### 3.3 Raw transcripts (invariants 5 and 10)

Transcripts are third-party voice content (instructor, classmates), the most sensitive thing Tether holds. Proposal:
- Source transcript segments stay in their own tables with their own retention; derived summaries/notes are separate (invariant 5, SEC-013). In Google, **transcripts are never merged into a notes or study-guide Doc**: separate Doc, separate folder entry, clearly titled.
- **No automatic transcript upload.** Writing the transcript to Google is an explicit per-session student action after the lecture ("Save the transcript to your Google Drive?"), default off, because it moves third-party voices outside Tether's retention and deletion reach. If the PO wants (d) to cover transcripts by default, that is a PO decision after the legal review in 3.7, and the default still needs a visible choice.
- Derived Docs carry source timestamps as plain text (for example `[00:12:30]`) so provenance survives without Tether. Links back to Tether would die in an outage; do not depend on them.
- Invariant 10: Google API request and response bodies, Doc text, titles built from lecture content and Drive file names are never logged. Drive/Doc file titles are built from course name, date and fixed words, not from transcript text, because titles also appear in Google activity feeds, notifications and share dialogs. Add a log-canary test that exercises the Docs adapter with canary text.
- Limited-use obligations of the Google API Services User Data Policy [UNVERIFIED]: if Tether ever *reads* Doc/Drive content back (for example to detect student edits or to feed Claude), that is use of Google user data and must be disclosed and kept within policy. Default: Tether reads only metadata (revision id, modified time) of files it created, never body text; sending Doc body text to Claude is not allowed without a separate PO and legal decision.

### 3.4 Writing and drift (extends decision (c))

- Layout: one private Tether root folder, one subfolder per course, Docs inside, created by the app. Tether **never sets sharing, permissions or link access**; new files are private to the student. (Test: no `permissions.create` call exists in the adapter; allowlisted API methods only.)
- Writes go through the outbox with deterministic, resumable steps (plan, execute, record), the same idempotency pattern as calendar. Create uses a marker in `appProperties` plus a stored remote id; retries find an existing file by marker before creating.
- **Student edits and deletes are respected** (decision (c) generalized): if a Doc was modified by the student since Tether's last write (compare `modifiedTime`/revision id and the stored fingerprint) Tether does not overwrite; it writes the new version as a new section or a new dated Doc and tells the student. If the student deletes or trashes a Doc, Tether records `deleted_remotely`, stops managing it, never recreates silently. A calendar event deleted in Google is likewise left alone, mapping marked `deleted_remotely`.
- The Tether copy is not retroactively changed by student edits in Google (one-way write). Say this in the UI; two-way sync is out of scope.

### 3.5 Revocation, deletion, export semantics

| Action | In Tether | In Google | Notes |
|---|---|---|---|
| Student revokes access (Google account page or Tether) | Connection `revoked`/`needs_reauth`; sync/export stop; working copy still follows PO-5 retention | Everything already written stays, owned by the student | **Student loses:** future updates to Docs/events, Tether-initiated cleanup, ability to re-adopt files if re-consent does not restore access [VERIFY], and any Tether-only artifacts (live view, search, AI help over history once retention purges) |
| Student deletes a Doc/event in Google | `deleted_remotely`, left alone | gone (Drive trash 30 days is Google's behaviour [UNVERIFIED]) | No recreate without explicit confirm |
| Student deletes content in Tether (session, course, account) | Hard delete within SEC-014 SLA (<=30 days incl. backups) | **Copies in Google remain unless the student also chooses to remove them** while a valid token exists | New: the delete dialog must offer "Also remove what Tether saved to Google" (default: keep, per (d)), executed before revocation; if the token is gone, say plainly "We can no longer remove copies in your Google account; you can delete them there" |
| Account deletion | Same path as above; delete token row regardless of revocation outcome | Same choice offered; never forced | SEC-014 wording must be amended (section 4) |
| Retention purge (180-day proposal) | Purges transcripts and working copies in Postgres | Google copies are not purged automatically | Disclose: Tether's retention does not govern the student's own Google copies |
| Export (Tether side) | Includes mapping/links and derived content per existing rules; **excludes tokens**; may point to Google file ids | n/a | Export is largely satisfied by the Google copies; keep the export endpoint for non-mirrored data |

Our purge obligation in plain terms (proposal, for legal to confirm): Tether purges data it holds; once a student has directed Tether to place a copy in their own account, that copy is theirs. Tether must (1) tell them this before the first write, (2) offer deletion of Google copies while it can, (3) retain mapping rows only as long as needed and delete them on account deletion, and (4) never keep a content copy elsewhere to "repair" Google.

### 3.6 Who else can see Google-stored content

- **Sharing is the student's act.** If the student shares a Doc or folder, recipients see the content including any third-party voices and any AI-generated study content; Tether cannot see or control that after the fact. Study guides containing knowledge-weakness notes are sensitive if shared; UI copy warns before the first write and Docs include a plain footer ("Created by Tether; check before sharing") [PO/UX call].
- **University-managed Workspace accounts:** the institution's administrators may access, retain, export, search (eDiscovery) or delete student Drive content, may block the app (`admin_blocked`), and the account and content may be removed at graduation or departure. That undermines "content survives" and exposes lecture transcripts to the school. Recommendation: detect a Workspace account (the `hd` claim [VERIFY]) and show a clear notice, allowing the student to choose a personal account (consistent with one account per user) or proceed informed. Needs PO and legal input.
- Google processes content under its own terms; Tether must disclose Google as a recipient in its privacy notice.
- Calendar events are visible to anyone the student shares the Tether calendar with; event descriptions stay minimal (no snippets, no instructor contact details), per the calendar proposal.
- Drive file names and Doc titles can surface in notifications, search suggestions and sharing emails: keep them free of lecture content and personal data beyond course/date.

### 3.7 Hand-off to `legal-compliance-reviewer` (questions, not conclusions)

1. Are student-created notes and AI study guides held by Tether, and copies in Drive, education records under FERPA, and does Tether act as a school official/service provider in any pilot arrangement? (Applicability unknown; do not assert.)
2. Is copying a lecture transcript containing the instructor's and classmates' speech into the student's personal or school-managed Google account covered by the recording consent and campus recording policy, or does it need its own notice? Interaction with PO-2 and G1.
3. Controller/processor roles and notice for Google as a recipient; Google API Services User Data Policy limited-use and verification obligations.
4. Deletion duty when a copy has been placed in the student's own account; wording for SEC-014 and PO-5.
5. Minors and PO-7 age rules versus Google account terms; school-managed account use of third-party apps.
6. Cross-border location of Google and Tether storage.
7. Retention of consent/audit evidence versus deletion rights.

### 3.8 Tether outage behaviour

- Existing Docs, folders and events remain in Google and stay editable; calendar reminders (if the student opted in) keep firing because they live in Google.
- Lost during an outage: live transcription, new summaries/catch-ups, new syncs, "I Lost Track", Tether search and provenance links. Pending outbox writes resume when the service returns (idempotent). Content captured but not yet written is at risk only if Tether's data is also lost; checkpoint writes (for example at session end and on each summary revision milestone) limit that window. Be honest in UI copy: "Saved to Google at <time>".
- Tether total loss (database destroyed): student content in Google is intact; mapping is rebuildable only if `appProperties` markers and folder structure survive **[VERIFY]**. Tokens are unrecoverable without backup, so students reconnect.
- Google outage or quota exhaustion: Tether keeps working locally, marks writes `retry_later`, backs off per calendar proposal, never blocks recording, review or reading on Google health.

### 3.9 Residual risks (new)

| ID | Risk | Rating (proposed) |
|---|---|---|
| GR-1 | Transcript or study-guide content copied into an institution-managed account becomes visible to the institution | High until PO/legal decide |
| GR-2 | Deletion in Tether does not remove Google copies; users may assume it does | Medium; mitigated by dialogs and disclosure |
| GR-3 | Token compromise on a compromised host reaches the student's Tether-created files and calendar only (scope-limited) | Medium |
| GR-4 | KEK loss forces mass reconnect | Low impact, tested drill required |
| GR-5 | `drive.file` capability assumptions wrong, forcing a broader scope | Medium until [VERIFY] done; broadening needs PO approval |
| GR-6 | Student shares a Doc containing third-party voices or learning-weakness notes | Medium; student-controlled |
| GR-7 | Unverified OAuth app limits to test users with 7-day refresh expiry; not usable for real students | Blocking for real use, not for design |

---

## 4. Invariant conflicts and proposed amendments (wording only; CLAUDE.md not edited)

| Item | Conflict | Proposed wording |
|---|---|---|
| Invariant 5 (store source transcripts separately) | Not violated, but must extend to Google | "...Source transcripts and derived summaries are separate in Tether **and in any Google file Tether creates**; transcripts are never merged into a notes or study-guide document." |
| Invariant 8 (deletion/export controls) | Deletion cannot reach copies once a token is gone | "...deletion controls state exactly what is removed from Tether and, while a valid grant exists, offer removal of copies Tether placed in the student's Google account; copies in the student's account are otherwise the student's." |
| Invariant 10 (do not log raw lectures) | Fine; add Google content | "...or log lecture text, Google API request/response bodies, or Google token material." |
| Invariant 4 (calendar writes) | Fine; extend to Docs | "Syllabus dates are reviewed by the student before Calendar writes; **Calendar and Docs/Drive writes are student-approved, idempotent, never set sharing, and respect student edits and deletions in Google.**" |
| New invariant 11 (proposal) | None exists on residency | "Postgres is the operational system of record. Content Tether creates for a student is written, with their approval, to their own Google account so it survives a Tether outage; Tether holds no content the student cannot also get in Google or by export, and never alters sharing." |
| Integrations stack line | Scopes | "Google OAuth 2.0 with separate sign-in and per-product incremental consent: `calendar.app.created` and `drive.file` only unless the Product Owner approves broader scopes; one Google account per user." |
| SEC-014 / PO-5 | "Hard-deletes ... content" is not true of Google copies | Add: "...excluding copies the student directed Tether to place in their own Google account, which Tether offers to remove while a valid grant exists." Retention disclosure must say Tether retention does not govern Google copies. |
| ADR-012 | Docs mock-only decision | Supersede with an ADR once Docs contract exists (`docs.v1`); mock stays for Development/Test. |
| calendar proposal Q7, section 2 | "account may differ from sign-in" | Superseded by PO-9d: Google OIDC is the identity; data account must have the same `sub`. |
| backend-foundation "dev auth" in Identity | Dev login is an identity path | Dev auth is Development-only and guarded; Google OIDC is the only production identity (section 2.7). Update ADR-016/identity docs. |
| calendar proposal Q8 (disconnect default) | Open | Resolved by (d): default keep content; removal is an explicit option. |
| Gate register | G1 and the Google-recipient review | No change: remain BLOCKED/open; PO assumption of clearance is not evidence. |

---

## 5. Ordered controls and tests before any real Google call

"PO" = Product Owner approval required. Items 1 to 12 are local/fake and fit "approved safe work while gates are open".

| # | Control / test | Owner | PO? |
|---|---|---|---|
| 1 | Resolve [VERIFY] list (scope capability of `drive.file`/`calendar.app.created`, ID idempotency, revoke endpoint, re-consent access, Workspace `hd`, limited-use policy) from dated primary sources | vendor-verification-researcher | No |
| 2 | Confirm section 3.1 model and decisions Q1-Q5 below; ADRs for `docs.v1`/`calendar.v1`, new invariant 11 | Lead Architect | **Yes** |
| 3 | `ITokenCipher` port, AES-256-GCM envelope, AAD, key ids; tests: round trip, tamper fails, wrong user/connection AAD fails, wrong `kek_id` fails, DB column holds no plaintext, rotation re-wrap job, loss of KEK yields `needs_reauth` not a crash | backend-engineer, security-engineer | No |
| 4 | Key startup guards (B1-11): fail on missing/short/sample keys outside Development; HKDF labels; test-key refusal; readiness reports key ids | backend-engineer, devops-engineer | No |
| 5 | Schema migrations (section 2.1) incl. unique `user_id` and unique `google_sub`; tests for both uniqueness rules | database-architect | No |
| 6 | State/PKCE/nonce/redirect tests: tamper, replay, expiry, cross-session, cross-user, wrong redirect, `error=access_denied`, partial scope, extra scope, `sub` mismatch against sign-in | google-integration, qa-engineer | No |
| 7 | CSRF/Origin on connect (POST + antiforgery); GET connect rejected; name the CSRF header (B1-09) | backend-engineer | No |
| 8 | Scope allowlist test; adapter method allowlist (no `permissions.*`, no `files.list` over the whole Drive, no body reads); host allowlist for outbound HTTP | google-integration, security-engineer | No |
| 9 | Log and AI canaries: tokens, Doc text and titles absent from all log sinks including Npgsql/EF categories (closes B1-02 for this surface) and from prompts; architecture test: `Tether.Ai` cannot reference Integrations | qa-engineer | No |
| 10 | Fake Google server tests: all failure codes, `invalid_grant`, `admin_blocked`, crash between execute and record, no duplicates on replay, student-edit and student-delete left alone, disconnect order (remove then revoke), revoke failure still deletes the row | google-integration, qa-engineer | No |
| 11 | Delete/export semantics tests: delete dialog options, token-gone message, export excludes tokens, account deletion runs disconnect, scan test for token strings in every schema | backend-engineer, qa-engineer | No |
| 12 | Audit events for connect, grant, withdraw, sync, remove, disconnect (ids and counts only); coalescing and retention decision (B1-04); fix B1-01 deadlock retry for the deletion path before relying on it | backend-engineer | Audit retention: **Yes** |
| 13 | Secret store, KEK custody and two-custodian backup procedure; restore drill that includes a KEK; rotation drill in staging | devops-engineer, production-readiness-auditor | Vendor/cost choice for KMS/vault: **Yes** |
| 14 | Legal review (section 3.7), consent/notice text, privacy policy naming Google as recipient, Workspace-account notice | legal-compliance-reviewer, counsel | **Yes** |
| 15 | Google Cloud project, OAuth client in Testing status, throwaway test account, test users | Product Owner | **Yes** |
| 16 | Real-account smoke test per calendar proposal section 8.5 plus Docs/Drive steps (create, no overwrite after edit, trash respected, revoke and re-consent behaviour), evidence in docs/qa | google-integration, qa-engineer | **Yes** (external write) |
| 17 | OAuth verification submission, domain, privacy policy; classification of sensitive scopes | Product Owner, google-integration | **Yes** |
| 18 | Independent security re-review of implemented code; device/screen-reader QA of connect, consent and delete dialogs | security-engineer, device-accessibility-validation | No |
| 19 | Production go/no-go from the gate register | Product Owner | **Yes** |

### PO questions arising from this design

1. Q1: Confirm "Postgres operational, Google durable copy" (3.1) versus a stricter reading of "Google is the only store".
2. Q2: Transcript upload to Google: explicit per-session opt-in (recommended) or default on after legal review?
3. Q3: Are knowledge estimates, quiz results and mastery signals "created content" for (d)? Recommendation: not mirrored by default.
4. Q4: University-managed accounts: warn and allow, or require a personal Google account for Docs/Drive?
5. Q5: Token KEK custody for the pilot: secret-store KEK (recommended, no cost) versus Data Protection with certificate; and later KMS choice and spend.
6. Q6: Delete-in-Tether default for Google copies: keep (recommended, per (d)) with an explicit remove option.
7. Q7 (from PO-9d): if a student's Google account is closed or suspended they cannot sign in. Accept that, or fund a support-mediated export/recovery path with PO-run identity checks? No password fallback is proposed.

---

## 6. Summary

1. Recommend envelope encryption (AES-256-GCM, per-row DEK, versioned KEK) behind `ITokenCipher`: secret-store KEK for the pilot, KMS-held KEK for production; Data Protection stays for cookies only; KEK backed up apart from DB backups.
2. Replace random per-process keys with one root secret per environment plus HKDF labels; startup fails outside Development when any key is missing (closes B1-11); KEK loss degrades to reconnect, not content loss.
3. Scopes: sign-in `openid email profile`; Calendar `calendar.app.created`; Docs/Drive `drive.file`; separate incremental consent per product; one Google `sub` per user, unique across users, same as sign-in; POST-initiated connect with state, PKCE, nonce and exact redirect.
4. Model: Postgres stays the operational record, Google holds the student's durable copy; Tether never sets sharing, respects student edits/deletes, no automatic transcript upload, Tether retention and deletion do not reach Google copies (disclose; offer removal while a grant exists).
5. Biggest risks: transcripts landing in institution-managed accounts (GR-1), deletion expectation gap (GR-2), unverified `drive.file` capability (GR-5); CLAUDE.md needs amendments to invariants 4, 5, 8, 10 plus a new invariant 11 (section 4).
6. PO-9d folded in (2.7): Google `sub` is the identity key, a mismatched data account aborts and revokes, account closure locks the student out (Q7 needs a PO decision), dev login is Development-only and replaced by Google OIDC (or a Fake provider locally).
7. Everything is a proposal and [UNVERIFIED]; no gate is passed; no real Google call may happen before controls 1-14 and PO approvals at 2, 12-17, 19.
