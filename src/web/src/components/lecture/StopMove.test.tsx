import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ComponentType } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import * as b from "@/lib/lecture/builders";
import { axeViolations } from "@/test/axe";
import { ANNOUNCE, STOP_DIALOG_COPY, STOP_MOVE_COPY as C, STOPPED_COPY } from "./copy";
import { PENDING_MAX_MS } from "./useLectureSession";
import { StopDialog } from "./Notices";
import {
  ARRIVED_FROM_STOP_KEY,
  loadStopMove,
  markArrivedFromStop,
  saveStopMove,
  stopMoveKey,
  takeArrivedFromStop,
} from "./settings";
import { baseEvents, flush, mountLecture, SID } from "./testUtils";

// The real next/link renders (markup unchanged); the wrapper only records its props, so the Stopped note's link can be
// pinned to replace semantics (Lead decision 1 on the Stop redirect, redesign-round2.md).
const linkCalls = vi.hoisted(() => [] as Array<Record<string, unknown>>);
vi.mock("next/link", async (importOriginal) => {
  const mod = (await importOriginal()) as { default: unknown };
  const d = mod.default as { default?: unknown };
  const Real = (d.default ?? d) as ComponentType<Record<string, unknown>>;
  function RecordingLink(props: Record<string, unknown>) {
    linkCalls.push(props);
    return <Real {...props} />;
  }
  return { __esModule: true, default: RecordingLink };
});

// PO-19 (docs/ux/stop-redirect.md section 6, unit criteria 1 and 3 to 10): the move notice after the student's own Stop,
// the "Don't show this again" preference, the arrival on the lecture's page, the never-open cases. Synthetic data on the
// harness runtime; no network, no microphone.

const USER = "u.1"; // a dot, so the key's segment escaping is exercised everywhere
const HREF = `/lectures/${SID}`;

beforeEach(() => {
  window.localStorage.clear();
  window.sessionStorage.clear();
});
afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
  document.title = "";
});

const liveRoute = () => ({ onOpenPage: vi.fn(), openPageHref: HREF, userId: USER });
const moveDialog = () => screen.queryByRole("dialog", { name: C.heading });
const stoppedHeading = () => document.getElementById("stopped-h2");
const pageLink = () => screen.queryByRole("link", { name: STOPPED_COPY.openPage });

/** Every element that receives focus, in order (to prove the Stopped heading was never focused). */
function focusLog(): string[] {
  const log: string[] = [];
  document.addEventListener("focusin", (e) => log.push((e.target as HTMLElement).id || (e.target as HTMLElement).textContent || ""));
  return log;
}

/** Records whether a move dialog was ever in the DOM (no flash of a dialog on the direct path). */
function dialogWatch() {
  let seen = false;
  const obs = new MutationObserver(() => {
    if (document.querySelector('[data-testid="stop-move-dialog"]')) seen = true;
  });
  obs.observe(document.body, { childList: true, subtree: true });
  return { seen: () => seen, stop: () => obs.disconnect() };
}

async function stopFromBar() {
  await userEvent.click(screen.getByRole("button", { name: /^Stop$/ }));
  await userEvent.click(within(screen.getByRole("dialog", { name: STOP_DIALOG_COPY.heading })).getByRole("button", { name: "Stop" }));
}

const liveRegions = () =>
  Array.from(document.querySelectorAll('[role="status"],[role="alert"],[role="log"],[aria-live]'))
    .filter((e) => (e.getAttribute("aria-live") ?? (e.getAttribute("role") === "log" ? "off" : "polite")) !== "off")
    .map((e) => e.getAttribute("data-region"));

