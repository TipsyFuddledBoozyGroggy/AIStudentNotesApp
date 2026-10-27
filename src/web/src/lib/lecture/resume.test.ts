import { describe, expect, it } from "vitest";
import { finalEvent } from "./builders";
import { initialLectureState, lectureReducer, resumeSeq, type LectureState } from "./reducer";
import { MockLectureSource, scriptedLecture } from "./mockSource";
import { BACKOFF_MS, PAGE_LIMIT, ResumeController, backoffDelay, type ResumeDeps, type ResumeSnapshot } from "./resume";
import type { HubEvent, JoinRequest, JoinResponse } from "./types";

const S = "ses_1";
const flush = () => new Promise<void>((r) => setTimeout(r, 0));
const emptyJoin: JoinResponse = { state: "Active", currentSeq: "0", currentSummaryRevision: null, replay: { fromSeq: null, toSeq: null, truncated: false } };

function harness(over: Partial<ResumeDeps> = {}, rnd = 0.5) {
  let state: LectureState = initialLectureState(S);
  const timers: { fn: () => void; ms: number; live: boolean }[] = [];
  const statuses: ResumeSnapshot[] = [];
  const joins: JoinRequest[] = [];
  const deps: ResumeDeps = {
    sessionId: S,
    connect: async () => {},
    join: async (req) => {
      joins.push(req);
      return emptyJoin;
    },
    fetchTranscript: async () => ({ events: [], hasMore: false }),
    resumePoint: () => ({ lastSeq: resumeSeq(state), lastSummaryRevision: state.summary?.revision ?? null }),
    onJoined: (response, lastSeqSent) => void (state = lectureReducer(state, { kind: "joined", response, lastSeqSent })),
    onEvents: (events: HubEvent[]) => void (state = lectureReducer(state, { kind: "events", events })),
    onFastForward: (throughSeq) => void (state = lectureReducer(state, { kind: "fastForward", throughSeq })),
    onStatus: (s) => void statuses.push(s),
    setTimeout: (fn, ms) => {
      const t = { fn, ms, live: true };
      timers.push(t);
      return t;
    },
    clearTimeout: (h) => void ((h as { live: boolean }).live = false),
    random: () => rnd,
    ...over,
  };
  const fire = async () => {
    const t = timers.find((x) => x.live);
    if (!t) throw new Error("no live timer");
    t.live = false;
    t.fn();
    await flush();
  };
  return {
    deps, timers, statuses, joins, fire,
    get state() { return state; },
    set state(s: LectureState) { state = s; },
  };
}

describe("backoff", () => {
  it("follows 0/2/5/10/30 s and caps at 30 s", () => {
    expect([0, 1, 2, 3, 4, 5, 9].map((a) => backoffDelay(a, () => 0.5))).toEqual([0, 2000, 5000, 10000, 30000, 30000, 30000]);
    expect(BACKOFF_MS).toEqual([0, 2000, 5000, 10000, 30000]);
  });
  it("applies +/-20 % jitter and never to the zero step", () => {
    expect(backoffDelay(1, () => 0)).toBe(1600);
    expect(backoffDelay(1, () => 0.999999)).toBe(2400);
    expect(backoffDelay(0, () => 0)).toBe(0);
    expect(backoffDelay(0, () => 0.99)).toBe(0);
  });
});

