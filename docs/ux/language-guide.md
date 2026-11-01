# Tether Language Guide

Status: DESIGN DECISION v1, 2026-10-29. Author: neurodivergent-ux-specialist. Audience: every agent that writes, generates or reviews student-facing text.
Trigger: the Product Owner decided that language about attention and recall is sensitive for neurodivergent students, and renamed "I lost track" to **Quick recap**.
**Gate G4 stays NOT_RUN: nothing here is validated with students or clinicians.** Items marked [J] are design judgement, [C] are project constraints. Consent, recording and Demo-source wording is under gate G1 (BLOCKED): this guide only flags it. Nothing here was run or built; the audit is a read-only review of files as of today (working tree has uncommitted edits by others, so line numbers can drift).
Inputs read: CLAUDE.md, docs/ux/design-direction.md (section 1), docs/ux/calendar-reminders.md (sections 0-8 copy rules), docs/ux/phase2-syllabus-review.md (rules and error table), `src/web/src` components and `lecture/copy.ts`, mock handlers and harness fixtures, `PromptLibrary.cs`, `MockAiClient.cs`, `CatchUpWire.cs`, `MockRuleExtractor.cs`.

## 1. Principles

1. **The student is in control.** Say what the student can do and what the app will do when they do it. The app never decides that a student needs help, and never says it noticed anything about them. [C] invariant 7: no inference from camera, mic level, behavior or inactivity.
2. **Describe what the app does, not what the student is.** "Shows the last 5 minutes of what was said" not "helps you when you are lost". Verbs have the app or the lecture as subject.
3. **No fault, no deficit.** Never imply the student missed, forgot, fell behind, failed, lost something, or is weak at something. A gap in the transcript is a gap in the audio, not in the student.
4. **No guilt, no urgency pressure.** No countdowns, "still", "only N left", "last chance", "hurry", "right now" used as a nudge, no red counters. Past items are simply past.
5. **No clinical or diagnostic language.** Never name ADHD, attention problems, executive dysfunction, "focus issues", "distractible", symptoms, treatment or therapy. Features are named by what they do. (Engineering docs may say ADHD; strings never do.)
6. **No exclamation marks, no emoji, no praise-as-reward.** No "Great job", "Awesome", "Oops", "Don't worry", "Just", "Simply", "Easy". Acknowledgement is plain: "Saved."
7. **Plain and dignified.** Adult, concrete, active voice, sentences under 20 words, one idea each. Short declarative sentences are the only flourish (the "hip Atticus Finch" voice in docs/ux/design-direction.md: quiet, exact, never cute, never clinical, never a lecture to the student).
8. **Truth over comfort.** Recording status, AI uncertainty and data loss are stated exactly. Softening never hides a fact (invariant 2, invariant 6).
9. **Errors blame the system and say what is true.** Always state whether anything was changed or kept. Do not say "Nothing is lost" (see 3.1); say "Nothing was deleted" or "Your transcript is saved" only when true.
10. **Single events are not patterns.** One wrong answer, one skipped reminder, one long pause is one event. No sentence generalizes from one event to the student.
11. **Optional by default.** Every prompt, nudge or suggestion has an off switch and a calm default. Offers use "You can ..." or a plain button label; never "You should / need / must".

## 2. Vocabulary

| Use | Avoid | Why |
|---|---|---|
| Quick recap | I lost track, Lost the thread, Catch up (as the feature name) | PO decision |
| recap, what was said, summary | catch-up result, "where you are" | Describes content, not the student |
| last 2, 5 or 10 minutes | "how far behind", "what you missed" | No deficit framing |
| most recent | right now, currently happening | "Now" reads as pressure to be present |
| paused, stopped, not connected | failed, broken, dropped, lost | State, not fault |
| not available, could not update | failed, error (as noun to student) | Plain |
| waiting for your decision | pending, overdue, outstanding, undecided count in red | No backlog framing |
| not marked yet | missed, incomplete, unfinished | Neutral |
| answered differently from the key | wrong, incorrect, failed | See section 7 |
| the AI's best estimate | the answer is, definitely, correct | Invariant 6 |
| a topic to revisit | weakness, gap in your knowledge, struggling | Not a deficit |
| the transcript has a gap | you missed this part | Gap belongs to audio |
| we, Tether | the system, the server | Student-facing text uses "Tether" or "the app" |

