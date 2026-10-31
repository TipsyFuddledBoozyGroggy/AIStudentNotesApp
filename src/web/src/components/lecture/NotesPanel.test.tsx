import { act, cleanup, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import * as b from "@/lib/lecture/builders";
import type { LectureNotes, SummaryRevisionPayload } from "@/lib/lecture";
import { ApiError } from "@/lib/api/errors";
import { axeViolations } from "@/test/axe";
import { NOTES_COPY as C, TLDR_COPY } from "./copy";
import type { LectureAi } from "./ports";
import { baseEvents, flush, mountLecture, SID } from "./testUtils";
import { NOTES_POLL_FAST_MS, NOTES_POLL_SLOW_MS } from "./useNotes";

// Slice 8a (PO-18d option B; docs/ux/make-notes-rail-demo-notice.md 1.5 to 1.9). Synthetic data, harness runtime.

afterEach(() => {
  vi.useRealTimers();
  window.localStorage.clear();
});

const KEY_RE = /^[A-Za-z0-9_-]{1,64}$/;

const base: LectureNotes = {
  notesId: "nt_0123456789abcdef0123456789abcdef",
  revision: 1,
  status: "Pending",
  source: null,
  requestedAt: "2026-10-31T14:02:11Z",
  generatedAt: null,
  coveredFromMs: null,
  coveredToMs: null,
  sections: [],
  finalSummaryRevision: null,
  finalSummaryStatus: null,
  truncated: false,
};
const PENDING = base;
const READY: LectureNotes = {
  ...base,
  status: "Ready",
  source: "mock",
  generatedAt: "2026-10-31T14:02:13Z",
  coveredFromMs: 1000,
  coveredToMs: 2900,
  sections: [
    {
      heading: "The opening",
      bullets: [
        { text: "The lecture opened with its first line.", citations: [{ segmentId: "seg_1", startMs: 1000 }] },
        {
          text: "A second line followed it.",
          citations: [
            { segmentId: "seg_2", startMs: 2000 },
            { segmentId: "seg_1", startMs: 1000 },
          ],
        },
      ],
    },
  ],
  finalSummaryRevision: 2,
  finalSummaryStatus: "Ready",
};
const at = (status: LectureNotes["status"], revision = 1): LectureNotes => ({ ...base, status, revision });
const FINAL: SummaryRevisionPayload = {
  revision: 2,
  basedOnSeq: "3",
  tldr: "The final summary of the lecture.",
  bullets: [],
  coveredFromMs: 0,
  coveredToMs: 2900,
  status: "Final",
  citations: [],
  source: "mock",
};

type AiMock = LectureAi & { getNotes: ReturnType<typeof vi.fn>; requestNotes: ReturnType<typeof vi.fn>; getSummary: ReturnType<typeof vi.fn> };
function aiMock(o: { get?: (LectureNotes | null | Error)[]; post?: (LectureNotes | Error)[]; summary?: SummaryRevisionPayload | null } = {}): AiMock {
  const gets = [...(o.get ?? [null])];
  const posts = [...(o.post ?? [PENDING])];
  const next = <T,>(q: T[]) => (q.length > 1 ? q.shift()! : q[0]!);
  return {
    getSummary: vi.fn().mockResolvedValue(o.summary ?? null),
    startQuickRecap: vi.fn(),
    getCatchUp: vi.fn().mockResolvedValue(null),
    getNotes: vi.fn(async () => {
      const v = next(gets);
      if (v instanceof Error) throw v;
      return v;
    }),
    requestNotes: vi.fn(async () => {
      const v = next(posts);
      if (v instanceof Error) throw v;
      return v;
    }),
  } as unknown as AiMock;
}

const stopped = (ai: LectureAi, extra: Parameters<typeof mountLecture>[0] = {}) =>
  mountLecture({ events: baseEvents("Stopped", "user"), track: "ended", ai, ...extra });
const notesSection = () => document.getElementById("lecture-notes")!;
const make = () => screen.queryByRole("button", { name: "Make notes" });
const advance = (ms: number) => act(async () => void (await vi.advanceTimersByTimeAsync(ms)));

describe("Notes block before any press (1.3, 1.9 AC1)", () => {
  it("Stopped: h2 Notes, the DRAFT intro, the one primary Make notes and What is sent?, first in the column before the search tools", async () => {
    await stopped(aiMock());
    const section = notesSection();
    const h2 = within(section).getByRole("heading", { level: 2, name: "Notes" });
    expect(h2).toHaveAttribute("id", "notes-h2");
    expect(h2).toHaveAttribute("tabindex", "-1");
    const intro = section.querySelector("#notes-intro")!;
    expect(intro).toHaveTextContent(C.intro);
    expect(intro.textContent!.endsWith("(Draft wording.)")).toBe(true);
    expect(intro.closest(".sr-only")).toBeNull();
    const btn = make()!;
    expect(btn).toHaveClass("btn-primary");
    expect(btn).toHaveAccessibleDescription(C.intro);
    const what = within(section).getByRole("button", { name: "What is sent?" });
    expect(what).toHaveAttribute("aria-expanded", "false");
    const body = document.getElementById(what.getAttribute("aria-controls")!)!;
    expect(body).toHaveTextContent(C.whatIsSentBody);
    expect(body.textContent!.endsWith("(Draft wording.)")).toBe(true);
    expect(body).not.toBeVisible();
    // DOM order in the column: the Notes block, then the review tools, then the transcript (1.2).
    const tools = screen.getByRole("region", { name: "Review this lecture" });
    expect(section.compareDocumentPosition(tools) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(section.compareDocumentPosition(screen.getByRole("log", { name: "Transcript lines" })) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    // The only filled button in the transcript column (1.1, AC3).
    const column = tools.parentElement!;
    expect(column.contains(section)).toBe(true);
    expect(Array.from(column.querySelectorAll(".btn-primary")).map((e) => e.textContent)).toEqual(["Make notes"]);
  });

  it("What is sent? opens in place, keeps focus, announces nothing", async () => {
    const m = await stopped(aiMock());
    const what = screen.getByRole("button", { name: "What is sent?" });
    const before = m.statusLog.length;
    await userEvent.click(what);
    expect(what).toHaveAttribute("aria-expanded", "true");
    expect(document.getElementById("notes-what-body")).toBeVisible();
    expect(what).toHaveFocus();
    expect(m.statusLog.length).toBe(before);
    await userEvent.click(what);
    expect(document.getElementById("notes-what-body")).not.toBeVisible();
  });

  it.each([
    ["Active", baseEvents()],
    ["Paused", baseEvents("Paused", "user")],
    ["Failed", [...baseEvents(), b.sessionStateEvent(SID, "4", "Failed", "abandoned")]],
  ] as const)("not rendered while %s, and no notes route is called", async (_s, events) => {
    const ai = aiMock();
    await mountLecture({ events: [...events], track: "ended", ai });
    expect(document.getElementById("lecture-notes")).toBeNull();
    expect(make()).toBeNull();
    expect(ai.getNotes).not.toHaveBeenCalled();
    expect(ai.requestNotes).not.toHaveBeenCalled();
  });

  it("a runtime without the notes routes shows no block (harness stand-ins)", async () => {
    await stopped({ getSummary: vi.fn().mockResolvedValue(null), startQuickRecap: vi.fn(), getCatchUp: vi.fn() } as unknown as LectureAi);
    expect(document.getElementById("lecture-notes")).toBeNull();
  });
});

describe("no POST without a click (1.9 AC2): the LectureAi port is spied across every non-press event", () => {
  it("mount Live, Focus toggle, Stop, a theme change, 15 minutes of timers, unmount and reopen: zero requestNotes; one GET per Stopped mount", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const ai = aiMock();
    await mountLecture({ ai });
    expect(ai.getNotes).not.toHaveBeenCalled(); // Live: no notes route at all
    const focusBtn = screen.getByRole("button", { name: "Focus" });
    await userEvent.click(focusBtn);
    expect(focusBtn).toHaveAttribute("aria-pressed", "true");
    await userEvent.click(focusBtn);
    // Stop through the bar and its dialog (the student's Stop is not a notes press).
    await userEvent.click(screen.getByRole("button", { name: "Stop" }));
    await userEvent.click(within(screen.getByRole("dialog")).getByRole("button", { name: "Stop" }));
    await flush();
    expect(notesSection()).toBeInTheDocument();
    expect(ai.getNotes).toHaveBeenCalledTimes(1);
    document.documentElement.setAttribute("data-theme", "dark");
    document.documentElement.setAttribute("data-skin", "gamer");
    await advance(15 * 60_000);
    expect(ai.requestNotes).not.toHaveBeenCalled();
    expect(ai.getNotes).toHaveBeenCalledTimes(1); // no poll without a Pending press
    cleanup();
    document.documentElement.removeAttribute("data-theme");
    document.documentElement.removeAttribute("data-skin");

    // Reopen (a new mount of the same screen on a Stopped session): one fresh GET, still no POST.
    const ai2 = aiMock({ get: [READY] });
    await stopped(ai2);
    await advance(15 * 60_000);
    expect(ai2.getNotes).toHaveBeenCalledTimes(1);
    expect(ai2.requestNotes).not.toHaveBeenCalled();
    expect(ai.requestNotes).not.toHaveBeenCalled();
  });
});

describe("the press (1.5, AC3, AC4)", () => {
  it("one activation = one POST with a key; a second activation while busy sends nothing; label kept; then Pending with focus on Notes and one announcement", async () => {
    let release!: (n: LectureNotes) => void;
    const ai = aiMock();
    ai.requestNotes.mockImplementation(() => new Promise<LectureNotes>((r) => (release = r)));
    const m = await stopped(ai);
    const btn = make()!;
    await userEvent.click(btn);
    expect(btn).toHaveAttribute("aria-busy", "true");
    expect(btn).toHaveTextContent("Make notes");
    expect(btn).toHaveClass("btn-steady");
    await userEvent.click(btn);
    expect(ai.requestNotes).toHaveBeenCalledTimes(1);
    expect(ai.requestNotes.mock.calls[0]![0]).toMatch(KEY_RE);
    await act(async () => release(PENDING));
    await flush();
    expect(make()).toBeNull();
    expect(screen.getByTestId("notes-pending")).toHaveTextContent(C.pending);
    expect(document.activeElement).toBe(document.getElementById("notes-h2"));
    expect(m.statusLog.filter((t) => t === C.pendingAnnounce)).toHaveLength(1);
    // No spinner, percentage, elapsed time or new live region in the block.
    const section = notesSection();
    expect(section.querySelector('[role="progressbar"],[role="status"],[role="alert"],[aria-live],[aria-busy]')).toBeNull();
    expect(section.textContent).not.toMatch(/%|\d+:\d\d|\bstill\b/i);
  });

  it("a press answered Ready at once (a replay) shows the sheet, announces Notes ready once, focus on Notes", async () => {
    const m = await stopped(aiMock({ post: [READY] }));
    await userEvent.click(make()!);
    await flush();
    expect(notesSection()).toHaveClass("lecture-sheet");
    expect(document.activeElement).toBe(document.getElementById("notes-h2"));
    expect(m.statusLog.filter((t) => t === C.readyAnnounce)).toHaveLength(1);
  });

  it("NotNeeded is final: its sentence, no button, one announcement", async () => {
    const m = await stopped(aiMock({ post: [at("NotNeeded")] }));
    await userEvent.click(make()!);
    await flush();
    expect(screen.getByTestId("notes-not-needed")).toHaveTextContent(C.notNeeded);
    expect(make()).toBeNull();
    expect(m.statusLog.filter((t) => t === C.notNeededAnnounce)).toHaveLength(1);
  });

  it.each([
    ["Unavailable", C.unavailable],
    ["BudgetLimited", C.budget],
  ] as const)("%s: the sentence as a note-notice above the button, which is offered again; a new press takes a NEW key", async (status, text) => {
    const ai = aiMock({ post: [at(status), at("Pending", 2)] });
    const m = await stopped(ai);
    await userEvent.click(make()!);
    await flush();
    const note = screen.getByTestId("notes-status");
    expect(note).toHaveClass("note", "note-notice");
    expect(note).toHaveTextContent(text);
    expect(m.statusLog.filter((t) => t === C.unavailableAnnounce)).toHaveLength(1);
    const again = make()!;
    expect(note.compareDocumentPosition(again) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    await userEvent.click(again);
    await flush();
    expect(ai.requestNotes).toHaveBeenCalledTimes(2);
    const [k1, k2] = ai.requestNotes.mock.calls.map((c) => c[0] as string);
    expect(k1).not.toBe(k2);
    expect(screen.getByTestId("notes-pending")).toBeInTheDocument();
  });

  it("429: the calm rate-limit message, focus on it, the button stays; the key is dropped (answered)", async () => {
    const ai = aiMock({ post: [new ApiError(429, "rate_limited", "x"), PENDING] });
    await stopped(ai);
    await userEvent.click(make()!);
    await flush();
    const p = screen.getByTestId("notes-problem");
    expect(p).toHaveTextContent("One moment. Try again in about a minute. Nothing was changed.");
    expect(p).toHaveClass("note", "note-notice");
    expect(p).toHaveFocus();
    expect(make()).toBeInTheDocument();
    await userEvent.click(make()!);
    const [k1, k2] = ai.requestNotes.mock.calls.map((c) => c[0] as string);
    expect(k1).not.toBe(k2);
  });

  it("a network failure keeps the SAME key for the next press of the same intent; the truth then comes from the server", async () => {
    const ai = aiMock({ post: [new ApiError(0, "network_error", "x"), PENDING] });
    await stopped(ai);
    await userEvent.click(make()!);
    await flush();
    expect(screen.getByTestId("notes-problem")).toHaveTextContent(C.errors.network);
    await userEvent.click(make()!);
    await flush();
    const [k1, k2] = ai.requestNotes.mock.calls.map((c) => c[0] as string);
    expect(k1).toBe(k2);
  });

  it.each([
    [new ApiError(409, "invalid_state", "x"), C.errors.invalid_state],
    [new ApiError(404, "not_found", "x"), C.errors.not_found],
    [new ApiError(500, "http_error", "x"), C.errors.generic],
  ])("other press problems render their calm sentence: %s", async (err, text) => {
    await stopped(aiMock({ post: [err] }));
    await userEvent.click(make()!);
    await flush();
    expect(screen.getByTestId("notes-problem")).toHaveTextContent(text);
    expect(screen.getByTestId("notes-problem")).toHaveFocus();
  });
});

describe("Pending: poll, leave and return (1.5, AC5)", () => {
  it("GET every 3 s for the first minute, then every 10 s; Ready arrives announced once, without moving focus or scroll; unmount stops the poll", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const ai = aiMock({ get: [PENDING] });
    const m = await stopped(ai);
    expect(ai.getNotes).toHaveBeenCalledTimes(1);
    await advance(NOTES_POLL_FAST_MS - 100);
    expect(ai.getNotes).toHaveBeenCalledTimes(1); // never faster than 3 s
    await advance(200);
    expect(ai.getNotes).toHaveBeenCalledTimes(2);
    await advance(60_000);
    const afterMinute = ai.getNotes.mock.calls.length;
    expect(afterMinute).toBeLessThanOrEqual(2 + 20);
    await advance(NOTES_POLL_SLOW_MS * 3);
    expect(ai.getNotes.mock.calls.length - afterMinute).toBeLessThanOrEqual(3);

    const field = screen.getByRole("searchbox", { name: "Search this lecture" });
    field.focus();
    const y = window.scrollY;
    ai.getNotes.mockResolvedValue(READY);
    await advance(NOTES_POLL_SLOW_MS + 50);
    expect(notesSection()).toHaveClass("lecture-sheet");
    expect(document.activeElement).toBe(field);
    expect(window.scrollY).toBe(y);
    expect(m.statusLog.filter((t) => t === C.readyAnnounce)).toHaveLength(1);
    expect(ai.requestNotes).not.toHaveBeenCalled();
    const n = ai.getNotes.mock.calls.length;
    await advance(60_000);
    expect(ai.getNotes.mock.calls.length).toBe(n); // Ready is final: the poll ended
  });

  it("leaving while Pending stops the poll; coming back starts from one fresh GET and shows the true state", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const ai = aiMock({ get: [PENDING] });
    await stopped(ai);
    await advance(NOTES_POLL_FAST_MS + 50);
    cleanup();
    const n = ai.getNotes.mock.calls.length;
    await advance(60_000);
    expect(ai.getNotes.mock.calls.length).toBe(n);
    const back = aiMock({ get: [READY] });
    await stopped(back);
    expect(back.getNotes).toHaveBeenCalledTimes(1);
    expect(notesSection()).toHaveClass("lecture-sheet");
    expect(screen.queryByText(/you left/i)).toBeNull();
  });

  it("409 on the mount GET is 'not pressed yet' (the entry point); 404 shows the calm not-found sentence; a failed GET offers Try again and keeps the press", async () => {
    await stopped(aiMock({ get: [null] }));
    expect(make()).toBeInTheDocument();
    cleanup();

    await stopped(aiMock({ get: [new ApiError(404, "not_found", "x")] }));
    expect(screen.getByTestId("notes-status")).toHaveTextContent(C.errors.not_found);
    expect(make()).toBeNull();
    cleanup();

    const ai = aiMock({ get: [new ApiError(0, "network_error", "x"), at("Unavailable")] });
    await stopped(ai);
    expect(screen.getByTestId("notes-status")).toHaveTextContent(C.checkFailed);
    expect(make()).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: C.checkAgain }));
    await flush();
    expect(ai.getNotes).toHaveBeenCalledTimes(2);
    expect(screen.getByTestId("notes-status")).toHaveTextContent(C.unavailable);
    expect(ai.requestNotes).not.toHaveBeenCalled();
  });
});