describe("criterion 1: the Stop confirm and the frozen strings are unchanged", () => {
  it("StopDialog renders byte-identical markup and strings (Keep recording focused, one sentence, two buttons)", () => {
    const { container } = render(<StopDialog open paused={false} onConfirm={vi.fn()} onCancel={vi.fn()} />);
    expect(container.innerHTML).toBe(
      '<div class="dialog-scrim fixed inset-0 z-40 flex items-end justify-center p-4 sm:items-center"><div role="dialog" aria-modal="true" aria-labelledby="stop-h2" class="dialog"><h2 id="stop-h2" class="display text-xl">Stop and save this lecture?</h2><p class="mt-2 text-muted">The transcript so far is kept.</p><div class="mt-6 flex flex-wrap justify-end gap-3"><button type="button" class="btn-primary">Keep recording</button><button type="button" class="btn-secondary">Stop</button></div></div></div>',
    );
    expect(screen.getByRole("button", { name: "Keep recording" })).toHaveFocus();
  });

  it("STOP_DIALOG_COPY and STOPPED_COPY keep their values; openPage is the only new key", () => {
    expect(STOP_DIALOG_COPY).toEqual({ heading: "Stop and save this lecture?", confirm: "Stop", cancel: "Keep paused", cancelActive: "Keep recording" });
    expect(STOPPED_COPY).toEqual({
      heading: "Lecture stopped",
      user: "Your transcript is saved.",
      auto_stop_paused_timeout: "Lecture stopped automatically after 30 minutes paused. Your transcript is saved.",
      hard_cap: "This lecture reached the 3 hour limit and was stopped. Your transcript is saved.",
      abandoned: "This lecture ended unexpectedly. Here is what we saved.",
      system: "This lecture was ended by the system. Your transcript is saved.",
      back: "Back to course",
      stoppingOffline: "Stopping... will finish when you are back online.",
      openPage: "Open this lecture's page",
    });
  });
});

