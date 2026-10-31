import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { axeViolations } from "@/test/axe";
import { baseEvents, mountLecture } from "./testUtils";
import type { LectureAi } from "./ports";

afterEach(() => {
  vi.useRealTimers();
  window.localStorage.clear();
});

const fakeAi = () =>
  ({
    getSummary: vi.fn().mockResolvedValue(null),
    startQuickRecap: vi.fn(),
    getCatchUp: vi.fn().mockResolvedValue(null),
  }) as unknown as LectureAi;

describe("Lecture screen redesign (design-direction slices 4-5)", () => {
  it("has exactly one h1 and the bar keeps its frozen names", async () => {
    await mountLecture();
    expect(screen.getAllByRole("heading", { level: 1 })).toHaveLength(1);
    const bar = screen.getByRole("banner", { name: "Recording status" });
    expect(within(bar).getByRole("heading", { level: 2, name: "Recording" })).toBeInTheDocument();
    expect(within(bar).getByRole("button", { name: "Pause" })).toBeInTheDocument();
    expect(within(bar).getByRole("button", { name: "Stop" })).toBeInTheDocument();
  });

  it("Leave is absent while Active or Paused (Lead decision) and present once Stopped", async () => {
    await mountLecture();
    expect(screen.queryByRole("button", { name: /Leave/ })).toBeNull();
  });

  it("Leave is absent while Paused", async () => {
    await mountLecture({ events: baseEvents("Paused", "user"), track: "ended" });
    expect(screen.getByRole("region", { name: "Recording is paused" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Leave/ })).toBeNull();
  });

  it("Stopped: one exit (Back to course), no Leave, no Focus toggle, no Pause updates (review S2)", async () => {
    const m = await mountLecture({ events: baseEvents("Stopped", "user"), track: "ended" });
    expect(screen.queryByRole("button", { name: /Leave/ })).toBeNull();
    expect(screen.queryByRole("button", { name: "Focus" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Pause updates" })).toBeNull();
    await userEvent.click(screen.getByRole("button", { name: "Back to course" }));
    expect(m.onBack).toHaveBeenCalled();
  });

  it("finished lecture: Search this lecture filters loaded lines; Export is disabled with its reason (slice 8)", async () => {
    await mountLecture({ events: baseEvents("Stopped", "user"), track: "ended" });
    const search = screen.getByRole("searchbox", { name: "Search this lecture" });
    expect(screen.getByText("2 lines.")).toBeInTheDocument();
    await userEvent.type(search, "second");
    expect(screen.getByText("1 of 2 lines match.")).toBeInTheDocument();
    expect(screen.getByText("Second line of the lecture.")).toBeInTheDocument();
    expect(screen.queryByText("First line of the lecture.")).toBeNull();
    const exp = screen.getByRole("button", { name: "Export to Google Docs" });
    expect(exp).toHaveAttribute("aria-disabled", "true");
    expect(exp).toHaveAccessibleDescription(/Nothing is sent anywhere/);
  });

  it("no review search while the lecture is live", async () => {
    await mountLecture();
    expect(screen.queryByRole("searchbox", { name: "Search this lecture" })).toBeNull();
  });

  it("elapsed time shows whole minutes by default (nothing ticks every second)", async () => {
    await mountLecture();
    expect(screen.getByTestId("elapsed").textContent).toMatch(/^\d+ min$/);
  });

  it("Focus toggle: pressed state, keeps the bar and Quick recap, collapses the TL;DR in place, adds no live region", async () => {
    const m = await mountLecture({ ai: fakeAi() });
    const focus = screen.getByRole("button", { name: "Focus" });
    expect(focus).toHaveAttribute("aria-pressed", "false");
    expect(screen.getByRole("button", { name: /recap of the last 5 minutes$/ })).toBeInTheDocument();
    await userEvent.click(focus);
    expect(focus).toHaveAttribute("aria-pressed", "true");
    expect(m.container.querySelector('[data-layout="focus"]')).not.toBeNull();
    // Bar and dock stay; the TL;DR becomes a one-line summary with its rest one press away.
    expect(screen.getByRole("banner", { name: "Recording status" })).toBeInTheDocument();
    expect(screen.getAllByRole("button", { name: /recap of the last \d+ minutes$/ })).toHaveLength(3);
    const more = screen.getByRole("button", { name: "More of the TL;DR" });
    expect(more).toHaveAttribute("aria-expanded", "false");
    await userEvent.click(more);
    expect(screen.getByRole("button", { name: "Less of the TL;DR" })).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("button", { name: "Pause updates" })).toBeInTheDocument();
    const live = Array.from(document.querySelectorAll('[role="status"],[role="alert"],[aria-live]')).filter((e) => e.getAttribute("aria-live") !== "off");
    expect(live.map((e) => e.getAttribute("data-region"))).toEqual(["status", "alert"]);
    expect(await axeViolations(m.container)).toEqual([]);
    await userEvent.click(focus);
    expect(focus).toHaveAttribute("aria-pressed", "false");
  });

  // ---- Slice 3 (redesign-round2 2.3 / 5.1 / 5.2): the lecture screen. jsdom reports the wide layout (useWide). ----
  const recapPayload = {
    catchUpId: "cu_s3",
    windowMinutes: 5,
    kind: "Ai",
    aiState: "Ready",
    source: "mock",
    text: "Most recent: the chain rule.",
    citations: [{ segmentId: "seg_1", startMs: 1000 }],
    nowDiscussing: { text: "The chain rule, right after the power rule.", basis: "lecture", citations: [{ segmentId: "seg_1", startMs: 1000 }] },
    shortCatchUp: [{ text: "The derivative of x squared is two x.", basis: "lecture", citations: [{ segmentId: "seg_1", startMs: 1000 }] }],
  };
  const recapAi = () =>
    ({
      getSummary: vi.fn().mockResolvedValue({ revision: 1, basedOnSeq: "2", tldr: "Derivatives and the power rule.", bullets: ["Power rule."], coveredFromMs: 0, coveredToMs: 9000, status: "Draft", citations: [{ segmentId: "seg_1", startMs: 1000 }], source: "mock" }),
      startQuickRecap: vi.fn().mockResolvedValue(recapPayload),
      getCatchUp: vi.fn().mockResolvedValue(null),
    }) as unknown as LectureAi;

  it("desktop: the recap result opens as a sheet in the transcript column, before the transcript and outside the rail; heading focused; Close recap returns to the transcript", async () => {
    await mountLecture({ ai: recapAi() });
    await userEvent.click(screen.getByRole("button", { name: /\(your usual\)/ }));
    const result = await screen.findByTestId("catchup-result");
    expect(result.className).toMatch(/\blecture-sheet\b/);
    expect(document.getElementById("lecture-quick-recap")!.contains(result)).toBe(false);
    const log = screen.getByRole("log", { name: "Transcript lines" });
    expect(result.compareDocumentPosition(log) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy(); // DOM order = visual order
    expect(screen.getByRole("heading", { level: 2, name: "Here is what was said" })).toHaveFocus();
    // Synthesis in the serif role, verbatim claims in the sans; citation times are links, not text-accent utilities.
    expect(within(result).getByText(/The chain rule, right after the power rule\./).closest("p")!.className).toMatch(/\bsynthesis\b/);
    expect(within(result).getByText("The derivative of x squared is two x.").closest("li")!.className).toMatch(/\bhang-row\b/);
    for (const b of within(result).getAllByRole("button", { name: /go to transcript line$/ })) expect(b.className).toBe("cite");
    const stamp = within(result).getByTestId("demo-summary-tag");
    expect(stamp).toHaveTextContent("Demo summary"); // G1: the Demo summary tag keeps its pre-redesign presentation, not a Stamp
    expect(stamp).toHaveTextContent("Made by a test routine, not an AI model.");
    expect(within(result).getByRole("button", { name: "Back to lecture" })).toBeInTheDocument();
    await userEvent.click(within(result).getByRole("button", { name: "Close recap" }));
    expect(screen.queryByTestId("catchup-result")).toBeNull();
    expect(log).toHaveFocus();
  });

  it("the rail is one ledger: Quick recap row (sans title, three windows in a row, usual marker on its own line) then the TL;DR row with the serif summary line", async () => {
    await mountLecture({ ai: recapAi() });
    const rail = document.querySelector("[data-lecture-rail]")!;
    expect(rail.className).toMatch(/\blecture-rail\b/);
    const [qr, tldr] = Array.from(rail.children);
    expect(qr.id).toBe("lecture-quick-recap");
    expect(tldr.id).toBe("lecture-tldr");
    expect(qr.className).toMatch(/\brail-row\b/);
    expect(tldr.className).toMatch(/\brail-row\b/);
    expect(tldr.className).not.toMatch(/\bnote\b/);
    expect(screen.getByRole("heading", { name: "Quick recap" }).className).toBe("rail-title");
    expect(screen.getByRole("heading", { name: "TL;DR" }).className).toBe("rail-title");
    const usual = screen.getByRole("button", { name: "Last 5 min (your usual): recap of the last 5 minutes" });
    expect(usual).toHaveTextContent("Last 5 min (your usual)");
    expect(usual.querySelector("span.block")).toHaveTextContent("(your usual)");
    await waitFor(() => expect(screen.getByText("Derivatives and the power rule.")).toBeInTheDocument());
    expect(screen.getByText("Derivatives and the power rule.").closest("p")!.className).toMatch(/\bsynthesis\b/);
    expect(screen.getByRole("button", { name: "From 00:01" }).className).toBe("cite");
  });

  it("Focus mode: the TL;DR is a neutral Note above the transcript and the toggle is a tertiary with a pressed state (no accent tint)", async () => {
    await mountLecture({ ai: recapAi() });
    const focus = screen.getByRole("button", { name: "Focus" });
    expect(focus.className).toMatch(/\bbtn-tertiary\b/);
    expect(focus.className).not.toMatch(/accent/);
    await userEvent.click(focus);
    expect(focus).toHaveAttribute("aria-pressed", "true");
    const tldr = document.getElementById("lecture-tldr")!;
    expect(tldr.className).toMatch(/\bnote note-neutral\b/);
    const log = screen.getByRole("log", { name: "Transcript lines" });
    expect(tldr.compareDocumentPosition(log) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it("the transcript is the sheet (heading inside); safety states are notes with a shape and a word; the Demo-source notice is unchanged (G1)", async () => {
    await mountLecture({ events: baseEvents("Paused", "user"), track: "ended" });
    const sheet = document.querySelector(".transcript-panel")!;
    expect(sheet.className).toMatch(/\blecture-sheet\b/);
    expect(within(sheet as HTMLElement).getByRole("heading", { name: "Transcript" })).toBeInTheDocument();
    const paused = document.querySelector('[data-panel="paused"]')!;
    expect(paused.className).toBe("note note-notice");
    expect(paused.querySelector("svg[aria-hidden='true']")).not.toBeNull();
    expect(within(paused as HTMLElement).getByRole("heading", { name: "Recording is paused" }).className).toBe("note-title");
    const demo = screen.getByTestId("demo-notice");
    expect(demo.className).toBe("panel panel-notice flex gap-3 py-3");
    expect(within(demo).getByRole("heading", { name: "Demo source: these words are not from your microphone" }).className).toBe("font-bold");
    expect(within(demo).getByText("Details")).toBeInTheDocument();
    expect(screen.getByTestId("demo-tag").className).toBe("tag tag-warning hidden sm:inline-flex");
  });

  it("the usual Quick recap window is the only filled button in the dock; busy never greys the buttons out", async () => {
    await mountLecture({ ai: fakeAi() });
    const usual = screen.getByRole("button", { name: /\(your usual\)/ });
    expect(usual.className).toMatch(/btn-primary/);
    const others = screen.getAllByRole("button", { name: /recap of the last \d+ minutes$/ }).filter((b) => b !== usual);
    for (const b of others) expect(b.className).not.toMatch(/btn-primary/);
    for (const b of [usual, ...others]) expect(b.className).toMatch(/btn-steady/);
  });
});
