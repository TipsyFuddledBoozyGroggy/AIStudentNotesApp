import { readFileSync } from "node:fs";
import path from "node:path";
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

// Slice 8a (PO-18d): NOTES_COPY is pinned word for word against the table in docs/ux/make-notes-rail-demo-notice.md 1.3
// (templates are called with their "${name}" placeholders so the table's own text is compared), and every DRAFT (G1)
// disclosure string ends in the visible "(Draft wording.)" suffix.
describe("NOTES_COPY (slice 8a)", () => {
  const spec = readFileSync(path.resolve(process.cwd(), "../../docs/ux/make-notes-rail-demo-notice.md"), "utf8");
  const section = spec.slice(spec.indexOf("### 1.3"), spec.indexOf("### 1.4"));
  const rows = [...section.matchAll(/^\| `([^`]+)` \| `([^`]+)` \|/gm)].map((m) => [m[1]!, m[2]!] as const);
  const ph = (s: string) => s; // identity: placeholders are passed through literally
  const value = (key: string): string => {
    if (key === "TLDR_COPY.final") return copy.TLDR_COPY.final;
    const v = key.startsWith("errors.") ? copy.NOTES_COPY.errors[key.slice(7) as keyof typeof copy.NOTES_COPY.errors] : copy.NOTES_COPY[key as keyof typeof copy.NOTES_COPY];
    if (typeof v === "function") return key === "covers" ? (v as (a: string, b: string) => string)(ph("${from}"), ph("${to}")) : (v as (a: string) => string)(ph("${to}"));
    return v as string;
  };

  it("the spec table has every row this registry implements", () => {
    expect(rows.length).toBe(24);
    const inTable = new Set(rows.map(([k]) => k));
    // checkFailed / checkAgain are the spec's 1.5 "new key" (no table row); everything else is in the table.
    const keys = Object.keys(copy.NOTES_COPY).flatMap((k) => (k === "errors" ? Object.keys(copy.NOTES_COPY.errors).map((e) => `errors.${e}`) : [k]));
    expect(keys.filter((k) => !inTable.has(k))).toEqual(["checkFailed", "checkAgain"]);
    expect(spec).toContain(`"${copy.NOTES_COPY.checkFailed}" (new key \`checkFailed\`) and a tertiary "${copy.NOTES_COPY.checkAgain}"`);
  });

  it.each(rows)("%s matches the spec word for word", (key, text) => {
    expect(value(key)).toBe(text);
  });

  it("DRAFT (G1) disclosure strings carry the visible suffix; the notice id is the counsel-pending placeholder", () => {
    for (const k of copy.NOTES_DRAFT_KEYS) expect([k, copy.NOTES_COPY[k].endsWith("(Draft wording.)")]).toEqual([k, true]);
    expect(copy.NOTES_NOTICE_VERSION).toBe("notes-notice-draft-1");
    expect(copy.NOTES_NOTICE_VERSION).toMatch(/^[A-Za-z0-9._-]{1,64}$/);
  });

  it("the new strings pass the lint and stay under 20 words a sentence", () => {
    const notes = collectStrings(copy.NOTES_COPY);
    for (const [, re] of FORBIDDEN) expect(notes.filter((s) => re.test(s))).toEqual([]);
    expect(sentencesOver(notes, 20)).toEqual([]);
    expect(notes.filter((s) => /\b(still|just|right now|behind|missed|lost)\b/i.test(s))).toEqual([]);
  });
});
