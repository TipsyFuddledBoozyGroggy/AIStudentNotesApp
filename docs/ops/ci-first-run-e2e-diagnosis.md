# CI first run: e2e failure diagnosis

Run 41313272560, 2026-11-01. Diagnosis only; no code changed. Evidence: `gh run view --log-failed` and the two uploaded artifacts (`e2e-harness-output`, `e2e-real-output`, including `api.log`, `web.log`, error-context and a trace). Local reruns were not done (told not to touch the dev stack).

## Summary

| # | Failing test | Class | Root cause (confidence) |
|---|---|---|---|
| 1 | e2e-harness/redesign.spec.ts:142 phone 390 recap sheet, 3 readable lines | (a) environment, Linux text metrics | Lecture status bar is 3 rows (157px) instead of 2 (112px) on the Linux runner, so one transcript line is lost (high on the symptom, medium on the exact cause) |
| 2 | e2e/syllabus.spec.ts:95 full flow (plus 2 tests "did not run") | (b) real product bug | Browser in a UTC runner sends `defaultTimezone: "Etc/UTC"`; the API allow-list does not contain it, so POST returns 400 (high) |
| 3 | geometry.spec.ts:284 Paper lecture bar at 390 | (a) same as #1 | "phone bar height 157 > 112 at 390" (high) |
| 4 | geometry.spec.ts:504 slice 8c rail, "all open" at 1280, scroll 600 | (a) or (c), unresolved | axe `target-size` on `button[aria-label="Edit Quiz 1 on in class"]` at one scroll position only (low) |
| 5 | geometry.spec.ts:639 landmarks at 390 (glass light solid) | (a) or (c) | transcript landmark 20px lower than Paper (medium) |
| 6 | geometry.spec.ts:639 landmarks at 1280 (gamer dark texture, calm light) | (a) or (c) | transcript landmark 84px lower than Paper in 2 of ~12 variants (medium) |

