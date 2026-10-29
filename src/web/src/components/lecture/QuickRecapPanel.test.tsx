import { act, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api/errors";
import * as b from "@/lib/lecture/builders";
import type { CatchUpPayload, SummaryRevisionPayload } from "@/lib/lecture";
import { axeViolations } from "@/test/axe";
import { CATCHUP_POLL_MAX, CATCHUP_POLL_MS } from "./useQuickRecap";
import type { LectureAi } from "./ports";
import { baseEvents, flush, mountLecture, SID } from "./testUtils";

afterEach(() => {
  vi.useRealTimers();
  window.localStorage.clear();
});

const extractive = (over: Partial<CatchUpPayload> = {}): CatchUpPayload => ({
  catchUpId: "cu_1",
  windowMinutes: 5,
  text: "[00:01] First line of the lecture.\n[00:05] Second line of the lecture.",
  citations: [
    { segmentId: "seg_1", startMs: 1000 },
    { segmentId: "seg_2", startMs: 5000 },
  ],
  kind: "Extractive",
  aiState: "Pending",
  ...over,
});
const aiVersion = (over: Partial<CatchUpPayload> = {}): CatchUpPayload => ({
  catchUpId: "cu_1",
  windowMinutes: 5,
  text: "Most recent: limits.",
  citations: [],
  kind: "Ai",
  aiState: "Ready",
  source: "mock",
  confidence: "medium",
  nowDiscussing: { text: "Limits are being introduced.", basis: "lecture", citations: [{ segmentId: "seg_2", startMs: 5000 }] },
  shortCatchUp: [
    { text: "A limit is a value approached.", basis: "lecture", citations: [{ segmentId: "seg_1", startMs: 1000 }] },
    { text: "Think of a speed limit.", basis: "general_knowledge", citations: [] },
  ],
  detail: [{ text: "Detail line.", basis: "uncertain", citations: [], whyItMatters: "Needed for derivatives." }],
  keyTerms: [{ term: "limit", definition: "a value approached", basis: "lecture", citations: [] }],
  clarifyingQuestions: ["Does the limit always exist?"],
  gaps: ["Audio unclear near 00:03."],
  ...over,
});

type Fn = ReturnType<typeof vi.fn>;
function fakeAi(over: Record<string, unknown> = {}) {
  return {
    getSummary: vi.fn().mockResolvedValue(null),
    startQuickRecap: vi.fn().mockResolvedValue(extractive()),
    getCatchUp: vi.fn().mockResolvedValue(extractive()),
    ...over,
  } as unknown as LectureAi & { startQuickRecap: Fn; getCatchUp: Fn; getSummary: Fn };
}

const press = (m: number) => userEvent.setup().click(screen.getByRole("button", { name: new RegExp(`recap of the last ${m} minutes$`) }));

describe("Quick recap panel", () => {
  it("is hidden when the runtime has no AI routes", async () => {
    await mountLecture();
    expect(screen.queryByRole("heading", { name: "Quick recap" })).toBeNull();
  });

  it("offers 2/5/10 min windows; one press runs at once with a fresh Idempotency-Key, moves focus to the result and announces politely", async () => {
    const ai = fakeAi();
    const m = await mountLecture({ ai });
    expect(screen.getAllByRole("button", { name: /recap of the last \d+ minutes$/ })).toHaveLength(3);
    await press(2);
    expect(ai.startQuickRecap).toHaveBeenCalledTimes(1);
    const [w, key] = ai.startQuickRecap.mock.calls[0]!;
    expect(w).toBe(2);
    expect(key).toMatch(/^[A-Za-z0-9_-]{1,64}$/);
    const heading = await screen.findByRole("heading", { name: "Here is what was said" });
    expect(heading).toHaveFocus();
    expect(screen.getByTestId("catchup-extract")).toHaveTextContent("First line of the lecture.");
    expect(screen.getByTestId("catchup-notice")).toHaveTextContent("A short recap is being written");
    expect(m.statusLog).toEqual(["Getting what was said..."]); // one spoken cue when working; the result is announced by the focus move
    expect(m.alertLog).toEqual([]);
    await press(5);
    expect(ai.startQuickRecap.mock.calls[1]![1]).not.toBe(key); // new press, new key
  });

  it("a second press while one is running is ignored (no duplicate request)", async () => {
    let release!: (p: CatchUpPayload) => void;
    const ai = fakeAi({ startQuickRecap: vi.fn(() => new Promise<CatchUpPayload>((r) => (release = r))) });
    await mountLecture({ ai });
    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: /recap of the last 5 minutes$/ }));
    await user.click(screen.getByRole("button", { name: /recap of the last 10 minutes$/ }));
    expect(ai.startQuickRecap).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId("qr-working")).toBeInTheDocument();
    await act(async () => release(extractive({ aiState: "Unavailable" })));
  });

  it("hub catchup.ready upgrades the extractive view to the AI view; mock shows Demo summary; citations jump to the transcript", async () => {
    Element.prototype.scrollIntoView = vi.fn(); // not implemented in jsdom
    const ai = fakeAi();
    const m = await mountLecture({ ai });
    await press(5);
    await screen.findByTestId("catchup-result");
    expect(screen.queryByTestId("demo-summary-tag")).toBeNull();
    await act(async () => m.controls.push(b.catchUpEvent(SID, aiVersion())));
    const result = screen.getByTestId("catchup-result");
    expect(result).toHaveAttribute("data-kind", "Ai");
    expect(within(result).getByTestId("demo-summary-tag")).toHaveTextContent("Demo summary");
    expect(within(result).getByText("Limits are being introduced.")).toBeInTheDocument();
    expect(within(result).getByText("Not from your lecture")).toBeInTheDocument();
    expect(within(result).getByText("Confidence: medium")).toBeInTheDocument();
    expect(within(result).getByText("Audio unclear near 00:03.")).toBeInTheDocument();
    await waitFor(() => expect(m.statusLog).toContain("Recap ready."), { timeout: 3500 }); // announcer spaces messages 2 s apart
    await userEvent.setup().click(within(result).getAllByRole("button", { name: /^\d\d:\d\d, go to transcript line$/ })[0]!);
    expect(document.activeElement).toHaveAttribute("data-segment-id");
    // detail is behind a toggle
    expect(within(result).queryByText("Detail line.")).toBeNull();
    await userEvent.setup().click(within(result).getByRole("button", { name: "Show more detail" }));
    expect(within(result).getByText("Detail line.")).toBeInTheDocument();
    expect(within(result).getByText("Not sure about this")).toBeInTheDocument();
    expect(within(result).getByText("Does the limit always exist?")).toBeInTheDocument();
  });

  it("a real model recap shows no Demo summary tag, and an extractive event never replaces an AI one", async () => {
    const m = await mountLecture({ ai: fakeAi() });
    await press(5);
    await act(async () => m.controls.push(b.catchUpEvent(SID, aiVersion({ source: "model" }))));
    expect(screen.queryByTestId("demo-summary-tag")).toBeNull();
    await act(async () => m.controls.push(b.catchUpEvent(SID, extractive())));
    expect(screen.getByTestId("catchup-result")).toHaveAttribute("data-kind", "Ai");
  });

  it("polls while pending, stops when the AI version arrives", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const ai = fakeAi({ getCatchUp: vi.fn().mockResolvedValueOnce(extractive()).mockResolvedValue(aiVersion()) });
    await mountLecture({ ai });
    await press(5);
    await act(async () => void (await vi.advanceTimersByTimeAsync(CATCHUP_POLL_MS * 2 + 50)));
    expect(screen.getByTestId("catchup-result")).toHaveAttribute("data-kind", "Ai");
    const calls = ai.getCatchUp.mock.calls.length;
    await act(async () => void (await vi.advanceTimersByTimeAsync(CATCHUP_POLL_MS * 5)));
    expect(ai.getCatchUp.mock.calls.length).toBe(calls);
  });

  it("poll is bounded: after the cap it stops and says the recap is slow, keeping the excerpt", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const ai = fakeAi();
    await mountLecture({ ai });
    await press(5);
    await act(async () => void (await vi.advanceTimersByTimeAsync(CATCHUP_POLL_MS * (CATCHUP_POLL_MAX + 5))));
    expect(ai.getCatchUp).toHaveBeenCalledTimes(CATCHUP_POLL_MAX);
    expect(screen.getByTestId("catchup-notice")).toHaveTextContent("Still writing your recap. Here is what was said so far.");
    expect(screen.getByTestId("catchup-extract")).toBeInTheDocument();
  });

  it("a poll failure stops polling (no retry loop)", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const ai = fakeAi({ getCatchUp: vi.fn().mockRejectedValue(new ApiError(0, "network_error", "x")) });
    await mountLecture({ ai });
    await press(5);
    await act(async () => void (await vi.advanceTimersByTimeAsync(CATCHUP_POLL_MS * 6)));
    expect(ai.getCatchUp).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId("catchup-notice")).toBeInTheDocument();
  });

  it.each([
    ["Unavailable", "A recap is not available at the moment. Here is what was said."],
    ["BudgetLimited", "A recap is not available for this lecture."],
  ] as const)("aiState %s shows the raw excerpt with calm copy and does not poll", async (aiState, text) => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const ai = fakeAi({ startQuickRecap: vi.fn().mockResolvedValue(extractive({ aiState })) });
    await mountLecture({ ai });
    await press(5);
    expect(screen.getByTestId("catchup-notice")).toHaveTextContent(text);
    await act(async () => void (await vi.advanceTimersByTimeAsync(CATCHUP_POLL_MS * 3)));
    expect(ai.getCatchUp).not.toHaveBeenCalled();
  });

  it("NotNeeded shows the server's own empty-window text", async () => {
    const ai = fakeAi({
      startQuickRecap: vi.fn().mockResolvedValue(extractive({ aiState: "NotNeeded", text: "No transcript text in this window yet.", citations: [] })),
    });
    await mountLecture({ ai });
    await press(5);
    expect(screen.getByTestId("catchup-extract")).toHaveTextContent("No transcript text in this window yet.");
    expect(screen.queryByTestId("catchup-notice")).toBeNull();
  });

  it.each([
    [new ApiError(409, "invalid_state", "x"), "This lecture has ended"],
    [new ApiError(429, "rate_limited", "x"), "One moment. Try again in about a minute."],
    [new ApiError(404, "not_found", "x"), "We can't find this lecture"],
    [new ApiError(0, "network_error", "x"), "We could not reach the server"],
    [new ApiError(500, "http_error", "x"), "That did not work. Nothing was changed."],
  ])("error %#: calm message, announced politely (never an alert), exactly one request", async (err, text) => {
    const ai = fakeAi({ startQuickRecap: vi.fn().mockRejectedValue(err) });
    const m = await mountLecture({ ai });
    await press(5);
    expect(screen.getByTestId("qr-error")).toHaveTextContent(text);
    await waitFor(() => expect(m.statusLog.at(-1)).toContain(text), { timeout: 3500 }); // spaced 2 s after "Getting what was said..."
    expect(m.alertLog).toEqual([]);
    expect(ai.startQuickRecap).toHaveBeenCalledTimes(1);
    await flush();
    expect(ai.startQuickRecap).toHaveBeenCalledTimes(1);
    expect(screen.getByRole("button", { name: /recap of the last 5 minutes$/ })).not.toHaveAttribute("aria-disabled");
  });

  it("after a network failure the same press retried reuses its key; after a definitive answer it does not", async () => {
    const ai = fakeAi({
      startQuickRecap: vi
        .fn()
        .mockRejectedValueOnce(new ApiError(0, "network_error", "x"))
        .mockRejectedValueOnce(new ApiError(429, "rate_limited", "x"))
        .mockResolvedValue(extractive()),
    });
    await mountLecture({ ai });
    await press(5);
    await press(5);
    await press(5);
    const keys = ai.startQuickRecap.mock.calls.map((c) => c[1]);
    expect(keys[1]).toBe(keys[0]);
    expect(keys[2]).not.toBe(keys[1]);
  });

  it("renders AI text as inert plain text", async () => {
    const evil = '<img src=x onerror="window.__pwn=1"> <script>window.__pwn=2</script>';
    const ai = fakeAi({
      startQuickRecap: vi.fn().mockResolvedValue(
        aiVersion({
          nowDiscussing: { text: evil, basis: "lecture", citations: [] },
          shortCatchUp: [{ text: evil, basis: "lecture", citations: [] }],
          clarifyingQuestions: [evil],
        }),
      ),
    });
    await mountLecture({ ai });
    await press(5);
    const result = screen.getByTestId("catchup-result");
    expect(result.querySelector("img, script")).toBeNull();
    expect(result).toHaveTextContent("<img src=x onerror=");
    expect((window as unknown as { __pwn?: number }).__pwn).toBeUndefined();
    expect(result.querySelector("a[href]")).toBeNull();
  });

  it("Back to lecture closes the result and returns focus to the transcript", async () => {
    await mountLecture({ ai: fakeAi() });
    await press(5);
    await userEvent.setup().click(await screen.findByRole("button", { name: "Back to lecture" }));
    expect(screen.queryByTestId("catchup-result")).toBeNull();
    expect(screen.getByRole("log", { name: "Transcript lines" })).toHaveFocus();
  });

  it("adds no live region of its own, and is axe clean (extractive and AI)", async () => {
    const m = await mountLecture({ ai: fakeAi() });
    await press(5);
    await act(async () => m.controls.push(b.catchUpEvent(SID, aiVersion())));
    await userEvent.setup().click(screen.getByRole("button", { name: "Show more detail" }));
    const live = Array.from(document.querySelectorAll('[role="status"],[role="alert"],[aria-live]')).filter((e) => e.getAttribute("aria-live") !== "off");
    expect(live.map((e) => e.getAttribute("data-region"))).toEqual(["status", "alert"]);
    expect(await axeViolations(m.container)).toEqual([]);
  });

  it("works while paused (the lecture need not be recording)", async () => {
    const ai = fakeAi();
    await mountLecture({ ai, events: baseEvents("Paused", "user") });
    await press(10);
    expect(ai.startQuickRecap).toHaveBeenCalledWith(10, expect.any(String));
  });
});

