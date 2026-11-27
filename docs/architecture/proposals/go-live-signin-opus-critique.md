# Cross-critique (Opus) of go-live-signin-fable.md

Status: **PROPOSAL**, pairing protocol step 2. Inputs read: `go-live-signin-fable.md`, `docs/product/go-live-stories.md` (S1, S4),
`docs/architecture/account-export-delete.md` section 9 (ADR-021). No code, no network. **[V]** = checked in the repo, **[A]** = assumed.
The two proposals agree on most of the design. Shared ground: a hand-written flow on the calendar.v1 sign-in rules, ID tokens checked by
`Microsoft.IdentityModel.JsonWebTokens`, a state table plus a `__Host-` binder cookie, a derived nonce and PKCE verifier, two invite lists,
dropping the unique email index, dev rows left inert, Production behind Entra first, Entra off second, and a smoke check that probes dev
login for 404. The differences below are what is left.

## 1. Where Fable is better; I switch
1. **The template loses its Development branch.** The API is always Production and `Auth__DevLogin__Enabled` is deleted rather than made
   a parameter. Mine kept "Google configured ⇒ Production" plus a Development fallback, which leaves a path back to dev login in Azure. Fable's is stronger.
2. **Rollback is a workflow input, `access` = `team` | `invited` | `closed`, plus the precondition script `api-signin-mode.sh`.** Rollback
   is one approved run (S4.6), with no PO script in between. `closed` is a real emergency stop. My `-DropEntraGate`/`-RestoreEntraGate`
   secret toggles took two actions.
3. **No internal pass in Run B.** Run B refuses to start unless Azure already shows a Production API with no dev-login variable. With that
   precondition, switching the gate in place cannot expose dev login. My internal first pass bought nothing and cost a short outage.
4. **`external_subject = "google:" + sub`** instead of my new `provider` column. There is no backfill and the prefix pattern already exists [V].
5. **Dev login has to look its user up by `external_subject = "dev:" + email` once the email index is no longer unique.** It looks up by
   email today [V IdentityEndpoints.DevLogin]. I missed this.
6. **Return path on the client.** The server keeps only the `landing` enum. The exact deep path lives in `sessionStorage` (same tab,
   same origin), and the client accepts only a single-slash path that matches a known route. This meets ADR-021 section 9 (1) with no
   server-side return-path validator and no extra column. I drop my server-side `returnPath`. A new `landing` key per real need, for
   example `account`, keeps the server enum honest.
7. **Smaller points I adopt:** the SameSite Lax/Strict documentation fix that closes M2C-08 [V register]; a preflight warning when the
   invite list has more than one entry before PO-20ax; no background cleanup timer (idle billing); `dependsOn: [api]` on the web app.

## 2. Where mine is better
1. **The outside smoke check includes `GET callback?state=x&code=y` and expects a 302 with a relative
   `Location: /signin?...outcome=state_invalid`.** Only this proves, on the deployed site, that the Next proxy passes a redirect and its
   `Location` through. Fable proves it only locally with Playwright. Both are cheap, so keep both.