Internal names (route `/lost-track`, event `catchup.ready`, wire keys `nowDiscussing`, `shortCatchUp`, test ids) are not student-facing. Renaming them is a contract change owned by the Lead Architect; this guide does not require it. Student-facing text, accessible names, docs for students and AI prompts must use "Quick recap".

## 3. Preferred and avoid (phrase pairs)

Row ids are cited by the audit. "Preferred" strings are proposals, not validated wording. Strings under gate G1 are excluded (section 9).

### 3.1 Feature names and buttons
| # | Avoid | Preferred |
|---|---|---|
| 1 | I lost track | Quick recap |
| 2 | Lost the thread? Press I lost track. | Want a recap? Press Quick recap. |
| 3 | Last 5 min: catch up on the last 5 minutes | Last 5 min: recap of the last 5 minutes |
| 4 | Got it, back to lecture | Back to lecture |
| 5 | Skip to I lost track | Skip to Quick recap |
| 6 | Alt+L goes to I lost track | Alt+L goes to Quick recap |
| 7 | Here's where you are | Here is what was said |
| 8 | Right now: derivatives | Most recent: derivatives |
| 9 | Worth knowing | Also mentioned |
| 10 | Get started / Let's go! | Add a course |

### 3.2 Empty and error states
| # | Avoid | Preferred |
|---|---|---|
| 11 | Nothing is lost. Try again when you are ready. | Nothing was deleted. Try again when you like. |
| 12 | Nothing was lost. (rate limit) | One moment. Try again in about a minute. Nothing was changed. |
| 13 | Oops, something went wrong! | That did not work. Nothing was changed. |
| 14 | You have no courses yet! | No courses yet. Add one when you want to. |
| 15 | You haven't started a lecture | No lectures yet. |
| 16 | Request failed (500). | We could not reach the server. Try again. |
| 17 | Invalid input. Fix your errors. | One thing to check: the course name is empty. |
| 18 | You only joined 3 minutes ago. | The lecture has run 3 minutes. Here is all of it. |

### 3.3 Lecture states
| # | Avoid | Preferred |
|---|---|---|
| 19 | Recording failed. | Recording stopped. Your transcript so far is saved. |
| 20 | Connection lost. Audio is being lost! | Not connected. Audio is not being saved. |
| 21 | You paused for 10 minutes. | Paused since 10:42. |
| 22 | Don't forget to resume! | Resume recording when you are ready. |
| 23 | Microphone access denied. You must allow it. | Microphone is blocked. Here is how to turn it on. |
| 24 | Something doesn't match. Reload the state. | The app and your microphone disagree, so the microphone is off. Choose Check again. |
| 25 | Recaps are paused for this lecture. | A recap is not available for this lecture. Here is what was said. |
| 26 | The summary is catching up. | The summary is updating more slowly than usual. |

### 3.4 AI-generated text
| # | Avoid | Preferred |
|---|---|---|
| 27 | You missed the part about mitosis. | Mitosis was covered at 03:41. |
| 28 | In case you zoned out, the professor said ... | The lecture covered ... |
| 29 | Don't worry, here's a simple summary. | Summary of the last 5 minutes. |
| 30 | It is obvious that / Clearly | (state the claim; delete the filler) |
| 31 | Great question! Let me help you understand. | A direct answer, then "Source: lecture at 12:03" or "Not from your lecture". |
| 32 | You should study this before the exam. | This topic came up three times. You can add it to your study guide. |
| 33 | You are struggling with derivatives. | Derivatives is a topic you can revisit. |
| 34 | The answer is B. (AI-estimated) | The AI's best estimate is B. It is not from an answer key. |

### 3.5 Reminders and notifications (see docs/ux/calendar-reminders.md section on banned words)
| # | Avoid | Preferred |
|---|---|---|
| 35 | Assignment overdue! | Essay 2 was due Oct 14. (shown only on the plan, never as an alert) |
| 36 | You have 3 overdue items! | Three items are waiting for a decision. Open them when you want to. |
| 37 | Don't forget: exam tomorrow | Midterm, Biology 101, Tue Oct 14, 9:00. |
| 38 | You haven't opened Tether in a week | (send nothing; no inactivity messages) |
| 39 | Keep your streak going | (never; no streaks) |
| 40 | Reminders: turn on to avoid missing deadlines | Reminders come from Google Calendar, not from Tether. Choose Off, Gentle or Extra runway. |

