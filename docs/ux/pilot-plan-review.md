# UX review of the pilot plan: Stage 2 and the one person it is about

Status: REVIEW, 2026-11-04. Author: neurodivergent-ux-specialist. Reviews `docs/product/pilot-plan.md` (uncommitted draft). Audience: Lead Architect, product-manager (owner of the plan), Product Owner (PO).

This file proposes changes. It changes nothing by itself. I edited no other file and touched no git state. Nothing here is validated with the person it is about or with any student or clinician; gate G4 stays NOT_RUN.

What I read: the pilot plan; `CLAUDE.md` (invariants 2, 6, 7, 8, 10; gate table); `docs/ux/language-guide.md`; `docs/ux/student-experience.md`; `docs/ux/design-direction.md` section 1; `docs/research/human-validation-plan.md`; `docs/RELEASE_GATES.md` G4 row; `docs/DECISIONS.md` PO-15, PO-15b, PO-15c, PO-16, PO-20u, PO-20al, PO-20ax; `docs/legal/anthropic-terms-review.md` F-M10 (via search). Nothing was run; this is a read-only review.

Plain words for the people involved: "he" or "the student" is the PO's son, the only intended user. "The PO" is his parent, who also owns the account, pays the bill, builds the tool and decides the stages. That overlap is the subject of most of this review.

---

## 1. The five findings that matter most