describe("Ready (1.4, 1.6, AC6, AC7)", () => {
  it("a lecture sheet: h2, stamp line with coverage and the Demo notes tag, the TL;DR line, then sections; serif h3, sans bullets; cites focus transcript lines", async () => {
    await stopped(aiMock({ get: [READY] }));
    const section = notesSection();
    expect(section).toHaveClass("lecture-sheet");
    expect(section.parentElement).toHaveClass("xl:col-span-2");
    const order = Array.from(section.querySelectorAll("h2, [data-testid], h3")).map((e) => e.getAttribute("data-testid") ?? e.tagName);
    expect(order.slice(0, 5)).toEqual(["H2", "notes-stamp", "demo-notes-tag", "notes-final", "H3"]);
    expect(screen.getByTestId("notes-stamp")).toHaveTextContent("Covers 00:01 to 00:02.");
    const tag = screen.getByTestId("demo-notes-tag");
    expect(tag).toHaveTextContent("Demo notes");
    expect(tag).toHaveTextContent("Made by a test routine, not an AI model.");
    expect(screen.getByTestId("notes-final")).toHaveTextContent(C.finalDone);
    expect(screen.queryByTestId("notes-truncated")).toBeNull();
    const h3 = within(section).getByRole("heading", { level: 3, name: "The opening" });
    expect(h3).toHaveClass("synthesis", "text-xl");
    const bullets = section.querySelectorAll("[data-notes-bullet]");
    expect(bullets).toHaveLength(2);
    for (const li of Array.from(bullets)) {
      expect(li).toHaveClass("hang-row");
      expect(li.querySelector(".synthesis")).toBeNull();
      expect(li.querySelectorAll("button.cite").length).toBeGreaterThanOrEqual(1);
    }
    const cites = within(section).getAllByRole("button", { name: /^\d\d:\d\d, go to transcript line$/ });
    expect(cites.map((c) => c.textContent)).toEqual(["00:01", "00:02", "00:01"]);
    Element.prototype.scrollIntoView = vi.fn(); // not implemented in jsdom
    await userEvent.click(cites[1]!);
    expect(document.activeElement).toBe(document.querySelector('[data-segment-id="seg_2"]'));
    expect(make()).toBeNull(); // Ready is final: no button, ever
  });

  it("truncated: the 'These notes stop at' sentence with coveredToMs; a Final step that did not run keeps the TL;DR line", async () => {
    await stopped(aiMock({ get: [{ ...READY, truncated: true, coveredToMs: 61_000, finalSummaryRevision: null, finalSummaryStatus: "BudgetLimited" }] }));
    expect(screen.getByTestId("notes-truncated")).toHaveTextContent(C.truncated("01:01"));
    expect(screen.getByTestId("notes-final")).toHaveTextContent(C.finalKept);
  });

  it("source model shows no Demo tag; headings and bullets are plain text (markup stays inert)", async () => {
    const evil = '<img src=x onerror="alert(1)"> **b**';
    await stopped(aiMock({ get: [{ ...READY, source: "model", sections: [{ heading: evil, bullets: [{ text: evil, citations: [{ segmentId: "seg_1", startMs: 1000 }] }] }] }] }));
    expect(screen.queryByTestId("demo-notes-tag")).toBeNull();
    expect(notesSection().querySelector("img")).toBeNull();
    expect(within(notesSection()).getByRole("heading", { level: 3 })).toHaveTextContent(evil);
  });

  it("the Final summary: read once over GET /summary when the TL;DR on screen is not Final; the TL;DR row then shows the caption", async () => {
    const ai = aiMock({ get: [READY], summary: FINAL });
    await stopped(ai);
    await flush();
    const tldr = document.getElementById("lecture-tldr")!;
    expect(within(tldr).getByTestId("tldr-final")).toHaveTextContent(TLDR_COPY.final);
    expect(tldr).toHaveTextContent(FINAL.tldr);
    const calls = ai.getSummary.mock.calls.length;
    await flush();
    expect(ai.getSummary.mock.calls.length).toBe(calls); // once
  });

  it("no notes-driven summary read when the hub already delivered the Final revision", async () => {
    const ai = aiMock({ get: [READY] });
    const m = await stopped(ai);
    const before = ai.getSummary.mock.calls.length;
    await act(async () => void m.controls.push(b.summaryEvent(SID, 2, FINAL.tldr, FINAL)));
    await flush();
    expect(within(document.getElementById("lecture-tldr")!).getByTestId("tldr-final")).toBeInTheDocument();
    expect(ai.getSummary.mock.calls.length).toBe(before);
  });

  it("exactly three live regions with Ready notes on screen (status, alert, finals)", async () => {
    const m = await stopped(aiMock({ get: [READY] }), { settings: { announceLines: true } });
    const live = Array.from(m.container.ownerDocument.querySelectorAll('[role="status"],[role="alert"],[role="log"],[aria-live]')).filter(
      (e) => (e.getAttribute("aria-live") ?? (e.getAttribute("role") === "log" ? "off" : "polite")) !== "off",
    );
    expect(live.map((e) => e.getAttribute("data-region"))).toEqual(["status", "alert", "finals"]);
  });
});