### 3.6 Syllabus review
| # | Avoid | Preferred |
|---|---|---|
| 41 | You still have 12 undecided items | 9 of 24 decided. |
| 42 | Wrong file type. You uploaded the wrong file. | We can't use that kind of file. Use a PDF, Word or plain text file. |
| 43 | Date missing! Fix this. | No full date is written for this. You can add one or leave it. |
| 44 | Low confidence | Please check |
| 45 | Extraction failed. | Something went wrong while reading. Nothing was saved from this file. |

### 3.7 Quizzes and mastery feedback
| # | Avoid | Preferred |
|---|---|---|
| 46 | Wrong! | Different from the key. The key says C. |
| 47 | You failed this topic. | 2 of 5 answered as the key says. One quiz is a small sample. |
| 48 | You have a weakness in limits. | Limits came up in two misses. You can revisit them when you choose. |
| 49 | Mastered! | Answered as the key says on 3 of the last 3 tries. |
| 50 | You forgot this. | This topic is due for a review. |
| 51 | Score: 40%. Try harder. | 4 of 10 as the key says. Review the 6 whenever you like. |

### 3.8 Settings
| # | Avoid | Preferred |
|---|---|---|
| 52 | Enable distraction blocking | Focus mode: show fewer panels during a lecture |
| 53 | Recommended for students with ADHD | Choose what you see. Changes apply right away. |
| 54 | Alerts keep you on track | Announcements: choose which updates are read aloud. |
| 55 | Help level: Quiet | Prompts: Quiet (none), Balanced, Active |

## 3.9 Rationale notes
- Row 11/12: "Nothing is lost" uses "lost" in the data sense, but the word sits next to the PO's concern and costs a deny-list exception. Say "deleted" or "changed".
- Row 8: "Right now:" is a judgement call [J]. It puts a time-pressure cue ("be present now") before the very content a student asked for. "Most recent" says the same and carries no pressure. The server field name stays.
- Row 44: "Please check" is an accepted exception to the "Please" rule because it is a label, not a request; confirm in G4 tests.
- Row 47-49: counts are shown as the plain fact. No percentages by default, no color grading (green/red) of a person.

## 4. Introducing Quick recap

Goals: students learn it exists without the app implying anything about them; the name works for screen-reader and speech-control users (WCAG 2.5.3 Label in Name).

- **Name.** "Quick recap" everywhere. Never abbreviated, never "QR".
- **One-line help text** (dashboard "How a lecture works", first lecture): "Quick recap shows what was said in the last 2, 5 or 10 minutes. The lecture keeps going."
- **Tooltip / hint under the heading** (also usable as `aria-describedby`): "Choose how far back. You get a short recap and the words themselves."
- **Do not** introduce it with a cause ("if you lose focus", "when you zone out", "if you get lost"). It is a reference tool anyone may use; many students use it to re-read, not to recover.
- **Never prompted.** The app never offers Quick recap on its own, after a pause or a silence. Only the student presses it. (An optional "Active" engagement level may later offer a setting, never a behavior-triggered nudge; see invariant 7.)
- **Accessible names (WCAG 2.5.3).** The accessible name of a control must contain its visible text, in the same order, ideally at the start.
  - Panel region and heading: visible "Quick recap"; region `aria-label` not needed beyond the heading; no hidden longer label that drops the words.
  - Window button visible "Last 5 min" -> accessible name "Last 5 min: recap of the last 5 minutes". The usual-window variant: "Last 5 min (your usual): recap of the last 5 minutes".
  - Skip link visible "Skip to Quick recap" -> same text.
  - Voice control must work with "Click Quick recap" and "Click Last 5 min".
