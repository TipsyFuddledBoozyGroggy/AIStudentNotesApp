import { collectStrings, FORBIDDEN, sentencesOver } from "@/lib/copyLint";
import * as copy from "./copy";

// Voice rules (design-direction section 1, m2 sections 6 and 9): lint every student-facing string in copy.ts.
// Functions are called with sample arguments so templated strings are checked too. Rules live in lib/copyLint.ts,
// shared with the syllabus copy lint.
const all = collectStrings(copy);

it("copy.ts has strings to lint", () => {
  expect(all.length).toBeGreaterThan(100);
});

it.each(FORBIDDEN)("no %s in lecture copy", (_name, re) => {
  expect(all.filter((s) => re.test(s))).toEqual([]);
});

it("no sentence is longer than 30 words (target: under 20)", () => {
  expect(sentencesOver(all, 30)).toEqual([]);
});
