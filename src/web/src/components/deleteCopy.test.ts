import { readFileSync } from "node:fs";
import path from "node:path";
import { collectStrings, FORBIDDEN, FORBIDDEN_SYLLABUS, sentencesOver } from "@/lib/copyLint";
import * as copy from "./deleteCopy";

// PO-20by approved the strings in docs/ux/delete-wording-draft.md as DRAFT wording. Every string the draft states
// literally is pinned to it word for word, so the registry cannot drift from what the Product Owner read; the derived
// strings (singular forms, the course variants, the generic and empty-course lines) are listed so a new one is noticed.
const draft = readFileSync(path.resolve(process.cwd(), "../../docs/ux/delete-wording-draft.md"), "utf8");
const all = collectStrings(copy);

it("has strings to lint", () => {
  expect(all.length).toBeGreaterThan(30);
});

it.each([...FORBIDDEN, ...FORBIDDEN_SYLLABUS])("no %s in the delete copy", (_name, re) => {
  expect(all.filter((s) => re.test(s))).toEqual([]);
});

it("sentences stay under 20 words (language guide principle 7)", () => {
  expect(sentencesOver(all, 20)).toEqual([]);
});

it("no promise the code cannot keep: no 'permanently', 'forever', backup, retention period or vendor claim (draft section 4, L-07, L-08)", () => {
  expect(all.filter((s) => /permanent|forever|gone for good|backup|within \d|days|outside service|Google|vendor|nothing is lost/i.test(s))).toEqual([]);
});

const literal: [string, string][] = [
  ["lecture.button", copy.LECTURE_DELETE_COPY.button],
  ["lecture.heading", copy.LECTURE_DELETE_COPY.heading],
  ["lecture.body", copy.LECTURE_DELETE_COPY.body],
  ["lecture.cannot", copy.LECTURE_DELETE_COPY.cannot],
  ["lecture.kept", copy.LECTURE_DELETE_COPY.kept],
  ["lecture.confirm", copy.LECTURE_DELETE_COPY.confirm],
  ["lecture.keep", copy.LECTURE_DELETE_COPY.keep],
  ["lecture.busy", copy.LECTURE_DELETE_COPY.busy],
  ["lecture.done", copy.LECTURE_DELETE_COPY.done],
  ["lecture.doneNote", copy.LECTURE_DELETE_COPY.doneNote("{lecture title}")],
  ["lecture.gone", copy.LECTURE_DELETE_COPY.gone],
  ["course.button", copy.COURSE_DELETE_COPY.button],
  ["course.blocked", copy.COURSE_DELETE_COPY.blocked],
  ["course.heading", copy.COURSE_DELETE_COPY.heading],
  ["course.body", copy.COURSE_DELETE_COPY.body],
  ["course.lectures", copy.COURSE_DELETE_COPY.lectures(9).replace("9", "{n}")],
  ["course.syllabi", copy.COURSE_DELETE_COPY.syllabi(9).replace("9", "{m}")],
  ["course.demoCalendar", copy.COURSE_DELETE_COPY.demoCalendar],
  ["course.cannot", copy.COURSE_DELETE_COPY.cannot],
  ["course.kept", copy.COURSE_DELETE_COPY.kept],
  ["course.confirm", copy.COURSE_DELETE_COPY.confirm],
  ["course.done", copy.COURSE_DELETE_COPY.done],
  ["course.doneNote", copy.COURSE_DELETE_COPY.doneNote("{course name}")],
  ["course.section", copy.COURSE_DELETE_COPY.section],
  ["problem.network", copy.DELETE_PROBLEM_COPY.network],
  ["problem.conflict", copy.DELETE_PROBLEM_COPY.conflict],
  ["problem.rate", copy.DELETE_PROBLEM_COPY.rate],
];

it.each(literal)("%s is in the PO-approved draft word for word", (_key, text) => {
  expect(draft).toContain(`"${text}"`);
});

it("the derived strings are exactly these (each one needs the UX specialist's review; none is in the draft verbatim)", () => {
  const derived = [
    copy.COURSE_DELETE_COPY.bodyEmpty,
    copy.COURSE_DELETE_COPY.lectures(1),
    copy.COURSE_DELETE_COPY.syllabi(1),
    copy.COURSE_DELETE_COPY.gone,
    copy.DELETE_PROBLEM_COPY.session,
    copy.DELETE_PROBLEM_COPY.generic,
    copy.DELETE_PROBLEM_COPY.dismiss,
  ];
  expect(derived).toEqual([
    "This deletes the course.",
    "1 lecture: transcript, summary, recaps and notes",
    "1 syllabus: the text, the items and your decisions",
    "This course is no longer here. It may have been deleted in another tab.",
    "Please sign in again. Nothing was deleted.",
    "That did not work. Nothing was deleted.",
    "Dismiss",
  ]);
  for (const s of derived) expect(draft).not.toContain(`"${s}"`);
});

it("every failure line says whether anything changed", () => {
  for (const [k, v] of Object.entries(copy.DELETE_PROBLEM_COPY)) if (k !== "dismiss") expect([k, /Nothing was deleted\./.test(v)]).toEqual([k, true]);
});
