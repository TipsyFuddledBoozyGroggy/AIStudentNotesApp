import { screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { LectureNotes } from "@/lib/lecture";
import type { LectureAi } from "../lecture/ports";
import { baseEvents, mountLecture } from "../lecture/testUtils";
import { DOCS_COPY as C } from "./copy";

// DX-F06 through the real mount point: the Notes sheet of a Stopped lecture on its own page (/lectures/[sessionId]) shows
// Save to a Demo Doc after Export notes and Copy notes only when the notes are Ready; Pending shows nothing of it.

const SID = "0192f0a1-b2c3-4d5e-8f9a-0b1c2d3e4f01";
vi.mock("next/navigation", () => ({ useParams: () => ({ sessionId: SID }), useRouter: () => ({ replace: vi.fn(), push: vi.fn() }) }));

const NOTES: LectureNotes = {
  notesId: "nt_0192f0a1b2c34d5e8f9a0b1c2d3e4f52",
  revision: 1,
  status: "Ready",
  source: "mock",
  requestedAt: "2026-11-05T15:02:11Z",
  generatedAt: "2026-11-05T15:02:13Z",
  coveredFromMs: 1000,
  coveredToMs: 2000,
  sections: [{ heading: "The opening", bullets: [{ text: "The lecture opened.", citations: [{ segmentId: "seg_1", startMs: 1000 }] }] }],
  finalSummaryRevision: null,
  finalSummaryStatus: "Ready",
  truncated: false,
};

const ai = (notes: LectureNotes) =>
  ({
    getSummary: vi.fn().mockResolvedValue(null),
    startQuickRecap: vi.fn(),
    getCatchUp: vi.fn().mockResolvedValue(null),
    getNotes: vi.fn().mockResolvedValue(notes),
    requestNotes: vi.fn(),
  }) as unknown as LectureAi;

describe("Notes sheet mount (F06)", () => {
  it("Ready: the button follows Export notes and Copy notes, with its help line", async () => {
    await mountLecture({ events: baseEvents("Stopped", "user"), track: "ended", ai: ai(NOTES) });
    await waitFor(() => expect(screen.getByRole("button", { name: C.entry.button })).toBeInTheDocument());
    const names = [...document.querySelectorAll("#lecture-notes button")].map((b) => b.textContent);
    const i = names.indexOf(C.entry.button);
    expect(names.slice(i - 2, i + 1)).toEqual(["Export notes", "Copy notes", C.entry.button]);
    expect(screen.getByRole("button", { name: C.entry.button })).toHaveAccessibleDescription(C.entry.help);
  });

  it("Pending: no Demo Doc button", async () => {
    await mountLecture({ events: baseEvents("Stopped", "user"), track: "ended", ai: ai({ ...NOTES, status: "Pending", source: null, sections: [], finalSummaryStatus: null }) });
    await waitFor(() => expect(screen.getByTestId("notes-pending")).toBeInTheDocument());
    expect(screen.queryByRole("button", { name: C.entry.button })).toBeNull();
  });
});