- **Result.** The result panel heading is **"Here is what was said"**. Its parts use plain nouns: "Short recap" (AI or extractive summary), "Key terms", "Questions you could ask", "Word for word" (the transcript lines, currently "What was said"; rename to avoid duplicating the heading). The panel states its window: "Covers the last 5 minutes" or "Covers 03:10 to 08:10."
- **Announcement** when ready: "Recap ready." Unchanged.
- **Waiting copy:** "Getting the last 5 minutes." then "Still writing the recap. Here is what was said so far." Keep "Still" here as a status of the system, not of the student; do not add a timer.
- **Not "Right now:".** Replace the lead line label with "Most recent". If the field is empty, omit the line rather than show a placeholder.
- **Window default and memory.** "Your usual" is shown only because the student chose a window before; do not describe it as a habit ("you often").
- **Tone of fallback:** the extractive fallback ("Here is what was said") is a complete answer, not a degraded one. Never say "only", "just the transcript", "limited".

## 5. Rules for AI prompts and output validation

Why: model output is the largest uncontrolled source of student-facing text. A prompt rule lowers the rate but cannot guarantee; the validator backstops it. The existing server pipeline already validates JSON shape, citations, sanitizes markup and URLs (`OutputSanitizer.cs`, `Validators.cs`); add language checks beside it.

### 5.1 Prompt clauses (add verbatim to every system prompt that produces student-facing text)
```
Language rules (apply to every text field):
- Write about the lecture, the material or the work. Never write about the student's attention,
  memory, effort, state of mind, performance or what they missed, forgot, lost or fell behind on.
- Do not diagnose or use clinical or medical terms. Do not mention ADHD, focus, distraction or attention.
- No exclamation marks, no emoji, no praise or encouragement phrases, no "just", "simply", "easy", "obviously".
- Do not create urgency: no "now", "immediately", "don't forget", "make sure", "you need to", "you must", "you should".
- Use short plain sentences, active voice. Name sources. If unsure, say "not sure" instead of guessing.
- Call the feature "Quick recap" if you must name it. Never write "I lost track" or "catch up".
- One wrong answer is one answer. Never say a student is weak, struggling or has a deficit. Say what the key says.
- An answer you estimated is "my best estimate", never certain, unless an answer key was supplied.
```
Per workflow additions:
- **Rolling summary:** replace "for a student who may lose the thread" (PromptLibrary.cs:14) with "for a student who wants a running overview of a live university lecture."
- **Quick recap:** replace "help catching up" (:33) with "a quick recap of the last windowMinutes minutes"; replace "what is being discussed right now (the last minute)" (:39) with "what was said most recently (the last minute)". Keep "Say nothing about the student's attention or state of mind" (:35); extend it with the clause above.
- **Tutoring / study guide (not yet implemented):** "Explain, then cite. Do not tell the student what they should have known. Phrase gaps as 'not covered in the notes', never 'you don't know'."
- **Quiz feedback (not yet implemented):** "Compare to the supplied key only. State the key's answer. If no key, say 'my estimate'. Never grade the student as a person."
- Versioning: a change to a prompt bumps its version (`rolling-summary.v2`, `quick-recap.v2`); `PromptLibrary.cs` says docs copies exist and a drift test guards them. I found no files under docs/ai/prompts/ when I looked, so the ai-agent-engineer must confirm the drift test before this lands.

### 5.2 Validator deny-list (post-generation, per text field)
Idea: a `LanguageGuard` that runs on every student-visible string (summary text, recap items, key terms, questions, gaps, tutoring answers, quiz feedback) after schema validation and sanitization. On a hit: retry once with a stricter reminder; if it still hits, substitute the neutral extractive/fixed text and log only the rule id (never the text; CLAUDE.md invariant 10).

