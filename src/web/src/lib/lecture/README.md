# lecture.v1 client library (increment F1)

Pure, tested logic for the live-lecture UI. No screens here. Contract: `docs/API_CONTRACTS.md` (frozen v1) and `src/shared/contracts/lecture.v1/*.schema.json` (source of truth for wire shape).

| File | Role |
|---|---|
| `types.ts` | hand-written TS mirror of the schemas (schemas win on disagreement) |
| `validators.ts` | `createLectureValidators(schemas)`: Ajv 2020, every file registered with `addSchema` |
| `schemas.node.ts` | Node-only loader (tests/scripts) reading `src/shared/contracts` in place. Not exported from `index.ts` |
| `reducer.ts` | pure state reducer + selectors |
| `resume.ts` | `ResumeController` (reconnect/backoff/Join/REST paging), injectable timers/random/transport |
| `indicator.ts` | `deriveRecordingIndicator` (C-16) |
| `mockSource.ts` | `MockLectureSource` with fault injection, `scriptedLecture()` |
| `plainText.ts`, `PlainTextSpan.tsx` | plain-text rendering guard |
| `builders.ts` | typed synthetic event constructors |

## How schemas are loaded (no copies)
Tests call `loadValidatorsFromDisk()`, which reads `../shared/contracts/lecture.v1/*.schema.json` relative to the `src/web` working directory (override with `TETHER_CONTRACTS_DIR`, same variable as the .NET tests). Nothing is copied into `src/web`. Browser/runtime validation is not wired yet; options for when a screen needs it: (a) `next.config` `experimental.externalDir` plus static JSON imports from `../shared/contracts`, or (b) a prebuild script that writes a git-ignored bundle. Both read-only against `src/shared`. Not decided here.

Ajv options: `strict: true`, `strictTypes: false` (schemas put `minLength` beside a `$ref`), `validateFormats: false` (timestamps are constrained by `pattern`; avoids the `ajv-formats` dependency).

## Rules for UI authors
- Render server text only via `<PlainText value=.../>` or `{plainText(x)}`. Never raw-HTML APIs or markdown (a test scans `src/`).
- Announce only `selectAnnounceable(state)` (finals), throttled by the UI. `selectPartials` entries are `announceable: false`.
- Resume with `resumeSeq(state)` (contiguous frontier), not `maxSeq`.
- Drive the recording indicator from `deriveRecordingIndicator`, never from a button press.