2. **The client secret is typed into a hidden prompt.** It never sits in a file inside the repository working tree (see defect D1).
3. **The invite list's source file lives outside the repository** (`%USERPROFILE%\.tether\`), not in the git-ignored but agent-readable `secrets\`.
4. **I state the S1.16 conflict.** Fable lists the shared-IP limit as a known limit, but it does not say that S1 item 16 ("a burst of failed
   sign-ins does not lock out other people") is not met. That needs a PO/Lead decision (section 4d).

## 3. Defects and risks in Fable's proposal
- **D1 [V]** Step 3 feeds the script `-ClientFile .\.env.google.local` from the repository folder. The vendor report says the client JSON
  is kept there (git-ignored by `.env.*` [V .gitignore]). Every agent works in that tree with file-read tools, so this breaks PO-20cg
  ("agents never see the client secret"). The secret must be typed into a hidden prompt, or the file moved outside the repo, and the
  existing copy moved out too. (I did not open or list that file.)
- **D2 [V]** `secrets\invited-emails.txt` is also inside the tree agents read. Email addresses are personal data. Move it to the PO's profile folder.
- **D3 [V]** Smoke check 1 treats "400 or 429 means the route exists". The global limiter (`AnonymousPermitPerMinute`, one shared IP in
  Azure) applies to every request that passes the middleware, matched route or not [V `Program.cs` order], so a 429 does not show that
  the route exists. A 429 should mean "retry until the deadline, then fail closed"; otherwise a busy minute closes the ingress for nothing.
- **D4 [A]** The rate-limit values "300 a minute per address" (calendar draft) are in effect one global bucket in Azure [V main.bicep
  comment]. Fine, but they should be written as global numbers.
- **D5 [V]** "No return-URL parameter anywhere" is right on the server, but it has to name the `sessionStorage` path as the answer to
  ADR-021 section 9 (1). Otherwise the delete flow's "sign in again" lands on home.
- **D6 [A]** Re-authentication before delete (ADR-021 question 1) needs `prompt=login` or `max_age` and an `auth_time` check. Neither
  proposal designs it. `purpose` must stay an open enum (`signin`, later `reauth`, `connect`) in `auth.v1`.
- **D7 [V]** Neither proposal handles the deleted-user row: ADR-021 section 9 (2) says refuse while `deleted_at` is set. Fable leaves it
  "open". It should be specified now: `state_invalid`-style calm copy, nothing created, and the unique `sub` index unaffected.
- **D8 [A]** The key-ring wrapper (Fable's and mine) is hand-written crypto. See section 4b: drop it.

## 4. Positions on the Lead's points
**(a) PO burden.** I pick **one PO command that updates both copies and restarts the API, with no deploy**:
`set-invites.ps1 -Add a@example.com` (also `-Remove`, `-List` which prints the count only). It edits the local list file, writes the
GitHub secret (so the next deploy keeps it), writes the Container Apps secret, then restarts the API revision. The startup step
re-reads the list and revokes the sessions of removed people. The PO already has his own az profile with rights on the group [V bootstrap usage].
Risks:
- The restart drops live hub and audio connections for a few seconds. The script warns about this and offers to wait if a lecture is live.
- The change skips the GitHub reviewer step. The PO is that reviewer, and he runs the script himself.
- The two copies can drift if one write fails. The script writes GitHub first, then Azure, stops at the first failure and says which copy is current.
- Addresses appear in a local command line. That is on the PO's machine only, never in a log.
Rejected: a database list (the PO cannot reach the database, whose firewall admits Azure only [V], so it would need an admin endpoint);
reading the secret back in Bicep with `listSecrets` (an [A] platform trick, and it hides the source of truth). Google's test-user list
stays a manual console step; no supported API exists [A].

**(b) Key ring.** **Plain key XML in Postgres, residual risk recorded.** No wrapper and no certificate. Reasons:
- Whoever can read the database secret can also read the root secret and any certificate secret: Contributor on the group sees every
  Container Apps secret [V main.bicep header]. Wrapping only protects against a database-only leak, which in practice means a SQL
  injection read or a stolen backup.
- Even then, a forged cookie needs a live session id from the same database (ADR-016), and that leak has already exposed all student text.
- `ProtectKeysWithCertificate` adds a PO secret, an expiry to manage, and a known container pitfall [A]: decryption looks in the certificate
  store unless `UnprotectKeysWithAnyCertificate` is set. That is real cost for a narrow gain.
- Record it as accepted residual risk on M1-F07. Move to Blob plus Key Vault wrapping when Key Vault arrives for Google token keys (Q5).
  Test that keys survive a host rebuild on the same database.

**(c) Sign-in state.** The two proposals are the same here; the Lead's point misreads mine. Both use a database row (state hash, binder
hash, purpose, expiry, `consumed_at`) plus a `__Host-` binder cookie, and both derive the nonce and verifier from the state. Settle on:
- Single use by one atomic `UPDATE ... consumed_at IS NULL AND expires_at > now`.
- Burn the row on a binder mismatch.
- Replay or a concurrent double callback gives one session.
- Store `HMAC(oauth-state.v1, state)`, not SHA-256 (no gain from confirming a guess, and it costs nothing).
- One label with domain-separation suffixes, as Fable has. Nothing secret is stored.

**(d) Rate limits.** **Accept for the invite-only POC; do not trust forwarded headers yet.**
- The chain is browser → external envoy → Next (its proxy appends its own peer [A]) → internal envoy → API, and the hop count and
  addresses are platform-managed and undocumented [A].
- With the wrong `ForwardLimit` the API trusts the client's own `X-Forwarded-For`. That makes every per-IP limit spoofable, which is
  worse than one shared bucket.
- Now: separate global buckets for `auth-start` (about 60 a minute) and `auth-callback` (about 60 a minute). A callback with no binder
  cookie goes to a small bucket of its own, so a forged-callback flood cannot starve real returns. Add the global state-row cap. Signed-in
  users keep their per-user buckets.
- Follow-up story: measure the hop chain once in Azure with the PO's own request (count of entries only, no addresses logged), then trust
  exactly that hop.
- Tell the PO that S1.16 is partly met until then.

**(e) School accounts.** Add `Auth:Google:HostedDomainPolicy` = `refuse` (default) | `allow`.
- `hd` present and `refuse`: outcome `school_account`, nothing created, copy "This looks like a school or work Google account. For now
  Tether works with personal Google accounts only. Nothing was saved."
- `hd` absent is treated as personal (fails open; the invite list is the backstop).
- Keep the `hd`-present column for `allow`.
- Mark it the Lead's reading of PO-20w until the PO confirms. T-hd stays NOT RUN; Run A records what a personal account returns.

**(f) Old dev rows.** **Revoke at boot, purge later through the erasure service.** On startup outside Development, one UPDATE revokes
every session of a `dev:` user. `SessionPolicy` also rejects `dev:` users outside Development. A purge now would run ahead of ADR-021's
erasure participants, and the evidence tables without foreign keys could orphan text. Purging the dev rows with the real erasure
service later is a useful first run of it on synthetic data.

**(g) Rollback against S4.**
- One step: run the workflow with `access=team`. Entra comes back, the API stays Production with Google, and Google-created accounts are untouched.
- Add an optional `release_sha` input. It must be on main and at or after the go-live base tag, so a rollback can never deploy a template
  that has dev login (Fable's warning, enforced by the workflow rather than by README capitals).
- Keep the Entra registration until the PO signs off; Fable's step 5 comes after that.
- Rehearsal on the deployed site before invitees are told: A (`team`), B (`invited`, PO checks), R (`team`, timed, PO sees the Microsoft
  prompt again and his Google account still works), B' (`invited`, final).
- S4.3 holds: in every step either Entra, or a Production API with Google and the invite list, guards the site.

## 5. Remaining disagreements and my recommended resolution
| # | Topic | Fable | Opus | Recommend |
|---|---|---|---|---|
| 1 | Client secret entry | script reads `.env.google.local` in the repo | hidden prompt | Hidden prompt; move the existing file out of the tree (D1) |
| 2 | Invite-list source file | `secrets\` in the repo | PO profile folder | PO profile folder (D2) |
| 3 | Invite change path | edit + script + full deploy | (now) one script: both secrets + restart | One script with restart, section 4a |
| 4 | Key-ring protection | AES-GCM wrapper | AES-GCM wrapper (now: none) | Plain XML in Postgres, risk recorded, Key Vault later |
| 5 | Proxy `Location` proof | local Playwright only | outside smoke check on the bogus-state callback | Both |
| 6 | `hd` at sign-in | record only, refuse at connect | (now) switch, default refuse | Switch, default `refuse`, pending PO confirmation |
| 7 | Rate limits / S1.16 | known limit | split global buckets, no-binder bucket, flag S1.16 | Mine now; trusted-hop story after measurement |
| 8 | Rollback to an older release | README warning | `release_sha` input bounded by go-live tag | Enforce in the workflow |
| 9 | Deleted-user and reauth hooks | open | specify refusal now; keep `purpose` open | Specify in `auth.v1` before freeze |
| 10 | Smoke 429 on dev-login probe | breach | retry | Retry until the deadline, then fail closed |