describe("Quick recap review fixes (M1-M7)", () => {
  const lines = (n: number) => Array.from({ length: n }, (_, i) => `[00:${String(i).padStart(2, "0")}] Line number ${i + 1}.`).join("\n");

  it("M1: the panel is a fixed bottom bar below lg and a sticky column from lg", async () => {
    await mountLecture({ ai: fakeAi() });
    const panel = document.getElementById("lecture-quick-recap")!;
    expect(panel.className).toMatch(/\bfixed\b/);
    expect(panel.className).toMatch(/lg:sticky/);
  });

  it("M2: skip links go to Quick recap (focus only, no catch-up) and TL;DR; Alt+R works only when shortcuts are on", async () => {
    const ai = fakeAi();
    await mountLecture({ ai });
    expect(screen.getByRole("link", { name: "Skip to TL;DR" })).toHaveAttribute("href", "#lecture-tldr");
    await userEvent.setup().click(screen.getByRole("link", { name: "Skip to Quick recap" }));
    expect(screen.getByRole("button", { name: /^Last 5 min \(your usual\): recap of the last 5 minutes$/ })).toHaveFocus();
    expect(ai.startQuickRecap).not.toHaveBeenCalled();
  });

  it("M2: Alt+R is ignored by default", async () => {
    await mountLecture({ ai: fakeAi() });
    await userEvent.keyboard("{Alt>}r{/Alt}");
    expect(screen.getByRole("button", { name: /\(your usual\)/ })).not.toHaveFocus();
  });

  it("M2: Alt+R focuses the usual window (without starting anything) when shortcuts are enabled", async () => {
    const ai = fakeAi();
    await mountLecture({ ai, settings: { shortcuts: true } });
    await userEvent.keyboard("{Alt>}r{/Alt}");
    expect(screen.getByRole("button", { name: /\(your usual\)/ })).toHaveFocus();
    expect(ai.startQuickRecap).not.toHaveBeenCalled();
  });

  it("M3: default window is 5, the last used one is remembered and marked in text, storage failures are tolerated", async () => {
    await mountLecture({ ai: fakeAi() });
    expect(screen.getByRole("button", { name: "Last 5 min (your usual): recap of the last 5 minutes" })).toHaveTextContent("Last 5 min (your usual)");
    await press(10);
    expect(window.localStorage.getItem("tether.lecture.quickRecapWindow.v1")).toBe("10");
    expect(screen.getByRole("button", { name: "Last 10 min (your usual): recap of the last 10 minutes" })).toHaveTextContent("Last 10 min (your usual)");
    expect(screen.getByRole("button", { name: "Last 5 min: recap of the last 5 minutes" })).toHaveTextContent("Last 5 min");
  });

  it("M3: a blocked localStorage does not break the panel", async () => {
    const get = vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => {
      throw new Error("blocked");
    });
    const set = vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
      throw new Error("blocked");
    });
    const ai = fakeAi();
    await mountLecture({ ai, settings: {} });
    await press(2);
    expect(ai.startQuickRecap).toHaveBeenCalledTimes(1);
    get.mockRestore();
    set.mockRestore();
  });

  it("M4: only the last 5 lines of What was said show by default; Show earlier words reveals the rest", async () => {
    const ai = fakeAi({ startQuickRecap: vi.fn().mockResolvedValue(extractive({ aiState: "Unavailable", text: lines(12) })) });
    await mountLecture({ ai });
    await press(10);
    const list = screen.getByTestId("catchup-extract");
    expect(within(list).getAllByRole("listitem")).toHaveLength(5);
    expect(list).toHaveTextContent("Line number 12.");
    expect(list).not.toHaveTextContent("Line number 7.");
    const more = screen.getByRole("button", { name: "Show earlier words" });
    expect(more).toHaveAttribute("aria-expanded", "false");
    await userEvent.setup().click(more);
    expect(within(screen.getByTestId("catchup-extract")).getAllByRole("listitem")).toHaveLength(12);
    expect(screen.getByRole("button", { name: "Show fewer words" })).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByText("These are the words from the lecture, newest last. Not a summary.")).toBeInTheDocument();
  });

  it("M5: coverage is honest for short lectures, partial windows and empty windows", async () => {
    const ai = fakeAi({
      startQuickRecap: vi
        .fn()
        .mockResolvedValueOnce(extractive({ windowMinutes: 10, coveredFromMs: 0, coveredToMs: 180_000, aiState: "Unavailable" }))
        .mockResolvedValueOnce(extractive({ windowMinutes: 5, coveredFromMs: 0, coveredToMs: 300_000, aiState: "Unavailable" }))
        .mockResolvedValueOnce(extractive({ windowMinutes: 5, text: "", citations: [], coveredFromMs: 600_000, coveredToMs: 900_000, aiState: "NotNeeded" })),
    });
    await mountLecture({ ai });
    await press(10);
    const result = await screen.findByTestId("catchup-result");
    expect(result).toHaveTextContent("The lecture has run 3 minutes. Here is all of it.");
    await press(5);
    expect(screen.getByTestId("catchup-result")).toHaveTextContent("Covers the last 5 minutes");
    await press(5);
    expect(screen.getByTestId("catchup-empty")).toHaveTextContent("Nothing was said in this time.");
    expect(screen.queryByTestId("catchup-extract")).toBeNull();
  });

  it("M7: one spoken cue while working, nothing extra when the result arrives; not shown as an alert", async () => {
    const m = await mountLecture({ ai: fakeAi() });
    await press(5);
    await flush();
    expect(m.statusLog.filter((s) => /ready|where you are/i.test(s))).toEqual([]);
    expect(m.statusLog.filter((s) => s === "Getting what was said...")).toHaveLength(1);
    expect(m.alertLog).toEqual([]);
  });

  it("S9: the previous result is labelled when a newer press fails", async () => {
    const ai = fakeAi({ startQuickRecap: vi.fn().mockResolvedValueOnce(extractive()).mockRejectedValueOnce(new ApiError(500, "http_error", "x")) });
    await mountLecture({ ai });
    await press(5);
    expect(screen.queryByTestId("catchup-earlier")).toBeNull();
    await press(2);
    expect(screen.getByTestId("catchup-earlier")).toHaveTextContent("Earlier recap, last 5 min");
  });
});

