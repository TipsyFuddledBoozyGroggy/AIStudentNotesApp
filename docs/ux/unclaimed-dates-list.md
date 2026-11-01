# "Dates we noticed": the unclaimed-dates list on the syllabus Review screen (PO-20b, SBV-05)

Owner: neurodivergent-ux-specialist. Status: SPEC, 2026-11-01; BUILT (commit 400f1cc) and reviewed by the owner the same day, see section 14 for the review results and proposed copy changes; not human-validated (gate G4 NOT_RUN). Implements the web half of PO-20b (`docs/DECISIONS.md`: "build the unclaimed-dates list on the Review screen; student sees each noticed date with its source line and can turn it into an item") and closes security row SBR-06 together with SBV-05 (`docs/security/findings-register.md`). Depends on: `docs/API_CONTRACTS_P2.md` (syllabus.v1, FROZEN, additive only), `src/shared/contracts/syllabus.v1/syllabus-unclaimed-date.schema.json` and `syllabus-item-create-request.schema.json`, `docs/ux/redesign-round2.md` (primitives 2.7, invariants 3.3), `docs/ux/dropped-dates-notice.md` (section 6 reserved the one action this spec now uses), `docs/ux/language-guide.md` (binding), `src/web/src/lib/copyLint.ts`.

**Gate G1: not touched.** Nothing here is consent, recording, disclosure or Demo wording. The two `DemoLine`s on the Review screen keep their position and text; the list sits after the item groups, far below them, and the one new button on the dropped-dates Note comes after both Demo lines as that spec required. I grepped every string in section 5 for "record", "microphone", "consent", "Demo", "Google": none.

I read, I did not run: `Review.tsx`, `ItemRow.tsx`, `ItemEditForm.tsx`, `copy.ts`, `copyLint.ts`, `storage.ts`, `grouping.ts`, `client.ts`, `types.ts`, `Disclosure.tsx`, `globals.css` (group head, item-row areas, review-body, review-rail), `Review.test.tsx` (mount helper, dropped-notice block), `tokens.test.ts` (live-region allow-list), `syllabusDb.ts`, `syllabusHandlers.ts`, `SyllabusEndpoints.cs`, `MockRuleExtractor.cs` (`Extract`, `AddUnclaimed`), the two schemas, contract sections 3, 4, 5, 10, 15, `e2e/syllabus.spec.ts` and `e2e-screens/screens.spec.ts` (how they reach the Review page). No source file was edited.

---

## 1. Facts the design rests on (read, not assumed)

| Fact | Where | Consequence |
|---|---|---|
| `GET /syllabi/{id}/unclaimed-dates?cursor&limit` returns `{items, nextCursor}`; each item is `{id, snippet}`; `snippet` is the shared schema: `text` (<= 600, server-sliced from canonical text, never model text), `start`/`end` (UTF-16 offsets), `page`, `precision`, `context` (`{text <= 1500, start, end}`, the full line or paragraph, `context.start <= start`, `end <= context.end`). All four of `text`, `start`, `end`, `context` are null together after the retention purge. | `syllabus-unclaimed-date.schema.json`, `syllabus-common.schema.json` `$defs/snippet`, contract 4 | The row shows the **line** (`context.text`) with the date span emphasised; nothing on the row is written by a model. After the purge the list has nothing to show. |
| The mock extractor emits an unclaimed date for every date on a line with no item keyword, and for every extra date on a claimed line; `text` is the bare date hit (for example "Oct 5"), `context` is the whole line unless the line is over 1,500 code points, in which case context equals the hit. | `MockRuleExtractor.cs:108-114, 464-475`; `ExtractorTests.cs:119-123` | The snippet alone is useless to a student ("Oct 5"); the line is the evidence. Context can equal the snippet; the row must cope. |
| Cap 200 per syllabus; `counts.unclaimedDates` (0..200) is on the Syllabus resource and already parsed by the web (`parse.ts:161, 217`). | contract 10; `parse.ts` | The heading count is known before any fetch. The common case (0) costs nothing. |
| "Never auto-promoted." Promotion is listed under **Deferred** in contract 15 ("promoting an unclaimed date to an item"). | contract 3, 15 | Turning one into an item is the student's `POST /syllabi/{id}/items` (`origin: "student"`). There is no server link between the unclaimed date and the created item, and no endpoint to mark an unclaimed date dismissed or used. |
| `syllabus-item-create-request` requires `kind`, `title`, `date`; `date` is a real `YYYY-MM-DD`; "undated student items are not supported in v1"; `additionalProperties: false`. | schema | The student types the date. The client must not parse "Oct 5" into a date (that would be the client inventing what the server declined to derive). No `fromUnclaimedDateId` can be sent. |
| A student item has `snippet: null`, `derivation: "student_entered"`, confidence at most `probably_right`, is never batch-eligible, starts `undecided`. 300 items per syllabus: 200 AI plus 100 student. | contract 4, 10 | The new item lands in the groups as "Not decided" and is approved like any other. The 100 cap can be hit; its error code is not in the code table (Lead question 4). |
| **No add-item UI exists in `src/web`.** `SyllabusApi` has no `createItem` and no unclaimed-dates method; `syllabusHandlers.ts` has no `POST /syllabi/:id/items` handler; `syllabusDb.ts` serves no unclaimed list (comment at line 116). The backend route exists (`SyllabusEndpoints.cs:48, 56`). | grep `src/web/src` | The "existing add item flow" named in the task is the contract and server. The frontend builds the client method, the form, the parser and the mock. Section 9 lists them. |
| The Review groups end with "Needs a date, always last"; the first group with an undecided item is open, the rest collapsed; "Expand all" works over `groups`. | `grouping.ts:23-47`, `Review.tsx:49-52, 439-441` | The list is placed after "Needs a date" and follows the collapsed-by-default convention the student already knows. |
| The dropped-dates Note (`counts.droppedInvalid > 0`) renders two constant sentences and reserved one tertiary action, "See dates we noticed", for when this list exists. | `docs/ux/dropped-dates-notice.md` 6; `Review.tsx:351-357` | Section 3 answers "stays, changes or merges". |
| Exactly three live regions; `aria-live` only in `TldrPanel.tsx` and `Transcript.tsx`; syllabus screens announce through `useAnnouncer()`. | `tokens.test.ts:371-374`, `Announcer.tsx:30` | Every announcement here goes through `announcer.status()` / `announcer.alert()`. No new `aria-live`, `role=status` or `role=alert`. |
| Local storage: `readDraft`/`writeDraft`/`removeDraft` are user-scoped (`tether.syllabus.u.<userId>.<key>`), cleared on sign-out and on syllabus delete (`clearSyllabusDrafts` matches `edit.<syllabusId>.` keys). | `storage.ts:35-103` | Hide/added state lives here in v1 (section 4.6) and `clearSyllabusDrafts` must learn one more prefix. |

---

## 2. Decisions