describe("criterion 3: the student's own Stop opens the move notice", () => {
  it("opens within the pending window, focus on the primary, the Stopped heading not focused, nothing announced", async () => {
    const route = liveRoute();
    const m = await mountLecture({ route });
    const focused = focusLog();
    const status = m.statusLog.length;
    const alerts = m.alertLog.length;
    await stopFromBar();
    const dlg = await waitFor(() => {
      const d = moveDialog();
      expect(d).not.toBeNull();
      return d!;
    });
    await flush();
    expect(screen.getByTestId("state-label")).toHaveTextContent("Stopped");
    expect(dlg).toHaveAttribute("aria-modal", "true");
    expect(dlg).toHaveAttribute("aria-labelledby", "stop-move-h2");
    expect(dlg).toHaveClass("dialog");
    expect(dlg.parentElement).toHaveClass("dialog-scrim");
    expect(within(dlg).getByRole("heading", { level: 2, name: C.heading })).toHaveClass("display", "text-xl");
    expect(dlg).toHaveTextContent(C.body);
    expect(dlg).toHaveTextContent(C.bodyHere);
    expect(within(dlg).getByRole("checkbox", { name: C.checkbox })).toHaveAccessibleDescription(C.checkboxHint);
    expect(within(dlg).getByRole("checkbox")).not.toBeChecked();
    expect(document.activeElement).toBe(within(dlg).getByRole("button", { name: C.open }));
    expect(focused).not.toContain("stopped-h2");
    expect(m.statusLog.length).toBe(status); // the dialog's name carries the moment
    expect(m.alertLog.length).toBe(alerts);
    expect(route.onOpenPage).not.toHaveBeenCalled(); // nothing moves without a press
    expect(await axeViolations(m.container)).toEqual([]);
  });

  it("no timer: the notice is still open, unchanged, after ten minutes", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const route = liveRoute();
    await mountLecture({ route });
    await stopFromBar();
    await waitFor(() => expect(moveDialog()).not.toBeNull());
    await act(async () => void (await vi.advanceTimersByTimeAsync(10 * 60_000)));
    expect(moveDialog()).not.toBeNull();
    expect(route.onOpenPage).not.toHaveBeenCalled();
  });

  it("Escape closes it and focuses #stopped-h2; the link to the lecture's page is in the Stopped note", async () => {
    const route = liveRoute();
    await mountLecture({ route });
    await stopFromBar();
    await waitFor(() => expect(moveDialog()).not.toBeNull());
    await userEvent.keyboard("{Escape}");
    expect(moveDialog()).toBeNull();
    expect(stoppedHeading()).toHaveFocus();
    expect(pageLink()).toHaveAttribute("href", HREF);
    expect(pageLink()).toHaveClass("btn-tertiary");
    expect(route.onOpenPage).not.toHaveBeenCalled();
  });

  it("Stay here with the box checked saves no key and focuses #stopped-h2", async () => {
    const route = liveRoute();
    await mountLecture({ route });
    await stopFromBar();
    const dlg = await waitFor(() => moveDialog()!);
    await userEvent.click(within(dlg).getByRole("checkbox", { name: C.checkbox }));
    expect(within(dlg).getByRole("checkbox")).toBeChecked();
    await userEvent.click(within(dlg).getByRole("button", { name: C.stay }));
    expect(moveDialog()).toBeNull();
    expect(stoppedHeading()).toHaveFocus();
    expect(window.localStorage.getItem(stopMoveKey(USER))).toBeNull();
    expect(loadStopMove(USER)).toBe(false);
    expect(route.onOpenPage).not.toHaveBeenCalled();
  });

  it("Open the lecture's page calls onOpenPage once and saves nothing when the box is not checked", async () => {
    const route = liveRoute();
    await mountLecture({ route });
    await stopFromBar();
    const dlg = await waitFor(() => moveDialog()!);
    const open = within(dlg).getByRole("button", { name: C.open });
    await userEvent.click(open);
    await userEvent.click(open); // a second press moves nothing twice
    expect(route.onOpenPage).toHaveBeenCalledTimes(1);
    expect(loadStopMove(USER)).toBe(false);
  });

  it("Open with the box checked writes the flag first: loadStopMove(userId) is direct afterwards", async () => {
    const route = liveRoute();
    let flagAtMove: boolean | null = null;
    route.onOpenPage.mockImplementation(() => void (flagAtMove = loadStopMove(USER)));
    await mountLecture({ route });
    await stopFromBar();
    const dlg = await waitFor(() => moveDialog()!);
    await userEvent.click(within(dlg).getByRole("checkbox", { name: C.checkbox }));
    await userEvent.tab(); // checkbox -> the primary, then Enter presses it
    expect(within(dlg).getByRole("button", { name: C.open })).toHaveFocus();
    await userEvent.keyboard("{Enter}");
    expect(route.onOpenPage).toHaveBeenCalledTimes(1);
    expect(flagAtMove).toBe(true);
    expect(loadStopMove(USER)).toBe(true);
    expect(window.localStorage.getItem(`tether.lecture.u.u%2E1.stopMove.v1`)).toBe("direct");
  });

  it("Stop on the Paused note opens the same notice (one modal at a time: the confirm is gone)", async () => {
    const route = liveRoute();
    await mountLecture({ events: baseEvents("Paused", "user"), track: "ended", route });
    await userEvent.click(screen.getByRole("button", { name: "Stop lecture" }));
    await userEvent.click(within(screen.getByRole("dialog")).getByRole("button", { name: "Stop" }));
    await waitFor(() => expect(moveDialog()).not.toBeNull());
    expect(screen.getAllByRole("dialog")).toHaveLength(1);
  });
});

