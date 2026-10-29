# Google API Verification Report (OAuth, Calendar, Drive/Docs)

Fetch date for every row: **2026-10-28**. Author: vendor-verification-researcher (subagent). Scope: fact-check of [UNVERIFIED]/[VERIFY] items in `docs/architecture/google-calendar-integration.md`, `docs/security/google-tokens-and-data-residency.md`, `docs/ux/calendar-reminders.md`. No Google API call, credential, or account was used; only public documentation pages (plus the public OIDC discovery JSON, which is a static metadata document, no credentials) were read. Proposal docs were not edited.

Evidence key: **P-sum** = official Google page, but the fetch tool returned a model-written summary containing quoted sentences; quotes below are as returned. Re-open the URL before quoting externally or relying on exact wording. **S** = secondary source (lead only). Several pages were truncated by the tool; those gaps are listed in section 12. Nothing here is measured behaviour. Verdicts: VERIFIED / CONTRADICTED / NOT FOUND IN OFFICIAL DOCS (NF).

## 1. Calendar scope `calendar.app.created`

| # | Claim | Verdict | Source | Quote | Impact |
|---|---|---|---|---|---|
| 1.1 | Scope lets app create secondary calendars and create/change/delete events on them | VERIFIED | https://developers.google.com/identity/protocols/oauth2/scopes ; https://developers.google.com/workspace/calendar/api/auth (P-sum) | "Make secondary Google calendars, and see, create, change, and delete events on them" | Matches design. |
| 1.2 | `calendars.insert` accepts this scope | VERIFIED | https://developers.google.com/workspace/calendar/api/v3/reference/calendars/insert (P-sum) | Scopes listed: `calendar`, `calendar.app.created`, `calendar.calendars` | Calendar creation works without broad scope. Page also warns against service accounts as data owner. |
| 1.3 | `events.insert/patch/delete/list/get` accept this scope | VERIFIED | .../reference/events/insert, /patch, /delete, /list, /get (P-sum) | Each lists `calendar.app.created` among accepted scopes | Full event CRUD on the app calendar is allowed. |
| 1.4 | App can read/modify events it created | VERIFIED (by scope wording) | scopes page | "see, create, change, and delete events on them" (on the secondary calendars) | OK. Wording is about calendars the app made, not "events the app made"; a student-added event on the Tether calendar is presumably also visible. Test. |
| 1.5 | App cannot read the primary calendar | NF (no explicit negative) | https://developers.google.com/workspace/calendar/api/auth (P-sum) | Fetch summary: "The page doesn't say whether this scope allows reading the user's primary calendar." | Inference from the description only. Treat as "no primary access" but prove with a test (section 11 T2). |
| 1.6 | Sensitivity class of `calendar.app.created` | NF | scopes page; calendar auth page (no sensitivity column); https://developers.google.com/workspace/calendar/api/guides/auth (only generic category definitions) | Pages give definitions only: Sensitive = "Grant access to personal user data, resources, or actions."; Non-sensitive = "Grant access only to limited data that's immediately relevant to a specific action." | Cannot be settled from docs fetched. Cloud Console scope picker is authoritative; PO to read it. Plan for **sensitive** (worst case): verification review, justification, demo video. |
| 1.7 | `calendar.calendars` and `calendar.events.owned` exist as alternatives | VERIFIED | calendar auth page | `calendar.calendars`: "View and change the properties of calendars you can access, and create secondary calendars." `calendar.events.owned`: "View, create, change, and delete events on calendars you own." | Both are broader than app.created. Do not adopt. |

## 2. Drive scope `drive.file` and Docs

| # | Claim | Verdict | Source | Quote | Impact |
|---|---|---|---|---|---|
| 2.1 | `drive.file` is non-sensitive | VERIFIED | https://developers.google.com/workspace/drive/api/guides/api-specific-auth ; https://developers.google.com/workspace/docs/api/auth (P-sum) | Docs table: `drive.file` "Recommended, Non-sensitive"; `documents` = Sensitive; `drive` = Restricted | `drive.file` avoids sensitive/restricted review (basic/brand verification still applies, see 7). |
| 2.2 | Covers files the app creates and files user opens/shares with the app | VERIFIED | Drive auth page (P-sum) | "Create new Drive files, or modify existing files, that you open with an app or that the user shares with an app" ; scope text: "See, edit, create, and delete only the specific Google Drive files you use with this app." | Docs created by Tether are in scope. |
| 2.3 | `drive.file` works with Drive API REST resources | VERIFIED | Drive auth page | "works with all Drive API REST Resources" | Folder creation/placement via Drive API plausible but not stated explicitly. Test (T6). |
| 2.4 | Docs API accepts `drive.file` | VERIFIED (scope table level) | https://developers.google.com/workspace/docs/api/auth | `drive.file` listed in the Docs API scopes table as "Recommended". Per-method scope lists for `documents.create`/`batchUpdate` were **not** fetched. | Likely fine; confirm method-level lists and test (T6). |
| 2.5 | Files stay accessible to same client after sign-out/re-grant; `appProperties` markers survive | NF | not fetched | n/a | Rebuild-from-markers claim in token doc remains unverified; test (T6). |
| 2.6 | Drive trash retention of 30 days | NF | not fetched | n/a | Remove the "30 days" claim from copy or find the Drive support page. |

