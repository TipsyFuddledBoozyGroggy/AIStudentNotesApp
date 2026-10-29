import * as copy from "./copy";

// Voice rules (design-direction section 1, m2 sections 6 and 9): lint every student-facing string in copy.ts.
// Functions are called with sample arguments so templated strings are checked too.
function strings(v: unknown, out: string[] = []): string[] {
  if (typeof v === "string") out.push(v);
  else if (typeof v === "function") strings((v as (...a: unknown[]) => unknown)("Biology 101", "00:12", 3), out);
  else if (Array.isArray(v)) v.forEach((x) => strings(x, out));
  else if (v && typeof v === "object") Object.values(v).forEach((x) => strings(x, out));
  return out;
}

const all = strings(copy);

const FORBIDDEN: [string, RegExp][] = [
  ["exclamation mark", /!(?!\s*$)|!$/],
  ["emoji", /\p{Extended_Pictographic}/u],
  ["chirpy filler", /\b(oops|awesome|don't worry)\b|\bjust\b(?! now)/i],
  ["clinical or labelling", /\b(adhd|focus problems?|distracted|lazy)\b/i],
  ["guilt or pressure", /\b(behind|overdue|missed|streak)\b/i],
  ["attention judgement", /\b(lost focus|inactive|idle|away)\b/i],
  // Product Owner rule (2026-10-29): the recovery feature is "Quick recap"; the old deficit-framed name must not return.
  ["old recovery name", /\b(lost track|lost the thread)\b/i],
  ["dishonest offline claim", /still recording on your device|we will fill it in/i],
  // Language guide (docs/ux/language-guide.md sections 2, 3.9, 9): pressure and deficit framing, raw errors.
  ["pressure or deficit framing", /\b(right now|nothing (is|was) lost|here's where you are|you have only been here|catch(-| )up on)\b/i],
  ["raw error text", /request failed|status code|\(\d{3}\)/i],
];

it("copy.ts has strings to lint", () => {
  expect(all.length).toBeGreaterThan(100);
});

it.each(FORBIDDEN)("no %s in lecture copy", (_name, re) => {
  expect(all.filter((s) => re.test(s))).toEqual([]);
});

it("no sentence is longer than 30 words (target: under 20)", () => {
  const long = all.flatMap((s) => s.split(/(?<=[.?])\s+/)).filter((s) => s.split(/\s+/).length > 30);
  expect(long).toEqual([]);
});
