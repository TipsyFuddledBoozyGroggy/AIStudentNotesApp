import { describe, expect, it } from "vitest";
import {
  catchUpEvent, correctionEvent, finalEvent, gapEvent, partialEvent, sessionStateEvent,
  sttStatusEvent, summaryEvent, summaryStatusEvent,
} from "./builders";
import {
  hasSeqHoles, initialLectureState, lectureReducer, resumeSeq, selectAnnounceable, selectPartials, selectSegments,
  type LectureAction, type LectureState,
} from "./reducer";
import type { HubEvent } from "./types";

const S = "ses_1";
const run = (events: HubEvent[], from = initialLectureState()): LectureState =>
  events.reduce((st, event) => lectureReducer(st, { kind: "event", event }), from);

describe("dedupe on (sessionId, seq) with BigInt seq", () => {
  it("applies a persisted event once", () => {
    const f = finalEvent(S, "4", 1, "hello");
    const st = run([f, f, f]);
    expect(Object.keys(st.segments)).toEqual(["seg_1"]);
    expect(st.maxSeq).toBe("4");
  });

  it("compares seq numerically above 2^53 and across digit counts", () => {
    const big = "9007199254740993"; // not representable as a double
    const big2 = "9007199254740992";
    const st = run([finalEvent(S, big, 1, "a"), finalEvent(S, big2, 2, "b")]);
    expect(st.maxSeq).toBe(big);
    expect(st.contiguousSeq).toBe(big);
    expect(hasSeqHoles(st)).toBe(false);
    const st2 = run([finalEvent(S, "9", 1, "a"), finalEvent(S, "10", 2, "b")]);
    expect(st2.maxSeq).toBe("10"); // string compare would say "9" > "10"
    expect(st2.contiguousSeq).toBe("10");
  });

  it("ignores events for another session", () => {
    const st = run([finalEvent(S, "1", 1, "a"), finalEvent("ses_other", "2", 2, "x")]);
    expect(Object.keys(st.segments)).toEqual(["seg_1"]);
  });

  it("same seq in a different session does not collide", () => {
    const a = run([finalEvent("ses_a", "1", 1, "a")]);
    const b = run([finalEvent("ses_b", "1", 1, "b")]);
    expect(a.segments.seg_1.originalText).toBe("a");
    expect(b.segments.seg_1.originalText).toBe("b");
  });

  it("detects holes from out-of-order delivery and closes them", () => {
    let st = run([finalEvent(S, "1", 1, "a"), finalEvent(S, "3", 3, "c")]);
    expect(hasSeqHoles(st)).toBe(true);
    expect(resumeSeq(st)).toBe("1");
    st = run([finalEvent(S, "2", 2, "b")], st);
    expect(hasSeqHoles(st)).toBe(false);
    expect(resumeSeq(st)).toBe("3");
    expect(selectSegments(st).map((s) => s.text)).toEqual(["a", "b", "c"]);
  });

  it("uses the join baseline so a first event after a hole is detected", () => {
    let st = initialLectureState(S);
    st = lectureReducer(st, {
      kind: "joined", lastSeqSent: "10",
      response: { state: "Active", currentSeq: "14", currentSummaryRevision: null, replay: { fromSeq: "11", toSeq: "14", truncated: false } },
    });
    st = run([finalEvent(S, "12", 1, "x")], st);
    expect(hasSeqHoles(st)).toBe(true);
    expect(resumeSeq(st)).toBe("10");
    expect(st.sessionState).toBe("Active");
  });

  it("fastForward advances the frontier and flags possibly missed non-final events", () => {
    let st = run([finalEvent(S, "1", 1, "a")]);
    st = lectureReducer(st, { kind: "fastForward", throughSeq: "50" });
    expect(resumeSeq(st)).toBe("50");
    expect(st.possiblyMissedNonFinalEvents).toBe(true);
    expect(hasSeqHoles(st)).toBe(false);
  });
});

describe("partials and finals", () => {
  it("replaces a partial by segmentId only with a higher revision", () => {
    const st = run([
      sessionStateEvent(S, "1", "Active"),
      partialEvent(S, 1, 1, "so the"),
      partialEvent(S, 1, 3, "so the derivative"),
      partialEvent(S, 1, 2, "stale"),
    ]);
    expect(selectPartials(st)).toHaveLength(1);
    expect(selectPartials(st)[0]).toMatchObject({ revision: 3, text: "so the derivative" });
  });

  it("never marks partials announceable and never returns them as announceable", () => {
    const st = run([partialEvent(S, 1, 1, "typing...")]);
    expect(selectPartials(st).every((p) => p.announceable === false)).toBe(true);
    expect(selectAnnounceable(st)).toEqual([]);
  });

  it("a final removes its partial and a late partial for a final segment is ignored", () => {
    let st = run([partialEvent(S, 1, 1, "so"), finalEvent(S, "1", 1, "So the end.")]);
    expect(selectPartials(st)).toEqual([]);
    st = run([partialEvent(S, 1, 9, "late")], st);
    expect(selectPartials(st)).toEqual([]);
    expect(selectAnnounceable(st)).toHaveLength(1);
  });

  it("clears partials on pause and ignores partials while not Active", () => {
    let st = run([partialEvent(S, 2, 1, "abc"), sessionStateEvent(S, "1", "Paused")]);
    expect(selectPartials(st)).toEqual([]);
    st = run([partialEvent(S, 2, 2, "abcd")], st);
    expect(selectPartials(st)).toEqual([]);
    st = run([sessionStateEvent(S, "2", "Active"), partialEvent(S, 2, 3, "abcde")], st);
    expect(selectPartials(st)).toHaveLength(1);
  });
});