## 3. Event IDs and idempotency

| # | Claim | Verdict | Source | Quote | Impact |
|---|---|---|---|---|---|
| 3.1 | Client-supplied IDs are allowed | VERIFIED | https://developers.google.com/workspace/calendar/api/guides/create-events (P-sum) | "When creating an event, you can choose to generate your own event ID that conforms to our format requirements." / "It also prevents duplicate event creation if the operation fails at some point after it is successfully executed." | Deterministic-ID design is supported. |
| 3.2 | ID charset and length | VERIFIED | https://developers.google.com/workspace/calendar/api/v3/reference/events (P-sum) | "those used in base32hex encoding, i.e. lowercase letters a-v and digits 0-9" ; "the length of the ID must be between 5 and 1024 characters" ; "the ID must be unique per calendar" | **Design impact:** IDs must be lowercase `[a-v0-9]`, 5-1024 chars. No hyphens, no `w-z`, no uppercase. A raw SHA-256 hex (0-9a-f) is valid; a GUID with hyphens is not. |
| 3.3 | Reusing an ID that exists returns 409 | VERIFIED | https://developers.google.com/workspace/calendar/api/guides/errors (P-sum) | 409 `duplicate`: "If you're creating a new instance, generate a new ID. To change an existing instance, use the update method instead." | Treat 409 on insert as "already exists", then `get`/update. |
| 3.4 | Reusing the ID of a **deleted** event returns 409 | NF in official docs | errors page says nothing about deleted IDs. Secondary leads only (S): googleapis/google-api-python-client issue #210 reports 409 after deletion; cloudflare-calendar-sync issue #6 proposes restoring the cancelled event by update. | n/a | **Do not rely on either behaviour.** Design should: never re-insert a deleted deterministic ID; on 409 do `get` (cancelled events have `status=cancelled` with `showDeleted`) and update to `confirmed`, or version the ID (include a generation counter). Test T4. |
| 3.5 | Deleting an already-deleted event gives 410 | VERIFIED (errors page only) | errors page (P-sum) | Summary: "For deletes, a 410 means the event is already gone and needs no further action." The `events.delete` page itself says nothing about 410. | Treat delete 404/410 as success (idempotent). |

## 4. Extended properties and queries

| # | Claim | Verdict | Source | Quote | Impact |
|---|---|---|---|---|---|
| 4.1 | Private properties are visible only to our client ID | **CONTRADICTED (not supported)** | https://developers.google.com/workspace/calendar/api/guides/extended-properties ; events reference (P-sum) | "Properties that are private to the copy of the event that appears on this calendar." / "private properties are set on one attendee's local 'copy' of the event" | Private is scoped to the **calendar copy**, not to an OAuth client. Docs say nothing about hiding them from other apps with access to the calendar. **Impact:** treat markers as non-secret and spoofable by any other client/user with write access; never trust them for authorization; keep DB mapping authoritative. |
| 4.2 | Shared properties visible to attendees | VERIFIED | extended-properties guide | "Shared properties are visible and editable by all attendees of an event" | Do not use shared; Tether has no attendees. |
| 4.3 | Query by private property | VERIFIED | https://developers.google.com/workspace/calendar/api/v3/reference/events/list (P-sum) | `privateExtendedProperty`: "Extended properties constraint specified as propertyName=value. Matches only private properties." | Marker-based reconciliation query is supported. Repeats of the same type are OR'ed; private+shared AND'ed. |
| 4.4 | Size limits | VERIFIED | extended-properties guide | "The maximum size of a property's key is 44 characters." Values 1024 chars; up to 300 properties and 32 kB per event. Longer keys "dropped without an error", values "cut off". | Silent truncation: keep keys short, validate by read-back. |
| 4.5 | Deleted events listable | VERIFIED | events.list | `showDeleted`: "Whether to include deleted events (with `status` equals "`cancelled`") in the result." Default false. | Reconciliation can see cancelled events if requested. |