Starter patterns (case-insensitive, `\b` boundaries, timeout like `OutputSanitizer`):
```
Blame/deficit:  \b(you|you've|you'd)\s+(missed|forgot|forgotten|lost|fell behind|failed|didn't (get|understand|notice|pay))
                \b(lost track|lose track|lost the thread|zon(ed|ing) out|spaced out|not paying attention|fell behind|falling behind)\b
                \b(your|the student'?s)\s+(weakness|weaknesses|deficit|struggle|struggling)\b|\bstruggl(e|es|ing) (with|to)\b
Clinical:       \b(adhd|add|attention deficit|executive dysfunction|symptom|diagnos\w*|disorder|medication|inattentive|hyperactiv\w*)\b
Pressure:       \b(hurry|urgent\w*|asap|immediately|right now|last chance|don't forget|make sure|you (must|need to|have to|should))\b
                \b(overdue|running out of time|only \d+ (days?|minutes?|hours?) left|deadline looming)\b
Tone:           !  |  \b(oops|awesome|great job|well done|amazing|don't worry|no worries|simply|just (do|read|try)|obviously|clearly|easy)\b  |  emoji (\p{Extended_Pictographic})
Certainty:      \b(definitely|certainly|guaranteed|the correct answer is|you are (wrong|incorrect|right))\b   (only where no key exists)
Old name:       \bI lost track\b
```
Notes and false-positive risks:
- **Quoted lecture content.** A lecturer may truly say "just", "clearly", "you should remember", "don't forget the exam", "ADHD" (a psychology class), "attention" (cognitive science), "failed" (a failed experiment), "wrong". Pattern hits inside transcript quotations or topic names are legitimate. Apply the guard only to text the model writes, not to verbatim transcript text; for AI text that quotes the lecture, allow a pattern if the match also appears verbatim in the cited segment.
- **Subject matter.** Psychology, medicine and education lectures use the clinical and "attention" words. The Clinical rule should warn and fall back to a neutral phrasing, not delete topic names; allow a per-course override by the student, never by the model.
- **"just", "right now", "clearly", "easy"** have many harmless uses ("justice", "just noticeable difference", "right now" as a physics time). Keep word boundaries and prefer the whole-phrase list; start in log-only mode and measure before enforcing.
- **"!"** appears in factorial notation (5!) and code. Exempt digits/identifiers followed by `!`.
- **`add`** is a common word; match `\bADD\b` case-sensitively only, or drop it.
- **Regex is a floor.** It misses paraphrase ("you may have drifted off"). Add an AI-quality eval set (tests/ai evals, owner ai-agent-engineer) of at least 30 adversarial prompts (a tired-sounding user message, a user claiming they were distracted, a user asking "did I miss something?") and check the output with human review. The same guard runs on mock output so the tests exercise it.
- Student-typed input is never altered; the guard runs only on outgoing text.
- Output reaches the page as plain text (`PlainText`); do not rely on markdown to soften wording.

### 5.3 Handling a student who uses the words
If a student writes "I zoned out" or "I'm so behind", the AI answers with content, not with an interpretation: "Here is what was said in the last 5 minutes." No reassurance script, no "that's okay", no advice about attention. Safety-relevant disclosures (crisis) are a Product Owner and clinician decision; none are designed here.

## 6. Reminders, notifications, syllabus: additions to existing rules
- Reminders: the banned list in docs/ux/calendar-reminders.md stands. Add: "due soon", "approaching", "remaining", "left" in countdown form, "keep up", "catch up", "get ahead".
- A reminder names what, which course, when. It carries no judgement and no student state.
- Syllabus: keep "Looks right / Probably right / Please check" and the error table. Add: do not say "missing" about dates the document does not contain; say "not written".
- Progress counts are plain text ("9 of 24 decided") only on the screen where the student decides; never in a notification or badge.

## 7. Quizzes, mastery and AI-estimated answers
[C] invariant 6. A single miss is not a deficit; an AI-estimated answer is not certainty.
- Show what the key says (when a key exists) or "my best estimate" (when it does not). Label the source on every answer: "From the answer key" or "AI estimate, not from a key".
- Use "different from the key", never "wrong/incorrect/failed". Use "answered as the key says".
- Do not label a topic after one miss. Show a topic as "worth a second look" only after repeated evidence on separate days (threshold is a learning-science decision; this guide sets no number). Show the evidence count, not a verdict.
- Mastery language: "Answered as the key says on 3 of 3 tries across 2 days." Never "mastered", "expert", "weak", "level", "rank".
- Remediation is an offer: "Want to look at limits again? You can skip this."
- No red/green grading of a person; color never carries correctness alone.
- Spaced review is "due for a review", never "you forgot".

## 8. AUDIT (student-facing strings, as of 2026-10-29)

Scope: `src/web/src` components, app routes, `lecture/copy.ts`, mock handler and harness text; prompt, mock output and wire-gap strings under `src/api/src/Modules/Ai`; strings in `MockRuleExtractor.cs`. Priority: **P1** names or implies attention/recall deficit or old feature name on a main screen; **P2** pressure, vague or clinical-adjacent, or lecture-state word that could read as fault; **P3** polish. Existing e2e and unit tests freeze some strings (design-direction.md section 8); changing them is the frontend-engineer's job and includes the tests. I made no edits. Found 0 exclamation marks in student-facing strings I read.