describe("criterion 4: with Don't show this again saved, the page moves at once", () => {
  it("onOpenPage is called once when Stopped arrives, no dialog is rendered at any point, the heading is not focused", async () => {
    saveStopMove(USER, true);
    const route = liveRoute();
    await mountLecture({ route });
    const watch = dialogWatch();
    const focused = focusLog();
    await stopFromBar();
    // No waitFor: the call happens in the effect of the commit that shows Stopped.
    expect(screen.getByTestId("state-label")).toHaveTextContent("Stopped");
    expect(route.onOpenPage).toHaveBeenCalledTimes(1);
    await flush();
    expect(route.onOpenPage).toHaveBeenCalledTimes(1);
    expect(watch.seen()).toBe(false);
    watch.stop();
    expect(moveDialog()).toBeNull();
    expect(focused).not.toContain("stopped-h2");
  });

  it("another user's saved flag does not apply (the key is user-scoped)", async () => {
    saveStopMove("someone-else", true);
    const route = liveRoute();
    await mountLecture({ route });
    await stopFromBar();
    await waitFor(() => expect(moveDialog()).not.toBeNull());
    expect(route.onOpenPage).not.toHaveBeenCalled();
  });

  it("no user id known: the dialog asks, and a checked box cannot be saved (silent)", async () => {
    const route = { ...liveRoute(), userId: null };
    await mountLecture({ route });
    await stopFromBar();
    const dlg = await waitFor(() => moveDialog()!);
    await userEvent.click(within(dlg).getByRole("checkbox"));
    await userEvent.click(within(dlg).getByRole("button", { name: C.open }));
    expect(route.onOpenPage).toHaveBeenCalledTimes(1);
    expect(window.localStorage.length).toBe(0);
  });
});