## 5. Reminders

| # | Claim | Verdict | Source | Quote | Impact |
|---|---|---|---|---|---|
| 5.1 | Max 5 overrides | VERIFIED | events reference; events.insert (P-sum) | "The maximum number of override reminders is 5." | Planner caps at 5. |
| 5.2 | Minutes range, 4-week limit | VERIFIED | events reference; calendarList reference | "Valid values are between 0 and 40320 (4 weeks in minutes)." | "N days before" reminders beyond 28 days must be rejected/clamped. |
| 5.3 | Methods popup and email | VERIFIED | events reference; https://developers.google.com/workspace/calendar/api/concepts/reminders | `"popup"`: reminders appear as a UI popup; reminders support popup and email. | Popup-only is allowed. |
| 5.4 | Overrides require `useDefault=false` | VERIFIED | reminders concept page (P-sum) | "Overrides can be set only when `useDefault` is `false`." | OK. |
| 5.5 | Explicit empty override list with `useDefault=false` means no reminders | NF | not stated | n/a | Design assumes "Off" works this way. Test T5. |
| 5.6 | Popup behaves normally on app-created secondary calendar; mobile delivery | NF | reminders page does not mention secondary calendars; says defaults are per calendar via CalendarList ("Default reminders are managed through the `CalendarList` collection") | n/a | Cannot confirm from docs. Device-level delivery cannot be proven by docs at all. Test T5 + physical device. Student can also change per-calendar defaults in the UI. |
| 5.7 | All-day event reminder minutes measured from 00:00 of that day | NF | reminders page: "maximum number of overrides, maximum minutes for a reminder, and all-day event reminder behavior" not covered | n/a | The 3,780-minute worked example in `calendar-reminders.md` is unverified. Test T5. |
| 5.8 | Later patch does not clobber a student-edited reminder | NF; related fact verified | https://developers.google.com/workspace/calendar/api/v3/reference/events/patch (P-sum) | "Array fields, if specified, overwrite the existing arrays; this discards any previous array elements." | `reminders.overrides` is an array inside an object; if Tether patches `reminders` it **will** overwrite. Omit `reminders` from patches unless user changed that setting; detect drift via `updated`/etag first. |

## 6. events.insert/update/patch/delete semantics, errors, quotas

| # | Claim | Verdict | Source | Quote | Impact |
|---|---|---|---|---|---|
| 6.1 | Patch semantics merge; arrays replaced | VERIFIED | events.patch (P-sum) | "This method supports patch semantics." ; "Array fields, if specified, overwrite the existing arrays; this discards any previous array elements." | As 5.8. |
| 6.2 | Patch costs extra quota | VERIFIED | events.patch | "each patch request consumes three quota units; prefer using a `get` followed by an `update`." | Sync worker: `get` then `update` (also gives etag/updated for drift detection). |
| 6.3 | `If-Match`/etag handling for updates | NF (patch page "doesn't mention ETags"); generic versioned-resource guide not fetched | n/a | n/a | Optimistic concurrency unconfirmed. Test T7. |
| 6.4 | 403 `rateLimitExceeded`/`userRateLimitExceeded`, 429 same handling | VERIFIED | errors page (P-sum) | "should be responded to in the same way, by using exponential backoff"; rate limit may arrive as 429 | Retry both; honour backoff with jitter. |
| 6.5 | 403 usage-limits JSON reason | VERIFIED (tool-reported) | errors page | Summary: the doc's JSON reason is `quotaExceeded`, not `calendarUsageLimitsExceeded` | Match on the documented reason; do not retry blindly (Calendar usage limits are Workspace admin quotas). |
| 6.6 | 404 | VERIFIED | errors page | 404 when "the resource never existed or the user can't access the calendar"; suggested backoff | A 404 on the Tether calendar can mean the **student deleted it** (or access lost). Distinguish via `calendarList.get`/`calendars.get`; do not retry forever. |
| 6.7 | 410 | VERIFIED | errors page | `fullSyncRequired`: "Sync token is no longer valid, a full sync is required." | Only matters if sync tokens used. Delete 410 = already gone. |
| 6.8 | 400 permanent | VERIFIED | errors page | "Don't retry 400 errors." | Park as `failed`. |
| 6.9 | Quota numbers | VERIFIED | https://developers.google.com/workspace/calendar/api/guides/quota (P-sum) | 10,000 requests/min/project; 600/min/user/project; "1,000,000 requests before charges apply"; "You cannot request an increase on this daily threshold limit."; "Exceeding the quota request limits is planned to incur charges." Unpublished limits can apply "if you attempt to write to a single calendar in quick succession." | **Cost/guardrail impact:** daily threshold is a billing threshold and charges are *planned*, so add a spend/usage guard (Invariant 10). Per-calendar write bursts have undocumented limits: throttle a 500-event sync (e.g. few req/s) and use backoff. Sliding window. |
| 6.10 | Batch request support/limits | NF | not fetched | n/a | Keep one request per event (as proposed). |

