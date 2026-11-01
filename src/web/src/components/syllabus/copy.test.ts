import fs from "node:fs";
import path from "node:path";
import { collectStrings, FORBIDDEN, FORBIDDEN_SYLLABUS, sentencesOver } from "@/lib/copyLint";
import * as copy from "./copy";

// Language guide (docs/ux/language-guide.md, binding) lint for the syllabus screens: the shared lecture rules plus the
// syllabus additions in lib/copyLint.ts.
const all = collectStrings(copy);

it("syllabus copy.ts has strings to lint", () => {
  expect(all.length).toBeGreaterThan(120);
});

it.each([...FORBIDDEN, ...FORBIDDEN_SYLLABUS])("no %s in syllabus copy", (_name, re) => {
  expect(all.filter((s) => re.test(s))).toEqual([]);
});

it("sentences stay under 20 words (language guide principle 7)", () => {
  expect(sentencesOver(all, 20)).toEqual([]);
});

it("confidence labels are exactly the three contract labels (language guide 3.6 row 44, contract section 4)", () => {
  expect(Object.values(copy.ITEM_COPY.conf)).toEqual(["Looks right", "Probably right", "Please check"]);
});

it("Demo tags are verbatim from the UX spec (gate G1: Demo wording is not reworded here)", () => {
  const spec = fs.readFileSync(path.resolve(process.cwd(), "../../docs/ux/phase2-syllabus-review.md"), "utf8");
  expect(spec).toContain(`\`upload.demoTag\`: "${copy.DEMO_TAGS.source}"`);
  expect(spec).toContain(`\`review.demoTag\`: "${copy.DEMO_TAGS.calendar}"`);
});

it("F2 calendar Demo lines are verbatim from the UX spec, and no copy offers reminders (contract row 13)", () => {
  const spec = fs.readFileSync(path.resolve(process.cwd(), "../../docs/ux/phase2-syllabus-review.md"), "utf8");
  expect(spec).toContain(`\`sync.confirm.demo\`: "${copy.SYNC_COPY.confirmDemo}"`);
  expect(spec).toContain(`\`sync.result.demo\`: "${copy.SYNC_COPY.resultDemo}"`);
  expect(copy.SYNC_COPY.tagWord).toBe("Demo calendar");
  expect(collectStrings(copy.SYNC_COPY).filter((s) => /remind/i.test(s))).toEqual([]);
});

it("the delete confirmation says demo events go too and exported copies stay (contract 5.2, row 25)", () => {
  expect(copy.DELETE_COPY.body).toMatch(/demo calendar are removed too/);
  expect(copy.DELETE_COPY.copies).toMatch(/not deleted/);
  expect(copy.DELETE_COPY.body).not.toMatch(/stay there/);
});

it("F2 accessible names start with the visible text (WCAG 2.5.3)", () => {
  const t = "Midterm exam";
  const S = copy.SYNC_COPY;
  const B = copy.BATCH_COPY;
  expect(S.removeName(t).startsWith(S.remove)).toBe(true);
  expect(S.undoRemoveName(t).startsWith(copy.ITEM_COPY.undo)).toBe(true);
  expect(S.retryName(t).startsWith(S.retry)).toBe(true);
  expect(B.groupName(B.week(2), "Week of Oct 12 to 18").startsWith(B.week(2))).toBe(true);
  expect(copy.LIST_COPY.openName("Oct 30, 3:05 PM").startsWith(copy.LIST_COPY.open)).toBe(true);
});

it("the notice is labelled DRAFT and placeholder, and claims no permission check (P2-D5)", () => {
  const n = collectStrings(copy.NOTICE_DRAFT).join(" ");
  expect(n).toMatch(/draft/i);
  expect(n).toMatch(/placeholder/i);
  expect(n).not.toMatch(/\b(allowed|permission|verified|checked that you may|you may upload)\b/i);
});