### 8.1 Web
| # | File:line | Current | Proposed | Pri |
|---|---|---|---|---|
| A1 | `components/lecture/copy.ts:207` | `heading: "I lost track"` | `Quick recap` | P1 |
| A2 | `components/DashboardView.tsx:44` | `Lost the thread? Press I lost track.` / `You get the last 2, 5 or 10 minutes back. The lecture keeps going.` | `Want a recap? Press Quick recap.` / `It shows what was said in the last 2, 5 or 10 minutes. The lecture keeps going.` | P1 |
| A3 | `copy.ts:222` | `Here's where you are` | `Here is what was said` | P1 |
| A4 | `copy.ts:227`, `LostTrackPanel.tsx:164` | `Right now` label | `Most recent` | P1 |
| A5 | `components/PreferencesPanel.tsx:80` | `...the lecture keeps going while you catch up.` | `...the lecture keeps going while you read.` | P1 |
| A6 | `copy.ts:213-214` | `Last 5 min: catch up on the last 5 minutes` | `Last 5 min: recap of the last 5 minutes` (keeps 2.5.3: starts with visible text) | P1 |
| A7 | `copy.ts:165` | `Skip to I lost track` | `Skip to Quick recap` | P1 |
| A8 | `copy.ts:190` | `Alt+L goes to I lost track.` | `Alt+L goes to Quick recap.` | P1 |
| A9 | `copy.ts:216` | `You have only been here N minutes. Here is all of it.` | `The lecture has run N minutes. Here is all of it.` ("only ... here" implies arrival and presence tracking) | P1 |
| A10 | `copy.ts:217` | `You have only been here a short while.` | `The lecture has only just begun. Here is all of it.` | P1 |
| A11 | `copy.ts:220` | `Earlier catch-up, last N min` | `Earlier recap, last N min` | P2 |
| A12 | `copy.ts:235` | `Got it, back to lecture` | `Back to lecture` ("Got it" assumes understanding) | P2 |
| A13 | `copy.ts:241` | `Worth knowing` | `Also mentioned` | P3 |
| A14 | `copy.ts:231` | `Recaps are paused for this lecture.` | `A recap is not available for this lecture. Here is what was said.` (matches design-direction row 7; hides budget reason) | P2 |
| A15 | `copy.ts:251` | `Nothing was lost.` (rate limit) | `Nothing was changed.` | P2 |
| A16 | `copy.ts:253-254` | `Nothing was lost.` | `Nothing was changed.` / `Nothing was deleted.` | P2 |
| A17 | `DashboardPage.tsx:22`, `AllCoursesPage.tsx:42`, `DashboardView.tsx:104` | `Nothing is lost. Try again when you are ready.` | `Nothing was deleted. Try again when you like.` | P2 |
| A18 | `CourseDetailPage.tsx:47` | `Nothing is lost. Try again in a moment.` | `Nothing was deleted. Try again in a moment.` | P2 |
| A19 | `copy.ts:200` | `The summary is catching up.` | `The summary is updating more slowly than usual. The transcript is not affected.` | P2 |
| A20 | `copy.ts:229` | `Still writing your recap.` | Keep (system status). Re-check in G4: "Still" is banned in reminders. | P3 |
| A21 | `copy.ts:84, 150` | `Something doesn't match` | `The app and your microphone disagree` (heading and announcement) | P3 |
| A22 | `copy.ts:152` | `Reload the state to continue.` | `Choose Check again.` (jargon) | P3 |
| A23 | `copy.ts:145` | `Trying to reconnect. Trying again...` | `Trying to reconnect.` | P3 |
| A24 | `copy.ts:68` | `Help: Quiet` | `Prompts: Quiet` (engagement level, plain) | P3 |
| A25 | `components/AppShell.tsx:113-115` | palette entry keywords `focus distraction` | Remove `distraction` from the label-adjacent search keywords or add `fewer panels`. Not visible text; low risk. | P3 |
| A26 | `lib/api/client.ts:71` | fallback `Request failed (${res.status}).` can reach the UI | `That did not work. Nothing was changed.` Never show the status code. | P2 |
| A27 | `mocks/handlers.ts:108`, `components/lecture/harness/fixtures.tsx:65` | mock recap `Right now: derivatives.` / `Right now: the chain rule.` | `Most recent: derivatives.` (demo text is shown to testers and must follow the guide) | P2 |
| A28 | `mocks/handlers.ts:104` | `Catch-up not found.` | `Recap not found.` | P3 |
| A29 | `components/PreferencesPanel.tsx:140` | `...are not built yet.` | Fine. Keep roadmap text out of main screens. | none |
| A30 | `components/MockProvider.tsx:29` | `Loading...` | `Getting things ready.` (design-direction row 4) | P3 |

