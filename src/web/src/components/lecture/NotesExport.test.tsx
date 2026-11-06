import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { LectureNotes, SummaryRevisionPayload } from "@/lib/lecture";
import { axeViolations } from "@/test/axe";
import { NOTES_EXPORT_COPY as X } from "./copy";
import { buildNotesMarkdown } from "./notesExport";
import type { LectureAi } from "./ports";
import { baseEvents, mountLecture } from "./testUtils";

// Finish-backlog item 2: Export notes / Copy notes on the Ready notes sheet. Harness runtime, synthetic data, no network.

const READY: LectureNotes = {
  notesId: "nt_0123456789abcdef0123456789abcdef",
  revision: 1,
  status: "Ready",
  source: "mock",
  requestedAt: "2026-11-05T14:02:11Z",
  generatedAt: "2026-11-05T14:02:13Z",
  coveredFromMs: 1000,
  coveredToMs: 2000,
  sections: [
    {
      heading: "The opening",
      bullets: [
        { text: "The lecture opened with its first line.", citations: [{ segmentId: "seg_1", startMs: 1000 }] },
        { text: "A second line followed it.", citations: [{ segmentId: "seg_2", startMs: 2000 }, { segmentId: "seg_1", startMs: 1000 }] },
      ],
    },
  ],
  finalSummaryRevision: 2,
  finalSummaryStatus: "Ready",
  truncated: false,
};
const FINAL: SummaryRevisionPayload = {
  revision: 2,
  basedOnSeq: "3",
  tldr: "The final summary of the lecture.",
  bullets: [],
  coveredFromMs: 0,
  coveredToMs: 2000,
  status: "Final",
  citations: [],
  source: "mock",
};
const INFO = { lectureTitle: "Week 3: membranes", courseName: "Demo: Biology", startedAt: "2026-11-05T15:00:00Z" };

function aiMock(notes: LectureNotes | null) {
  return {
    getSummary: vi.fn().mockResolvedValue(FINAL),
    startQuickRecap: vi.fn(),
    getCatchUp: vi.fn().mockResolvedValue(null),
    getNotes: vi.fn().mockResolvedValue(notes),
    requestNotes: vi.fn(),
  } as unknown as LectureAi & Record<"getSummary" | "getNotes" | "requestNotes" | "startQuickRecap" | "getCatchUp", ReturnType<typeof vi.fn>>;
}

const mount = (ai: LectureAi, o: { info?: typeof INFO | null } = {}) =>
  mountLecture({ events: baseEvents("Stopped", "user"), track: "ended", ai, route: { notesExport: o.info === null ? undefined : (o.info ?? INFO) } });

let blobs: Blob[];
let clicks: { download: string; href: string }[];
const origCreate = URL.createObjectURL;
const origRevoke = URL.revokeObjectURL;
beforeEach(() => {
  blobs = [];
  clicks = [];
  URL.createObjectURL = ((b: Blob) => {
    blobs.push(b);
    return "blob:http://localhost/test";
  }) as typeof URL.createObjectURL;
  URL.revokeObjectURL = () => undefined;
  vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(function (this: HTMLAnchorElement) {
    clicks.push({ download: this.download, href: this.href });
  });
});
afterEach(() => {
  URL.createObjectURL = origCreate;
  URL.revokeObjectURL = origRevoke;
  vi.restoreAllMocks();
  Object.defineProperty(navigator, "clipboard", { value: undefined, configurable: true });
});

const exportBtn = () => screen.getByRole("button", { name: X.exportButton });
const copyBtn = () => screen.getByRole("button", { name: X.copyButton });
const readySheet = async () => {
  await waitFor(() => expect(document.querySelector("#lecture-notes.lecture-sheet")).not.toBeNull());
  // The Final summary is read over REST once; wait until the TL;DR shows it so the file can include it.
  await waitFor(() => expect(screen.getByTestId("tldr-final")).toBeInTheDocument());
};