describe("criterion 5: never opens and never moves for anything but the student's own confirmed Stop", () => {
  const expectNoMove = async (route: ReturnType<typeof liveRoute>, watch: ReturnType<typeof dialogWatch>) => {
    await flush();
    expect(screen.getByTestId("state-label")).toHaveTextContent("Stopped");
    expect(watch.seen()).toBe(false);
    watch.stop();
    expect(route.onOpenPage).not.toHaveBeenCalled();
    expect(pageLink()).toHaveAttribute("href", HREF);
  };

  it.each(["auto_stop_paused_timeout", "hard_cap", "abandoned"])("a system Stopped (%s) with no press", async (reason) => {
    saveStopMove(USER, true); // even with the flag saved: system stops never navigate
    const route = liveRoute();
    const m = await mountLecture({ route });
    const watch = dialogWatch();
    await act(async () => m.controls.push(b.sessionStateEvent(SID, "4", "Stopped", reason)));
    await expectNoMove(route, watch);
  });

  it.each(["hard_cap", "abandoned"])("the student's Stop is pending but the server answers with a system reason (%s)", async (reason) => {
    const route = liveRoute();
    const m = await mountLecture({ route, swallowStateEvents: true });
    const watch = dialogWatch();
    await stopFromBar();
    await act(async () => m.controls.push(b.sessionStateEvent(SID, "4", "Stopped", reason)));
    await expectNoMove(route, watch);
  });

  it("server Failed while the Stop is pending", async () => {
    const route = liveRoute();
    const m = await mountLecture({ route, swallowStateEvents: true });
    const watch = dialogWatch();
    await stopFromBar();
    await act(async () => m.controls.push(b.sessionStateEvent(SID, "4", "Failed")));
    await expectNoMove(route, watch);
    expect(screen.getByText(/ended by the system/)).toBeInTheDocument();
  });

  it("a queued stop (not connected at the press) that completes later", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    saveStopMove(USER, true);
    const route = liveRoute();
    const m = await mountLecture({ route, online: false });
    const watch = dialogWatch();
    await stopFromBar();
    expect(screen.getByText(/will finish when you are back online/)).toBeInTheDocument();
    m.controls.restore();
    await act(async () => void (await vi.advanceTimersByTimeAsync(40_000)));
    await waitFor(() => expect(screen.getByTestId("state-label")).toHaveTextContent("Stopped"));
    await expectNoMove(route, watch);
  });

  it("Stopped arriving after PENDING_MAX_MS", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const route = liveRoute();
    const m = await mountLecture({ route, swallowStateEvents: true });
    const watch = dialogWatch();
    await stopFromBar();
    await act(async () => void (await vi.advanceTimersByTimeAsync(PENDING_MAX_MS + 200)));
    await act(async () => m.controls.push(b.sessionStateEvent(SID, "4", "Stopped", "user")));
    await expectNoMove(route, watch);
  });

  it("a screen mounted already Stopped (a reopen or reload of a stopped lecture)", async () => {
    saveStopMove(USER, true);
    const route = liveRoute();
    const watch = dialogWatch();
    await mountLecture({ events: baseEvents("Stopped", "user"), track: "ended", route });
    await expectNoMove(route, watch);
  });

  it("the Stop REST call fails: nothing opens, the error note shows what is true", async () => {
    const route = liveRoute();
    await mountLecture({ route, failActions: { stop: true } });
    const watch = dialogWatch();
    await stopFromBar();
    await waitFor(() => expect(screen.getByTestId("action-error")).toBeInTheDocument());
    expect(watch.seen()).toBe(false);
    watch.stop();
    expect(route.onOpenPage).not.toHaveBeenCalled();
  });

  it("the reopen page (no onOpenPage, no openPageHref): Stop focuses the heading as before, no dialog, no link", async () => {
    await mountLecture({ route: { userId: USER } });
    await stopFromBar();
    await waitFor(() => expect(stoppedHeading()).toHaveFocus());
    expect(moveDialog()).toBeNull();
    expect(pageLink()).toBeNull();
  });

  it.each([
    ["Stopped user", baseEvents("Stopped", "user")],
    ["Stopped hard_cap", baseEvents("Stopped", "hard_cap")],
    ["Failed", [...baseEvents(), b.sessionStateEvent(SID, "4", "Failed")]],
  ] as const)("the Stopped note shows the link only when openPageHref is given (%s)", async (_n, events) => {
    const a = await mountLecture({ events: [...events], track: "ended", route: { openPageHref: HREF } });
    expect(pageLink()).toHaveAttribute("href", HREF);
    expect(await axeViolations(a.container)).toEqual([]);
  });

  it("the Stopped note's link replaces the history entry (Back then goes to the course page, not the start flow)", async () => {
    linkCalls.length = 0;
    await mountLecture({ events: baseEvents("Stopped", "user"), track: "ended", route: { openPageHref: HREF } });
    const mine = linkCalls.filter((p) => p.href === HREF);
    expect(mine.length).toBeGreaterThan(0);
    for (const p of mine) expect(p.replace).toBe(true);
    // Markup unchanged by the prop: the same anchor, class and text as before.
    expect(pageLink()!.outerHTML).toBe(`<a class="btn-tertiary" href="${HREF}">${STOPPED_COPY.openPage}</a>`);
  });

  it("without openPageHref the Stopped note markup is exactly as before (one Back to course button with mt-3)", async () => {
    await mountLecture({ events: baseEvents("Stopped", "user"), track: "ended" });
    expect(pageLink()).toBeNull();
    const back = screen.getByRole("button", { name: "Back to course" });
    expect(back.className).toBe("btn-secondary mt-3");
    expect(back.parentElement!.querySelectorAll("a")).toHaveLength(0);
  });
});