## 7. OAuth app status, verification, branding

| # | Claim | Verdict | Source | Quote | Impact |
|---|---|---|---|---|---|
| 7.1 | Testing status: max 100 test users | VERIFIED | https://support.google.com/cloud/answer/15549945 (P-sum) | "limited to up to 100 test users listed in the OAuth consent screen" | Fine for smoke test. |
| 7.2 | Testing status: refresh tokens expire in 7 days | VERIFIED | https://developers.google.com/identity/protocols/oauth2 (P-sum) | "publishing status of 'Testing' is issued a refresh token expiring in 7 days" | Smoke test needs re-consent weekly; tests must expect `invalid_grant` after 7 days. |
| 7.3 | External user type | VERIFIED | support.google.com/cloud/answer/15549945 | "Projects configured with a user type of External are available to any user with a Google Account." (in Testing, restricted to listed test users) | OK. |
| 7.4 | Unverified-app cap | VERIFIED | same | "100 new users in total, after the app presents the unverified app screen" | Production-with-unverified is capped; not a student launch path. |
| 7.5 | Testing apps are not subject to verification | VERIFIED | https://support.google.com/cloud/answer/13464321 and 13461325 (P-sum) | "Apps in development, testing or staging are not applicable for verification." ; "publish your app from 'testing' to 'production'" | No review needed for the smoke test. |
| 7.6 | Verification applies to every app, not only sensitive scopes (brand verification) | VERIFIED | 13464321 (P-sum) | "All apps that access Google APIs must verify that they accurately represent their identity and intent" (homepage, privacy policy, domain ownership, branding). Page links a separate "when is verification not needed" article, not fetched. | Even a non-sensitive-only production app needs brand verification. Need public homepage + privacy policy on an owned domain before any non-test launch. |
| 7.7 | Sensitive/restricted scope requirements | VERIFIED | 13464321 | Narrowest scopes with justification; demo video "Must show the end-to-end flow of your app including the OAuth grant process."; restricted scopes need an annual security assessment by an empanelled assessor; if data used for AI training, video must show explicit consent. | If `calendar.app.created` is sensitive: demo video + justification. `drive.file` and OIDC need none of that. |
| 7.8 | Verification review duration | NF | pages say only the review team "will review your submission"; no timeframe | n/a | Cannot schedule; budget unknown. Only branding timing is documented (7.9). |
| 7.9 | Brand verification timing and homepage/privacy/domain rules | VERIFIED | https://developers.google.com/identity/protocols/oauth2/production-readiness/brand-verification (P-sum) | Automated check "typically takes a few minutes"; manual "usually takes 2-3 business days"; compliant results valid 7 days; "Your home page must be publicly accessible"; privacy policy "hosted within the same domain as your application's home page" and linked from the consent screen; must disclose how Google user data is accessed/used/stored/shared; verify top private domain in Search Console. | Needs a real domain Tether controls. |
| 7.10 | Policy against "Google" in app names | NF | brand pages say only that branding must "accurately represent the identity of the application" | n/a | Not confirmed or denied in fetched pages. Do not put "Google" in the app name or logo to avoid risk; treat as unresolved. |
| 7.11 | Limited Use obligations | VERIFIED (existence), text NF | https://support.google.com/cloud/answer/13464323 (P-sum) | "required to comply with Google's API Services User Data Policy regardless of whether they have been verified." | Policy text itself not fetched; legal-compliance-reviewer must read the policy page directly. |

## 8. Token behaviour

