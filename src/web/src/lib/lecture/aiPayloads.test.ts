import { describe, expect, it } from "vitest";
import { parseCatchUp, parseSummary } from "./aiPayloads";

const summary = {
  revision: 1, basedOnSeq: "3", tldr: "t", bullets: ["b"], coveredFromMs: 0, coveredToMs: 10, status: "Draft",
  citations: [{ segmentId: "seg_1", startMs: 5 }], source: "mock",
};
const catchUp = { catchUpId: "cu_abc", windowMinutes: 5, text: "x", citations: [], kind: "Extractive", aiState: "Pending" };

describe("parseSummary", () => {
  it("accepts a valid body and keeps source", () => {
    expect(parseSummary(summary)).toMatchObject({ revision: 1, source: "mock" });
  });
  it("drops an unknown source value and unknown fields", () => {
    const p = parseSummary({ ...summary, source: "weird", extra: "<b>x</b>" })!;
    expect(p.source).toBeUndefined();
    expect(p).not.toHaveProperty("extra");
  });
  it.each([null, "x", { ...summary, revision: 0 }, { ...summary, bullets: [1] }, { ...summary, status: "Nope" }, { ...summary, citations: [{}] }])(
    "rejects malformed %#",
    (v) => expect(parseSummary(v)).toBeNull(),
  );
});

describe("parseCatchUp", () => {
  it("accepts the extractive wire", () => {
    expect(parseCatchUp(catchUp)).toMatchObject({ kind: "Extractive", aiState: "Pending", windowMinutes: 5 });
  });
  it("keeps valid AI claims and drops malformed entries", () => {
    const p = parseCatchUp({
      ...catchUp, kind: "Ai", aiState: "Ready", source: "model", confidence: "high",
      nowDiscussing: { text: "n", basis: "lecture", citations: [{ segmentId: "seg_1", startMs: 1 }] },
      shortCatchUp: [{ text: "ok", basis: "uncertain", citations: [] }, { text: "bad", basis: "made_up", citations: [] }],
      keyTerms: [{ term: "t", basis: "general_knowledge", citations: [], definition: "d" }, { nope: 1 }],
      clarifyingQuestions: ["q", 3], gaps: ["g"],
    })!;
    expect(p.shortCatchUp).toHaveLength(1);
    expect(p.keyTerms).toHaveLength(1);
    expect(p.clarifyingQuestions).toEqual(["q"]);
    expect(p.source).toBe("model");
  });
  it.each([null, { ...catchUp, windowMinutes: 3 }, { ...catchUp, kind: "Other" }, { ...catchUp, catchUpId: "bad id!" }, { ...catchUp, citations: "x" }])(
    "rejects malformed %#",
    (v) => expect(parseCatchUp(v)).toBeNull(),
  );
});