| Question | Decision | Why |
|---|---|---|
| Name on screen | Section label **"Dates we noticed"** with the count of lines; the first help sentence carries the contract's full phrase ("did not turn them into items"). | The contract name is nine words; a group head is a caption. "Noticed" is the contract's own verb and is honest about what Tether did (saw a date) and did not do (make an item). It names no fault. |
| Where it sits | **Last section inside `.review-groups`, directly after "Needs a date"**, at every width. Desktop 1280: left column, under the groups, beside the sticky Calendar-step rail (the rail is untouched). Phone: after the last group, before the Calendar step. **Not in the rail.** | Reading order from most to least certain: dated items, items needing a date, then lines that did not become items. The rail holds the next step (add to calendar) and must stay short and sticky; 200 rows in a 320px sticky column would be a nested scroll trap and would narrow a 1,500-character line to a ribbon. In the groups column the rows get 680px at 1280. DOM order equals visual order at both widths (1.3.2, 2.4.3). |
| Collapsed or open | **Collapsed by default**, like every group except the first undecided one. **One exception:** when there are no decidable items at all (`d.length === 0`) and `counts.unclaimedDates > 0`, the section is the only content, so it renders open (section 4.8). | Consistency with what the student has already learned on this screen; the list is lower in value than items. Opening it by default would push the Calendar step down by up to 200 rows on a phone. |
| Expand all / Collapse all | **Do not include it.** Its open state is separate and untouched by the toolbar and by regrouping. | "Expand all" is for reading all the items; expanding 200 non-items under it would bury the Calendar step. Collapse all leaving it open is harmless (it is usually already closed). |
| Dropped-dates Note | **Stays, with the one reserved action added.** Sentences unchanged. When `counts.unclaimedDates > 0` and the list renders, the Note gains `See dates we noticed` (tertiary) which opens the section and focuses its head button, mirroring "Show them". The Note does not merge into the list and the list does not replace it. | The Note is about drops whose snippet did **not** fit (invisible anywhere); the list holds only the drops whose snippet did fit plus the ordinary noticed dates. They are different facts. The syllabus stays the complete list; the noticed-dates list is a helper (dropped-dates-notice 6). |
| The row | Caption "From your syllabus, page N:" (reuse `ITEM_COPY.sourceFrom`), then the **line** as a blockquote with the date span in bold, then two actions: **Add as item** (secondary) and **Hide** (tertiary). No title, no kind, no confidence, no decision words, no h3. | Nothing on the row is a Tether judgement, so none of the item vocabulary applies. 200 h3s reading "Oct 5" would wreck heading navigation; list semantics are enough. Bold plus words, never colour alone (1.4.1). |
| Turning one into an item | **Inline form under the row** (`.sheet`, the `ItemEditForm` fields): Title **prefilled with the line** (editable), Type default **Other**, **Date empty** and required, Time optional with All day checked. Save is `POST /syllabi/{id}/items`. | Prefilling the title turns a blank wall into a ramp (the student edits, not composes) and is the student's own syllabus text, not a model's. Leaving the date empty is the only honest option: the server did not derive one, so the client must not. "Other" presumes least. |
| Hide | In place: the Hide button becomes **Undo** in the same position, the row collapses to one line `Hidden: Oct 5`, focus stays, one status announcement. Undo never expires. Hidden rows are not rendered on the next mount; a tertiary `Show hidden and added dates (n)` at the end of the list brings them back. | The Approve/Skip -> Undo pattern the student already knows; the list visibly shrinks at 200 without losing focus or the way back. Nothing is irreversible, so nothing can be "done wrong". |
| Where hide/added state lives (v1) | **This browser, this user** (`writeDraft`, ids only). Stated on screen: "on this device". A server-side resolution needs an additive route (Lead question 1). | The contract has no endpoint for it today and the Lead owns contract text. Ids reveal nothing; cleared on sign-out and delete like drafts. Honest copy avoids the surprise on another device. |
| Density at 200 | Rows render **25 at a time** with `Show 25 more` (the last press removes the button and moves focus to the first new row's first control). Fetch happens **once, on first open** (all pages, `limit=200`, at most 2 requests). | A 200-row wall is the problem the PO's decision is meant to fix, not reproduce. Fetching lazily means the common case (never opened) costs nothing. |
| Where else | **Nowhere else.** Not on the course card, dashboard, one-at-a-time, confirm, result, Processing or NotRead. No badge, no count outside this screen. | Quiet mode (phase2 11; language guide 6). |
| After the purge | The section is **not rendered** when `retention.canonicalTextPurged` (every snippet is null); the "See dates we noticed" action is hidden too. The purged Note already says the wording was removed. | A list of 200 empty rows says nothing. |

---

## 3. Text wireframes

Desktop 1280, Paper, the section collapsed (`=` sheet, `.` ledger tint, `|` note stripe, `[[ ]]` the one fill, `_x_` tertiary, `[ ]` secondary):

```
        | Review your syllabus items                                   (display h1)
        | (Demo calendar) Demo mode: nothing here goes to Google Calendar.          <- unchanged
        | (Demo) Demo mode: items are found by a test routine, not a real AI model. <- unchanged
        | | Parts of this syllabus could not be turned into items.                 (only when droppedInvalid > 0)
        | | Your syllabus is the complete list. Check it for dates that are not shown here.   _See dates we noticed_
        | We found 6 items. 2 approved, 0 skipped, 4 not decided.   1 needs a date. _Show them_
        | Times are in America/New_York.
        | Group by (Week | Type)   _Review one at a time_   _Expand all_
        |
        | v Week of Sep 14 to 20, 2 items, 1 not decided              | . Calendar step                 .
        | . Quiz 1 in class   Mon, Sep 14 · All day   [Undo] _Skip_ _Edit_ | . 2 approved, 0 skipped,        .
        | . ...                                                       | .   4 not decided.               .
        | > Week of Oct 12 to 18, 2 items, 2 not decided              | . [[ Add 2 approved items to     .
        | > Needs a date, 1 item, 1 not decided                       | .     the demo calendar ]]       .
        | > Dates we noticed, 12 lines                                | .                                .
        |                                                             (sticky rail, unchanged)
        | Your data ...
```

The same section open, first rows (the head is `h2 > button[aria-expanded=true]`, class `group-head`, like the groups):

```
        | v Dates we noticed, 12 lines
        |   These lines from your syllabus have a date, but Tether did not turn them into items.   (help)
        |   Each line is shown word for word. You can add one as an item, or hide it.              (help)
        |   Hiding a line keeps it out of this list on this device. You can undo it at any time.   (help)
        | . From your syllabus, page 2:                                      [ Add as item ]  _Hide_
        | . | Bring a calculator on **Oct 5** for the in-class exercise.
        | .|-----------------------------------------------------------------------------------------
        | . From your syllabus, page 3:                                      [ Add as item ]  _Hide_
        | . | Field trip permission slips due **Nov 2**; see the course site for the form.
        | .|-----------------------------------------------------------------------------------------
        | . Hidden: **Oct 19**                                                               _Undo_
        | .|-----------------------------------------------------------------------------------------
        | . Added as an item: Field trip permission slips                                 _Go to the item_
        | .|-----------------------------------------------------------------------------------------
        | . ... (25 rows)
        |   _Show 25 more_
        |   _Show hidden and added dates (3)_                      (only on a later visit, when some exist)
```

`**Oct 5**` is `<strong>` inside the quote (the date span the server found). The `|` before the quote is the existing `border-l-2 border-hair pl-3` source block.

A row with the add form open (desktop; `.sheet`, same fields and order as `ItemEditForm`):

```
        | . From your syllabus, page 2:
        | . | Bring a calculator on **Oct 5** for the in-class exercise.
        | . =====================================================================================
        | . = Title
        | . = [Bring a calculator on Oct 5 for the in-class exercise.          ]
        | . = This starts as the line from your syllabus. You can change it.
        | . = Type        [Other v]
        | . = Date
        | . = Example: 2026-10-14
        | . = Read the date from the line above and enter it here.
        | . = [__________]
        | . = Time (optional) [______]   [x] All day
        | . = [[ Add item ]]  [ Cancel ]
        | . =====================================================================================
```

Phone 390 (one column, DOM order; actions stack under the quote below 768 exactly as `.item-row` does today):

```
Review your syllabus items
(Demo calendar) Demo mode: nothing here goes to Google Calendar.
(Demo) Demo mode: items are found by a test routine, not a real AI model.
We found 6 items. 2 approved, 0 skipped, 4 not decided.
1 needs a date. _Show them_
...
> Needs a date, 1 item, 1 not decided
v Dates we noticed, 12 lines
  These lines from your syllabus have a date, but Tether did not turn them into items.
  Each line is shown word for word. You can add one as an item, or hide it.
  Hiding a line keeps it out of this list on this device. You can undo it at any time.
. From your syllabus, page 2:
. | Bring a calculator on **Oct 5** for the in-class exercise.
. [ Add as item ]  _Hide_
.|--------------------------------------------
. Hidden: **Oct 19**   _Undo_
.|--------------------------------------------
  _Show 25 more_
| Calendar step
| 2 approved, 0 skipped, 4 not decided.
| [[ Add 2 approved items to the demo calendar ]]
Your data ...
```

No items at all, some noticed dates (section 4.8): the empty-state sentences change and the section renders open, with no rail.

```
No items were made from this syllabus, but some dates were noticed.
They are listed below. You can add any of them as an item.
v Dates we noticed, 4 lines
  ... rows ...
```

---

## 4. Component behaviour (for `Review.tsx` and a new `UnclaimedDates.tsx`)

### 4.1 Render conditions
- Render the section when `items !== null`, `!syllabus.retention.canonicalTextPurged`, and `syllabus.counts.unclaimedDates > 0`. Otherwise no DOM node.
- Place it as the **last child of `.review-groups`**. When `d.length === 0`, `.review-body` renders with `.review-groups` holding only this section and **no `.review-rail`** (nothing to add to a calendar). Below 1280 nothing changes; from 1280 the single-column case is just a grid with an empty second track, which is fine, or the frontend drops the grid class when there is no rail (either; geometry test pins whichever).
- Not rendered in one-at-a-time, confirm or result modes (those return before the list, as today).

### 4.2 Head
- `section[aria-labelledby="unclaimed-h"]` > `h2#unclaimed-h` > `button.group-head.min-h-[44px]` with `aria-expanded`, `aria-controls="unclaimed-list"`, `<Chevron open>` first, then `<span class="font-bold text-ink">Dates we noticed</span><span class="text-muted">, 12 lines</span>`. Visible text is the accessible name (2.5.3).
- Count shown: the number of rows **not** hidden or added (server count minus local states; after the fetch, the real list minus local states). When every row is hidden or added: `, all hidden or added`.
- Open state: component state `unclaimedOpen`, default false (true in the 4.8 case). Not in the groups' `open` set; Expand all / Collapse all and `chooseBy` do not touch it. Not persisted.
- Focus: the head button receives focus only from "See dates we noticed" (section 4.9), never on mount, never on a fetch result.

### 4.3 Fetch
- On the first time the section opens (or the first time "See dates we noticed" is pressed), call `api.listUnclaimedDates(syllabus.id)` (new; follows `nextCursor` with `limit=200`, at most 2 pages, parses every page with `parseUnclaimedPage`, rejects anything that is not the schema). Cache in state for the life of the component; never refetch on `tick` (unclaimed dates do not change after extraction; a syllabus re-read is a new syllabus).
- Loading: `<p class="help">Getting the dates we noticed.</p>` inside the open body. Plain text, not live.
- Error: `div.note.note-notice` with `The dates we noticed could not be shown. Nothing was changed.` and a `btn-secondary` `Try again` that repeats the fetch. Not live, not focused (a fetch is not the student's press; the itemsError pattern).
- A row whose `snippet.text` is null while the syllabus is not purged should not occur; if it does, skip the row and count nothing (defensive, no copy).

### 4.4 Row (`li.ledger-row.item-row[data-unclaimed-id]` in `ul#unclaimed-list.ledger`)
- Reuse the `.item-row` grid areas: `.item-main` holds the caption and the quote; `.item-actions` the two buttons (right of the quote from 768, under it below); `.item-extra` the open form, the hidden line or the added line.
- Caption: `<p class="text-sm text-muted">{ITEM_COPY.sourceFrom(snippet.page)}</p>`.
- Quote: compute `a = snippet.start - context.start`, `b = snippet.end - context.start`. If `context !== null`, `0 <= a < b <= context.text.length` and `context.text.slice(a, b) === snippet.text`, render one `<blockquote class="whitespace-pre-line break-words">` with `<PlainText value={before} /><strong class="font-semibold"><PlainText value={snippet.text} /></strong><PlainText value={after} />`. Otherwise (offsets disagree with the text, or context is null) render the snippet as its own blockquote and, when `context && context.text !== snippet.text`, the existing `ITEM_COPY.sourceAround` caption plus the context blockquote (the `ItemRow` fallback). Text nodes only (contract C-28); never `dangerouslySetInnerHTML`, never a regex over the text to find the date.
- When `context.text === snippet.text` (a very long line, or `precision: "line"`), the quote is the snippet in bold: nothing else to show.
- Actions: `button.btn-secondary.btn-sm` visible `Add as item`, `aria-label={addName(snippet.text)}`, `aria-expanded` while the form is open; `button.btn-tertiary.btn-sm` visible `Hide`, `aria-label={hideName(snippet.text)}`. The snippet text in the name is the short date hit ("Oct 5"), so names stay short; duplicates ("Oct 5" twice) are allowed and accepted.
- 44px: both buttons are the `btn-sm` tier that `ItemRow` already uses for Approve/Skip (44px hit area by the primitive). The head is `min-h-[44px]`.

### 4.5 Hide and Undo
- Press Hide: write `{s: "hidden"}` to local state (4.6); the row re-renders as `.item-main` > `<p class="text-sm text-muted">Hidden: <strong class="font-semibold text-ink">{snippet.text}</strong></p>` and `.item-actions` > the **same button element** now visible `Undo`, `aria-label={undoName(snippet.text)}`; focus stays on it; `announcer.status(hiddenMsg(snippet.text))`. The caption and quote are not rendered (the row shrinks to one line).
- Press Undo: remove the local state; the row renders in full again; the same element is `Hide` again; focus stays; `announcer.status(shownMsg(snippet.text))`.
- Hidden rows stay in place for the life of the component (Undo never expires). On the next mount they are not rendered (4.7).

### 4.6 Local state (v1, this browser)
- Key `unclaimed.<syllabusId>.<unclaimedId>` through `writeDraft`/`readDraft`/`removeDraft` (user-scoped). Value `{ s: "hidden" } | { s: "added", itemId: string }`. Ids only; never the text.
- `storage.ts`: `clearSyllabusDrafts(syllabusId)` must also remove `unclaimed.<syllabusId>.` keys (one more prefix beside `edit.`). `clearAllDrafts` already removes everything under the user prefix. Form drafts use key `unclaimed-add.<syllabusId>.<unclaimedId>` and are removed by the same prefix rule (`unclaimed` is a prefix of `unclaimed-add`; make the match explicit rather than accidental).
- With no signed-in owner known, `readDraft` returns null and every row shows; `writeDraft` is a no-op. The page works.

### 4.7 Reconciliation on mount
- `added` whose `itemId` is **not** in `items` (the student deleted the item): treat as kept; show the row in full; remove the stale state.
- `added` whose item exists: not rendered by default; counted in `Show hidden and added dates (n)`.
- `hidden`: not rendered by default; counted the same way.
- `Show hidden and added dates (n)` is a `btn-tertiary.btn-sm` after the last row (and after `Show 25 more` when both exist). Press: render those rows inline at the end of the list in their state (hidden rows as 4.5's one-liner with Undo; added rows as 4.10's line with "Go to the item"); the button leaves the DOM; focus moves to the first revealed row's control. Nothing announced (the student is looking at the result).

### 4.8 No items, some noticed dates
- When `items && d.length === 0 && counts.unclaimedDates > 0 && !purged`: the empty-state block renders `REVIEW_COPY.emptyNoticed` and `emptyNoticedBody` instead of `empty` / `emptyBody`, then `.review-body` with the section **open**. The dropped Note still renders above when `droppedInvalid > 0`.
- When `counts.unclaimedDates === 0`: the existing empty state, unchanged.

### 4.9 The dropped-dates Note action
- In the existing `[data-dropped-notice]` Note, after the second sentence, add `button.btn-tertiary.btn-sm` visible `See dates we noticed` (same accessible name) **only when** the section renders (4.1). The two sentences do not change (`Review.test.tsx` byte-identity on the `p` elements stays true; the test that asserts "no button" inside the Note changes to "no button unless the list renders", section 9).
- Press: `setUnclaimedOpen(true)`, then focus the section's head button (the "Show them" mechanism: a `focusGroup`-style effect that runs after the open state renders). No announcement, no scroll beyond what focus causes (`scroll-padding-top` keeps it clear of the header).

### 4.10 Add as item
- Press `Add as item`: the form (`UnclaimedAddForm`, built from the `ItemEditForm` fields; frontend may extract a shared fields component) opens in `.item-extra` as `form.sheet[aria-label=legend(snippet.text)]`; the Hide button is hidden while the form is open (one decision at a time); `aria-expanded=true` on Add as item.
- Initial values: `title` = `context.text` (or `snippet.text` when context is null) with whitespace collapsed to single spaces, trimmed, cut to **200 code points at the last space before the cap** (the server cap is code points, contract 2.2); `kind: "other"`; `date: ""`; `time: ""`; `allDay: true`. The title help sentence sits under the field (`aria-describedby`). The date field has two descriptions: `EDIT_COPY.dateHint` ("Example: 2026-10-14") and `dateHelp`.
- Initial focus: the **Title** field (reading order; the student sees the prefilled line first). Alternative for G4: focus Date, the one field Tether cannot fill.
- Validation: on blur and on save, never per keystroke; the existing `validate` rules (`titleEmpty`, `titleLong`, `dateEmpty`, `dateFormat`, `dateReal`, `time`); a failed save focuses the first invalid field. The date is **required** here (the schema requires it) so `dateEmpty` always applies.
- Draft: values are kept under `unclaimed-add.<syllabusId>.<unclaimedId>` while typing and shown with `EDIT_COPY.kept` ("We kept your changes.") on reopen; removed on save or cancel. Reload mid-form: the row shows `Add as item` with `aria-expanded=false`; pressing it restores the draft (mirrors `hasDraft` in `ItemRow`; frontend may also auto-open rows with a draft as `ItemRow` does; either, pinned by a test).
- Save: `api.createItem(syllabus.id, body, idempotencyKey)` where `body` is `{kind, title, date, allDay, time: allDay ? null : time}`, `timezone` omitted (server uses `defaultTimezone`), `notes` omitted. One fresh `Idempotency-Key` per press; reuse it only to retry the same body after a network failure; any answered error discards it. `aria-disabled` on Add item while busy, label `Adding...`, width fixed.
- 201: `removeDraft`; local state `{s: "added", itemId}`; `applyItems`-style insert of the created item into `items` (new element; sort is done by `groupItems`); open the group that now holds it (`groupItems(list, by)` find, add key to `open`); the row renders 4.11's added line; focus moves to its `Go to the item` button (the pressed control is gone); `announcer.status(addedMsg(item.title))`. Nothing scrolls.
- Errors (the `ItemEditForm` and `ItemRow` patterns; never server text): `400` -> `EDIT_COPY.errors.server` ("One thing to check: a value could not be used. Your changes are kept."), focus Title; `409 invalid_state` -> `form.notReady`; `429` -> `ACTION_COPY.rateLimited`; network -> `ACTION_COPY.network` (values kept, key kept); `401` -> `ActionProblem kind="session"` plus `announcer.alert("syllabus-session", ACTION_COPY.sessionHeading)` as `ItemRow` does; the student-item cap -> `form.limit` **only if** the Lead confirms the code (question 4), else `ACTION_COPY.generic`. Messages render as `<p class="font-semibold">` inside the form, the form stays open, values stay.
- Cancel / Escape: `removeDraft`; form closes; focus returns to `Add as item`.

### 4.11 Added line and "Go to the item"
- `.item-main` > `<p class="text-sm">Added as an item: <PlainText value={item.title} /></p>`; `.item-actions` > `button.btn-tertiary.btn-sm` visible `Go to the item`, `aria-label={goToItemName(item.title)}`. The caption and quote are not rendered.
- Press: open the group holding `itemId` and `setFocusItem({ id: itemId, target: "row" })` (existing effect; focuses the row's first button, Approve). The student approves there like any item. No announcement.
- If the item no longer exists in `items` when pressed (deleted in another tab, then `tick` re-read): the row reverts to kept (4.7) and focus stays on the row's `Add as item`.

### 4.12 Paging
- Render the first 25 kept rows; `button.btn-tertiary.btn-sm` `Show 25 more` (`showMore(min(25, left))`) after the list. Press: render the next 25; focus stays on the button while rows remain; on the press that reveals the last rows the button leaves the DOM and focus moves to the first newly shown row's `Add as item`. Hidden/added rows revealed by 4.7 are not paged (they are shown on request).

### 4.13 What does not change
- The summary line, the toolbar, the groups, the Calendar step rail, the skip link, the Demo lines, "Your data", the links. `tick` re-reads do not touch the cached unclaimed list. `ItemRow`, `OneAtATime`, `Sync`, `Batch` unchanged. The new student item is excluded from batch approve by the server (`edited`/confidence rules); the client's `clearItems` already requires `looks_right`, which a student item never has.

---

## 5. Wording (DRAFT, for `copy.ts`; G4 NOT_RUN)

Checked by hand against `copyLint.ts` `FORBIDDEN` and `FORBIDDEN_SYLLABUS`: no exclamation mark, emoji, "just", "lost", "missed", "behind", "fail", "missing", "pending", "undecided", "remaining", "make sure", "you should/must/need to", "verified", "%", "percent", "error", "simply", "easy". Longest sentence 15 words (limit 20). `collectStrings` calls every function with `("Biology 101", "00:12", 3, 2)`; none of the templates produces a forbidden pattern with those arguments. `copy.test.ts` lints the whole module automatically once the registry is exported.

```ts
/** PO-20b (docs/ux/unclaimed-dates-list.md): the "Dates we noticed" section on the Review list. Mock only; G4 NOT_RUN. */
export const UNCLAIMED_COPY = {
  label: "Dates we noticed",
  lines: (n: number) => (n === 1 ? "1 date" : `${n} dates`),
  allHandled: "all hidden or added",
  help: "These lines from your syllabus have a date, but Tether did not turn them into items.",
  help2: "Each line is shown word for word. You can add one as an item, hide it, or leave it.",
  help3: "Hiding a line keeps it out of this list on this device. You can undo it at any time.",
  add: "Add as item",
  addName: (t: string) => `Add as item: ${t}`,
  hide: "Hide",
  hideName: (t: string) => `Hide: ${t}`,
  hidden: "Hidden:",
  undo: "Undo",
  undoName: (t: string) => `Undo hiding ${t}`,
  hiddenMsg: (t: string) => `Hidden: ${sentence(t)}`,
  shownMsg: (t: string) => `Shown again: ${sentence(t)}`,
  added: "Added as an item:",
  goToItem: "Go to the item",
  goToItemName: (t: string) => `Go to the item: ${t}`,
  addedMsg: (t: string) => `Added as an item: ${sentence(t)} It is not decided yet.`,
  showMore: (n: number) => `Show ${n} more`,
  showHandled: (n: number) => `Show hidden and added dates (${n})`,
  loading: "Getting the dates we noticed.",
  loadError: "The dates we noticed could not be shown. Nothing was changed.",
  tryAgain: "Try again",
  form: {
    legend: (t: string) => `Add an item from: ${t}`,
    titleHelp: "This starts as the line from your syllabus. You can change it.",
    dateHelp: "Read the date from the line above and enter it here. Add the year if the line leaves it out.",
    save: "Add item",
    busy: "Adding...",
    cancel: "Cancel",
    conflict: "No more items can be added to this syllabus. Nothing was changed. Your changes are kept.",
    notReady: "Items can't be added while this syllabus is being read. Nothing was changed.",
    limit: "This syllabus holds as many added items as it can. Nothing was changed. Your changes are kept.",
  },
} as const;
```

Applied from section 14.2 by the Lead's decision (R1, R2a, R4a to R4d; 2026-11-01): `sentence(t)` is the copy.ts helper that closes a sentence after inserted text with exactly one period; `form.conflict` is shown for any 409 until the server names distinct codes.

Additions to `REVIEW_COPY`:

```ts
  /** PO-20b: the one action the dropped-dates Note may carry (dropped-dates-notice.md 6), shown only when the list renders. */
  droppedSee: "See dates we noticed",
  /** Empty items but counts.unclaimedDates > 0 (unclaimed-dates-list.md 4.8). */
  emptyNoticed: "No items were made from this syllabus, but some dates were noticed.",
  emptyNoticedBody: "They are listed below. You can add any of them as an item.",
```

Reused as-is: `ITEM_COPY.sourceFrom`, `ITEM_COPY.sourceAround`, `EDIT_COPY.title/type/date/dateHint/time/allDay/kept/errors.*/kinds`, `ACTION_COPY.network/rateLimited/generic/sessionHeading/sessionBody/signIn`.

Why these words:
- "have a date, but Tether did not turn them into items" states both halves of the fact with Tether as the subject; the student did nothing.
- "shown word for word" says there is no paraphrase on the row, which is the whole point of the list.
- "on this device" is the truth about where Hide lives in v1 (section 2); without it the list would look broken on a second device.
- "Add as item" / "Go to the item" use the screen's noun; "Hide" says what happens and is undone by "Undo", the word already on every row.
- "Added as an item: X. It is not decided yet." tells a screen-reader user the next step without a nudge ("not decided" is the existing status word).
- The 4.8 empty state replaces "No dates or deadlines were found" because that sentence would be false beside a list of noticed dates.

Not chosen: "Dismiss" (jargon, and it hides what happens), "Ignore" (judgement), "Not needed" (ambiguous out of context), "Leftover dates" (deficit framing), "Unclaimed dates" (engineering word), "Create item" (the screen says "items" and "add").

---

## 6. Keyboard, focus, screen reader

Tab order inside the section (DOM = visual at every width): head button -> (open) row 1 `Add as item` -> row 1 `Hide` -> row 2 ... -> `Show 25 more` -> `Show hidden and added dates (n)`. With a form open in row k: row k `Add as item` (expanded) -> Title -> Type -> Date -> Time -> All day -> Add item -> Cancel -> row k+1. The help sentences are not focusable. After the section comes the Calendar step (rail or in-flow), as today.

| Action | Key | Focus after |
|---|---|---|
| Open / close the section | Enter or Space on the head | stays on the head |
| See dates we noticed (Note) | Enter / Space | the section's head button (opened) |
| Add as item | Enter / Space | Title field in the form |
| Save the form | Enter in a field, or Add item | `Go to the item` on the same row (success); first invalid field (validation); Title (server message) |
| Cancel the form | Escape anywhere in the form, or Cancel | `Add as item` on the same row |
| Hide | Enter / Space | same element, now Undo |
| Undo | Enter / Space | same element, now Hide |
| Go to the item | Enter / Space | the item's first button in its (opened) group |
| Show 25 more | Enter / Space | stays on the button; on the last batch, the first new row's `Add as item` |
| Show hidden and added dates | Enter / Space | the first revealed row's control |

Screen reader, one row (NVDA/JAWS/VoiceOver wording varies; this is the content): "list, 25 items" ... "From your syllabus, page 2:" "blockquote, Bring a calculator on Oct 5 for the in-class exercise." "Add as item: Oct 5, button, collapsed" "Hide: Oct 5, button". After Hide: status "Hidden: Oct 5." and the focused control reads "Undo hiding Oct 5, button". After a save: status "Added as an item: Bring a calculator on Oct 5. It is not decided yet." and focus "Go to the item Bring a calculator on Oct 5, button". Bold is not announced; the date is carried in the button names instead, which is why the names end with the snippet text.

Live regions: **exactly three remain**. All announcements are `useAnnouncer().status(...)` (hidden, shown again, added) or `.alert(...)` (session, like `ItemRow`). No `aria-live`, `role=status`, `role=alert`, `aria-busy` on the section; `aria-busy` only on the Add item button while a request is in flight. The loading sentence and the fetch error are plain text. `tokens.test.ts` allow-list and the live-region count tests stay as they are.

WCAG 2.2 AA targets: 1.3.1 (section with named h2, `ul`/`li`, form labels), 1.3.2 and 2.4.3 (DOM order = visual = task order), 1.4.1 (bold plus the button names and the words "Hidden:" / "Added as an item:"; never colour), 1.4.3 (ink and muted on ledger, already in the contrast matrix), 1.4.4 / 1.4.10 (68ch measure, actions stack below 768, no horizontal scroll at 320 or 400% zoom; a 1,500-character line wraps with `break-words`), 1.4.12 (text spacing survives; nothing fixed-height), 2.1.1 (all actions are buttons), 2.2.1 (Undo never expires), 2.4.6 (heading names the list), 2.4.7 / 2.4.11 (the section is in the groups column, so the sticky rail can never cover it), 2.5.3 (visible label starts every accessible name), 2.5.8 (44px: head, both row buttons, form controls, tertiaries), 3.2.2 (no context change on open, hide, show more), 3.3.1 / 3.3.3 (field errors with suggestions, `aria-describedby`, `aria-invalid`), 3.3.4 (Hide is reversible; an added item can be skipped or deleted), 3.3.7 (draft kept), 4.1.2 (`aria-expanded` / `aria-controls` on the head and on Add as item), 4.1.3 (status through the existing region).

Reduced motion: nothing moves; the chevron swaps glyphs (no rotation), `hidden` toggles instantly. Forced colours / high contrast: `.ledger` outline and row rules are already `CanvasText`; `<strong>` keeps weight; buttons keep their borders. Low-stimulation: paint only; the section is not a safety surface and has no fill to strip.

---

## 7. Design-system conformance (no new CSS, no new tokens)

Used: `.group-head` (head button), `.ledger` / `.ledger-row` (list), `.item-row` / `.item-main` / `.item-actions` / `.item-extra` (row areas), `.help` (help sentences, loading), `.note.note-notice` (fetch error), `.sheet` (form), `.field` / `.field-label` (form fields, as `ItemEditForm`), `.btn-secondary.btn-sm`, `.btn-tertiary.btn-sm`, `.btn-primary.btn-sm` (Add item inside the sheet, as Save in the edit form), `border-l-2 border-hair pl-3` (quote block), `text-sm text-muted`, `font-semibold`, `whitespace-pre-line break-words`, `<Chevron>`, `<PlainText>`. Sans throughout: everything on the row is verbatim or a control; nothing is synthesis, so no serif. Test hooks: `data-unclaimed-id` on rows, `data-unclaimed-section` on the section; no visible counts beyond the head.

One primary per region: the form's `Add item` is the only filled button inside the form sheet (the edit form does the same); the Calendar step's fill is in another region. If the Lead prefers no second fill on the screen, `Add item` becomes `btn-secondary` and the spec accepts it.

---

## 8. Quiet mode, tone, equity

- Nothing is automatic: no fetch until opened, no open on mount (except 4.8, where it is the only content), no announcement on open, no badge, no count anywhere but this screen, nothing on the course card or dashboard, no reminder about hidden lines.
- Nothing is inferred about the student. Hide is a preference, never read as "ignored a deadline".
- No way to do it wrong: every action is one step and reversible on the same row; the list never says a date was skipped, forgotten or left; the empty-state sentence does not contradict the list.
- Honest about limits: "on this device"; the dropped Note still points to the syllabus as the complete list.
- Low working-memory cost: the line is on screen while the date is typed; the title starts filled; 25 rows at a time; the head carries the count so the student can decide not to open it.

---

## 9. Build notes (frontend-engineer; file-disjoint from backend)

| Piece | Where | Note |
|---|---|---|
| `UnclaimedDate` type, `parseUnclaimedPage` | `lib/syllabus/types.ts`, `parse.ts` | Reuse `parseSnippet` (`parse.ts:299`, already enforces the R2-04 null-together rule); reject unknown members; cap 200. Add schema-valid and invalid fixtures beside the other syllabus.v1 web fixtures (none exist for `unclaimed-date` under `src/shared/contracts/syllabus.v1/` today: glob found schemas only). |
| `listUnclaimedDates(id)`, `createItem(id, body, idempotencyKey)` | `lib/syllabus/client.ts`, `requests.ts` | `createItem` posts `syllabus-item-create-request` with `Idempotency-Key`; parse the 201 with `parseItem`. |
| `UnclaimedDates.tsx`, `UnclaimedAddForm.tsx` (or a shared fields component extracted from `ItemEditForm`) | `components/syllabus/` | Sections 4.2 to 4.12. |
| `Review.tsx` | section placement (4.1), 4.8 empty state, 4.9 Note action, item insert on 201 | Keep the dropped Note's two `p` elements byte-identical. |
| `copy.ts` | `UNCLAIMED_COPY`, three `REVIEW_COPY` keys | Section 5 verbatim. |
| `storage.ts` | `clearSyllabusDrafts` learns `unclaimed.<syllabusId>.` and `unclaimed-add.<syllabusId>.` | SB-06 parity. |
| MSW | `syllabusHandlers.ts`: `GET /syllabi/:id/unclaimed-dates`, `POST /syllabi/:id/items`; `syllabusDb.ts`: unclaimed rows per scenario (e.g. 3 for `ready`, 0 for `purged` with null snippets, a text marker `mock:unclaimed-200` for 200), `recount` sets `counts.unclaimedDates` from the entry, student items `origin: "student"`, `snippet: null`, `confidence: "probably_right"`, `decision: "undecided"` | The mock must enforce 409 `invalid_state` outside ready/partial so the error path is testable. |

---

## 10. Acceptance checklist (frontend and QA tick; evidence is a test name or a capture)

1. `counts.unclaimedDates: 0` on ready, partial, hostile, purged fixtures: no `[data-unclaimed-section]`, no "Dates we noticed" text, no `See dates we noticed` button, no request to `/unclaimed-dates`.
2. `counts.unclaimedDates: 12`, ready: the section is the last child of `.review-groups`, after the `Needs a date` h2 and before `[data-calendar-step]` in DOM order; its head is `h2 > button.group-head[aria-expanded=false][aria-controls=unclaimed-list]` named `Dates we noticed, 12 lines`; no request to `/unclaimed-dates` has been made.
3. Opening the head sends exactly one `GET /syllabi/{id}/unclaimed-dates` (two when `nextCursor` is set in the mock); while pending the body shows `Getting the dates we noticed.` with no live region; then 12 `li[data-unclaimed-id]` (25 max) each with the caption, one blockquote whose `strong` text equals `snippet.text` and whose full text equals `context.text`, and buttons named `Add as item: {text}` and `Hide: {text}`.
4. A fixture whose offsets do not match the context text renders the snippet blockquote, then `The line around it:` and the context blockquote; no `strong`; nothing thrown. A fixture with `context.text === snippet.text` renders one bold blockquote and no second caption.
5. Fetch failure: `note.note-notice` with `The dates we noticed could not be shown. Nothing was changed.` and `Try again`; the announcer log is empty; `Try again` repeats the request.
6. Hide: the same button element becomes `Undo` (`aria-label` `Undo hiding {text}`), keeps focus, the row shows `Hidden: {text}` and no quote, the head count drops by one, the status log holds exactly `Hidden: {text}.`; Undo reverses all of it with `Shown again: {text}.`; `localStorage` holds `tether.syllabus.u.<user>.unclaimed.<syl>.<unc>` = `{"s":"hidden"}` and never any snippet text. Remount: the row is not rendered; `Show hidden and added dates (1)` is; pressing it renders the one-line hidden row with Undo and moves focus to it.
7. Add as item: the form opens in the row with `aria-label` `Add an item from: {text}`, Title prefilled with the collapsed line cut at 200 code points on a space, Type `Other`, Date empty with both descriptions, All day checked, focus on Title; Hide is not in the DOM while the form is open. Save with an empty date focuses Date with `Enter a date like 2026-10-14.` and sends nothing. A valid save sends one `POST /syllabi/{id}/items` with an `Idempotency-Key` and body `{kind, title, date, allDay, time}` only (no `timezone`, no `notes`, no extra member); on 201 the row shows `Added as an item: {title}` and `Go to the item` (focused), the item appears in its group (opened) as `Not decided` with no `See where this came from`, the summary count rises by one, the status log holds exactly `Added as an item: {title}. It is not decided yet.`; `Go to the item` focuses that row's first button.
8. Errors: 400 shows `EDIT_COPY.errors.server` inside the form with focus on Title and values kept; 409 `invalid_state` shows `form.notReady`; network keeps values and the same key for the retry; 401 renders the session `ActionProblem` and one alert. In every case no item is inserted locally.
9. Remount with an `added` state whose `itemId` is not in the items: the row renders in full with `Add as item`; the stale key is removed.
10. Paging: a 200-row fixture renders 25 rows and `Show 25 more`; seven presses render all 200; after the seventh the button is gone and focus is on row 176's `Add as item`; `Expand all` does not open the section; `Collapse all` leaves it as it was.
11. Dropped Note: with `droppedInvalid > 0` and `unclaimedDates > 0`, the Note holds its two unchanged `p` elements plus one `btn-tertiary` named `See dates we noticed`; pressing it opens the section and focuses its head; with `unclaimedDates: 0` or purged, the Note has no button (the existing "no button" assertion becomes conditional).
12. Empty items plus `unclaimedDates > 0`: `No items were made from this syllabus, but some dates were noticed.` and `They are listed below. You can add any of them as an item.` render; `No dates or deadlines were found` does not; the section renders **open**; no `[data-calendar-step]`.
13. Purged fixture with `unclaimedDates > 0`: no section, no Note button; the purged Note renders as today.
14. Not rendered in one-at-a-time, confirm, result, `SyllabusCard`, `NotRead`, `Processing`.
15. Live regions: the existing count tests still report three; `document.querySelectorAll('[aria-live],[role=status],[role=alert]')` inside the section is empty in every state.
16. axe (wcag2a/aa/21aa/22aa/best-practice): 0 violations with the section closed, open with 25 rows, a form open, a hidden row, an added row, and the 4.8 state, at 390 and 1280 in Paper, Fog and one skin; `target-size` clean with the rail present at 1280 x 900 (the section is in the left column).
17. Geometry at 1280: the section's left edge equals the groups' left edge; its top is below the last group; no row box intersects `.review-rail`; at 320 no horizontal overflow with a 1,500-character context line.
18. `copy.test.ts` passes with `UNCLAIMED_COPY` and the three `REVIEW_COPY` keys; add one assertion that no string in `UNCLAIMED_COPY` contains a digit except through a template argument (the head count is the only number, and it comes from data).
19. The two `DemoLine`s are byte-identical in text and position (existing pins stand).

---

## 11. Test plan

**Unit (Vitest + Testing Library, MSW node server):** new `UnclaimedDates.test.tsx` covering checklist 2 to 10 and 15 with the `renderWithAnnouncer` helper and a `syllabusDb` scenario that seeds unclaimed rows; `Review.test.tsx` additions for 1, 11 to 14, 19 (the dropped-notice block's "no button" test becomes conditional); `copy.test.ts` for 18; `parse.test.ts` for the page parser with valid, offset-mismatch, purged-null and over-cap fixtures; `storage.test.ts` for `clearSyllabusDrafts` removing both new prefixes; one axe run per state for 16.

**Harness e2e (Playwright, `playwright.harness.reuse.config.ts`): a decision is needed (Lead question 3).** There is no syllabus harness page; the lecture harness is a fixture route (`components/lecture/harness/HarnessFixture.tsx`), while the screens spec and the real-stack spec reach the Review page through the real API with synthetic text. Options:
- (A) Add a syllabus harness fixture route that renders `<Review>` with an in-memory `SyllabusApi` (200 unclaimed rows, a failing fetch toggle, a 400/409 create toggle). Gives deterministic geometry (17), density (10) and the axe matrix (16) without the API or the dev-login limit. Recommended for the density and a11y evidence.
- (B) Real stack only: paste a syllabus whose lines carry dates but no keyword (for example `Oct 5 bring a calculator`), which the mock extractor turns into unclaimed dates (`MockRuleExtractor.cs:108-110`); 200 lines give the cap. Covers the flow end to end (open, add as item with a typed date, approve it, hide, reload, show hidden). Recommended for `e2e/syllabus.spec.ts` as one added step, kept to 1 to 3 unclaimed dates so the run stays short.
Both are useful; A is the one that needs a decision because it adds a route.

**Screens (`e2e-screens/screens.spec.ts`):** add one unclaimed-date line to the synthetic "History of Law" text so `syllabus-review` captures the collapsed head at 390 and 1280 in the base looks; a second capture `syllabus-review-noticed-open` is optional.

**Real-stack contract:** `SyllabusOwnershipTests.cs:46` already covers IDOR on the unclaimed route; the create route's ownership and `invalid_state` tests exist on the server. No backend change is required for v1.

**Not covered by any automated test:** whether "Dates we noticed", "Hide" and the prefilled title are understood (G4). Questions for `human-research-coordinator`: "What do you think these lines are, and why are they not items?" "After pressing Hide, what do you expect on your phone?" "Did the filled-in title help or get in the way?" "Should the date field be focused first instead of the title?"

---

## 12. Security, privacy, data integrity, latency, failure paths

- **Untrusted text:** `snippet.text` and `context.text` reach the page for the first time. They render only as text nodes through `PlainText` (contract C-28, same as `ItemRow`). The bold split uses offsets only after `context.text.slice(a, b) === snippet.text` is confirmed and the offsets are inside the string; a mismatch falls back to separate blockquotes. No regex runs over the text to find the date; no HTML, no markdown, no link detection.
- **Prefilled title:** the student's own syllabus line becomes a student item title (<= 200 code points) only when the student presses Add item. It may contain formula- or HTML-looking text; it is stored as plain text and escaped at every sink (contract 2.2); the AI never sees student titles. Nothing the client does weakens TextPolicy: the server re-validates.
- **Local storage:** ids only (`{s, itemId}`), user-scoped, cleared on sign-out and syllabus delete (SB-06 parity); never a snippet, title or date. A form draft holds what the student typed (title, date, time), the same class of data as the existing edit drafts, under the same scoping.
- **Requests:** one new read of an existing owner-scoped route and one new write to an existing owner-scoped route with `Idempotency-Key`; no new endpoints, no tokens, nothing leaves the same-origin proxy. The 100-student-item cap and per-user rate limits are the server's.
- **Logging:** the client logs nothing. Backend logging rules (never snippet or title text) are unchanged.
- **Data integrity:** the server never auto-promotes; the client never invents a date (the field starts empty); the created item is the server's 201 body, not a client construction; a local `added` state that points at a missing item heals itself (4.7). The unclaimed list is immutable after extraction, so a cached fetch cannot go stale except by purge or delete, both of which remove the whole screen's data.
- **Latency:** zero until opened; then one or two small GETs. The form's save is one POST.
- **Failure paths:** fetch failure (calm note, retry), create 400/409/429/401/network (section 4.10, values kept), storage unavailable (page works, hide does not persist), item deleted elsewhere (row reverts).
- **Residual (accepted for v1, flagged):** hidden state is per device; a student with two devices sees the full list on the second. Stated in the help text. A server-side resolution is Lead question 1.

---

## 13. Open questions

### For the Product Owner
1. **Hide lives on this device in v1** (section 2, 4.6). Accept for the personal-use scope (PO-15/16), with a server-side record as a later additive change? The alternative is to wait for the contract change before building.
2. **Prefilled title** from the syllabus line (recommended) or an empty Title field? The prefilled line can be long and plain; the student edits it.
3. **Default type "Other"** (recommended) or no default (a required choice)?
4. **Collapsed by default, not part of Expand all** (recommended). Would you rather it open by default when it holds five lines or fewer?
5. **No count anywhere but this screen** (course card and dashboard stay silent). Confirm.

### For the Lead Architect
1. **Contract:** no route marks an unclaimed date hidden or used, and `syllabus-item-create-request` has no member to name the source unclaimed date (`additionalProperties: false`). If a server-side record is wanted, propose an additive optional route (for example `PUT /syllabi/{id}/unclaimed-dates/{uncId}/resolution` with `{state: "hidden" | "item", itemId?}`) and an optional additive member on the create request; both need schema, fixtures and contract tests together (contract 17). Until then the spec is local-state v1.
2. **Web client gaps:** `createItem`, `listUnclaimedDates`, `parseUnclaimedPage`, an `UnclaimedDate` type, MSW handlers and `syllabusDb` rows do not exist (section 9). Confirm frontend-engineer owns all of them in one slice, and that web fixtures for the unclaimed-date page may be authored where the other syllabus.v1 web fixtures live.
3. **Harness:** add a syllabus harness fixture route (option A, section 11) or rely on the real stack only (option B)? Recommended: both, A for density and a11y, B for the flow.
4. **Error code for the 100-student-item cap:** not in contract section 9. Name it so `form.limit` can be shown; otherwise the generic message applies.
5. **Summary wording:** `REVIEW_COPY.summary` says "We found N items"; once a student adds items the count includes items Tether did not find. Small and tolerable for v1; a later copy pass could say "N items." when any `origin: "student"` item exists. Not changed here (pinned by tests).
6. **Second fill:** the form's `Add item` is `btn-primary` inside the sheet, like the edit form's Save. If you want one fill per screen rather than per region, make it `btn-secondary`; the spec accepts either.
7. **Security check (PO-20b names one):** security-engineer to read section 12 and the built code; the new surfaces are the first rendering of unclaimed snippet text, the prefilled title path, the local-storage keys and the create call.

### Consent / recording wording
None. Nothing in this spec is G1 text. The Demo lines are untouched.

G4: nothing here is human-validated.

---

## 14. Review of the build (neurodivergent-ux-specialist, 2026-11-01)

Read, not run: `copy.ts` (`UNCLAIMED_COPY`, `REVIEW_COPY.droppedSee/emptyNoticed/emptyNoticedBody`), `UnclaimedDates.tsx`, `UnclaimedAddForm.tsx`, `Review.tsx` (lines 104-209, 296-312, 380-430, 506-580), `UnclaimedDates.test.tsx` (focus, name and announcement assertions at lines 165-302, 351-359, 417-436), `ItemEditForm.tsx` (`validate`, server-failure focus at 146-147), `ItemRow.tsx:87, 228`, `Announcer.tsx`, `lib/api/errors.ts`, `ItemService.cs:104-107`, `MockRuleExtractor.cs:108-114`, `API_CONTRACTS_P2.md` 133. No source file was edited; no test was run (nothing in source changed). G1: none of the strings below is consent, recording, disclosure or Demo wording. Severity words follow `docs/security/severity-policy.md`, calibrated to the personal-use scope (PO-15/16); where a real multi-user release would rate higher, that is said.

### 14.1 Verdict on the built copy and behaviour

The build matches sections 4 to 6 as read, and the tests pin the parts that matter for a neurodivergent student: collapsed by default, fetch on first open only, head focus only from "See dates we noticed", Hide/Undo on one element with focus kept, `Show 25 more` keeping focus until the last press, no live region inside the section, draft kept across a remount, Escape and Cancel returning focus to `Add as item`. The 200-row case reads calmly as built: the head carries one number and the student can leave it closed; opened, the first thing is three short help sentences, then 25 rows, then one tertiary button. Nothing counts down, nothing is red, nothing says left or remaining. The findings below are copy precision and two behaviours, none of which blocks the push.

### 14.2 Findings

| # | Finding | Rating | Proposed change (old -> new; frontend-engineer to apply, tests re-pin where named) |
|---|---|---|---|
| R1 | **Double period.** `UnclaimedDates.tsx:282` strips trailing periods from the title before `addedMsg`, only there. The same item, one press later, goes through `ItemRow.tsx:87` `approvedMsg(t)` unstripped, so the student hears "Added as an item: ... exercise. It is not decided yet." then "Approved: ... exercise.." A title ending in "?" is not covered either. The fix is a wording rule, not a call-site patch. | LOW | **Rule:** a template never appends a period directly after inserted document or student text; one helper in `copy.ts` closes the sentence and every `*Msg(t)` uses it. `const sentence = (t: string) => `${t.replace(/[.?!…]+$/u, "")}.`` (or the equivalent that adds the period only when none is there). Apply to `ITEM_COPY.approvedMsg/skippedMsg/undoneMsg/savedMsg`, `UNCLAIMED_COPY.hiddenMsg/shownMsg/addedMsg`, `SYNC_COPY.removedMsg/addedBackMsg`; delete the `.replace` at `UnclaimedDates.tsx:282`. Accessible names (`Approve X.`, `Go to the item X.`) are not sentences and keep the title verbatim. `UnclaimedDates.test.tsx:272` already asserts the single-period form, so it stays green; `copy.test.ts` should gain one case: a title ending in "." or "?" yields exactly one terminal mark. Check `copyLint` does not read the regex literal as a `!` in copy. |
| R2 | **409 shows `ACTION_COPY.generic`.** From the Review screen the syllabus is always `ready`/`partial` (terminal statuses), so a 409 `invalid_state` on create is in practice the 100-student or 300-total cap, yet the form says "That did not work. Nothing was changed." and moves focus to **Title** (`UnclaimedAddForm.tsx:127`). Together these tell the student the title or date was wrong and invite retyping, when nothing they type will change the answer; the message also omits "Your changes are kept" although the form stays open with the values (language guide 1.9). The sentence is truthful, so it is acceptable as a floor; it is not helpful. | LOW for this scope (one student rarely reaches 100 hand-added items); MEDIUM for real users | (a) New `form.conflict`, true for both server causes without guessing which: old `ACTION_COPY.generic` -> new **"No more items can be added to this syllabus. Nothing was changed. Your changes are kept."** Shown for any 409. Keep `form.notReady` and `form.limit` in the file for when the Lead names distinct codes (question 4); add "Your changes are kept." to `form.limit`. (b) Behaviour, both forms: for a non-400 failure focus the problem paragraph (`tabIndex=-1`, the `dataProblemRef` pattern in `Review.tsx:111-113`) rather than Title, so a screen-reader user hears the message; today the paragraph is neither associated nor announced, so after a 409 they hear only "Title, edit, Bring a calculator ...". `ItemEditForm.tsx:147` has the same pre-existing gap; fixing both at once keeps the two forms alike. `UnclaimedDates.test.tsx:351, 359` re-pin. |
| R3 | 20px timestamp-type compactness. | out of scope | Not reviewed here, by instruction. |
| R4a | **Count word.** The head says "12 lines" but each row is one date hit, and `MockRuleExtractor.cs:110,114` emits one row per date on a line, so two rows can share a line and "12 lines" can overstate. | LOW | `lines: (n) => (n === 1 ? "1 date" : `${n} dates`)` -> head reads "Dates we noticed, 12 dates". Repetitive but exact, and it matches the label and "Show hidden and added dates". The help sentences keep "line": they describe what is shown on the row, which is true. Tests at `UnclaimedDates.test.tsx:279` and any "N lines" name re-pin. Optional; if the PO prefers "lines", record the small inaccuracy as accepted. |
| R4b | **Implied obligation at 200.** `help2` offers two actions, add or hide; a student who reads 200 rows as a pile may feel each needs one of the two. The needs-date copy already says "You can add one or leave it." | LOW | `help2` old "Each line is shown word for word. You can add one as an item, or hide it." -> new **"Each line is shown word for word. You can add one as an item, hide it, or leave it."** |
| R4c | **The year.** A syllabus line usually says "Oct 5" with no year; the Date field needs one. `dateHelp` does not say so, which is a working-memory cost at the one field Tether cannot fill. | LOW | `form.dateHelp` old "Read the date from the line above and enter it here." -> new **"Read the date from the line above and enter it here. Add the year if the line leaves it out."** (11 + 9 words; two `aria-describedby` sentences on the field, as today). |
| R4d | **`Go to the item X` has no separator** while the two sibling names do (`Add as item: X`, `Hide: X`), and X is often a whole sentence. | LOW | `goToItemName: (t) => `Go to the item: ${t}``. `UnclaimedDates.test.tsx:265` re-pins. |
| R4e | **`Show hidden and added dates (n)` reveals all n at once, unpaged** (`UnclaimedDates.tsx:320`). On a later visit with, say, 150 hidden rows this is the 150-row wall the paging avoids. Rare (needs many hides, then a new visit, then the press). | LOW, accept for v1 | Note only. If it is ever reworked, page the revealed rows with the same `Show 25 more`. |
| R4f | **Duplicate accessible names at scale.** Ten rows for "Oct 5" give ten `Add as item: Oct 5` buttons; a screen-reader user listing buttons cannot tell them apart. Accepted in 4.4; the blockquote before each button carries the line. | LOW, accepted | G4 question 7 below. No change proposed. |
| R4g | **Escape drops the typed date** without a confirm (spec 4.10; `ItemEditForm` does the same). The prefilled title is restored by the prefill, so the loss is the date and type only. | LOW | No change for v1; G4 question 8. |
| R4h | **The head count falls as rows are hidden.** Factual, but it can read as a meter to drive to zero. | LOW, neutral | No change; G4 question 9. |

Checked as built and found in order (no finding): `help3` "on this device" is honest for v1 and plain; `droppedSee`, `emptyNoticed`, `emptyNoticedBody`, `loading`, `loadError`, `legend`, `titleHelp`, `allHandled` pass the language guide checklist (no forbidden words, longest sentence 15 words, subject is Tether, the line or the student's action); focus order head -> rows -> `Show 25 more` -> `Show hidden and added dates` with help text unfocusable (tests 165-222, 429-436); every accessible name starts with its visible label (2.5.3); `aria-expanded`/`aria-controls` on the head and on `Add as item`; no `aria-live` in the section (test `liveInside()`); hidden row reads "Hidden: Oct 5" with Undo on the same element; the 4.8 state opens the section and drops the rail; the two Demo lines are untouched.

### 14.3 Copy lint check of the proposed strings

"No more items can be added to this syllabus. Nothing was changed. Your changes are kept." / "Each line is shown word for word. You can add one as an item, hide it, or leave it." / "Read the date from the line above and enter it here. Add the year if the line leaves it out." / "Go to the item: {t}" / "12 dates": checked by hand against `copyLint.ts` `FORBIDDEN` and `FORBIDDEN_SYLLABUS` (no "!", "just", "lost", "missed", "behind", "fail", "missing", "pending", "remaining", "right now", "you should/must/need to", "error", "%"); longest sentence 11 words. Not run through `copy.test.ts` because no source was changed; the frontend run will confirm.

### 14.4 What a human neurodivergent reviewer should be asked (G4 NOT_RUN; via `human-research-coordinator`, voluntary and consented)

Show the Review screen with 200 noticed dates, the section collapsed, on a phone and a laptop.
1. Before opening: "What do you think 'Dates we noticed, 200 dates' is? Do you feel you have to do anything with it?"
2. After opening: "Read the three grey sentences. In your own words, what can you do here, and what happens if you do nothing?"
3. "Press Hide on one. What do you expect to see on your other phone or laptop tomorrow?" (tests "on this device")
4. "Press Add as item. Did the filled-in title help, or would you rather start empty? Should the cursor land on the date instead?"
5. "The line says 'Oct 5' and the date box wants a full date. Which year did you type, and how did you decide?"
6. Show the 409 message: "What would you do next? Does it sound like you did something wrong?"
7. Screen-reader users: "With several 'Add as item: Oct 5' buttons, can you tell which line each one belongs to? Is 'Hidden: Oct 5.' enough to know what happened?"
8. "You typed a date and pressed Escape. What did you expect to happen to what you typed?"
9. "The count in the heading goes down as you hide rows. Does that feel like progress, pressure, or nothing?"
10. "Which word is right for what is in this list: lines, dates, or something else?"

Potential-harm check for the clinician/expert reviewer: does any wording in this section ("noticed", "hide", "did not turn them into items", a falling count) read as the app judging what the student did or did not do with their syllabus?