describe("criterion 6: arriving on the lecture's page through the move", () => {
  it("arrivedFromStop: focus on #stopped-h2 once Stopped, one status announcement", async () => {
    const m = await mountLecture({ events: baseEvents("Stopped", "user"), track: "ended", route: { arrivedFromStop: true } });
    await waitFor(() => expect(stoppedHeading()).toHaveFocus());
    await waitFor(() => expect(m.statusLog).toContain(ANNOUNCE.movedAfterStop));
    await flush();
    expect(m.statusLog.filter((t) => t === ANNOUNCE.movedAfterStop)).toHaveLength(1);
    expect(m.alertLog).toEqual([]);
  });

  it("arrivedFromStop while the hub has not joined yet: nothing until Stopped arrives, then the focus and one announcement", async () => {
    // As on the real reopen page: the screen starts empty (here: not connected) and the server state arrives later.
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const m = await mountLecture({ events: baseEvents("Stopped", "user"), track: "ended", online: false, route: { arrivedFromStop: true } });
    expect(m.statusLog).not.toContain(ANNOUNCE.movedAfterStop);
    m.controls.restore();
    await act(async () => void (await vi.advanceTimersByTimeAsync(40_000)));
    await waitFor(() => expect(stoppedHeading()).toHaveFocus());
    expect(m.statusLog.filter((t) => t === ANNOUNCE.movedAfterStop)).toHaveLength(1);
  });

  it("without the prop: no announcement and no focus move", async () => {
    const m = await mountLecture({ events: baseEvents("Stopped", "user"), track: "ended" });
    await flush();
    expect(stoppedHeading()).not.toHaveFocus();
    expect(m.statusLog).not.toContain(ANNOUNCE.movedAfterStop);
  });

  it("the marker is one-shot: removed on the first read, true only for its own session", () => {
    markArrivedFromStop(SID);
    expect(window.sessionStorage.getItem(ARRIVED_FROM_STOP_KEY)).toBe(SID);
    expect(takeArrivedFromStop(SID)).toBe(true);
    expect(window.sessionStorage.getItem(ARRIVED_FROM_STOP_KEY)).toBeNull();
    expect(takeArrivedFromStop(SID)).toBe(false); // a reload
    markArrivedFromStop("ses_other");
    expect(takeArrivedFromStop(SID)).toBe(false);
    expect(window.sessionStorage.getItem(ARRIVED_FROM_STOP_KEY)).toBeNull(); // removed even when it names another session
  });

  it("blocked sessionStorage never throws and reads as no arrival", () => {
    vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => {
      throw new Error("blocked");
    });
    vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
      throw new Error("blocked");
    });
    expect(() => markArrivedFromStop(SID)).not.toThrow();
    expect(takeArrivedFromStop(SID)).toBe(false);
  });
});

describe("criterion 7: keyboard inside the notice", () => {
  it("Tab cycles checkbox -> primary -> secondary -> checkbox, both directions; the backdrop does nothing", async () => {
    const route = liveRoute();
    await mountLecture({ route });
    await stopFromBar();
    const dlg = await waitFor(() => moveDialog()!);
    const box = within(dlg).getByRole("checkbox");
    const open = within(dlg).getByRole("button", { name: C.open });
    const stay = within(dlg).getByRole("button", { name: C.stay });
    expect(open).toHaveFocus();
    // DOM order is heading, checkbox, primary, secondary (spec 3: keyboard order).
    expect(Array.from(dlg.querySelectorAll("input, button"))).toEqual([box, open, stay]);
    await userEvent.tab();
    expect(stay).toHaveFocus();
    await userEvent.tab();
    expect(box).toHaveFocus(); // wraps to the checkbox, not out of the dialog
    await userEvent.tab();
    expect(open).toHaveFocus();
    await userEvent.tab({ shift: true });
    expect(box).toHaveFocus();
    await userEvent.tab({ shift: true });
    expect(stay).toHaveFocus(); // wraps backwards
    await userEvent.tab({ shift: true });
    expect(open).toHaveFocus();
    await userEvent.click(dlg.parentElement!); // the scrim
    expect(moveDialog()).not.toBeNull();
    expect(route.onOpenPage).not.toHaveBeenCalled();
    expect(window.localStorage.length).toBe(0);
  });

  it("Space on the checkbox toggles it without closing anything", async () => {
    await mountLecture({ route: liveRoute() });
    await stopFromBar();
    const dlg = await waitFor(() => moveDialog()!);
    within(dlg).getByRole("checkbox").focus();
    await userEvent.keyboard(" ");
    expect(within(dlg).getByRole("checkbox")).toBeChecked();
    expect(moveDialog()).not.toBeNull();
  });
});

