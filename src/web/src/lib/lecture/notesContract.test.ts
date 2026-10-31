import { describe, expect, it } from "vitest";
import { NOTES_NOTICE_VERSION } from "@/components/lecture/copy";
import { notesRequestBody, parseNotes } from "./aiPayloads";
import { loadFixtures, loadValidatorsFromDisk } from "./schemas.node";

// Slice 8a (PO-18d, API_CONTRACTS section 17): the web STRICT notes parser against the real lecture.v1 notes schemas and
// the shared fixtures, under an ajv CODE-POINT validator (the `u` flag), the way the syllabus R3-07 test runs. Two
// oracles must agree on every fixture: Ajv (the schema) and parseNotes (what the UI will render). The one pinned
// difference is deliberate: an unknown member is DROPPED by the parser (never rendered), while the strict schema rejects it.

const cp = loadValidatorsFromDisk(undefined, { unicodeRegExp: true });
const isNotes = (f: { schema: string }) => f.schema === "notes";
const isRequest = (f: { schema: string }) => f.schema === "notes-request";
const valid = loadFixtures("valid");
const invalid = loadFixtures("invalid");
const validNotes = valid.filter(isNotes);
const invalidNotes = invalid.filter(isNotes);

/** Invalid notes fixtures the parser accepts after dropping members it does not know. Exactly these. */
const DROPPED_NOT_REJECTED = ["notes--unknown-field.json"];

describe("lecture.v1 notes schemas under ajv with the u flag", () => {
  it("both notes schemas compile with the u flag", () => {
    expect(typeof cp.byName("notes")).toBe("function");
    expect(typeof cp.byName("notes-request")).toBe("function");
  });

  it("has the backend's notes fixtures to run", () => {
    expect(validNotes.length).toBeGreaterThanOrEqual(7);
    expect(invalidNotes.length).toBeGreaterThanOrEqual(18);
    expect(valid.filter(isRequest).length).toBeGreaterThanOrEqual(1);
    expect(invalid.filter(isRequest).length).toBeGreaterThanOrEqual(8);
  });

  it.each(validNotes.map((f) => [f.file, f] as const))("valid notes fixture: ajv accepts it and parseNotes returns it unchanged: %s", (_n, f) => {
    expect(cp.byName("notes")(f.json), cp.explain("notes")).toBe(true);
    expect(parseNotes(f.json)).toEqual(f.json);
  });

  it.each(invalidNotes.map((f) => [f.file, f] as const))("invalid notes fixture: ajv rejects it and parseNotes refuses it: %s", (_n, f) => {
    expect(cp.byName("notes")(f.json)).toBe(false);
    if (DROPPED_NOT_REJECTED.includes(f.file)) {
      const parsed = parseNotes(f.json) as Record<string, unknown> | null;
      expect(parsed).not.toBeNull();
      expect(parsed).not.toHaveProperty("transcript"); // the unknown member is gone, never rendered
      expect(cp.byName("notes")(parsed)).toBe(true); // and what is left is schema-valid
    } else {
      expect(parseNotes(f.json)).toBeNull();
    }
  });

  it("the request body the client builds is the one valid shape; every invalid request fixture is rejected", () => {
    const v = cp.byName("notes-request");
    expect(notesRequestBody(NOTES_NOTICE_VERSION)).toEqual({ noticeVersion: "notes-notice-draft-1" });
    expect(v(notesRequestBody(NOTES_NOTICE_VERSION)), cp.explain("notes-request")).toBe(true);
    for (const f of valid.filter(isRequest)) expect(f.json).toEqual(notesRequestBody(NOTES_NOTICE_VERSION));
    for (const f of invalid.filter(isRequest)) expect([f.file, v(f.json)]).toEqual([f.file, false]);
    expect(v({})).toBe(false); // the earlier empty body is refused (NAS-05)
  });

  it("explain() never echoes offending values", () => {
    cp.byName("notes")({ notesId: "SECRET-CANARY" });
    expect(cp.explain("notes")).not.toContain("SECRET-CANARY");
  });
});

describe("parseNotes agrees with ajv on mutations of the valid fixtures", () => {
  const ready = validNotes.find((f) => f.file === "notes--ready.json")!.json as Record<string, unknown>;
  const pending = validNotes.find((f) => f.file === "notes--pending.json")!.json as Record<string, unknown>;
  const sec = (ready.sections as Record<string, unknown>[])[0]!;
  const bul = (sec.bullets as Record<string, unknown>[])[0]!;
  const withBullet = (b: Record<string, unknown>) => ({ ...ready, sections: [{ ...sec, bullets: [b] }] });
  const cases: [string, unknown][] = [
    ["notesId upper-case hex", { ...ready, notesId: "nt_0192F0A1B2C34D5E8F9A0B1C2D3E4F50" }],
    ["revision 0", { ...ready, revision: 0 }],
    ["revision a float", { ...ready, revision: 1.5 }],
    ["requestedAt without offset", { ...ready, requestedAt: "2026-10-31T14:02:11" }],
    ["coveredToMs a string", { ...ready, coveredToMs: "9500" }],
    ["truncated a string", { ...ready, truncated: "false" }],
    ["sections not an array", { ...ready, sections: {} }],
    ["51 sections", { ...ready, sections: Array.from({ length: 51 }, () => sec) }],
    ["a section without bullets", { ...ready, sections: [{ ...sec, bullets: [] }] }],
    ["13 bullets", { ...ready, sections: [{ ...sec, bullets: Array.from({ length: 13 }, () => bul) }] }],
    ["9 citations", withBullet({ ...bul, citations: Array.from({ length: 9 }, () => ({ segmentId: "seg_1", startMs: 1 })) })],
    ["a citation with a bad segment id", withBullet({ ...bul, citations: [{ segmentId: "seg-1", startMs: 1 }] })],
    ["empty bullet text", withBullet({ ...bul, text: "" })],
    ["501 code points of bullet text", withBullet({ ...bul, text: "\u{1F600}".repeat(501) })],
    ["121 code points of heading", { ...ready, sections: [{ ...sec, heading: "x".repeat(121) }] }],
    ["Ready with a null finalSummaryStatus", { ...ready, finalSummaryStatus: null }],
    ["Pending with truncated true", { ...pending, truncated: true }],
    ["Pending with a finalSummaryRevision", { ...pending, finalSummaryRevision: 2 }],
    ["an array", [ready]],
    ["null", null],
  ];
  it.each(cases)("rejects: %s", (_n, v) => {
    expect(cp.byName("notes")(v)).toBe(false);
    expect(parseNotes(v)).toBeNull();
  });

  it("accepts 500 astral code points (1000 UTF-16 units): maxLength counts code points", () => {
    const v = withBullet({ ...bul, text: "\u{1F600}".repeat(500) });
    expect(cp.byName("notes")(v)).toBe(true);
    expect(parseNotes(v)).not.toBeNull();
  });

  it("drops unknown members at every level and keeps the text as plain data", () => {
    const v = {
      ...ready,
      extra: "<b>x</b>",
      sections: [{ ...sec, html: "<script>", bullets: [{ ...bul, model: "x", citations: [{ segmentId: "seg_1", startMs: 2000, score: 1 }] }] }],
    };
    const p = parseNotes(v)!;
    expect(p).not.toHaveProperty("extra");
    expect(p.sections[0]).not.toHaveProperty("html");
    expect(p.sections[0]!.bullets[0]).toEqual({ text: bul.text, citations: [{ segmentId: "seg_1", startMs: 2000 }] });
    expect(cp.byName("notes")(p)).toBe(true);
  });
});