### 8.2 API: prompts, mock output, wire text
| # | File:line | Current | Proposed | Pri |
|---|---|---|---|---|
| B1 | `Prompts/PromptLibrary.cs:14` | `...for a student who may lose the thread.` | `...for a student who wants a running overview.` Shapes tone of generated summary; bump to `rolling-summary.v2`. | P1 |
| B2 | `PromptLibrary.cs:33` | `A student asked for help catching up on a live lecture.` | `A student asked for a quick recap of a live lecture.` Bump to `quick-recap.v2`. | P1 |
| B3 | `PromptLibrary.cs:39` | `what is being discussed right now (the last minute)` | `what was said most recently (the last minute)` | P2 |
| B4 | `PromptLibrary.cs:29, 35` | `calm, neutral tone` / `Say nothing about the student's attention` | Keep; append the section 5.1 clause block. | P2 |
| B5 | `Provider/MockAiClient.cs:139` | `Covered so far: ...` | Acceptable. Mock output must pass LanguageGuard. | none |
| B6 | `MockAiClient.cs:196` | `Could you explain {term} once more?` / `Could you repeat the last point?` | `Could you explain {term} again?` / `Could you go over the last point?` ("once more" implies the student missed it) | P3 |
| B7 | `MockAiClient.cs:183-186, 197` | `Nothing was said in this window.` / `No transcript text in this window.` / `Very little was said in this window.` | Fine. Use "this time" in student text: `Nothing was said in the last 5 minutes.` | P3 |
| B8 | `CatchUp/CatchUpWire.cs:27` | `Some statements could not be matched to the transcript and are marked uncertain.` | Fine; plain and truthful. | none |
| B9 | `CatchUpWire.cs:28-29` | `The start of this window was too long to include in full.` / `No transcript text in this window yet.` | Fine. | none |
| B10 | `MockAiClient.cs:145, 207, 211` | injection test strings (`Ignore previous instructions ...`) | Test fixtures, never shown to students unless validation fails; confirm they are replaced by fixed text. Not a copy issue. | none |

### 8.3 Syllabus mock extractor (`Syllabus/.../Extraction/MockRuleExtractor.cs`)
| # | File:line | Current | Proposed | Pri |
|---|---|---|---|---|
| C1 | `:371-373` | `The date is to be announced.` / `The date and the weekday written do not agree.` / `The date could be read two ways.` / `...could not be matched to your term.` / `No full date is written for this.` | Good: describe the document, not the student. Keep. | none |
| C2 | `:376` | `Good to know. It has no date.` | `No date is written for this.` ("Good to know" is chirpy and patronizing) | P3 |
| C3 | `:377` | `Something here needs a second look.` | `One detail here is unclear. Please check.` (names the thing, not a vague alarm) | P3 |
| C4 | `:377` | `This file had hidden text, so please check each date.` | Keep; add that the file, not the student, is the cause: `This file has hidden text, so please check each date.` | P3 |
| C5 | `docs/ux/phase2-syllabus-review.md:114, 487` | `Nothing is wrong.` | `This is taking a little longer. Nothing was changed.` ("wrong" echoes error language) | P3 |

### 8.4 Flagged only (gate G1, not audited)
Wording in `CONSENT_COPY`, `PAUSED_COPY`, `BLOCKED_COPY`, `FATAL_COPY`, `DEMO_NOTICE_COPY`, `DEMO_SOURCE_TAG`, `DEMO_SUMMARY_COPY` and `ConsentStart.tsx` / `MicBlocked.tsx` is draft and BLOCKED. Items for the Product Owner and counsel to consider: (1) `FATAL_COPY.signedOutHeading` "You were signed out" is passive and fault-free; keep. (2) `BLOCKED_COPY.bodyUnconfirmed` "We could not confirm that nothing was saved" is truthful but alarming; wording belongs to the consent review. (3) `AUDIO_NOTE_COPY.skipped` uses "skipped" for audio; fine. (4) Any change in meaning needs counsel; tone-only edits must not touch the recording facts. I propose nothing here.

