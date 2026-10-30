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
