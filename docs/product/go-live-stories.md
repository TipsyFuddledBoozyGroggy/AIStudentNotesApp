# Go-live slice: stories and acceptance criteria

**Status: DRAFT stories, written 2026-11-27 for PO-20cg. Proposals, nothing here is built or approved.** Author: product-owner agent. These are the yardstick for the engineers and the architect. Nothing here moves a gate: G1 BLOCKED, G2 UNVERIFIED, G3 and G4 NOT_RUN, G5 BLOCKED / NOT_VERIFIED. No item is PASS. Not legal advice.

**Order (the Lead's order from PO-20cg):** S1, then S2 and S3 (before any other user, PO-20ax), then S4, then S5. S4 also holds the production settings.

**Copy tone for every student-facing line below:** plain, calm, one idea per sentence, no blame, no urgency, no exclamation marks, no streaks or praise. Say what happened, what did not happen, and what the student can do. Wording shown in quotes is a proposal for the UX specialist to confirm against `docs/ux`.

**Who the users are.** The PO, the team, and up to 100 people the PO names as Google test users. They are a closed list, not the public. Their Google account is personal (PO-15a). Real students may be among them, so real-data rules apply from the first invited sign-in: user-scoped data, no raw content in logs, delete and export working.

---

## S1. Invite-only Google sign-in replaces the test login

**As** an invited person, **I want** to sign in with my Google account, **so that** I get my own account and my notes are mine (PO-9d, PO-10n: Google is the identity, no password page).
**As** the PO, **I want** only people I list in Google's console to get in, and nothing created for anyone else.

**Acceptance criteria**
1. First sign-in. Given I am on the PO's test-user list and have no Tether account, when I choose "Continue with Google" and approve, then an account is created once, tied to my Google identity, and I land on my empty dashboard signed in.
2. Returning sign-in. Given I already have an account, when I sign in with the same Google account, then I reach my existing data and no second account exists.
3. One account per person (PO-9d). The account key is Google's stable subject id, not the email text. A changed email does not make a new account.
4. Not invited. Given my Google account is not a test user, when I try to sign in, then Google or Tether refuses, no Tether account or row of any kind is created, and I see a plain message, for example: "This site is invite-only for now. Your Google account is not on the list. Nothing was saved." with no blame and no list of who is invited. Whichever side refuses (Google's own screen or Tether), the Tether side must be tested to create nothing.
5. Cancelled at Google. Given I press Cancel or close the Google screen, then I return to the sign-in page, signed out, nothing created, with "You cancelled. Nothing was changed. You can try again whenever you like."
6. Google error or unreachable. Given Google returns an error, a bad or replayed state, or times out, then I see "Sign-in did not work this time. Nothing was changed. Try again in a moment." No stack trace, no token, no provider text, no raw error code on screen. The failure is logged content-free (no tokens, no email in logs by default).
7. Personal accounts only (PO-14, PO-15a, PO-20w). A school-managed (Workspace) account is refused with a plain message and nothing created. PO-20w places the refusal "at connect"; because the sign-in account is the connected account (PO-9d), the Lead reads that as refusing at sign-in (to be confirmed by the PO). `hd` detection is UNVERIFIED with sign-in-only scopes (T-hd) and must be tested; what to do when the claim is absent is the open point in PO-20w.
8. Sign out. When I choose Sign out, then my session ends on the server (not only in the browser), the back button does not show my data, and I see the signed-out page.
9. Survives an app restart. Given I am signed in, when the API restarts or is redeployed, then I am still signed in until the session's normal absolute lifetime. Sessions are stored server-side or in keys that persist, not in memory.
10. Cookie and flow safety. The session cookie is Secure, HttpOnly and SameSite; the OAuth flow uses state, nonce and PKCE; the redirect address is fixed to the app's own address; the Google client secret is never in the repo, a log, the web bundle or an agent session.
11. Dev login unreachable in Azure. Given the deployed environment, then the dev login route returns 404 or is absent, a test proves it against the deployed settings, and the app runs in Production, not Development. The existing guard (dev login refused outside Development) is not weakened (PO-20cf).
12. Closed or suspended Google account (PO-9n, PO-10n): sign-in fails with the plain error in item 6; no password fallback exists.
13. Scope is sign-in only: openid, email, profile. No Docs, Drive or Calendar scope is requested in this slice. The AI and the web page never receive tokens (invariant 3).
14. No sign-up form, password field or "create account" page exists.
15. Accessibility: the sign-in, refused, cancelled and error screens are keyboard-only usable, announced once to a screen reader, focus lands on the message heading, WCAG 2.2 AA target (not human-verified, G3).
16. Sign-in rate limiting still applies; a burst of failed sign-ins does not lock out other people.

**PO's own steps:** in Google's console, keep the project in Testing status and add each invitee as a test user (max 100); add the app's redirect address; keep the client secret out of chat and the repo; run the script that stores the secret. Invitees may see a Google warning screen while the project is in Testing (UNVERIFIED for sign-in-only scopes; record what the first real sign-in shows).
**Out of scope:** open sign-up; own domain, homepage, privacy policy; Google brand verification; Workspace accounts; Docs, Drive or Calendar permissions; email invites sent by Tether; roles or admin screens; password or magic-link login.

---

## S2. Export all my data

**As** a signed-in person, **I want** one download of everything Tether holds for me, **so that** I can keep it or leave (invariant 8, PO-20ax).

**Acceptance criteria**
1. Where. Given I am signed in, then Settings has a clearly named "Download all my data" control. One button, no form, no reason asked.
2. Content. The file contains everything stored for me: profile fields Tether holds, courses, lectures with transcripts and their timestamps, summaries, recaps and notes with source timestamps, syllabi and decisions (original wording where kept), my choices and settings, and the consent and usage records about me. Source transcripts stay distinct from derived summaries and keep provenance (invariant 5).
3. Only mine. A test with two accounts shows the file holds none of the other's data. An unauthenticated or other-user request returns refusal, not data.
4. Format. A single download in an open, readable format (JSON, plus a plain Markdown copy of notes is welcome). Each section is labelled in plain words. The file says it was made by Tether, on which date, and what it does not contain (for example "Audio is not kept").
5. No secrets. The file never contains tokens, keys, password-like values, other users' ids or internal server details.
6. Calm timing. Given large data, when I press the button, then I see "Getting your data ready" and can leave the page; the result is not lost if I navigate; if it fails I see "Nothing was changed. Try again when you like." Nothing is emailed unless I asked for that.
7. Repeatable and free. I can export any number of times; it counts against no AI cap; it changes nothing.
8. Not logged. The export's content never enters logs; only a content-free event (who, when, size).
9. Works before delete. S3's dialog links to this export, and the export is tested on a seeded account before any other user is invited.
10. Accessible: keyboard operable, one announcement on start and one on finish.

**PO's own steps:** none beyond approving the wording and the deploy.
**Out of scope:** export to Google Docs (a separate story, later); scheduled or automatic exports; export for another person; admin export of someone else's data.

---

## S3. Delete my account

**As** a signed-in person, **I want** to delete my account and my Tether data, **so that** I stay in control (invariant 8, PO-20ax).

**Acceptance criteria**
1. Where. Settings has "Delete my account". It is not near the main actions and is never shown as a nudge or on a timer.
2. Dialog follows the existing delete-dialog rule (`docs/ux/delete-wording-draft.md`): no typing to confirm; the safe choice ("Keep my account") is the filled button and takes focus; Escape cancels; the page behind is inert; focus returns to the opener; the dialog names what goes and what stays in one short list; no countdown, no red alarm language.
3. The dialog says, in plain words: it cannot be undone; what is removed; that copies already held by AI or speech services, and anything already copied to the student's Google account, are not removed by Tether (PO-15c, and the consent bullet "Deleting here does not remove copies already held by those services or by Google"); and it offers "Download all my data first" (S2).
4. Removed (PO-20aw, PO-20ca): the account, sign-in link, sessions, courses, lectures, transcripts, summaries, notes, syllabi and decisions, settings, stored tokens (if any), and the consent, audit and AI-usage rows about me (removed by the evidence-purge functions on full account erasure). Test: after deletion a scan of every table finds no row keyed to my user id and no text of mine.
5. Kept: only what holds no student text and no personal identifier and is not tied to my identity, for example aggregate cost totals needed to keep the daily cap intact. If any row survives, the dialog and docs name it. Anything that cannot be fully anonymous is a PO question (see questions).
6. Not touched at Google (PO-15c): nothing in my Google account, Drive, Docs or Calendar is deleted or changed by Tether. Tether stores no Google token for a sign-in-only account, so there is nothing to revoke; if tokens exist they are deleted.
7. Signed out everywhere: all my sessions on every browser end at once. A request made with an old session after deletion is refused.
8. Afterwards I see one calm page: "Your account and data were deleted. Nothing in your Google account was changed." No survey, no "are you sure you want to leave".
9. Signing in again with the same Google account later is treated as a first sign-in (S1 item 1) if still invited; no old data returns.
10. Failure is safe: if deletion fails part way, I am told "Something went wrong. Some data may still be here. Try again.", nothing is half-hidden, and a retry finishes the job (idempotent). The student is never left signed in to a half-deleted account without being told.
11. Ordering safe: deletion is rejected while another deletion or export for the same account is running; it cannot delete another user's data.
12. Deletion events are logged content-free; no email or name in logs.
13. Proven on a seeded test account, never on a real student's notes (PO-20ax; the plan's rule that nobody is asked to delete real notes to prove delete works).

**PO's own steps:** approve the dialog wording; confirm the "kept" list (question 3). Optionally remove the person from the Google test-user list (Tether cannot do that; deleting the account does not by itself stop them signing in again).
**Out of scope:** a grace or undo period; deletion by the PO on someone's behalf; automatic deletion of idle accounts; recalling data already held by vendors; deleting Google-side copies.

---

## S4. Cut-over: Microsoft gate off, approvals, rollback

**As** the PO, **I want** the Microsoft gate removed only when Google sign-in is proven in the deployed site, **so that** the site is never open without a working gate.

**Acceptance criteria**
1. Order. The Microsoft gate stays on until S1 is verified in Azure (items 1 to 13 of S1 checked against the deployed site, with the checks recorded, not assumed from local runs) and S2 and S3 are tested on a seeded account. A second, independent check (qa-engineer) confirms from outside the network.
2. Production settings before removal: Production environment; dev login absent; sign-ins survive restarts and redeploys; Secure cookies; HTTPS only; no demo secrets or default passwords; the data store is not publicly reachable.
3. No open window. At no point is the app reachable with neither gate working. The cut-over is one change with a stated before and after, not two steps with a gap.
4. Fail closed. If Google sign-in is misconfigured after the change, visitors get the invite-only or error page, never the app, and no data is created.
5. The PO approves each deploy run before it touches Azure (PO-20cg), as the required reviewer of the GitHub environment `azure-dev`; agents never approve. The Lead tells the PO, before each approval, what the run changes, what it does not, and the one-step way back.
6. Rollback. Given the cut-over misbehaves, then one documented step restores the Microsoft gate (and the previous release) within minutes, tested once before the cut-over by actually doing it on the deployed site. Accounts created through Google survive a rollback, and users are told plainly the site is closed for a moment.
7. Invitees are told by the PO (not by the app) before the gate comes off; what the site is, that it is early, and that real AI is not on yet.
8. Spend: nothing in S4 increases cost beyond the USD 25 a month budget; if it would, stop and ask (PO-20cd).
9. Evidence: PROGRESS and the gate register record what was verified and by whom. No gate changes. G5 stays BLOCKED / NOT_VERIFIED until the PO's separate review; this slice is a go-live of an invite-only POC, not a production-readiness claim.

**PO's own steps:** list test users; approve each deploy; re-check the site from a phone or private window after the gate comes off; keep the Entra gate rollback in reach.
**Out of scope:** open sign-up; own domain and certificate; Google brand verification; uptime promises, backups and restore drills (G5); load testing; anything that makes the site public to non-invited people.

---

## S5. Real Speech and Claude replace the demo

**As** an invited person, **I want** notes and recaps from the real services, **so that** Tether is actually useful.
**As** the PO, **I want** real spend to stay under the caps and nothing labelled real that is not.

**Acceptance criteria**
1. Terms first. No real call is made until the PO has accepted the vendor terms in person (PO-20bl; no agent accepts). The build enforces this: with no recorded real-provider configuration the app stays on the mocks.
2. Caps (`docs/product/pilot-plan.md`, PO-20bk, PO-20bo), enforced in the app before every call and held across a restart: AI $0.25 per day and $5 per month across all users together; speech 10 minutes per session, 30 minutes per day, 120 minutes per month. With 100 possible users, the caps are global, not per person (see question 2). A per-person fair share is a separate decision.
3. At a cap. Given a cap is reached, then the call is not made, the student sees a calm line such as "Tether has reached its AI limit for today. Your notes are safe. Try again tomorrow." with no retry loop, the transcript and manual features keep working, and the cap state is visible to the PO.
4. Real labels. The "Demo source" and demo-AI labels disappear for a given output only when that output really came from the real provider. Per item: real speech with real AI shows no demo label; real speech with mock AI keeps the AI demo label; replay audio keeps its label. A test fails if a label says real while the mock ran, or says demo while the real service ran. If the real service fails and the app falls back, the label says so.
5. AI-generated output carries an "AI-generated" label; AI-estimated answers are not shown as answer-key certainty (invariant 6).
6. Honest notice. Before a person's first real use they see, once, in plain words: what is sent to which service, that the service may keep a copy under its terms, that deleting in Tether does not remove that copy, that the PO can technically read data in the system, and that "not now" is fine. The version of the notice is recorded with each send. The wording is a draft until the PO approves it (D8 is open).
7. Reversible. A person can turn the real AI service off for their own account in Settings, at once, with nothing deleted. Only the deployed configuration can turn a paid provider on; no screen can (one-way switch, P-SWITCH).
8. No raw content in logs (planted-phrase test on the deployed app); no provider key in the web bundle, repo or any log; keys live in managed secrets.
9. Speech source is limited (G1). Real speech runs only on audio the PO supplies or owns and on the PO's own voice, within the caps. The microphone feeds the real provider only for a person's own use with their own explicit consent screen, and no recording of other people (classmates, lecturers, a classroom) is built, advised or promoted. G1 stays BLOCKED and this story does not change consent wording. If the engineers cannot meet this while a microphone path exists, the path stays on mock (see question 4).
10. Pause and Stop close the speech stream and cancel any Claude call in flight; throttling (429) shows a clear message and does not retry in a loop.
11. Failure is graceful: a real-provider outage leaves the app usable, with a plain "The AI service did not answer. Your transcript is safe." message.
12. Spend check: after a measured session the PO compares the app's counter to the Azure bill; any gap the counter cannot explain pauses real calls.

**PO's own steps:** read and accept the vendor terms in the portal in person; confirm the model, region and caps; store the keys through the script (never in chat); approve each deploy; approve the notice wording; watch the first day's cost.
**Out of scope:** G1 recording of other people or any lecture without everyone's permission (stays BLOCKED, no gate marked PASS); a paid plan or raising caps; real syllabus extraction (stays mock, SBV-19 to SBV-22); real Google Docs or Calendar writes; diarization; claims about learning, disability or wellbeing (G4).

---

## Questions for the human PO

1. **Invite list size and who is on it.** Is it right that, for now, only you and the people you add by hand in Google's console can get in, and nobody can ask for access from the site? (Assumed yes.)
2. **Sharing the AI budget.** The AI limits ($0.25 a day, $5 a month) were set when one person was the user. With several invited people, should the limits be shared by everyone (the first people to use it may use it all up), or should each person get a small equal share?
3. **What may stay after someone deletes their account.** Your earlier answers say records of consent and AI use are removed with the account (PO-20aw), and you said no audit trail is needed for transcripts and syllabus text (PO-20ca). Is it fine that after account deletion nothing at all stays that points to that person, even if that means the daily cost total has to be kept only as a combined number? (Assumed yes.)
4. **Real speech and the microphone.** The earlier plan allowed real speech only on prepared audio files, never a live microphone, because a microphone can pick up other people. Do you want the first real speech to stay on prepared audio and your own voice only, with the microphone path kept on the demo source until the recording rules are settled? (Assumed yes.)
5. **Deleting an account and Google.** Tether can delete everything on its side but cannot remove someone from your Google test-user list. Are you happy to do that by hand when someone asks, or should the delete page tell them to ask you?
6. **Who tells invitees what.** Will you write the short message to invitees yourself (what the site is, that it is early, that Google will show an "unverified app" screen), or should the team draft it for you to send?