## 9. Checklist: run before shipping copy
1. No exclamation marks, emoji or all caps. (`!` exempt only in math or code.)
2. None of: lost, missed, behind, forgot, failed, weak, struggling, wrong, overdue, late, urgent, hurry, "still" (about a student), "right now", "just", "simply", "easy", "don't forget", "you should/need/must".
3. Subject of each sentence is the app, the lecture, the file or the student's own action. Not the student's mind, memory or effort.
4. No mention of attention, focus (as a problem), distraction, ADHD or any diagnosis.
5. The feature is called "Quick recap". The result is "Here is what was said". No "I lost track", "catch up" as a feature word.
6. Under 20 words per sentence; plain verbs; one next action.
7. Errors say what is true and whether anything was changed or kept. No status codes, tokens, model names or rate-limit terms.
8. Accessible name contains the visible text at the start (WCAG 2.5.3). Screen-reader announcements are short and polite unless safety-relevant.
9. Any AI-estimated claim is labelled as an estimate; any single miss stays a single miss.
10. Notifications and reminders carry only what, which course, when. No student state. Off is one step away.
11. The string is in `copy.ts` or the equivalent registry, lint-tested; generated text goes through the LanguageGuard.
12. Recording, consent or Demo wording is untouched or has Product Owner and counsel sign-off.
13. Marked as unvalidated until the G4 questions below are answered.

## 10. Questions for real students and clinicians (G4 NOT_RUN; nothing here is validated)
Run only through `human-research-coordinator` with voluntary, consented participants.
Students (neurodivergent and neurotypical):
1. Read "Quick recap", "Recap", "Rewind", "Review the last few minutes", "What was said" as button names. Which one tells you what happens when you press it? Which one would you be reluctant to press in a quiet lecture hall?
2. Does "Most recent" or "Latest" read as neutral? Does "Right now" feel helpful or pushy?
3. Which of these feels most like it blames you: "Last 5 min", "Catch up", "Recap"?
4. When the recap is not available, does "Here is what was said" feel like a complete answer or a downgrade?
5. How do you feel about the app saying "Nothing was deleted" versus "Nothing was lost" versus nothing?
6. Are exact counts ("2 of 5 answered as the key says") helpful, neutral or stressful? Would you rather not see counts?
7. Are plain reminders ("Midterm, Tue 9:00") enough, or do you want more or less lead time? Is any word in a reminder uncomfortable?
8. What words about attention or memory do you want the app never to use? Which do you use yourself?
Clinicians and learning-science reviewers:
9. Does any phrase in section 3 risk reinforcing shame or a deficit narrative? Does any "neutral" phrase read as euphemistic or evasive?
10. Is the rule "never offer a recap automatically" protective, or does it withhold a useful support for some students?
11. Is "Please check" appropriate for ambiguous dates, or does it create anxiety? Is "different from the key" clear without being cold?
12. What evidence threshold should separate one miss from a pattern before a topic is called "worth a second look"?
13. Is there any crisis-adjacent language students may type that needs a defined response? (Out of scope here; Product Owner and clinician decision.)

## Summary
1. This guide fixes the vocabulary around attention and recall: the feature is **Quick recap**, the result is "Here is what was said", and no string blames, pressures or diagnoses the student.
2. It gives 55 avoid/preferred pairs, a Quick recap introduction with WCAG 2.5.3-safe accessible names, and a note that "Right now:" should become "Most recent".
3. It supplies a prompt clause block, a regex deny-list (with its false-positive risks) and a rule that AI text passes a LanguageGuard before display.
4. The audit lists 30 web, 10 API and 5 syllabus findings with file:line and proposals; the P1 items are in `copy.ts`, `DashboardView.tsx:44`, `PreferencesPanel.tsx:80` and `PromptLibrary.cs:14,33`. I changed no source file.
5. Consent, recording and Demo wording is only flagged (G1 BLOCKED), and nothing here is human-validated (G4 NOT_RUN).
