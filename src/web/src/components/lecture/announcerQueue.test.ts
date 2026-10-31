import { describe, expect, it } from "vitest";
import * as copy from "./copy";
import { AnnouncementQueue, FinalsBatcher, type Emitted } from "./announcerQueue";

function harness() {
  let t = 0;
  const timers: { at: number; fn: () => void }[] = [];
  const status: Emitted[] = [];
  const alerts: Emitted[] = [];
  const q = new AnnouncementQueue({
    now: () => t,
    setTimeout: (fn, ms) => {
      const h = { at: t + ms, fn };
      timers.push(h);
      return h;
    },
    clearTimeout: (h) => timers.splice(timers.indexOf(h as never), 1),
    onStatus: (e) => status.push(e),
    onAlert: (e) => alerts.push(e),
  });
  const advance = (ms: number) => {
    t += ms;
    for (const h of timers.filter((x) => x.at <= t).sort((a, b) => a.at - b.at)) {
      timers.splice(timers.indexOf(h), 1);
      h.fn();
    }
  };
  return { q, status, alerts, advance };
}

describe("AnnouncementQueue (UX section 0 message queue)", () => {
  it("spaces announcements at least 2 s apart", () => {
    const h = harness();
    h.q.status("a");
    h.q.status("b");
    expect(h.status.map((s) => s.text)).toEqual(["a"]);
    h.advance(1999);
    expect(h.status).toHaveLength(1);
    h.advance(1);
    expect(h.status.map((s) => s.text)).toEqual(["a", "b"]);
  });

  it("alert preempts a queued status", () => {
    const h = harness();
    h.q.status("first");
    h.q.status("queued status");
    h.q.alert("k", "urgent");
    h.advance(2000);
    expect(h.alerts.map((s) => s.text)).toEqual(["urgent"]);
    expect(h.status.map((s) => s.text)).toEqual(["first"]);
    h.advance(2000);
    expect(h.status.map((s) => s.text)).toEqual(["first", "queued status"]);
  });

  it("drops identical text within 10 s", () => {
    const h = harness();
    h.q.status("same");
    h.advance(5000);
    h.q.status("same");
    h.advance(5000);
    expect(h.status).toHaveLength(1);
    h.advance(1000);
    h.q.status("same");
    expect(h.status).toHaveLength(2);
  });

  it("fires an alert once per episode until the episode clears", () => {
    const h = harness();
    h.q.alert("disconnect", "Not connected");
    h.advance(20_000);
    h.q.alert("disconnect", "Not connected");
    h.q.alert("disconnect", "Not connected");
    expect(h.alerts).toHaveLength(1);
    h.q.clearEpisode("disconnect");
    h.q.alert("disconnect", "Not connected");
    expect(h.alerts).toHaveLength(2);
  });
});

describe("FinalsBatcher", () => {
  it("emits at most one batch per 30 s with N new lines and at most 5 lines", () => {
    const b = new FinalsBatcher();
    b.add(["1", "2", "3", "4", "5", "6", "7"]);
    expect(b.take(0)).toBe("7 new lines. 1. 2. 3. 4. 5");
    b.add(["8"]);
    expect(b.take(29_999)).toBeNull();
    expect(b.dueIn(10_000)).toBe(20_000);
    expect(b.take(30_000)).toBe("1 new line. 8");
  });
});

describe("copy lint", () => {
  const all = JSON.stringify(copy, (_k, v) => (typeof v === "function" ? v("X", "Y", "Z") : v)).toLowerCase();
  it("has no attention-judgment words (UX section 9 AC4)", () => {
    for (const w of ["distracted", "lost focus", "inactive", "idle", "away"]) expect(all).not.toContain(w);
  });
  it("never claims the device is still recording or will fill in later (UX section 6 AC6)", () => {
    expect(all).not.toContain("still recording on your device");
    expect(all).not.toContain("we will fill it in");
  });
  it("consent version matches the server draft registry", () => {
    expect(copy.CONSENT_TEXT_VERSION).toBe("draft-2");
  });
});