describe("corrections", () => {
  it("applies the correction but keeps the original and its provenance", () => {
    const st = run([finalEvent(S, "1", 1, "derivative is two x"), correctionEvent(S, "2", 1, 2, "derivative is 2x")]);
    const seg = selectSegments(st)[0];
    expect(seg.text).toBe("derivative is 2x");
    expect(seg.corrected).toBe(true);
    expect(seg.originalText).toBe("derivative is two x");
    expect(seg.provider).toBe("replay");
    expect(seg.corrections).toHaveLength(1);
  });

  it("is order independent: correction before final, and highest revision wins", () => {
    const st = run([correctionEvent(S, "3", 1, 3, "v3"), correctionEvent(S, "2", 1, 2, "v2"), finalEvent(S, "1", 1, "v1")]);
    const seg = selectSegments(st)[0];
    expect(seg.text).toBe("v3");
    expect(seg.originalText).toBe("v1");
    expect(seg.corrections.map((c) => c.revision)).toEqual([2, 3]);
    expect(hasSeqHoles(st)).toBe(false);
  });

  it("duplicate correction is not applied twice", () => {
    const c = correctionEvent(S, "2", 1, 2, "fixed");
    const st = run([finalEvent(S, "1", 1, "x"), c, c]);
    expect(selectSegments(st)[0].corrections).toHaveLength(1);
  });

  it("a second final for the same segment does not overwrite the first", () => {
    const st = run([finalEvent(S, "1", 1, "first"), finalEvent(S, "2", 1, "second")]);
    expect(selectSegments(st)[0].originalText).toBe("first");
  });
});

describe("summary, status, gaps, catch-up, errors", () => {
  it("ignores summary revision <= current, accepts higher", () => {
    let st = run([summaryEvent(S, 2, "two")]);
    st = run([summaryEvent(S, 2, "dup"), summaryEvent(S, 1, "old")], st);
    expect(st.summary?.tldr).toBe("two");
    st = run([summaryEvent(S, 3, "three")], st);
    expect(st.summary?.revision).toBe(3);
  });

  it("tracks session.state by seq (older state cannot override newer), stt and summary status", () => {
    const st = run([
      sessionStateEvent(S, "5", "Stopped"),
      sessionStateEvent(S, "4", "Paused", "user"),
      sttStatusEvent(S, "Degraded"),
      summaryStatusEvent(S, "Delayed", 12),
    ]);
    expect(st.sessionState).toBe("Stopped");
    expect(st.sttStatus).toBe("Degraded");
    expect(st.summaryStatus).toEqual({ state: "Delayed", lagSeconds: 12 });
  });

  it("records gap events once, without fabricating text", () => {
    const g = gapEvent(S, "2", 1000, 4000, "network");
    const st = run([finalEvent(S, "1", 1, "a"), g, g]);
    expect(st.gaps).toEqual([{ seq: "2", fromMs: 1000, toMs: 4000, reason: "network" }]);
    expect(selectSegments(st)).toHaveLength(1);
  });

  it("keeps an Ai catch-up over a later Extractive one with the same id", () => {
    let st = run([catchUpEvent(S, { kind: "Ai", text: "ai" })]);
    st = run([catchUpEvent(S, { kind: "Extractive", text: "ext" })], st);
    expect(st.catchUps.cu_1.text).toBe("ai");
    st = run([catchUpEvent(S, { catchUpId: "cu_2", kind: "Extractive", text: "e2" }), catchUpEvent(S, { catchUpId: "cu_2", kind: "Ai", text: "a2" })], st);
    expect(st.catchUps.cu_2.text).toBe("a2");
  });

  it("does not mutate previous state", () => {
    const a = run([finalEvent(S, "1", 1, "a")]);
    const snapshot = JSON.stringify(a);
    run([finalEvent(S, "2", 2, "b")], a);
    expect(JSON.stringify(a)).toBe(snapshot);
  });

  it("reset and batch actions work", () => {
    const actions: LectureAction[] = [{ kind: "events", events: [finalEvent(S, "1", 1, "a")] }];
    let st = initialLectureState();
    for (const a of actions) st = lectureReducer(st, a);
    expect(Object.keys(st.segments)).toHaveLength(1);
    expect(lectureReducer(st, { kind: "reset", sessionId: null }).segments).toEqual({});
  });
});