describe("criterion 9: the preference helpers", () => {
  it("key shape tether.lecture.u.<seg>.stopMove.v1 with dots escaped; save true writes direct, false removes", () => {
    expect(stopMoveKey("u1")).toBe("tether.lecture.u.u1.stopMove.v1");
    expect(stopMoveKey("a.b")).toBe("tether.lecture.u.a%2Eb.stopMove.v1");
    expect(stopMoveKey("x/y z")).toBe("tether.lecture.u.x%2Fy%20z.stopMove.v1");
    expect(loadStopMove("u1")).toBe(false);
    saveStopMove("u1", true);
    expect(window.localStorage.getItem("tether.lecture.u.u1.stopMove.v1")).toBe("direct");
    expect(loadStopMove("u1")).toBe(true);
    expect(loadStopMove("u2")).toBe(false);
    saveStopMove("u1", false);
    expect(window.localStorage.getItem("tether.lecture.u.u1.stopMove.v1")).toBeNull();
    expect(loadStopMove("u1")).toBe(false);
  });

  it("one id cannot address another's key (the segment has no dot)", () => {
    // "a" + ".x" must not reach a key of user "a": the escaped segment of "a.x" is "a%2Ex".
    saveStopMove("a.x", true);
    expect(loadStopMove("a")).toBe(false);
    expect(Object.keys(window.localStorage)).toEqual(["tether.lecture.u.a%2Ex.stopMove.v1"]);
  });

  it("any value other than direct reads as ask; no user id reads as ask and writes nothing", () => {
    window.localStorage.setItem(stopMoveKey("u1"), "true");
    expect(loadStopMove("u1")).toBe(false);
    window.localStorage.clear();
    saveStopMove(null, true);
    saveStopMove(undefined, true);
    saveStopMove("", true);
    expect(window.localStorage.length).toBe(0);
    expect(loadStopMove(null)).toBe(false);
  });

  it("throwing storage never throws out of the helpers", () => {
    for (const m of ["getItem", "setItem", "removeItem"] as const)
      vi.spyOn(Storage.prototype, m).mockImplementation(() => {
        throw new Error("blocked");
      });
    expect(() => saveStopMove("u1", true)).not.toThrow();
    expect(() => saveStopMove("u1", false)).not.toThrow();
    expect(loadStopMove("u1")).toBe(false);
  });

  it("a throwing localStorage on the live route: the notice still asks and Open still moves", async () => {
    const route = liveRoute();
    await mountLecture({ route });
    vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => {
      throw new Error("blocked");
    });
    vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
      throw new Error("blocked");
    });
    await stopFromBar();
    const dlg = await waitFor(() => moveDialog()!);
    await userEvent.click(within(dlg).getByRole("checkbox"));
    await userEvent.click(within(dlg).getByRole("button", { name: C.open }));
    expect(route.onOpenPage).toHaveBeenCalledTimes(1);
  });
});

describe("criterion 10: three live regions", () => {
  it("exactly three with the notice open and after Stay here; the notice and the link add none", async () => {
    // announceLines on, as the existing count test does: the finals region is live only when the student opts in.
    await mountLecture({ route: liveRoute(), settings: { announceLines: true } });
    await stopFromBar();
    const dlg = await waitFor(() => moveDialog()!);
    expect(liveRegions()).toEqual(["status", "alert", "finals"]);
    expect(dlg.querySelectorAll("[aria-live],[role=status],[role=alert]")).toHaveLength(0);
    await userEvent.click(within(dlg).getByRole("button", { name: C.stay }));
    expect(pageLink()).not.toBeNull();
    expect(liveRegions()).toEqual(["status", "alert", "finals"]);
  });
});
