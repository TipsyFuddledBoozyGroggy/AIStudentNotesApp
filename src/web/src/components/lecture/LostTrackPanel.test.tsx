import { act, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api/errors";
import * as b from "@/lib/lecture/builders";
import type { CatchUpPayload, SummaryRevisionPayload } from "@/lib/lecture";
import { axeViolations } from "@/test/axe";
import { CATCHUP_POLL_MAX, CATCHUP_POLL_MS } from "./useLostTrack";
import type { LectureAi } from "./ports";
import { baseEvents, flush, mountLecture, SID } from "./testUtils";

afterEach(() => vi.useRealTimers());

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
  text: "Right now: limits.",
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
    startLostTrack: vi.fn().mockResolvedValue(extractive()),
    getCatchUp: vi.fn().mockResolvedValue(extractive()),
    ...over,
  } as unknown as LectureAi & { startLostTrack: Fn; getCatchUp: Fn; getSummary: Fn };
}

const press = (m: number) => userEvent.setup().click(screen.getByRole("button", { name: `Catch up on the last ${m} minutes` }));

describe("I Lost Track panel", () => {
  it("is hidden when the runtime has no AI routes", async () => {
    await mountLecture();
    expect(screen.queryByRole("heading", { name: "I lost track" })).toBeNull();
  });

  it("offers 2/5/10 min windows; one press runs at once with a fresh Idempotency-Key, moves focus to the result and announces politely", async () => {
    const ai = fakeAi();
    const m = await mountLecture({ ai });
    expect(screen.getAllByRole("button", { name: /^Catch up on the last/ })).toHaveLength(3);
    await press(2);
    expect(ai.startLostTrack).toHaveBeenCalledTimes(1);
    const [w, key] = ai.startLostTrack.mock.calls[0]!;
    expect(w).toBe(2);
    expect(key).toMatch(/^[A-Za-z0-9_-]{1,64}$/);
    const heading = await screen.findByRole("heading", { name: "Here's where you are" });
    expect(heading).toHaveFocus();
    expect(screen.getByTestId("catchup-extract")).toHaveTextContent("First line of the lecture.");
    expect(screen.getByTestId("catchup-notice")).toHaveTextContent("A short recap is being written");
    expect(m.statusLog.at(-1)).toContain("Here's where you are");
    expect(m.alertLog).toEqual([]);
    await press(5);
    expect(ai.startLostTrack.mock.calls[1]![1]).not.toBe(key); // new press, new key
  });

  it("a second press while one is running is ignored (no duplicate request)", async () => {
    let release!: (p: CatchUpPayload) => void;
    const ai = fakeAi({ startLostTrack: vi.fn(() => new Promise<CatchUpPayload>((r) => (release = r))) });
    await mountLecture({ ai });
    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: "Catch up on the last 5 minutes" }));
    await user.click(screen.getByRole("button", { name: "Catch up on the last 10 minutes" }));
    expect(ai.startLostTrack).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId("lt-working")).toBeInTheDocument();
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
    await userEvent.setup().click(within(result).getAllByRole("button", { name: /^From .* go to transcript line$/ })[0]!);
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
    expect(screen.getByTestId("catchup-notice")).toHaveTextContent("taking longer than expected");
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
    ["Unavailable", "Summary isn't available right now. Here's what was said."],
    ["BudgetLimited", "The recap limit for this lecture has been reached."],
  ] as const)("aiState %s shows the raw excerpt with calm copy and does not poll", async (aiState, text) => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const ai = fakeAi({ startLostTrack: vi.fn().mockResolvedValue(extractive({ aiState })) });
    await mountLecture({ ai });
    await press(5);
    expect(screen.getByTestId("catchup-notice")).toHaveTextContent(text);
    await act(async () => void (await vi.advanceTimersByTimeAsync(CATCHUP_POLL_MS * 3)));
    expect(ai.getCatchUp).not.toHaveBeenCalled();
  });

  it("NotNeeded shows the server's own empty-window text", async () => {
    const ai = fakeAi({
      startLostTrack: vi.fn().mockResolvedValue(extractive({ aiState: "NotNeeded", text: "No transcript text in this window yet.", citations: [] })),
    });
    await mountLecture({ ai });
    await press(5);
    expect(screen.getByTestId("catchup-extract")).toHaveTextContent("No transcript text in this window yet.");
    expect(screen.queryByTestId("catchup-notice")).toBeNull();
  });

  it.each([
    [new ApiError(409, "invalid_state", "x"), "This lecture has ended"],
    [new ApiError(429, "rate_limited", "x"), "That is a lot of catch-ups at once"],
    [new ApiError(404, "not_found", "x"), "We can't find this lecture"],
    [new ApiError(0, "network_error", "x"), "We could not reach the server"],
    [new ApiError(500, "http_error", "x"), "That did not work. Nothing was lost."],
  ])("error %#: calm message, announced politely (never an alert), exactly one request", async (err, text) => {
    const ai = fakeAi({ startLostTrack: vi.fn().mockRejectedValue(err) });
    const m = await mountLecture({ ai });
    await press(5);
    expect(screen.getByTestId("lt-error")).toHaveTextContent(text);
    expect(m.statusLog.at(-1)).toContain(text);
    expect(m.alertLog).toEqual([]);
    expect(ai.startLostTrack).toHaveBeenCalledTimes(1);
    await flush();
    expect(ai.startLostTrack).toHaveBeenCalledTimes(1);
    expect(screen.getByRole("button", { name: "Catch up on the last 5 minutes" })).not.toHaveAttribute("aria-disabled");
  });

  it("after a network failure the same press retried reuses its key; after a definitive answer it does not", async () => {
    const ai = fakeAi({
      startLostTrack: vi
        .fn()
        .mockRejectedValueOnce(new ApiError(0, "network_error", "x"))
        .mockRejectedValueOnce(new ApiError(429, "rate_limited", "x"))
        .mockResolvedValue(extractive()),
    });
    await mountLecture({ ai });
    await press(5);
    await press(5);
    await press(5);
    const keys = ai.startLostTrack.mock.calls.map((c) => c[1]);
    expect(keys[1]).toBe(keys[0]);
    expect(keys[2]).not.toBe(keys[1]);
  });

  it("renders AI text as inert plain text", async () => {
    const evil = '<img src=x onerror="window.__pwn=1"> <script>window.__pwn=2</script>';
    const ai = fakeAi({
      startLostTrack: vi.fn().mockResolvedValue(
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

  it("Got it closes the result and returns focus to the transcript", async () => {
    await mountLecture({ ai: fakeAi() });
    await press(5);
    await userEvent.setup().click(await screen.findByRole("button", { name: "Got it, back to lecture" }));
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
    expect(ai.startLostTrack).toHaveBeenCalledWith(10, expect.any(String));
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
    ["Delayed", "The summary is a little behind."],
    ["Failed", "The summary is paused for now."],
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