Not the cause, checked: login rate limit (API run with 1000/min auth, and the syllabus test logged in fine, earlier tests passed), cold start (the API and web were up; 26 real-stack tests passed first), Docker/Postgres (migrations applied, courses created), ports, missing env, Playwright browser install, font download (all woff2 requests returned 200 in the harness trace; Next's "Failed to find font override values for Atkinson Hyperlegible Next" is a harmless size-adjust notice).

## 2. Syllabus full flow: real product bug (the important one)

Symptom: after "Read my syllabus", `heading "Reading your syllabus"` never appears (30s timeout). The error-context page snapshot shows the form still on screen with the 400 message "One thing to check: the text or a date could not be used. Your text is kept." (`copy.ts` errors.invalid, mapped from HTTP 400 in `CreateForm.tsx:457`). The snapshot also shows "Times will use your time zone: Etc/UTC."

Chain:
1. `src/web/src/lib/syllabus/format.ts:62-73` `browserTimezone()` takes `Intl.DateTimeFormat().resolvedOptions().timeZone`. On the GitHub runner (TZ UTC) Chromium reports `UTC`, which fails the Area/Location check `isZone`, so it falls back to the literal `"Etc/UTC"`.
2. `src/api/src/Modules/Syllabus/Tether.Syllabus/Application/Support.cs` `ZoneAllowList` (line 236 on) has no `Etc/UTC` and no `UTC` (grep of the module and contracts finds neither). `SyllabusService.Validate` line 156 returns `Invalid("defaultTimezone","not_allowed")`, a 400.
3. Locally the developer machine is in a US zone, so the test has always passed. This is not CI-only in the product sense: any student whose browser reports UTC gets a syllabus form that can never submit and a message that blames "the text or a date". Firefox with resistFingerprinting and Tor-style privacy setups report UTC, as do machines set to UTC. The student cannot fix it.

The other 2 syllabus tests (hostile text, file upload) show as "did not run" only because the file is serial and test 1 failed. They are not separately broken.

Proposed fixes (route to backend-engineer, then qa):
- `src/api/src/Modules/Syllabus/Tether.Syllabus/Application/Support.cs`: add `"Etc/UTC"` (and decide on `"UTC"`) to `ZoneAllowList.Zones`; `EnsureHostSupportsAll` already requires tzdata on the host, and `Etc/UTC` exists in tzdata. Check the DST-gap logic and the contract doc/enum for the zone list (`src/shared/contracts`, contract text should state the allow-list). Contract owner is the Lead Architect: this is a contract-visible allow-list change.
- Alternative or addition (frontend-engineer): `format.ts` `browserTimezone()` should map `UTC`, `Etc/UTC`, `GMT` explicitly rather than rely on the Area/Location regexp, and the form should offer a zone picker when the zone is unknown. Add unit tests in the web suite for `browserTimezone()` with `UTC` and in the Syllabus .NET tests for an `Etc/UTC` create returning 201.
- Test hygiene (qa-engineer): make the real-stack config hermetic, e.g. `timezoneId: "America/New_York"` in `playwright.reuse.config.ts`/`playwright.config.ts`, and add one explicit UTC-browser test so this regression stays covered rather than depending on the host zone.
- UX (adhd-ux-specialist, lower priority): the 400 message says the text or a date was unusable; for a not_allowed zone it misleads the student. A zone error should name the time zone.

## 1 and 3. Lecture bar wraps to 3 rows at 390 on Linux

Evidence:
- Failure 3: `phone bar height 157 > 112 at 390` (spec line 301). The slice 8b diagnostic line in the same CI log also reports `live@390: bar 157` and `disconnected@390: bar 189`.
- Failure 1: bar bottom 157, sheet top 337.6, three lines at 165-221, 233-289, 337-366; only 2 fit between bar and sheet. 157 minus the expected 112 is 45px, which is almost exactly the missing third line (it starts at 337, under the sheet).
- Trace screencast from the harness job shows the bar as three rows: [Recording pill + Connected], [Pause, Stop], [Lecture settings]. The design (see comments at geometry.spec.ts:294-297) expects Pause, Stop and Lecture settings (350px) on one row at layout width 390, leaving about 8px of slack at 390 (358px content width). On the runner the three controls need slightly more than that.
- The same font files loaded (200s), so this is not a missing font. The likely difference is Linux Chromium text shaping, hinting and rounding of advance widths for Atkinson Hyperlegible Next, versus Windows. Windows has subpixel positioning by default; headless Linux commonly rounds glyph advances, which widens text by a pixel or more across three buttons. I could not measure the button widths from the artifacts, so the exact few pixels are inferred, not proven.

Class: (a) environment, with a design-fragility element: an 8px margin is the only thing holding the bar to two rows at 390. It is also not necessarily a student-visible defect on every device, but an actual Android or Linux phone at 390 wide would likely get the 3-row bar too (45px less reading area), so the design margin deserves a look.

Proposed fixes:
- frontend-engineer, the lecture status bar component (the element with `data-lecture-status-bar`): give the Pause / Stop / Lecture settings row more slack at 390 (reduce horizontal padding or gap, or shorten "Lecture settings" label, allow the Settings control to shrink) so it holds 2 rows with at least 16px of slack. That helps real devices and makes the tests stable.
- qa-engineer: add a diagnostic step in CI (on failure) that logs the three controls' widths and `document.fonts` status, to confirm. Do not loosen the 112px bound until the product fix is decided; the bound is the UX requirement (M5).
- Optionally pin a visual baseline environment (Playwright Docker image) for geometry specs; geometry numbers are platform-sensitive by nature.

## 4 to 6. Other geometry failures (same family, lower confidence)

- Failure 4 (`geometry.spec.ts:504`): only `syllabus-review@1280 all open` failed, at one scroll position (600): axe `target-size` on the Edit button of the "Quiz 1" row. Earlier variants (collapsed at 1280, and presumably the other tested widths run before it) passed. A single scroll position failing points to a row wrapping differently or the button's 24px circle overlapping a neighbouring target or the sticky header at that offset. The artifact does not contain the axe node detail (it is only in the test message), so I cannot name the overlapping neighbour. Likely a text-metrics difference again, but it could also be a real narrow-overlap in the expanded row that Windows rounding hides.
- Failure 5 and 6 (`geometry.spec.ts:639`): the `transcript` landmark is 20px lower at 390 (glass light solid) and 84px lower at 1280 (gamer dark texture, calm light) than Paper. Only 3 of roughly 28 look variants miss, and calm light misses while calm dark (same fonts and layout) does not. That pattern fits a wrap in the content above the transcript (bar, heading, TL;DR) that sits right at a threshold, or the late swap of the non-preloaded heading faces (`preload: false` for Source Sans, JetBrains Mono and Inter in `src/web/src/app/layout.tsx`) racing the measurement even after `document.fonts.ready` (geometry.spec.ts:89). The test already allows extra heading lines only when `headingPx` grows, so a non-heading wrap fails it.

Class: (a) with a (c) component; cannot separate without a rerun. A cheap classifier: rerun the Geometry step once. If 5 and 6 move (different variants fail) they are flaky; if the same variants fail again they are deterministic platform differences.

Proposed fixes:
- qa-engineer: rerun only the Geometry step on the same commit and report which variants repeat; add to the failure message of 4 the axe node `html` and the neighbouring target; in `landmarks()` wait for the specific skin's heading face (`document.fonts.load(...)` for the face in use) before measuring.
- frontend-engineer: after #1/#3 is fixed, re-evaluate 5 and 6; they may disappear if the bar no longer sits at a wrap threshold.

## Other observations

- `api.log` contains many `SignalR HubException: rate_limited` stack traces from `LectureHub.Join` (LectureHub.cs line 62). The CI env relaxes the REST limiters but nothing in `ci.yml` relaxes the hub Join limiter; those tests still passed, so they are noise today, but they show the hub limiter is reachable in CI and could cause a future flake. Owner: backend-engineer to confirm whether the hub limit is configurable.
- Playwright cache restore warning ("Cache service responded with 400") is harmless.
- Workflow is correct about ordering: Real-stack e2e steps are serial and under the 10/min login limit; the API was started with the relaxed limits.

## Local equivalents

Not rerun in this task (instruction). The Lead's statement that these were green locally is consistent with the diagnosis: a developer machine in a non-UTC zone (#2), and Windows text metrics (#1, #3 to #6).

## Suggested routing, in order

1. backend-engineer: `ZoneAllowList` accepts `Etc/UTC`; contract note to Lead Architect. Real bug.
2. frontend-engineer: `browserTimezone()` handling and unknown-zone UX; lecture bar slack at 390.
3. qa-engineer: pin `timezoneId` in the Playwright configs plus one UTC test; CI failure diagnostics; geometry rerun.
4. After 1 to 3, rerun the CI and record the new run in `docs/RELEASE_GATES.md` (G5 stays not passed until a green run exists).