it("accessible names start with the visible text (WCAG 2.5.3)", () => {
  const t = "Midterm exam";
  const I = copy.ITEM_COPY;
  expect(I.approveName(t).startsWith(I.approve)).toBe(true);
  expect(I.skipName(t).startsWith(I.skip)).toBe(true);
  expect(I.editName(t).startsWith(I.edit)).toBe(true);
  expect(I.addDateName(t).startsWith(I.addDate)).toBe(true);
  expect(I.undoApproveName(t).startsWith(I.undo)).toBe(true);
  expect(I.undoSkipName(t).startsWith(I.undo)).toBe(true);
});

it("SBR-06: the dropped-dates notice carries no count (no digits) and is the spec's DRAFT wording", () => {
  const { dropped, droppedBody } = copy.REVIEW_COPY;
  expect(dropped).not.toMatch(/\d|%|percent/i);
  expect(droppedBody).not.toMatch(/\d|%|percent/i);
  const spec = fs.readFileSync(path.resolve(process.cwd(), "../../docs/ux/dropped-dates-notice.md"), "utf8");
  expect(spec).toContain(`dropped: "${dropped}"`);
  expect(spec).toContain(`droppedBody: "${droppedBody}"`);
});

it("PO-20b: UNCLAIMED_COPY and the three REVIEW_COPY keys are the spec's DRAFT wording (unclaimed-dates-list.md 5)", () => {
  const spec = fs.readFileSync(path.resolve(process.cwd(), "../../docs/ux/unclaimed-dates-list.md"), "utf8");
  const U = copy.UNCLAIMED_COPY;
  for (const [k, v] of Object.entries(U)) if (typeof v === "string") expect(spec, k).toContain(`${k}: "${v}"`);
  for (const [k, v] of Object.entries(U.form)) if (typeof v === "string") expect(spec, `form.${k}`).toContain(`${k}: "${v}"`);
  for (const k of ["droppedSee", "emptyNoticed", "emptyNoticedBody"] as const) expect(spec, k).toContain(`${k}: "${copy.REVIEW_COPY[k]}"`);
  expect(collectStrings(U).length).toBeGreaterThan(30); // linted with the rest of the module above
});

it("PO-20b: no string in UNCLAIMED_COPY carries a digit except through a template argument (the head count comes from data)", () => {
  const out: string[] = [];
  const walk = (v: unknown) => {
    if (typeof v === "string") out.push(v);
    else if (typeof v === "function") out.push(String((v as (...a: unknown[]) => unknown)("X", "X", "X")));
    else if (v && typeof v === "object") Object.values(v).forEach(walk);
  };
  walk(copy.UNCLAIMED_COPY);
  expect(out.filter((s) => /\d/.test(s))).toEqual([]);
  expect(copy.UNCLAIMED_COPY.lines(1)).toBe("1 line");
  expect(copy.UNCLAIMED_COPY.lines(12)).toBe("12 lines");
});

it("PO-20b: accessible names start with the visible text, and no G1 wording (consent, recording, Demo, Google) appears", () => {
  const U = copy.UNCLAIMED_COPY;
  expect(U.addName("Oct 5").startsWith(U.add)).toBe(true);
  expect(U.hideName("Oct 5").startsWith(U.hide)).toBe(true);
  expect(U.undoName("Oct 5").startsWith(U.undo)).toBe(true);
  expect(U.goToItemName("Quiz").startsWith(U.goToItem)).toBe(true);
  const all = [...collectStrings(U), copy.REVIEW_COPY.droppedSee, copy.REVIEW_COPY.emptyNoticed, copy.REVIEW_COPY.emptyNoticedBody].join(" ");
  expect(all).not.toMatch(/record|microphone|consent|demo|google/i);
});

it("slice 8c: the Calendar step's counts sentence is the summary line's second half in the same words", () => {
  const S = copy.SYNC_COPY;
  expect(S.railCounts(2, 0, 4)).toBe("2 approved, 0 skipped, 4 not decided.");
  expect(S.railCounts(1, 1, 1)).toBe("1 approved, 1 skipped, 1 not decided.");
  for (const [n, a, s, u] of [[6, 2, 0, 4], [1, 0, 0, 1], [11, 3, 2, 6]] as const) {
    expect(copy.REVIEW_COPY.summary(n, a, s, u).endsWith(` ${S.railCounts(a, s, u)}`)).toBe(true);
  }
});
