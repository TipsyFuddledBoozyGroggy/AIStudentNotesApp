import { act, screen, within, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import * as b from "@/lib/lecture/builders";
import { axeViolations } from "@/test/axe";
import { baseEvents, flush, mountLecture, SID } from "./testUtils";

afterEach(() => {
  vi.useRealTimers();
  document.title = "";
});

const stateLabel = () => screen.getByTestId("state-label").textContent;

describe("Live view (UX section 2)", () => {
  it("renders status bar with Recording label, Demo source tag (non-dismissible), Help: Quiet, and axe-clean", async () => {
    const m = await mountLecture();
    expect(stateLabel()).toBe("Recording");
    expect(screen.getByTestId("demo-tag")).toHaveTextContent("Demo source: replayed test audio");
    expect(within(screen.getByTestId("demo-tag")).queryByRole("button")).toBeNull();
    expect(screen.getByText("Help: Quiet")).toBeInTheDocument();
    expect(screen.getByTestId("connection")).toHaveTextContent("Connected");
    expect(document.title).toBe("Recording |");
    expect(await axeViolations(m.container)).toEqual([]);
  });

  it("AC1: partials/finals produce zero status/alert mutations and no finals-region text when announce is off", async () => {
    const m = await mountLecture({ script: [b.partialEvent(SID, 3, 1, "so the deriv"), b.finalEvent(SID, "4", 3, "So the derivative.")] });
    m.statusLog.length = 0;
    await act(async () => void m.controls.step());
    expect(screen.getByText("so the deriv")).toBeInTheDocument();
    await act(async () => void m.controls.step());
    expect(screen.getByText("So the derivative.")).toBeInTheDocument();
    expect(m.statusLog).toEqual([]);
    expect(m.alertLog).toEqual([]);
    const log = screen.getByRole("log", { name: "Transcript lines" });
    expect(log).toHaveAttribute("aria-live", "off");
    expect(log.querySelector('[data-region="finals"]')).toHaveAttribute("aria-live", "off");
    expect(log.querySelector('[data-region="finals"]')).toBeEmptyDOMElement();
  });

  it("exactly three live regions in total (status, alert, finals) and none elsewhere", async () => {
    const m = await mountLecture({ settings: { announceLines: true } });
    const live = Array.from(m.container.ownerDocument.querySelectorAll('[role="status"],[role="alert"],[role="log"],[aria-live]')).filter(
      (e) => (e.getAttribute("aria-live") ?? (e.getAttribute("role") === "log" ? "off" : "polite")) !== "off",
    );
    expect(live.map((e) => e.getAttribute("data-region"))).toEqual(["status", "alert", "finals"]);
  });

  it("partial is replaced in place by its final: one partial row, same segment, no duplicate", async () => {
    const m = await mountLecture({ script: [b.partialEvent(SID, 3, 1, "work in progress"), b.finalEvent(SID, "4", 3, "Finished line.")] });
    await act(async () => void m.controls.step());
    const row = m.container.querySelector("[data-partial-row]")!;
    expect(row).toHaveTextContent("work in progress");
    await act(async () => void m.controls.step());
    expect(row).toBeEmptyDOMElement();
    expect(m.container.querySelectorAll('[data-segment-id="seg_3"]')).toHaveLength(1);
  });

  it("AC4: red-team TL;DR, bullets and transcript text render as inert literal text", async () => {
    const evil = '<img src=x onerror="alert(1)"> **bold** [click](https://evil.example) <script>alert(1)</script>';
    const m = await mountLecture({
      script: [
        b.finalEvent(SID, "4", 3, evil),
        b.summaryEvent(SID, 1, evil, { bullets: [evil], citations: [{ segmentId: "seg_1", startMs: 1000 }] }),
      ],
    });
    await act(async () => void m.controls.step());
    await act(async () => void m.controls.step());
    const tldr = m.container.ownerDocument.getElementById("lecture-tldr")!;
    expect(tldr.textContent).toContain(evil);
    expect(m.container.querySelector("img, script, strong a, a[href^='https://evil']")).toBeNull();
    expect(tldr.querySelector("a, img, script")).toBeNull();
    expect(screen.getAllByText(evil, { exact: false }).length).toBeGreaterThan(1);
  });

  it("AC5: a TL;DR revision never changes document.activeElement", async () => {
    const m = await mountLecture({ script: [b.summaryEvent(SID, 1, "Summary one"), b.summaryEvent(SID, 2, "Summary two")] });
    const pause = screen.getByRole("button", { name: "Pause" });
    pause.focus();
    await act(async () => void m.controls.step());
    expect(document.activeElement).toBe(pause);
    await act(async () => void m.controls.step());
    expect(screen.getByText("Summary two")).toBeInTheDocument();
    expect(document.activeElement).toBe(pause);
    expect(screen.getByText("Updated at 00:01 (revision 2)")).toBeInTheDocument();
  });

  it("TL;DR ignores older revisions; Hold updates buffers a newer one until Show latest", async () => {
    const m = await mountLecture({ script: [b.summaryEvent(SID, 2, "Rev two"), b.summaryEvent(SID, 1, "Old rev"), b.summaryEvent(SID, 3, "Rev three")] });
    await act(async () => void m.controls.step());
    await act(async () => void m.controls.step());
    expect(screen.queryByText("Old rev")).toBeNull();
    const hold = screen.getByRole("button", { name: "Pause updates" });
    await userEvent.click(hold);
    expect(hold).toHaveTextContent("Resume updates");
    expect(hold).not.toHaveAttribute("aria-pressed");
    expect(screen.getByTestId("tldr-paused")).toHaveTextContent("Updates are paused. You are reading the summary from");
    await act(async () => void m.controls.step());
    expect(screen.getByText("Rev two")).toBeInTheDocument();
    expect(screen.getByText("Newer summary available")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Show latest" }));
    expect(screen.getByText("Rev three")).toBeInTheDocument();
  });

  it("citation buttons focus the cited transcript line", async () => {
    const m = await mountLecture({ script: [b.summaryEvent(SID, 1, "S", { citations: [{ segmentId: "seg_2", startMs: 2000 }] })] });
    Element.prototype.scrollIntoView = vi.fn();
    await act(async () => void m.controls.step());
    await userEvent.click(screen.getByRole("button", { name: "From 00:02" }));
    expect(m.container.querySelector('[data-segment-id="seg_2"]')).toHaveFocus();
  });

  it("transcript lines expose a spoken timestamp; corrections show an edited tag; gaps show a marker", async () => {
    const m = await mountLecture({
      script: [b.correctionEvent(SID, "4", 1, 2, "First line, corrected."), b.gapEvent(SID, "5", 10_000, 14_000, "network")],
    });
    await act(async () => void m.controls.step());
    await act(async () => void m.controls.step());
    expect(screen.getByText("First line, corrected.")).toBeInTheDocument();
    expect(screen.getByText("(edited)")).toBeInTheDocument();
    expect(screen.getByText("1 second")).toHaveClass("sr-only");
    expect(screen.getByText("Audio not available 00:10 to 00:14 (connection)")).toBeInTheDocument();
  });

  it("Alt+ shortcuts are OFF by default (contract 12.5); on only after the setting is enabled", async () => {
    const m = await mountLecture();
    await userEvent.keyboard("{Alt>}p{/Alt}");
    await flush();
    expect(m.track!.stopCalls).toBe(0);
    expect(stateLabel()).toBe("Recording");
  });

  it("Alt+P pauses when the shortcut setting is enabled", async () => {
    const m = await mountLecture({ settings: { shortcuts: true } });
    await userEvent.keyboard("{Alt>}p{/Alt}");
    await waitFor(() => expect(stateLabel()).toBe("Paused"));
    expect(m.track!.readyState).toBe("ended");
  });

  it("Show more reveals bullets 4 to 7 only", async () => {
    const bullets = Array.from({ length: 9 }, (_, i) => `Bullet ${i + 1}`);
    const m = await mountLecture({ script: [b.summaryEvent(SID, 1, "S", { bullets })] });
    await act(async () => void m.controls.step());
    expect(screen.queryByText("Bullet 4")).toBeNull();
    await userEvent.click(screen.getByRole("button", { name: "Show more" }));
    expect(screen.getByText("Bullet 7")).toBeInTheDocument();
    expect(screen.queryByText("Bullet 8")).toBeNull();
  });

  it("opt-in finals region announces a batched summary once, not partials", async () => {
    const m = await mountLecture({ settings: { announceLines: true }, script: [b.partialEvent(SID, 3, 1, "partial text"), b.finalEvent(SID, "4", 3, "Final text.")] });
    await act(async () => void m.controls.step());
    const finals = m.container.querySelector('[data-region="finals"]')!;
    expect(finals).toHaveTextContent("");
    await act(async () => void m.controls.step());
    expect(finals).toHaveTextContent("1 new line. Final text.");
    expect(finals).not.toHaveTextContent("partial text");
  });
});

describe("Paused state (UX section 3)", () => {
  it("AC1: Paused panel visible with Resume and Stop, not dismissible, no focus move on load", async () => {
    const m = await mountLecture({ events: baseEvents("Paused", "user"), track: "ended" });
    const panel = screen.getByRole("region", { name: "Recording is paused" });
    expect(within(panel).getByRole("button", { name: "Resume recording" })).toBeInTheDocument();
    expect(within(panel).getByRole("button", { name: "Stop lecture" })).toBeInTheDocument();
    expect(within(panel).queryByRole("button", { name: /close|dismiss/i })).toBeNull();
    expect(panel).not.toHaveAttribute("role", "dialog");
    expect(stateLabel()).toBe("Paused");
    expect(document.title).toBe("Paused |");
    expect(await axeViolations(m.container)).toEqual([]);
  });

  it("AC2: panel renders only after the local track is ended; a live track under a Paused server shows the mismatch warning instead", async () => {
    const m = await mountLecture({ events: baseEvents("Paused", "user"), track: "live" });
    await waitFor(() => expect(m.track!.readyState).toBe("ended"));
    expect(screen.queryByRole("region", { name: "Recording is paused" })).toBeNull();
    expect(screen.getByRole("region", { name: "Something doesn't match" })).toBeInTheDocument();
  });

  it("AC2 (press path): the track is stopped before the Paused panel appears", async () => {
    const m = await mountLecture();
    let endedWhenPanelAppeared: string | null = null;
    new MutationObserver(() => {
      if (document.querySelector('[data-panel="paused"]') && endedWhenPanelAppeared === null) endedWhenPanelAppeared = m.track!.readyState;
    }).observe(document.body, { childList: true, subtree: true });
    await userEvent.click(screen.getByRole("button", { name: "Pause" }));
    await waitFor(() => expect(screen.getByRole("region", { name: "Recording is paused" })).toBeInTheDocument());
    expect(endedWhenPanelAppeared).toBe("ended");
  });

  it("AC3 + AC4: after Pause focus is the panel Resume button; after Resume it is the status-bar Pause button; one announcement each", async () => {
    const m = await mountLecture();
    await userEvent.click(screen.getByRole("button", { name: "Pause" }));
    await waitFor(() => expect(screen.getByRole("button", { name: "Resume recording" })).toHaveFocus());
    expect(m.statusLog.filter((t) => t === "Recording is paused. Microphone is off.")).toHaveLength(1);
    expect(m.mic.requestCount).toBe(0);

    await userEvent.keyboard("{Enter}"); // keyboard activation of the focused Resume
    await waitFor(() => expect(screen.getByRole("button", { name: "Pause" })).toHaveFocus());
    expect(screen.queryByRole("region", { name: "Recording is paused" })).toBeNull();
    expect(m.mic.requestCount).toBe(1); // reacquired only by that press
    // 2 s minimum spacing between announcements (UX section 0), so it arrives after the pause message.
    await waitFor(() => expect(m.statusLog.filter((t) => t === "Recording resumed.")).toHaveLength(1), { timeout: 4000 });
    expect(m.statusLog.filter((t) => t === "Recording is paused. Microphone is off.")).toHaveLength(1);
    expect(stateLabel()).toBe("Recording");
  });

  it("system pause (auto_pause_no_audio): auto copy, one alert, focus not moved", async () => {
    const m = await mountLecture({ script: [b.sessionStateEvent(SID, "4", "Paused", "auto_pause_no_audio")] });
    const pause = screen.getByRole("button", { name: "Pause" });
    pause.focus();
    await act(async () => void m.controls.step());
    await waitFor(() => expect(screen.getByRole("region", { name: "Recording was paused automatically" })).toBeInTheDocument());
    expect(m.track!.readyState).toBe("ended"); // the client turned its mic off; not a mismatch
    expect(screen.queryByRole("region", { name: /Something doesn't match/ })).toBeNull();
    expect(screen.getByText(/No audio was received for 5 minutes/)).toBeInTheDocument();
    expect(document.activeElement).not.toBe(screen.getByRole("button", { name: "Resume recording" }));
    expect(m.alertLog.filter((t) => t.startsWith("Recording was paused automatically"))).toHaveLength(1);
    expect(m.statusLog).toEqual([]);
  });

  it("AC6: reconnect does not auto-resume; Resume needs a press", async () => {
    const m = await mountLecture({ events: baseEvents("Paused", "user"), track: "ended" });
    m.controls.drop();
    await flush();
    m.controls.restore();
    await flush();
    expect(m.mic.requestCount).toBe(0);
    expect(screen.getByRole("region", { name: "Recording is paused" })).toBeInTheDocument();
  });

  it("AC7: Stop confirm traps focus, Escape keeps paused and returns focus to Stop, Stop confirms", async () => {
    const m = await mountLecture({ events: baseEvents("Paused", "user"), track: "ended" });
    const stopBtn = screen.getByRole("button", { name: "Stop lecture" });
    await userEvent.click(stopBtn);
    const dlg = screen.getByRole("dialog", { name: "Stop and save this lecture?" });
    expect(dlg).toHaveAttribute("aria-modal", "true");
    expect(within(dlg).getByRole("button", { name: "Keep paused" })).toHaveFocus();
    await userEvent.tab();
    expect(within(dlg).getByRole("button", { name: "Stop" })).toHaveFocus();
    await userEvent.tab();
    expect(within(dlg).getByRole("button", { name: "Keep paused" })).toHaveFocus(); // wraps, no escape
    await userEvent.keyboard("{Escape}");
    expect(screen.queryByRole("dialog")).toBeNull();
    await waitFor(() => expect(stopBtn).toHaveFocus());
    expect(stateLabel()).toBe("Paused");

    await userEvent.click(stopBtn);
    await userEvent.click(within(screen.getByRole("dialog")).getByRole("button", { name: "Stop" }));
    await waitFor(() => expect(stateLabel()).toBe("Stopped"));
    expect(screen.getByRole("heading", { name: "Lecture stopped" })).toHaveFocus();
    expect(m.mic.requestCount).toBe(0);
  });

  it("REST failure on Pause: error shown, true state shown, track already off", async () => {
    const m = await mountLecture({ failActions: { pause: true } });
    await userEvent.click(screen.getByRole("button", { name: "Pause" }));
    await waitFor(() => expect(screen.getByTestId("action-error")).toBeInTheDocument());
    expect(m.track!.readyState).toBe("ended");
    expect(stateLabel()).toBe("Not recording"); // server Active but no mic: never "Recording"
  });

  it("Resume with mic denied keeps the lecture paused, says so, and does not loop", async () => {
    const m = await mountLecture({ events: baseEvents("Paused", "user"), track: "ended" });
    m.mic.outcome = "denied";
    await userEvent.click(screen.getByRole("button", { name: "Resume recording" }));
    await waitFor(() => expect(screen.getByTestId("resume-mic-issue")).toBeInTheDocument());
    await waitFor(() => expect(stateLabel()).toBe("Paused"));
    expect(m.mic.requestCount).toBe(1);
  });
});

describe("Stopped (UX sections 3 and 9)", () => {
  it("shows Stopped with saved-transcript copy, no Pause/Stop buttons, Back to course; axe clean", async () => {
    const m = await mountLecture({ events: baseEvents("Stopped", "user"), track: "ended" });
    expect(stateLabel()).toBe("Stopped");
    expect(screen.getByText("Your transcript is saved.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Pause" })).toBeNull();
    await userEvent.click(screen.getByRole("button", { name: "Back to course" }));
    expect(m.onBack).toHaveBeenCalled();
    expect(await axeViolations(m.container)).toEqual([]);
  });

  it.each([
    ["auto_stop_paused_timeout", /30 minutes paused/],
    ["hard_cap", /3 hour limit/],
    ["abandoned", /ended unexpectedly/],
    ["something_new", /ended by the system/],
  ])("UX 9 AC1: reason %s maps to its copy (unknown falls back to 'ended by the system')", async (reason, re) => {
    await mountLecture({ events: baseEvents("Stopped", reason), track: "ended" });
    expect(screen.getByText(re)).toBeInTheDocument();
  });

  it("server Failed is shown as stopped with 'ended by the system'", async () => {
    const events = [...baseEvents(), b.sessionStateEvent(SID, "4", "Failed")];
    await mountLecture({ events, track: "ended" });
    expect(stateLabel()).toBe("Stopped");
    expect(screen.getByText(/ended by the system/)).toBeInTheDocument();
  });
});

describe("Mic lost mid-session (UX section 5)", () => {
  it("AC4: unplugging alerts exactly once and shows the Microphone stopped panel; pause is requested", async () => {
    const m = await mountLecture();
    await act(async () => m.track!.simulateDeviceLoss());
    await waitFor(() => expect(screen.getByRole("region", { name: "Microphone stopped" })).toBeInTheDocument());
    await flush();
    expect(m.alertLog.filter((t) => t === "The microphone stopped. Recording is paused.")).toHaveLength(1);
    expect(m.alertLog.filter((t) => t.startsWith("Something doesn't match"))).toHaveLength(0);
    expect(stateLabel()).toBe("Paused");
    expect(m.mic.requestCount).toBe(0); // no automatic retry
  });
});

describe("Disconnected (UX section 6)", () => {
  it("AC1/AC2: banner text present, label never reads Recording while disconnected", async () => {
    const m = await mountLecture({ online: false });
    expect(m.container.querySelector("[data-banner=offline]")).toHaveTextContent("Not connected. Audio is not being saved.");
    expect(stateLabel()).toBe("Not recording");
    expect(screen.getByTestId("connection")).toHaveTextContent("Not connected");
    expect(screen.queryByText("Recording", { selector: "[data-testid=state-label]" })).toBeNull();
    expect(await axeViolations(m.container)).toEqual([]);
  });

  it("AC3: one alert per disconnect episode regardless of retry count", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const m = await mountLecture({ online: false });
    await act(async () => void (await vi.advanceTimersByTimeAsync(45_000)));
    expect(m.alertLog.filter((t) => t === "Not connected. Audio is not being saved.")).toHaveLength(1);
  });

  it("the local track is stopped after 60 s offline (client auto-pause)", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const m = await mountLecture({ online: false });
    expect(m.track!.readyState).toBe("live");
    await act(async () => void (await vi.advanceTimersByTimeAsync(61_000)));
    expect(m.track!.readyState).toBe("ended");
  });

  it("Stop while offline stops the mic at once and says it will finish when back online", async () => {
    const m = await mountLecture({ online: false });
    await userEvent.click(screen.getByRole("button", { name: "Stop" }));
    await userEvent.click(within(screen.getByRole("dialog")).getByRole("button", { name: "Stop" }));
    expect(m.track!.readyState).toBe("ended");
    expect(screen.getByText(/will finish when you are back online/)).toBeInTheDocument();
  });

  it("Resume is disabled with a reason while not connected", async () => {
    await mountLecture({ online: false, events: baseEvents("Paused", "user"), track: "ended" });
    const resume = screen.getByRole("button", { name: "Resume recording" });
    expect(resume).toHaveAttribute("aria-disabled", "true");
    expect(resume).toHaveAccessibleDescription(/not available while not connected/i);
  });

  it("AC5: replayed duplicates after reconnect leave no duplicate segment ids", async () => {
    const m = await mountLecture();
    m.controls.drop();
    await flush();
    m.controls.push(b.finalEvent(SID, "4", 3, "Spoken while away."));
    m.controls.restore();
    await act(async () => {
      m.controls.source.reconnect();
      await m.controls.source.join({ sessionId: SID, protocol: "lecture.v1" });
    });
    const ids = Array.from(m.container.querySelectorAll("[data-segment-id]")).map((e) => e.getAttribute("data-segment-id"));
    expect(new Set(ids).size).toBe(ids.length);
  });
});

describe("Mic/server mismatch (UX section 8)", () => {
  it("AC1: a live track after a server-driven pause is ended and the warning shows, alert once", async () => {
    const m = await mountLecture({ script: [b.sessionStateEvent(SID, "4", "Paused", "user")] });
    expect(m.track!.readyState).toBe("live");
    await act(async () => void m.controls.step());
    await waitFor(() => expect(m.track!.readyState).toBe("ended"));
    const panel = screen.getByRole("region", { name: "Something doesn't match" });
    expect(panel.textContent).toContain("We turned the microphone off to be safe.");
    expect(stateLabel()).toBe("Not recording");
    expect(m.alertLog.filter((t) => t.startsWith("Something doesn't match"))).toHaveLength(1);
    expect(screen.queryByRole("region", { name: "Recording is paused" })).toBeNull();
  });

  it("AC2: warning has a text heading, an icon marked decorative, and axe is clean", async () => {
    const m = await mountLecture({ events: baseEvents("Paused", "user"), track: "live" });
    await waitFor(() => screen.getByRole("region", { name: /Something doesn't match/ }));
    expect(await axeViolations(m.container)).toEqual([]);
  });

  it("AC3: Check again never restarts capture", async () => {
    const m = await mountLecture({ events: baseEvents("Paused", "user"), track: "live" });
    await waitFor(() => screen.getByRole("region", { name: /Something doesn't match/ }));
    await userEvent.click(screen.getByRole("button", { name: "Check again" }));
    await flush();
    expect(m.mic.requestCount).toBe(0);
    expect(m.track!.readyState).toBe("ended");
  });
});

describe("source scan", () => {
  it("UX 6 AC6 / UX 10 AC3: no 'still recording on your device' string, no camera, Notification, vibrate or AudioContext use", async () => {
    const fs = await import("node:fs");
    const path = await import("node:path");
    const dir = path.resolve(__dirname);
    const files = fs.readdirSync(dir).filter((f) => /\.(ts|tsx)$/.test(f) && !/\.test\./.test(f));
    for (const f of files) {
      const src = fs.readFileSync(path.join(dir, f), "utf8");
      expect(src, f).not.toMatch(/still recording on your device/i);
      expect(src, f).not.toMatch(/video\s*:\s*true|facingMode|new Notification|navigator\.vibrate|AudioContext/);
    }
  });
});