| # | Claim | Verdict | Source | Quote | Impact |
|---|---|---|---|---|---|
| 8.1 | Refresh token only on first authorization; need `access_type=offline` | VERIFIED | https://developers.google.com/identity/protocols/oauth2/web-server (P-sum) | "The `refresh_token` is only returned on the first authorization." ; `offline` "if your application needs to refresh access tokens"; to obtain a new one the user must revoke or you must send `prompt=consent`. | Persist the refresh token on first grant; a later sign-in without `prompt=consent` returns none. Reconnect flow must send `prompt=consent`. |
| 8.2 | "One refresh token per client/account" | **CONTRADICTED** | https://developers.google.com/identity/protocols/oauth2 | "There is currently a limit of 100 refresh tokens per Google Account per OAuth 2.0 client ID." ; web-server page: exceeding limits makes "older refresh tokens" stop working | Multiple refresh tokens can exist; oldest are invalidated past 100. Repeated `prompt=consent` in dev/tests can silently kill older tokens. Revoke old token on replacement. |
| 8.3 | Google may not rotate refresh token on refresh | NF | refresh-response behaviour not in fetched text | n/a | Keep "store a new one if returned" logic; test T8. |
| 8.4 | Incremental authorization via `include_granted_scopes` | VERIFIED | web-server page | "Enables applications to use incremental authorization to request access to additional scopes in context." ; "the new access token will also cover any scopes to which the user previously granted the application access." | Supported; access-token coverage verified. |
| 8.5 | A single refresh token covers all granted scopes | NF | page says only the new *access token* covers prior scopes; refresh-token coverage not stated (section truncated) | n/a | Do not assume. Test T3. Design should be able to hold one token and track granted `scope` string from each token response. |
| 8.6 | Refresh token expiry/invalidation reasons | VERIFIED | oauth2 overview (P-sum) | Revoked by user; "has not been used for six months"; "The user changed passwords and the refresh token contains Gmail scopes."; admin session length exceeded; admin set a requested service to Restricted. | Password change invalidates only tokens with **Gmail scopes**, so Tether's calendar/drive tokens should survive a password change. Admin session-length and Restricted-service policies can still kill them. 6-month idle expiry: refresh periodically or expect `needs_reauth`. |
| 8.7 | `invalid_grant` on expiry/invalidation | VERIFIED | web-server and native-app pages | "Authenticate the user again and ask for user consent to obtain new tokens." ; native-app page: the user account "may have been deleted or disabled" | Map `invalid_grant` to `needs_reauth`; do not distinguish deleted from revoked (cannot). |
| 8.8 | Push signal for suspension/deletion | NF | none found | n/a | Detect lazily at refresh. |
| 8.9 | Revocation endpoint | VERIFIED | web-server page; discovery doc https://accounts.google.com/.well-known/openid-configuration | `revocation_endpoint`: "https://oauth2.googleapis.com/revoke" ; "The token can be an access token or a refresh token." If an access token with a linked refresh token is revoked, "the refresh token will also be revoked." | Use this endpoint. |
| 8.10 | Per-scope revocation | NF; opposite stated | web-server page | "Revocation removes all OAuth 2.0 scopes previously granted to a project." (tool-reported wording; sentence also says it affects the project's clients) | **Impact:** no per-scope revoke at the endpoint. "Disconnect Docs only" cannot be done by revoking; it must be an app-level disconnect (stop using the capability, delete local mapping) or a full revoke plus re-consent with fewer scopes. Revocation also invalidates the **sign-in** grant used for OIDC identity. Also affects other OAuth clients "registered under" the same project, so keep dev/prod in separate projects. |
| 8.11 | User can remove access in Google Account | VERIFIED | https://support.google.com/accounts/answer/3466521 (P-sum) | Revoked app "can't access your data anymore"; data already obtained may remain with the developer. | Revoke does not delete what Tether stored; deletion workflow still needed. |

## 9. OIDC sign-in

| # | Claim | Verdict | Source | Quote | Impact |
|---|---|---|---|---|---|
| 9.1 | Discovery URL and JWKS | VERIFIED | https://developers.google.com/identity/openid-connect/openid-connect ; discovery JSON fetched | Discovery: `https://accounts.google.com/.well-known/openid-configuration`; `jwks_uri`: `https://www.googleapis.com/oauth2/v3/certs`; `token_endpoint`: `https://oauth2.googleapis.com/token`; `authorization_endpoint`: `https://accounts.google.com/o/oauth2/v2/auth`; `id_token_signing_alg_values_supported`: RS256 | Pin RS256; cache JWKS with standard cache headers (cache-header behaviour not fetched). |
| 9.2 | `iss` values | VERIFIED | OIDC page | "Always `https://accounts.google.com` or `accounts.google.com`" | Accept both strings. Discovery `issuer` is the https form. |
| 9.3 | `sub` is stable and never reused | VERIFIED | OIDC page | "unique among all Google Accounts and never reused." ; "Don't use the `email` field as a unique identifier for a user. Always use the `sub` field." | Confirms identity key `iss+sub`. Closes the "sub not reused" item (deleted-account orphan). |
| 9.4 | `nonce`, replay | VERIFIED | OIDC pages | "a random value generated by your app that enables replay protection" ; present it only once | Use single-use nonce bound to session. (The page is inconsistent on required vs optional; treat as required.) |
| 9.5 | `email`, `email_verified` | VERIFIED | OIDC page | "Provided only if you included the `email` scope" ; "True if the user's email address has been verified" | Require `email_verified=true` if email is used at all; never as key. |
| 9.6 | `hd` claim | VERIFIED | OIDC page | "The domain associated with the Google Workspace or Cloud organization of the user." ; "You must check this claim when restricting access..." ; `hd` request parameter is a UI hint only. | `hd` present = Workspace org account; absent = consumer account. Use to flag university-managed accounts (GR-1 warnings), not as authorization. |
| 9.7 | `aud`, signature, expiry validation | VERIFIED | OIDC page | `aud` "must be one of the OAuth 2.0 client IDs of your application"; verify signature via `jwks_uri`, `iss`, `aud`, expiry. | Standard. |
| 9.8 | PKCE supported; required for web server clients | PARTIAL: supported VERIFIED; required NF | Discovery doc: `code_challenge_methods_supported`: "plain", "S256". Native-app page: "Google supports the Proof Key for Code Exchange (PKCE) protocol"; S256 recommended; `code_challenge_method` "defaults to `plain`" when omitted; verifier 43-128 chars. Web-server and OIDC pages (as fetched) do not describe PKCE; OIDC code exchange lists `client_secret` as body field. | Use S256, always send `code_challenge_method=S256` explicitly (default is `plain`). Client secret still required at the token endpoint for web clients. Do not claim "Google requires PKCE" for web clients. |
| 9.9 | `state` | VERIFIED | web-server and OIDC pages | web-server marks CSRF check "Important"; OIDC page: "(Optional, but strongly recommended)" | Keep mandatory in Tether. |
| 9.10 | `prompt` values | VERIFIED | OIDC page | `none`, `consent`, `select_account` | `select_account` useful so students pick the right (non-university) account. |

## 10. Workspace-managed (university) accounts

| # | Claim | Verdict | Source | Quote | Impact |
|---|---|---|---|---|---|
| 10.1 | Admins can control third-party app access | VERIFIED | https://knowledge.workspace.google.com/admin/apps/control-which-apps-access-google-workspace-data (P-sum; redirect from support.google.com/a/answer/7281227) | Levels: Trusted ("access to all Google Workspace services... including restricted"), Limited ("only unrestricted Google services"), Specific Google data, Blocked ("Can't access any Google service."). Unconfigured apps default to "allow any third-party app", alternatives "allow only apps that request basic sign-in info" or "Don't allow users to access any third-party apps." | Tether (unconfigured) can be blocked outright, or limited to sign-in-only, per university. |
| 10.2 | Sign-in-only apps | VERIFIED | same | Basic-info option lets users sign in to apps that request "only the account name, email address, and profile picture." | OIDC sign-in can work while Calendar/Drive scopes are blocked, so handle partial-grant states. |
| 10.3 | Calendar blocking | VERIFIED | same | "If you restrict Calendar, only Trusted and Specific Google data apps can access Calendar data. Limited apps are blocked." Calendar is not on the high-risk list. | `calendar.app.created` can be blocked by a Restricted Calendar setting. Expected UI state `admin_blocked` is justified. |
| 10.4 | `drive.file` blocking | PARTIAL | same | Page does not name `drive.file`; says that under Restricted an admin can allow scopes "that allow apps to access user-selected files in Drive." | Not confirmed that `drive.file` passes; it is plausibly allowed for Limited apps but depends on the admin settings. Test on a Workspace account if available (T9). |
| 10.5 | What the end user sees | VERIFIED | same | "they're notified that the app is blocked"; custom message possible | Show calm explanatory state plus "use a personal account" choice (subject to legal review). |
| 10.6 | Exact error code (`admin_policy_enforced`) | NF | not fetched | n/a | Token doc names the code; unconfirmed. Handle generically. |
| 10.7 | Admin session-length policy / Restricted service kills refresh tokens | VERIFIED | oauth2 overview | "the session length set by the admin could have been exceeded" ; "If an admin set any of the services requested in your app's scopes to Restricted" | University accounts will see more frequent `needs_reauth`. |

## 11. Recommended tests against the real throwaway account (all **NOT RUN**; require PO approval; synthetic data; test user jolsendev@gmail.com is the designated test user)

| ID | Test | Resolves |
|---|---|---|
| T1 | Read the Cloud Console scope picker for `calendar.app.created`, `drive.file`, `openid email profile`; screenshot the sensitivity labels and any "verification required" notice | 1.6, 7.7 |
| T2 | With only `calendar.app.created`: call `calendarList.list`, `events.list` on `primary`, and `calendars.get primary`; expect failure; list events on the created calendar after adding a manual event in the Calendar UI | 1.4, 1.5 |
| T3 | Grant `openid email profile` first, then add Calendar via `include_granted_scopes=true`; inspect `scope` in each token response; refresh using the *first* refresh token and check which scopes the access token carries; repeat with `prompt=consent` and count tokens | 8.2, 8.5 |
| T4 | Insert event with deterministic ID, delete it, re-insert same ID: record status code; then `get` with `showDeleted`, update to confirmed; test an ID outside `[a-v0-9]` and length 4 and 1025 | 3.2, 3.4 |
| T5 | Popup reminders on the secondary calendar: `useDefault=false` with (a) empty overrides, (b) 5 overrides, (c) 6 overrides, (d) 40321 minutes; all-day event reminder offset; confirm on a physical phone and desktop that the popup fires | 5.5-5.7 |
| T6 | `drive.file`: create a Doc via Docs API and via Drive API, `batchUpdate`, create a folder and place file, set `appProperties`, sign out/in and reaccess, delete from trash | 2.3-2.6 |
| T7 | Patch vs get+update with a student-edited reminder/title; `If-Match` etag behaviour | 5.8, 6.3 |
| T8 | Refresh token response: is a new refresh token ever returned? Observe 7-day expiry, record exact `invalid_grant` payload after (a) revoke in Google Account, (b) revoke via endpoint | 7.2, 8.3, 8.7 |
| T9 | If a Workspace test domain is available (NOT the university): set Calendar/Drive Restricted, Blocked, and "sign-in only"; record error screens and codes | 10.4, 10.6 |
| T10 | Burst test on the one calendar: 500 inserts with the intended throttle; record 403/429 rates | 6.9 |
| T11 | Delete the Tether calendar in Calendar UI, then call events.insert: record status and reason | 6.6 |
| T12 | Private extended property visibility: with a second OAuth client (or the API explorer) under the same user, read an event and see whether private properties appear | 4.1 |

## 12. Could not fetch / gaps

- Sensitivity classification of `calendar.app.created`: neither the scopes page nor the Calendar auth page carried a sensitivity column (fetch limited; the scopes page was 153k characters, read in two halves). Not found.
- The web-server OAuth page was read in two parts (0-100k and 100k+); neither returned a PKCE section or an explicit refresh-token-for-incremental-scopes statement; sections may exist in text the summarizer skipped.
- Review timeframe for sensitive scope verification, "Google" in app-name rule, API Services User Data Policy / Limited Use text, "when verification is not needed" article, Calendar usage limits admin help page, Drive trash retention, events versioning/etag guide, Calendar batch limits: not fetched or not stated.
- All fetched content came through a summarizing fetch tool (P-sum); quotes are not guaranteed verbatim. Re-open before external quotation.

## 13. Summary

Counts (rows with a verdict; PARTIAL counted under the half it leans to): VERIFIED about 49; CONTRADICTED 2 (4.1 private properties visible "only to our client ID"; 8.2 "one refresh token per client/account"); NOT FOUND IN OFFICIAL DOCS about 17 (1.5 explicit negative, 1.6, 2.5, 2.6, 3.4, 5.5, 5.6, 5.7, 6.3, 6.10, 7.8, 7.10, 8.3, 8.5, 8.8, 10.6, plus 8.10 per-scope revocation and 9.8 "PKCE required"). Every NF item is covered by a test in section 11 or a design default below.

Three design impacts that matter most:

1. **Event IDs and idempotency.** IDs must be lowercase base32hex characters (`a-v`, `0-9`), 5-1024 chars, unique per calendar; a duplicate returns 409. What happens when reusing a *deleted* event's ID is not documented, so use ID versioning or restore-by-update on 409, and treat delete 404/410 as success. Private extended properties are scoped to the calendar copy, not to our client, so markers are hints, not a security boundary; the database mapping stays authoritative. Patch replaces whole arrays (reminders), so never patch `reminders` blindly.
2. **Tokens and revocation.** Refresh tokens are issued only on first grant (use `access_type=offline`, `prompt=consent` on reconnect); up to 100 per account per client with older ones silently invalidated; Testing-status tokens last 7 days; revocation removes all scopes granted to the project (no per-scope revoke) and ends the sign-in grant too. "Disconnect Docs only" must be an app-level action. Whether one refresh token covers incrementally added scopes is unverified (T3); keep dev and prod in separate Cloud projects.
3. **Verification and university accounts.** Sensitivity of `calendar.app.created` is not confirmed by docs (read the Console picker); `drive.file` is non-sensitive, `documents` sensitive, `drive` restricted. Every production app still needs brand verification (public homepage, privacy policy on the same owned domain, 2-3 business days manual); sensitive scopes add a demo video and unspecified review time. Workspace admins can block Calendar (Restricted setting blocks everything except Trusted/Specific apps) while still permitting sign-in-only, so `admin_blocked` and partial-grant states are real and need UX.

---

## Addendum 2026-10-29: Cloud Console evidence from the Product Owner (test T1, partial)

Source: five screenshots taken by the Product Owner on 2026-10-29 (Google Auth Platform pages for project `tether-oauth-dev`), stored outside git in `secrets/google-screenshots/`. Read by the Lead; not independently verified by a second person. HUMAN-ATTRIBUTED EVIDENCE, console UI only, no API calls made.

| Item | What the console showed | Effect |
|---|---|---|
| Data Access: sensitivity of `calendar.app.created` | Listed under **"Your non-sensitive scopes"** with the description "Make secondary Google calendars, and see, create, change, and delete events on them". "Your sensitive scopes" and "Your restricted scopes" both show "No rows to display". | The earlier plan assumed this scope might be "sensitive". The console labels it non-sensitive today. Production still needs brand verification (homepage, privacy policy, domain); the sensitive-scope review and demo video should not apply to the current scope set. Re-check at verification time, because Google can change classifications. |
| `openid`, `userinfo.email`, `userinfo.profile` | All listed as non-sensitive. | As expected. |
| `drive.file` | **Not in the list.** The picker only offers scopes for APIs enabled in the project, and only the Calendar API is enabled. | Not a defect. Enable the Google Drive API (free) or add the scope manually when the Docs/Drive work starts; then capture its label (T1 remainder). |
| Audience | User type **External**; app is in **Testing** (text: "While publishing status is set to 'Testing', only test users are able to access the app. Allowed user cap prior to app verification is 100"); counter "1 user (1 test, 0 other) / 100 user cap"; one test user listed. | Matches the plan. The 100 cap counts over the lifetime of the app. |
| Branding | App name **Tether**; support email set; "Verification is not required since your app is in Testing status". Logo not uploaded; application home page empty. | Do not upload a logo until ready for verification (the page says it triggers a verification submission outside Testing). Home page and privacy policy are needed before production. |
| Clients | One OAuth 2.0 client, name **Tether**, type **Web application**, created Oct 28, 2026. | Matches the plan. Redirect URIs were not in the screenshots. |
| Enabled APIs | **Google Calendar API** enabled; metrics for 30 days show "No data is available for the selected time frame". | Confirms no calls have been made to the Calendar API from this project. |

**Open from the screenshots**
- RESOLVED 2026-10-29: the PO confirmed the correct test and support address is `jolsendev@gmail.com` (the earlier `jwolsendev` was a typo in chat). Docs corrected.
- Not yet captured: redirect URIs on the client, Verification Center, and the Drive scope label.

### Addendum 2 (2026-10-29, two more Console screenshots from the Product Owner)
- **Verification Center:** "Verification is not required since your app is configured with a Testing publishing status." Nothing else listed (no pending review, no requirements shown while in Testing).
- **OAuth Overview (7 days):** Traffic, Errors and Users all show "No data is available for this project", so no OAuth requests have been made against this project. **OAuth token grant rate limit: 10,000 grants per day** ("Raise daily token limit" link). Useful as a project-wide ceiling for the quota and spend guard (CV-06).
- Still not captured: the client's **Authorized redirect URIs**, and the `drive.file` label (needs the Drive API enabled). 
