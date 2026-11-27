# Cross-critique of the Opus sign-in proposal (senior-engineer-fable)

Date: 2026-11-27. Step 2 of the pairing protocol. Read: `go-live-signin-opus.md`, `docs/product/go-live-stories.md` (S1, S4), `docs/architecture/account-export-delete.md` section 9 (ADR-021, proposed). **Nothing was built or run; no call to Google or Azure.** **[V]** = checked in this repository's code or docs today. **[A]** = assumed. "Mine" = `go-live-signin-fable.md`.

The two proposals agree on most of the design: `auth.v1` lifted from calendar.v1, hand-written flow with a library-checked ID token, redirect address from configuration, two invite lists, the email-index migration, key ring in PostgreSQL, Production with Google behind Entra first. The rest of this note is about the differences.

## 1. Where Opus is better and I switch

1. **Return path.** ADR-021 dependency (1) needs a safe same-origin return so "sign in again" lands back on the delete step. Mine had none. Take Opus's `returnPath`, held in the state row and never read from the callback query, with two changes (section 3, item 5).
2. **Invite file outside the repository** (`%USERPROFILE%\.tether\...`). Mine put it in `secrets\` inside the working tree, where agents working in the repository could read invitees' addresses. Opus's place is right.
3. **One guided PO script** that prints the exact redirect address instead of asking the PO to build it by hand. Keep my `-ClientFile` as an option (item 8 below).
4. **Extra outside checks:** the callback with a bogus state must answer 302 to `/signin?...outcome=state_invalid` (this proves the 302 through the proxy in Azure, which I could only prove locally), binder cookie attributes, HSTS. Also the Azure-side read that the Easy Auth state equals what was asked for.
5. **Separate HKDF labels** for nonce and verifier, a **global cap on live state rows**, the **`HttpClient` log category filter** for the Google client (not present today [V: Program.cs]), and the alg-confusion test signed with the *public key*.
6. **A persistent gate setting.** My workflow input defaulted to `team`, so a routine deploy with the default would quietly put Entra back. Converge: a stored setting plus an input that overrides it (section 4g).
7. **`google+ip` gate.** I dropped the IP layer; it is what makes the rollback rehearsal possible (section 4g).
8. Endpoint name `GET /auth/methods`, full `Auth:Google:RedirectUri` as the one configured value. No reason to differ.

## 2. Where mine is better, and why

1. **No Development branch in the Azure template.** Opus has `apiEnv = googleOn ? 'Production' : 'Development'` [V: his section 7]. A deleted or mistyped GitHub secret then turns a site holding real accounts back into a dev-login site. In mine the API is always Production and a missing Google value fails preflight.
2. **A person between the two runs, enforced.** Run B refuses to start unless Azure already shows a Production API (`api-signin-mode.sh`). Opus relies on run order.
3. **No outage at cut-over.** Opus's Run B makes the web internal, then public again. S4 item 3 asks for "one change, not two steps with a gap". Mine flips Easy Auth off in place, safe because the API is already Production and checked.
4. **`closed` as an explicit emergency mode**, and rollback as one workflow run rather than a script plus a run.
5. **Smoke order and strictness:** the dev-login probe runs first; 400 or 429 on it also count as a breach (the route exists).
6. **Dev login looks its user up by `dev:` subject, stated as a required change.** Today it looks up by email only [V: IdentityEndpoints.cs]. Opus's test list implies the change but his text does not say it; see defect 1.
7. **Limits from the reviewed policy** (300 a minute shared, 10 a minute per binder [V: calendar-policy.json]) instead of 30 a minute shared (section 4d).
8. **Warning when the invite list has more than one entry** while export and delete are unbuilt (PO-20ax).

## 3. Defects and risks in Opus's proposal

1. **Rollback "back to dev login" is an account-takeover path.** [V] The released dev login finds the user by email and signs in as that row. Once the unique email index is gone and Google users exist, an older image, or a new one whose lookup was not changed, lets anyone past Entra become any Google user by typing their address. No rollback may put dev login in front of a database that holds Google users.
2. **The "no parameter combination" claim covers end states, not transitions.** [A: ARM gives no order between the two container apps in one deployment.] Going from `google` back to Development changes the API mode and the web gate in one deployment; for a moment the web can be public with no Easy Auth while the API already offers dev login.
3. **Missing secret means Development** (section 2 item 1). [V in his text.] Fail-open in the direction that matters.
4. **`SessionPolicy` gains a provider check on every request.** [V] `SessionPolicy` is static, has two overloads (cookie path, hub and socket path) and no environment today. This is more surface than a one-off revoke, and not needed with section 4f.
5. **Return-path route list answered with 400.** [A] The API would carry a copy of the web's routes; a new page missing from it makes sign-in from that page fail outright. Use a structural check (one leading `/`, no `//`, no `\`, no control characters, decoded once and re-checked, 200 characters, not under `/api`, `/hubs`, `/ingest`) and fall back to `/`. Also do not append `?purpose=&outcome=signed_in` on success: every deep page would have to strip it.
6. **Shared start limit of 30 a minute** fails S1 item 16 in practice: three people retrying use it up. [V: every caller is one address.]
7. **Run A expectation "a non-test account sees `access_denied`".** [A] Google most likely stops that person on its own page and never returns. Expect "stays at Google; Tether creates nothing".
8. **`last_sign_in_at`** is not needed: ADR-021's recent-sign-in rule can read `sessions.created_at` [V: Session.cs]. `provider` as a column needs a backfill; the `google:` / `dev:` prefix needs none and the unique index already exists [V].
9. **The script needs `az login` as the PO just to print an address.** [V: PROGRESS iteration 13 records that this sign-in dance failed once from cmd.] Fall back to asking for the Application Url and appending the path.
10. **Pasting the client secret** means creating a new secret, since the console no longer shows the old one [V: vendor report], and then deleting the old one. Reading the kept client file avoids that. Support both.
11. **School accounts:** Opus stores the `hd` flag and refuses nothing, which contradicts S1 item 7. Mine has the same defect (section 4e).