describe("TL;DR over REST and status", () => {
  const rev = (over: Partial<SummaryRevisionPayload> = {}): SummaryRevisionPayload => ({
    revision: 3,
    basedOnSeq: "3",
    tldr: "Limits and rates.",
    bullets: ["One"],
    coveredFromMs: 0,
    coveredToMs: 65000,
    status: "Draft",
    citations: [{ segmentId: "seg_1", startMs: 1000 }],
    source: "mock",
    ...over,
  });

  it("fetches the latest summary once on connect and shows Demo summary for mock", async () => {
    const ai = fakeAi({ getSummary: vi.fn().mockResolvedValue(rev()) });
    await mountLecture({ ai });
    await waitFor(() => expect(screen.getByText("Limits and rates.")).toBeInTheDocument());
    expect(ai.getSummary).toHaveBeenCalledTimes(1);
    expect(within(document.getElementById("lecture-tldr")!).getByTestId("demo-summary-tag")).toBeInTheDocument();
  });

  it("source 'model' shows no demo tag; a newer hub revision wins, an older one is ignored", async () => {
    const ai = fakeAi({ getSummary: vi.fn().mockResolvedValue(rev({ source: "model" })) });
    const m = await mountLecture({ ai });
    await waitFor(() => expect(screen.getByText("Limits and rates.")).toBeInTheDocument());
    expect(screen.queryByTestId("demo-summary-tag")).toBeNull();
    await act(async () => m.controls.push(b.summaryEvent(SID, 2, "Older text")));
    expect(screen.queryByText("Older text")).toBeNull();
    await act(async () => m.controls.push(b.summaryEvent(SID, 4, "Newer text")));
    expect(screen.getByText("Newer text")).toBeInTheDocument();
  });

  it("a summary fetch failure is silent and shows the empty state", async () => {
    const ai = fakeAi({ getSummary: vi.fn().mockRejectedValue(new ApiError(500, "http_error", "x")) });
    const m = await mountLecture({ ai });
    await flush();
    expect(screen.getByText("The summary will appear here as the lecture goes on.")).toBeInTheDocument();
    expect(m.alertLog).toEqual([]);
    expect(ai.getSummary).toHaveBeenCalledTimes(1);
  });

  it.each([
    ["Delayed", "The summary is updating more slowly than usual."],
    ["Failed", "The summary could not update just now."],
  ] as const)("summary.status %s is calm, keeps the last good revision, and does not alert", async (state, text) => {
    const m = await mountLecture({ ai: fakeAi({ getSummary: vi.fn().mockResolvedValue(rev()) }) });
    await waitFor(() => expect(screen.getByText("Limits and rates.")).toBeInTheDocument());
    await act(async () => m.controls.push(b.summaryStatusEvent(SID, state, 30)));
    expect(screen.getByTestId("summary-status")).toHaveTextContent(text);
    expect(screen.getByTestId("summary-status")).toHaveTextContent("As of 01:05");
    expect(screen.getByText("Limits and rates.")).toBeInTheDocument();
    expect(m.alertLog).toEqual([]);
    expect(screen.queryByRole("button", { name: /try again|retry/i })).toBeNull();
  });
});