describe("ResumeController", () => {
  it("connects immediately and joins with no lastSeq on a fresh session", async () => {
    const h = harness();
    const c = new ResumeController(h.deps);
    c.start();
    await flush();
    expect(h.joins).toEqual([{ sessionId: S, protocol: "lecture.v1" }]);
    expect(c.snapshot()).toMatchObject({ status: "connected", offlineLabel: null });
  });

  it("after a disconnect shows the offline label, retries and joins with lastSeq", async () => {
    const h = harness();
    h.state = lectureReducer(h.state, { kind: "event", event: finalEvent(S, "7", 1, "x") });
    const c = new ResumeController(h.deps);
    c.start();
    await flush();
    c.notifyDisconnected();
    expect(c.snapshot()).toMatchObject({ status: "reconnecting", offlineLabel: "not connected, audio not being saved", nextDelayMs: 0 });
    expect(h.timers.at(-1)!.ms).toBe(0);
    await h.fire();
    expect(h.joins.at(-1)).toMatchObject({ lastSeq: "7" });
    expect(c.snapshot().status).toBe("connected");
  });

  it("escalates through the backoff schedule while connect fails, then resets on success", async () => {
    let fail = true;
    const h = harness({ connect: async () => { if (fail) throw new Error("boom SECRET"); } });
    const c = new ResumeController(h.deps);
    c.start();
    await flush();
    const delays: number[] = [h.timers[0].ms];
    for (let i = 0; i < 5; i++) { await h.fire(); delays.push(h.timers.at(-1)!.ms); }
    expect(delays).toEqual([0, 2000, 5000, 10000, 30000, 30000]);
    expect(c.snapshot().lastError).toBe("connection_failed"); // message text is never surfaced
    fail = false;
    await h.fire();
    expect(c.snapshot()).toMatchObject({ status: "connected", attempt: 0 });
    c.notifyDisconnected();
    expect(h.timers.at(-1)!.ms).toBe(0);
  });

  it("does not retry on fatal errors (not_found)", async () => {
    const h = harness({ join: async () => { throw Object.assign(new Error("x"), { code: "not_found" }); } });
    const c = new ResumeController(h.deps);
    c.start();
    await flush();
    expect(c.snapshot()).toMatchObject({ status: "failed", lastError: "not_found", offlineLabel: "not connected, audio not being saved" });
    expect(h.timers).toHaveLength(0);
  });

  it("stop() cancels a pending retry", async () => {
    const h = harness({ connect: async () => { throw new Error("x"); } });
    const c = new ResumeController(h.deps);
    c.start();
    await flush();
    c.stop();
    expect(h.timers.every((t) => !t.live)).toBe(true);
    expect(c.snapshot()).toMatchObject({ status: "stopped", offlineLabel: null });
  });

  it("ignores a late join result after stop()", async () => {
    let release!: (r: JoinResponse) => void;
    const h = harness({ join: () => new Promise<JoinResponse>((r) => void (release = r)) });
    const c = new ResumeController(h.deps);
    c.start();
    await flush();
    c.stop();
    release(emptyJoin);
    await flush();
    expect(c.snapshot().status).toBe("stopped");
    expect(h.state.sessionState).toBeNull();
  });

  it("truncated replay falls back to paged GET /transcript and then fast-forwards", async () => {
    const mock = new MockLectureSource({ sessionId: S, events: [], replayCap: 3 });
    for (let i = 1; i <= 5; i++) mock.log.push(finalEvent(S, String(i), i, `t${i}`));
    const pages: (string | null)[] = [];
    const h = harness({
      join: (r) => mock.join(r),
      fetchTranscript: async (after) => {
        pages.push(after);
        return mock.fetchTranscript(after, 2); // server clamps to a smaller page
      },
    });
    const c = new ResumeController(h.deps);
    c.start();
    await flush();
    expect(pages).toEqual([null, "2", "4"]);
    expect(Object.keys(h.state.segments).sort()).toEqual(["seg_1", "seg_2", "seg_3", "seg_4", "seg_5"]);
    expect(resumeSeq(h.state)).toBe("5");
    expect(h.state.possiblyMissedNonFinalEvents).toBe(true);
    expect(c.snapshot()).toMatchObject({ status: "connected", catchingUpViaRest: false });
    expect(PAGE_LIMIT).toBe(200);
  });

  it("retries rather than loop forever if paging makes no progress", async () => {
    const h = harness({
      join: async () => ({ ...emptyJoin, currentSeq: "9", replay: { fromSeq: null, toSeq: null, truncated: true } }),
      fetchTranscript: async () => ({ events: [], hasMore: true }),
    });
    const c = new ResumeController(h.deps);
    c.start();
    await flush();
    expect(c.snapshot().status).toBe("reconnecting");
  });
});

describe("MockLectureSource fault injection", () => {
  const apply = (src: MockLectureSource) => {
    let st = initialLectureState(S);
    src.subscribe((event) => void (st = lectureReducer(st, { kind: "event", event })));
    return () => st;
  };

  it("duplicates and out-of-order delivery converge to the same transcript as clean delivery", () => {
    const clean = new MockLectureSource({ sessionId: S, events: scriptedLecture(S) });
    const getClean = apply(clean);
    clean.playAll();
    const faulty = new MockLectureSource({
      sessionId: S, events: scriptedLecture(S),
      faults: [{ kind: "duplicate", index: 3 }, { kind: "swapWithNext", index: 4 }, { kind: "duplicate", index: 7 }],
    });
    const getFaulty = apply(faulty);
    faulty.playAll();
    expect(getFaulty().segments).toEqual(getClean().segments);
    expect(getFaulty().gaps).toEqual(getClean().gaps);
    expect(getFaulty().sessionState).toBe("Stopped");
  });

  it("a dropped event leaves a hole that Join(lastSeq) heals", async () => {
    const src = new MockLectureSource({ sessionId: S, events: scriptedLecture(S), faults: [{ kind: "drop", index: 3 }] });
    const get = apply(src);
    src.playAll();
    expect(Object.keys(get().segments)).not.toContain("seg_1");
    await src.join({ sessionId: S, protocol: "lecture.v1", lastSeq: resumeSeq(get())! });
    expect(Object.keys(get().segments).sort()).toEqual(["seg_1", "seg_2", "seg_3"]);
    expect(get().segments.seg_1.correction?.text).toBe("So the derivative of x squared is 2x.");
  });

  it("join of a foreign session fails like a missing one", async () => {
    const src = new MockLectureSource({ sessionId: S, events: [] });
    await expect(src.join({ sessionId: "ses_other", protocol: "lecture.v1" })).rejects.toMatchObject({ code: "not_found" });
  });

  it("events pushed while disconnected are recovered by reconnect + join", async () => {
    const src = new MockLectureSource({ sessionId: S, events: scriptedLecture(S).slice(0, 4) });
    const get = apply(src);
    src.playAll();
    src.dropConnection();
    src.push(finalEvent(S, "5", 9, "missed while offline"));
    expect(get().segments.seg_9).toBeUndefined();
    src.reconnect();
    await src.join({ sessionId: S, protocol: "lecture.v1", lastSeq: resumeSeq(get())! });
    expect(get().segments.seg_9?.originalText).toBe("missed while offline");
  });
});