Defects in mine that this round exposed: no return path; invite file inside the repo; `hd` not refused; Run B could ride along with new API code if `main` moved after Run A (fix: Run B requires the live API image to be this commit's).

## 4. Position on the Lead's points

**(a) PO burden: one command, no full deploy.** Pick: `invite add <address>` / `invite remove <address>`, a PO script that (1) updates the PO's local list, (2) rewrites the GitHub secret, (3) starts a small workflow "Apply invite list" that only copies that secret into the API's Container Apps secret and restarts the API. No image build, no Bicep, about two minutes. The PO still presses Approve: the Azure identity is trusted only through the `azure-dev` environment, which requires his review [V: workflow comment], and that matches PO-20cg. The Google console step stays manual [A: no supported way to script test users].
Rejected: a database list written by a PO script (the database accepts connections only from inside Azure and nobody can read its password [V: main.bicep]; getting the PO's machine in means opening the firewall and pulling the admin password to a workstation); the script calling `az` as the PO (truly one step, but an Azure change outside the reviewed pipeline, by the Owner account); an owner-only invite page in the app (best for the PO, but the stories put "roles or admin screens" out of scope [V]; raise it with the PO as the next step).
Risks: the restart drops live connections for about a minute (a lecture in progress pauses itself [V: infra/README]; sign-ins survive through the key ring); the local file is the only readable copy of the list; if the run is not approved the person is simply not in yet (fail closed); removal takes effect at that restart through the start-up revoke. ADR-021 dependency (4): erasure cannot remove an invite held outside the database, so the delete page should say "ask the person who invited you" (stories question 5).

**(b) Key ring: `ProtectKeysWithCertificate`; I withdraw the hand-written wrapper.** Keys in PostgreSQL through the EF Core store, protected by a certificate that a PO script generates in memory and sends straight to a GitHub secret (the `set-deploy-secrets.ps1` pattern [V]), ten-year validity, loaded by the API at start. Cost: about 20 lines, one generated secret, no Azure resource, no new PO step if folded into the Google script. It keeps "a database copy or backup alone cannot act as a user", which starts to matter when Google tokens are stored. Risks: loading the certificate and unprotecting on Linux [A], and behaviour with an expired certificate [A]; both are pinned by CI tests on the Linux runner. If those tests cannot be made green in half a day, ship plain key XML in PostgreSQL and record the residual risk (finding M1-F07 stays open as accepted); no hand-written crypto in either case. Losing or rotating the certificate signs everyone out once.

**(c) Sign-in state: there is no real disagreement.** Both proposals keep a state table and a `__Host-` binder cookie, and both derive nonce and verifier instead of storing them. The table is what gives single use: one atomic `UPDATE ... WHERE consumed_at IS NULL` at the callback, so a replay or double callback loses. Without it, replay protection would live only in the browser. Converge on: table + binder + derived values under separate labels (Opus), state and binder stored as plain SHA-256 of 256-bit random values (mine; no key needed for a lookup), `return_path` in the row, live-row cap of 3,000.

**(d) Rate limits: do not switch on forwarded headers blind; set limits a burst cannot exhaust, measure in Run A, then decide.** Only the platform can connect to the API, so the connecting address is trustworthy as "a platform hop". The forwarded-for list is not: part of it is written by the client, and the safe entry is the one the public ingress wrote, which depends on the exact number of hops (public ingress, Next, internal ingress) and on what each appends [A, all of it]. The code allows one hop and refuses unknown ranges [V: Program.cs, EdgeConfig.cs]. One hop too few changes nothing; one too many lets a caller choose their own bucket, which is worse than today. Now: start at 300 a minute shared plus 10 a minute per binder, callback 300 a minute, row cap 3,000, rejection before any write. That meets S1 item 16 as written (a burst of failed sign-ins by people does not lock others out); it does not stop a scripted flood, which can stall *new* sign-ins while it lasts. Signed-in people are limited per user and are unaffected [V]. In Run A, log once the hop count and the platform-side addresses only (never a client address), then set the trusted range and a two-hop limit with a test that a client-written entry cannot pick the bucket. Target: before anyone but the PO is invited, not before cut-over.

**(e) School accounts.** `Auth:Google:WorkspaceAccounts` = `refuse` (default) or `allow`. `hd` present and `refuse`: outcome `school_account` (new code), nothing created, copy draft: "This looks like a school or work Google account. Tether works with personal Google accounts for now. Nothing was saved. You can try again with a personal account." Absent `hd` = personal. Order: invite list first, then `hd`, so an uninvited school account hears one answer, not two. `allow` logs a start-up warning and stores the flag only. Whether `hd` arrives with sign-in scopes is documented but untested (T-hd) [V: vendor report 9.6]; record what Run A shows. The domain itself is never stored or logged.

**(f) Old dev-login rows: start Run A on a new, empty database.** Change the database name parameter (it exists [V: `postgresDatabaseName`]); migrations build the schema. No purge SQL, no cascade questions before the erasure work exists, no old sessions, and real-data rules start from a clean store. The old database stays on the same server at no extra cost until the PO deletes it. Keep the start-up revoke as well (one UPDATE, already needed for removed invitees). Not Opus's per-request provider check. Risk: the PO's test courses disappear from the site; tell him first.

**(g) Rollback against S4 item 6.** Before Run B the site is: this commit, Production, Google, behind Entra. Run B changes only the gate. So one step, running the workflow with access `team`, restores exactly the pre-cut-over release, and Google accounts survive because nothing else moved. To make it "minutes", a gate-only run skips the image build when the images for this commit already exist (they are tagged by commit [V]); today every run rebuilds [V]. Rehearsal on the deployed site with no open window: Run B with the IP layer limited to the PO's own address (`google+ip`), check from outside, run `team`, check Entra is back from outside, record both; then the real Run B. Never part of rollback: a release with dev login (defect 1). `closed` is the stop button if `team` itself fails. S4's "users are told" is the PO's message, not app code.

## 5. Remaining disagreements and recommended resolution

| # | Topic | Opus | Mine | Recommend |
|---|---|---|---|---|
| 1 | API mode when Google values are missing | Development + dev login | Preflight fails; no Development branch | Mine |
| 2 | Rollback to a dev-login release | Offered | Forbidden once a Google user exists | Mine (defect 1) |
| 3 | Run B sequence | Web internal, then public | Easy Auth off in place after a live pre-check | Mine, plus "same commit as live" rule |
| 4 | Gate choice | Script sets a stored flag | Workflow input | Both: stored setting, input overrides |
| 5 | Return path | Route list, 400 if unlisted | None | Opus's, structural check, fall back to `/`, no success query |
| 6 | Old dev rows | Inert + per-request provider check | Start-up revoke | New database at Run A + start-up revoke |
| 7 | Start limit | 30 a minute shared | 300 shared + 10 per binder | Mine now; trusted hops after measuring |
| 8 | Key-ring protection | Hand-written AES-GCM | Same | Neither: certificate; plain XML as fallback |
| 9 | Adding an invitee | Script + full deploy | Same | Script + "Apply invite list" run |
| 10 | School accounts | Store flag | Store flag | Refuse by default, switch |
| 11 | User key shape | `provider` column, `last_sign_in_at` | `google:` prefix, no new columns | Mine; architect may overrule |
| 12 | Secret entry, invite file, guided script | Paste; file in user profile; one script | Client file; file in repo tree; two scripts | Opus's script and file place, with `-ClientFile` |
