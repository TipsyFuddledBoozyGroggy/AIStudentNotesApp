import { describe, expect, it } from "vitest";
import { catchUpEvent, summaryEvent, gapEvent } from "../builders";
import { lectureReducer, initialLectureState } from "../reducer";
import { parseHubEvent } from "./eventGuard";

// N-06: hub AI payloads go through the same strict parsers as REST.
const S = "ses_1";
const cite = { segmentId: "seg_1", startMs: 1 };

describe("parseHubEvent", () => {
  it("passes a valid summary.revision and drops unknown fields", () => {
    const e = summaryEvent(S, 1, "t") as unknown as { payload: Record<string, unknown> };
    e.payload.extra = "<img onerror=x>";
    const out = parseHubEvent(e);
    expect(out?.type).toBe("summary.revision");
    expect(out && "extra" in (out.payload as object)).toBe(false);
  });

  it.each([
    ["citations not an array", { citations: "x" }],
    ["citation missing startMs", { citations: [{ segmentId: "s" }] }],
    ["bad status", { status: "Weird" }],
    ["missing basedOnSeq", { basedOnSeq: undefined }],
  ])("drops summary.revision with malformed nested field: %s", (_n, over) => {
    expect(parseHubEvent(summaryEvent(S, 1, "t", over as never))).toBeNull();
  });

  it("passes a valid catchup.ready and drops malformed nested claims", () => {
    const e = catchUpEvent(S, {
      kind: "Ai",
      shortCatchUp: [{ text: "ok", basis: "lecture", citations: [cite] }, { text: "bad", basis: "nope", citations: [] }],
    } as never);
    const out = parseHubEvent(e);
    expect(out?.type).toBe("catchup.ready");
    if (out?.type === "catchup.ready") expect(out.payload.shortCatchUp).toHaveLength(1);
  });

  it.each([
    ["citations contain junk", { citations: [null] }],
    ["bad windowMinutes", { windowMinutes: 7 }],
    ["bad id", { catchUpId: "a b" }],
  ])("drops catchup.ready with malformed field: %s", (_n, over) => {
    expect(parseHubEvent(catchUpEvent(S, over as never))).toBeNull();
  });

  it("a dropped malformed event leaves reducer state untouched by construction", () => {
    const bad = parseHubEvent(catchUpEvent(S, { citations: [null] } as never));
    expect(bad).toBeNull();
    const s0 = initialLectureState(S);
    const good = parseHubEvent(summaryEvent(S, 1, "ok"))!;
    const s1 = lectureReducer(s0, { kind: "event", event: good });
    expect(s1.summary?.tldr).toBe("ok");
  });

  it("leaves other event types untouched", () => {
    const g = gapEvent(S, "2", 0, 5);
    expect(parseHubEvent(g)).toEqual(g);
  });
});