describe("Export notes / Copy notes on the Ready sheet", () => {
  it("shows both controls, described by the plain help line, inside the notes sheet; not before notes exist", async () => {
    await mount(aiMock(READY));
    await readySheet();
    const sheet = document.getElementById("lecture-notes")!;
    expect(within(sheet).getByRole("button", { name: X.exportButton })).toHaveClass("btn-secondary"); // 44px min height (.btn)
    expect(within(sheet).getByRole("button", { name: X.copyButton })).toHaveClass("btn-tertiary");
    expect(exportBtn()).toHaveAccessibleDescription(X.help);
    expect(copyBtn()).toHaveAccessibleDescription(X.help);
    expect(X.help).toContain("Nothing is sent anywhere.");
    // The notes block keeps a single filled button in the column (Make notes is gone once Ready).
    expect(sheet.querySelectorAll(".btn-primary")).toHaveLength(0);
  });

  it.each([null, { ...READY, status: "Pending" as const, sections: [] }, { ...READY, status: "Unavailable" as const, sections: [] }])(
    "no export controls without Ready notes (%#)",
    async (notes) => {
      await mount(aiMock(notes));
      await waitFor(() => expect(document.getElementById("lecture-notes")).not.toBeNull());
      expect(screen.queryByRole("button", { name: X.exportButton })).toBeNull();
      expect(screen.queryByRole("button", { name: X.copyButton })).toBeNull();
    },
  );

  it("Export notes (keyboard): one Markdown download with a safe name and the same text the builder makes, said once; no request", async () => {
    const ai = aiMock(READY);
    const m = await mount(ai);
    await readySheet();
    const gets = ai.getNotes.mock.calls.length;
    const sums = ai.getSummary.mock.calls.length;
    exportBtn().focus();
    await userEvent.keyboard("{Enter}");
    expect(blobs).toHaveLength(1);
    expect(blobs[0]!.type).toBe("text/markdown;charset=utf-8");
    expect(clicks).toHaveLength(1);
    expect(clicks[0]!.download).toMatch(/^Week 3 membranes notes 2026-11-0[45]\.md$/);
    const text = await blobs[0]!.text();
    expect(text).toBe(buildNotesMarkdown({ notes: READY, summary: FINAL, ...INFO }));
    expect(text).toContain("# Week 3: membranes");
    expect(text).toContain("- Course: Demo: Biology");
    expect(text).toContain("The final summary of the lecture.");
    expect(text).toContain("- A second line followed it. (00:02, 00:01)");
    expect(text).toContain(X.file.mock);
    expect(text).not.toContain("First line of the lecture."); // transcript text is not in the file
    await waitFor(() => expect(m.statusLog).toContain(X.saved));
    expect(m.statusLog.filter((s) => s === X.saved)).toHaveLength(1);
    expect(screen.getByTestId("notes-export-result")).toHaveTextContent(X.saved);
    expect(exportBtn()).toHaveFocus();
    expect(ai.requestNotes).not.toHaveBeenCalled();
    expect(ai.getNotes.mock.calls.length).toBe(gets);
    expect(ai.getSummary.mock.calls.length).toBe(sums);
  });

  it("Copy notes: the same Markdown on the clipboard, said once", async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, "clipboard", { value: { writeText }, configurable: true });
    const m = await mount(aiMock(READY));
    await readySheet();
    await userEvent.click(copyBtn());
    await waitFor(() => expect(m.statusLog).toContain(X.copied));
    expect(writeText).toHaveBeenCalledTimes(1);
    expect(writeText.mock.calls[0]![0]).toBe(buildNotesMarkdown({ notes: READY, summary: FINAL, ...INFO }));
    expect(blobs).toHaveLength(0);
    expect(copyBtn()).toHaveFocus();
  });

  it("a browser that refuses the download or the clipboard: a calm note, focused, nothing claimed", async () => {
    const m = await mount(aiMock(READY));
    await readySheet();
    // @ts-expect-error simulating a browser without Blob URLs
    URL.createObjectURL = undefined;
    await userEvent.click(exportBtn());
    const p = await screen.findByTestId("notes-export-problem");
    expect(p).toHaveTextContent(X.saveFailed);
    await waitFor(() => expect(p).toHaveFocus());
    expect(m.statusLog).not.toContain(X.saved);

    Object.defineProperty(navigator, "clipboard", { value: { writeText: vi.fn().mockRejectedValue(new Error("denied")) }, configurable: true });
    await userEvent.click(copyBtn());
    await waitFor(() => expect(screen.getByTestId("notes-export-problem")).toHaveTextContent(X.copyFailed));
    await waitFor(() => expect(screen.getByTestId("notes-export-problem")).toHaveFocus());
    expect(m.statusLog).not.toContain(X.copied);
  });

  it("without lecture facts (the live route's untitled case) the file still exports with the untitled heading", async () => {
    await mount(aiMock(READY), { info: null });
    await readySheet();
    await userEvent.click(exportBtn());
    expect(clicks[0]!.download).toBe("lecture notes.md");
    expect(await blobs[0]!.text()).toMatch(new RegExp(`^# ${X.file.untitled}\\n`));
  });

  it("no axe violations on the Ready sheet with the export controls and a result note", async () => {
    const m = await mount(aiMock(READY));
    await readySheet();
    await userEvent.click(exportBtn());
    expect(await axeViolations(m.container)).toEqual([]);
  });
});