describe("TL;DR Final caption (AC9)", () => {
  it("shown only for a Final revision; a Draft has none; it does not expand the collapsed note or move focus", async () => {
    const m = await mountLecture({ events: baseEvents("Stopped", "user"), track: "ended" });
    const tldr = document.getElementById("lecture-tldr")!;
    await act(async () => void m.controls.push(b.summaryEvent(SID, 1, "Draft one.")));
    expect(within(tldr).queryByTestId("tldr-final")).toBeNull();
    const active = document.activeElement;
    await act(async () => void m.controls.push(b.summaryEvent(SID, 2, "Final one.", { status: "Final" })));
    expect(within(tldr).getByTestId("tldr-final")).toHaveTextContent("Final summary");
    expect(document.activeElement).toBe(active);
    const more = within(tldr).queryByRole("button", { name: /More of the TL;DR/ });
    if (more) expect(more).toHaveAttribute("aria-expanded", "false");
  });
});

describe("axe (jsdom; contrast is checked in the browser runs)", () => {
  it.each([
    ["entry", aiMock()],
    ["pending", aiMock({ get: [PENDING] })],
    ["ready", aiMock({ get: [READY] })],
    ["unavailable", aiMock({ get: [at("Unavailable")] })],
    ["not needed", aiMock({ get: [at("NotNeeded")] })],
  ])("%s has no violations", async (_n, ai) => {
    const m = await stopped(ai);
    expect(await axeViolations(m.container)).toEqual([]);
  });
});