| # | Finding | Where in the plan | Why it matters | Proposed fix (detail in section 3) |
|---|---|---|---|---|
| F1 | **The student has no decision in the plan.** D4 proposes "the PO alone" decides stage changes. The student appears only as someone who "has agreed, freely" (4.2.6) and who "can stop" (7.7). He is not asked before the plan is written, is not in the decision table, and is told about data handling "before first use" (4.2.5), which is after he has already agreed. | 4.2.5, 4.2.6, 7.7, D4, D13 | A pilot run **on** him looks like this: adults decide, he complies, his reaction is observed. A pilot run **with** him looks like: he is asked first, he can say no or "not now" without a conversation, he holds the stop switch, and nothing about him is written down that he did not choose to share. Invariant 7 (optional engagement) applies to the pilot itself, not only to the app. | Add decision **D14: the student's own yes**, with its own rules: asked once, in writing he can keep, with the warning text in front of him before he answers; "no" and "not now" are complete answers; a "yes" can be withdrawn at any time by stopping, with no reason and no follow-up questions that day. Move 4.2.5 (warning) before 4.2.6 (agreement). |
| F2 | **Success measure 4.6.1 measures him, not the tool.** "He uses it of his own accord on the days he planned to" is an adherence measure. It makes his usage the thing being scored and invites "you did not use it on Tuesday". The app's own rules forbid streaks, inactivity messages and usage-as-signal (language guide 1.1, 1.10, 3.5 row 38; student-experience 1.8, 1.10). | 4.6.1, 4.6.2, 4.6.6 | A measure that counts his days of use is attention surveillance by another route, done by his parent, about a tool his parent built. Even if nobody says anything, he will know the count exists. 4.6.2 ("every wrong statement he finds is logged and counted") turns him into an unpaid tester with homework. 4.6.6 ("no stress signal he reports lasts") asks the PO to track his stress over time. | Split 4.6 into **product measures** (accuracy on his own check, latency, cost, logs clean, delete works: none of these need him watched) and **his verdict** (one question he answers when he wants: "Would you use this again?"). Delete "on the days he planned to". Make error reporting one tap, optional, never counted against a target. Replace 4.6.6 with "he stopped when he wanted to, and stopping worked the first time". |
| F3 | **The PO is parent, builder, observer, data holder and judge at once.** D13 ("who may see his notes", default no one) is good, but the PO still sees the cost counter, AI-usage rows and logs, which show when and how often he used it (4.4, 4.8). 4.6.1 lets the PO record the student's words. 4.7 asks the PO to notice when he is "upset, anxious or overwhelmed". | 4.4, 4.6.1, 4.7, 4.8, D13 | The plan asks a parent to read a young person's mood and activity and act on it. That is exactly the kind of inference the product promises not to make (invariant 7). A parent who built the tool also has a stake in it working; the student may say yes to be kind. Neither person can fix this by trying harder; the structure has to carry it. | Make STOP conditions in 4.7 **behavioural and his**: he says stop, he stops using it, he reports a problem. Not "the PO judges he seems anxious". Non-use is never a signal of anything. The PO sees **daily cost totals only**, not per-session times, unless the student agrees otherwise (D13 extended). Only the student writes down the student's words. Add a one-line script the PO says once: "This is my project, not your work. Using it or not changes nothing between us." |
| F4 | **Section 9 topics are close to right but incomplete, and one phrase will read as a threat.** "Longer if flagged for safety" tells a young person that a company's safety system might flag *him*. The topics also leave out the three things he will actually want to know: who can see his notes (D13), what his parent can see (cost, counts), and that nothing about him is being measured or studied. | 9, 4.2.5 | The warning is the student's only written account of the deal. If it omits what the PO can see, the first time he learns the PO saw a cost line will feel like being watched. Clinical words and labels ("pilot", "trial", "participant", "neurodivergent", any diagnosis, "help you focus") must not appear; they would make the tool about him rather than about his notes. | Full draft text in section 4, checked against the language guide checklist. Replace "flagged for safety" with "if their automatic checks hold it back for review, they may keep it longer". Add topics: who sees notes; what the PO can see; "this is not a study"; how to stop; how to report a problem. |
| F5 | **"Report this" is promised but undefined, and the obvious implementation is barred.** 4.3 promises "a simple, private way for him to report 'this was wrong / this felt bad'". The natural thumbs up/down is the one feedback shape `anthropic-terms-review.md` F-M10 says Tether must not add (feedback and ratings are the vendor's training path), and thumbs are a praise mechanic the language guide excludes. | 4.3 | Without a defined control, feedback becomes "tell your dad", which is the observer problem again. | Section 5 specifies a local-only, content-free, one-tap control: **Report this** on every AI-written block, three choices ("Not right", "Not useful", "Felt off"), optional note, stored in Tether only, never sent to any vendor, visible to the student in his own list, shared with the PO only when he chooses. No scheduled check-ins (Quiet mode stays quiet). |

Other findings, lower weight, are in section 2.

---

## 2. Further findings

| # | Finding | Where | Proposed fix |
|---|---|---|---|
| F6 | 4.6.7 asks that "delete everything" be "exercised once on purpose" during Stage 2. That is his real notes. A measure must not require him to destroy his own work to satisfy a checklist. | 4.6.7, 4.2.3 | Delete-all and export-all are proven on a seeded test account **before** Stage 2 (already entry condition 4.2.3). In Stage 2 the only person who presses delete is him, when he wants to. Record that it worked if he does; do not schedule it. |
| F7 | 4.7 first bullet: "Stop that day; revisit only if he asks." Good rule. But "revisit" needs a default of silence: nobody raises it again. Pair it with 7.8 ("A STOP condition ends the session first and is discussed afterwards"), which for the student should read "discussed afterwards **only if he wants to**". | 4.7, 7.8 | Add to 7.8: "For the student, 'afterwards' means when and if he brings it up." |
| F8 | The age question (D7) changes the consent picture, not only the vendor picture. If he is under 18, a parent is both the legal decider and the observer, and "freely" is harder still. If he is an adult, the plan should treat him as the primary decider and the PO as account holder. | 4.2.2, D7, section 12 item 3 | Record D7 before D14. If adult: D14 is his decision, D4 is the PO's gate on spend and services only. If minor: D14 still exists as his own yes, and the plan says plainly that his no overrides the PO's go. |
| F9 | Anything written about his reactions, his words, his age or any label is **personal data in a public repository** (playbook section 6: "anything committed is public"). The plan already names "the PO's son"; that is the PO's call. His feedback, his words, his usage must never be committed. | 4.6.1, 4.6.2, playbook 6 | Add a rule to section 7: "No record of the student's words, reactions, usage, age or any personal label goes into the repository, issues, CI logs or the Review Desk. Stage 2 records live only on the PO's machine, under the student's control where they are about him." |
| F10 | 4.2.5 says the warning is shown "before first use of the real services". That is one dialog, once. People do not retain a one-time dialog. | 4.2.5, section 9 | Keep the one-time screen, and also make the same text reachable at any time from Settings ("What happens to my notes"). Consistent placement rule (student-experience 1.12). |
| F11 | The plan calls Stage 2 a "pilot" and the student the "user" throughout. Fine for the PO's document. The student-facing copy must not use "pilot", "trial", "test", "participant", "feedback session", "study". | section 9 | Section 4 of this file avoids them. Add a note in section 9 of the plan: "Words not to use with him: pilot, trial, test, participant, study, feedback, neurodivergent, any diagnosis, focus, attention." |
| F12 | 4.3 "Quiet mode and optional engagement stay on by default" is right. Add: the real-AI switch for his account is his to turn off too, not only the PO's mock-mode switch (7.1). A student who cannot turn the real service off himself has to ask his parent to stop. | 4.3, 7.1 | "The student can switch his own account back to the built-in stand-in (mock) from Settings, with one control, labelled 'Use the AI service: On / Off'. It takes effect at once. Nothing is deleted by turning it off." |
| F13 | 4.6.3 and 4.6.4 (latency, cost) are fine as product measures, but "any longer wait is logged" and "cost per day" are things the PO reads. Both are metadata that reveals session times. See F3. | 4.6.3, 4.6.4 | Aggregate per day; no per-session timestamps shown to the PO by default. |
| F14 | Section 9 topic "The notes can be wrong; check anything important against his own source." Good. Add the deadline case specifically, since 4.7 names it as a stop condition: dates never go to a calendar without his review (invariant 4), and he should know that. | 9, 4.7 | Covered in section 4 draft. |

---

## 3. Proposed structural changes to `docs/product/pilot-plan.md`

These are proposals for the plan's owner. I did not edit the plan.

### 3.1 Section 4.2 (entry conditions): reorder and add the student's decision

Replace items 5 and 6 with:

> 5. The student has read the plain-language explanation (section 9, approved under D8) **before** he is asked anything. It is shown to him in the app and is reachable afterwards from Settings.
> 6. The student has said yes (D14). "Yes" is given once, is his to withdraw at any time by stopping, and is never asked for again if he says no or "not now". Stopping needs no reason and is not followed by questions that day.
> 7. The PO has said, once and plainly, that using the tool or not changes nothing between them, and that nothing about him is being measured or studied.

### 3.2 Section 4.6 (success measures): split into product measures and his verdict

Replace 4.6 with:

> **4.6a Product measures (do not need him observed)**
> 1. Accuracy: he can mark any AI-written block "Not right" with one tap (section 5 of the UX review). The count of such marks per block type is a product measure. It is not a target he has to reach; zero marks means nothing either way.
> 2. Latency: Quick recap and note generation complete within the Stage 1 range. Logged as daily aggregates.
> 3. Cost per day at or under the cap; invoice matches the counter. Daily totals only.
> 4. Zero raw note content in logs (planted-phrase test on the real stack).
> 5. Export-all and delete-all were proven on a seeded account before the stage started (4.2.3). If he uses delete during the stage, it worked the first time.
> 6. Stop and the "Use the AI service: Off" switch each worked the first time he used them.
>
> **4.6b His verdict (his words, his timing)**
> 7. At any point, and at least once if he is willing, he answers one question in the app: "Would you use this again?" with "Yes / Not sure / No" and an optional note. The answer is stored in Tether only and is his to show or not.
>
> Deleted on purpose: "uses it on the days he planned to", "every wrong statement is logged and counted" (as his job), "no stress signal lasts". Usage frequency is not a success measure and is not reported.

### 3.3 Section 4.7 (STOP conditions): make the first bullet about what he does, not how he seems

Replace the first bullet with:

> - He says stop, turns the AI service off, or stops using it and says so. Stop that day. Nobody raises it again unless he does. Not using it, on any day or for any stretch, is not a signal of anything and is never asked about.

Keep the remaining bullets. The "Anyone wants to turn this into a claim about learning outcomes, disability or health" bullet is correct and should stay.

### 3.4 Section 4.4 and D13: what the PO sees

Extend D13:

> D13. Who may see his notes (default: no one), **and** what the PO sees of his activity. Proposal: the PO sees the daily cost total and the daily cap state, nothing per session, and no feedback marks unless the student shows them. The student can see everything the PO can see, on the same screen.

### 3.5 Section 7 (safety rules): add three rules

> 9. The student can turn the real AI service off for his own account, in Settings, at once.
> 10. Nothing about the student (words, reactions, usage, age, labels) is committed to the repository, issue tracker, CI or Review Desk. Records about him stay on the PO's machine, under his control where they are about him.
> 11. For the student, "discussed afterwards" (rule 8) means when and if he brings it up.

### 3.6 Section 10 (decisions): add D14

| ID | Decision | Needed by | Repo status |
|---|---|---|---|
| D14 | **The student's own yes.** Asked once, after reading the section 9 text; "no" and "not now" are complete answers; withdrawable at any time by stopping; never re-asked by the PO. If D7 says adult, D14 is the primary decision for Stage 2 and D4 covers only spend and services. If D7 says minor, D14 still exists and his no overrides the PO's go. | Before Stage 2 | New (this review) |

### 3.7 Section 9: replace the topic list with the full draft in section 4 below, plus a words-to-avoid line

---

## 4. Draft plain-language text for the student (D8; DRAFT, not approved, not counsel-reviewed)

Shown once before the real service is first used, and always available from Settings under "What happens to my notes". Checked against `docs/ux/language-guide.md` section 9 checklist: no exclamation marks, no clinical words, no "pilot / trial / test / participant", sentences under 20 words, subject is the app or his own action, each state says what is true and what he can do.

> **What happens to my notes**
>
> Tether can send the notes and questions you type to an AI service to write recaps and summaries. This page says what that means. You choose whether to turn it on.
>
> **What leaves this computer**
> Only the text you type into Tether. It goes to Claude, an AI run on Microsoft's Azure computers. Nothing else leaves, unless you choose to export a note to your own Google account.
>
> **How long they keep it**
> The AI company keeps what is sent for up to 30 days, then removes it. If their automatic checks hold something back for review, they may keep it longer. We cannot see or change what they keep.
>
> **Deleting**
> Delete in Tether removes Tether's copy at once. It cannot reach the copies the AI company already has, or anything you exported to Google.
>
> **Who can see your notes**
> Nobody but you. [D13 answer goes here; if the default holds: "Not your parent either, unless you show them."]
>
> **What your parent can see**
> The daily total of what the service cost, and whether the daily limit was reached. Not your notes, not when you used it, not what you reported.
>
> **Stopping**
> You can pause or stop at any time. You can turn the AI service off in Settings with one switch; Tether then uses its built-in stand-in instead. Nothing is deleted when you turn it off. You do not need to give a reason.
>
> **The notes can be wrong**
> Recaps and answers are the AI's best estimate. Check anything that matters against what you wrote. Tether never puts a date in a calendar without you reviewing it first.
>
> **Saying something is off**
> Every recap and answer has a "Report this" link. One tap marks it "Not right", "Not useful" or "Felt off". The marks stay in Tether. Nobody sees them unless you show them.
>
> **This is not a study**
> Nothing about you is being measured, scored or tracked. This page is about the service, not about you.
>
> [ Turn the AI service on ]   [ Not now ]

Notes for the PO on this draft:
- "Not now" is the secondary action and is complete. No "Are you sure?" follows it.
- "Up to 30 days" is the direct-API figure; the Foundry figure is UNVERIFIED (`azure-platform-verification.md` 1.3). If the PO cannot confirm it, change to "for a limited time, usually up to 30 days" and record why.
- Replace "your parent" with whatever he calls the PO; the point is the role, not the word.
- Words deliberately absent: pilot, trial, test, participant, feedback, study (except to deny it), neurodivergent, ADHD, focus, attention, help you, designed for you, safety flag.

---

## 5. Component behaviour note: "Report this" (local-only feedback)

Purpose: a one-tap, private way to say an AI-written block was wrong or felt off, without a screenshot, without telling anyone in person, and without feeding any vendor.

Constraints it satisfies: F-M10 in `anthropic-terms-review.md` (no feedback or rating call to the vendor); `CLAUDE.md` invariant 10 (no raw content in logs); language guide 1.6 (no praise mechanics, so no thumbs); student-experience 1.8 and 1.10 (no prompts, no inference); Quiet mode (nothing is ever asked on a schedule).

Behaviour:
- **Where:** a `.link`-styled "Report this" at the end of every AI-written block (recap, summary bullet group, Ask answer, quiz feedback). Not on verbatim transcript or his own text. Same place every time.
- **What it opens:** a small inline choice group (radio semantics, arrow keys), three options: "Not right", "Not useful", "Felt off". Optional single-line note, never required. One "Save" button. Escape or "Cancel" closes with nothing saved.
- **What is stored:** block id, block type, prompt version, choice, optional note, timestamp. **Not** the block text and **not** his source notes. The note is his text and is stored as his data, user-scoped, under the same delete rules as everything else.
- **Where it goes:** Tether's database only. No call to any vendor, no telemetry event, no log line with the note. A test asserts that saving a report makes zero outbound calls and writes no note text to logs.
- **What he sees after:** "Saved." in the single polite status region. No thanks, no praise, no "this helps us improve".
- **Where he can review:** Settings > "Things I reported": his list, with "Show this list" (opens a plain view he can turn the screen toward) and "Delete" per item. No export to anyone by default. If he wants the PO to see it, he shows it or exports it himself.
- **His verdict question:** on the same Settings page, one line: "Would you use this again?" with "Yes / Not sure / No" and an optional note, answerable any time, changeable any time. Never shown as a prompt or banner. Quiet mode means it waits for him.
- **Accessibility:** 44px targets; the link's accessible name starts with the visible text "Report this" (WCAG 2.5.3); choice group has a visible label; focus returns to the "Report this" link on close; works with the existing three live regions (no new one).
- **What it is not:** not a thumbs pair, not a star rating, not a counter he can see rise, not a streak, not a prompt after N uses.

Owner to build: frontend-engineer (control), backend-engineer (user-scoped table and ownership tests), qa-engineer (zero-outbound-call and no-log test). This note is a specification, not code.

---

## 6. Gate G4: what Stage 2 can and cannot say

`docs/RELEASE_GATES.md` G4 requires "a documented voluntary study with expert review". `docs/research/human-validation-plan.md` section 6 lists the claims that are barred. Stage 2 is one person, in his own family, using a tool his parent built, with no protocol, no independent reviewer and no comparison. It is personal use. It clears nothing.

Can be said, inside the project only:
- "One user tried it on his own notes and chose to keep using it / stop." (As design evidence, not as a result.)
- "He marked N blocks 'Not right'; the following prompt changes followed." (Product evidence.)
- Latency, cost, log cleanliness, delete and stop working. (Engineering evidence toward G2 and G5 preparation, not PASS.)

Cannot be said, anywhere:
- Anything about neurodivergent suitability, cognitive load, focus, attention, memory, executive function, learning, grades or wellbeing, positive or negative.
- Anything about him: no label, no diagnosis, no "a neurodivergent student found that...".
- "Validated", "tested with a neurodivergent student", "designed with/for a neurodivergent user and found helpful".
- That the language guide's wording choices are right. One person's comfort with "Quick recap" is not the G4 question in language guide section 10.
- That Quiet mode "works" for neurodivergent students.

Human-only tasks that remain for G4, untouched by Stage 2: expert review by named clinicians and learning scientists (human-validation-plan Stage 1); moderated sessions with consenting adult students with a written ethics determination (its Stage 2); a named accountable reviewer in the gate register (currently `_unassigned_`).

---

## 7. Check before the Stage 2 "go": is it with him or on him

Each line is a yes/no the PO can answer in writing. Any "no" means the structure, not the people, needs fixing first.

1. He read the section 4 text before anyone asked him anything.
2. He was asked once. "Not now" was offered as a plain button and would have ended it.
3. He can turn the real service off himself, without asking anyone.
4. He can stop, and nobody asks why that day, or later unless he raises it.
5. Nobody sees his notes unless he shows them (D13 answered and told to him).
6. He knows exactly what the PO can see (daily cost total) and nothing else is collected about him.
7. No measure counts how often or when he uses it.
8. Every "report" goes to a list he controls, not to a person by default, and never to a vendor.
9. He will never be asked to delete his real notes to prove delete works.
10. Nothing about him will be written into the public repository, an issue, CI output or the Review Desk.
11. The PO has said the one sentence: using it or not changes nothing between them.
12. Everyone involved knows Stage 2 proves nothing about neurodivergence, learning or health, and the plan says so in words he could read.

---

## 8. Questions for the Lead Architect and the PO

1. D7 (his age) changes who the primary decider is in section 3.6. Please record it before D14 is drafted into the plan.
2. D13 default is "no one, not even the PO". Does that extend to the PO's view of AI-usage rows and logs? This review proposes daily totals only. If the PO needs per-session data for cost debugging, the student should be told that in the section 4 text.
3. "Report this" needs a small schema and a user-scoped table. That is a backend contract addition (additive, not a frozen-contract change). Lead to confirm owner and sequence; it is an entry condition for Stage 2 under 4.3, so it comes before the stage, not during.
4. The words "the PO's son" are already in the committed-to-be draft and in `docs/DECISIONS.md` PO-15 (public). That is an existing PO choice; this review only asks that nothing further about him be added.
5. Should the section 4 text be reviewed by the `legal-compliance-reviewer` for the data-retention sentences before D8? The tone and structure are UX; the 30-day and Foundry statements are legal facts I did not re-verify.

---

## 9. What I did and did not do

- Read the files listed at the top. Ran no commands, built nothing, tested nothing.
- Edited no file other than this one. Did not touch `docs/product/pilot-plan.md`, git, or the Review Desk.
- Did not consult the student, the PO, any clinician or any student. Every judgement here is design judgement against the project's own written rules; none of it is validated.
- Vendor retention facts are quoted from the repo's legal review and remain UNVERIFIED where that review says so.
