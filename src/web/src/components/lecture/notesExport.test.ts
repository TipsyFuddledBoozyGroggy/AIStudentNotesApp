import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import type { LectureNotes, SummaryRevisionPayload } from "@/lib/lecture";
import { collectStrings, FORBIDDEN, sentencesOver } from "@/lib/copyLint";
import { DEMO_SUMMARY_COPY, NOTES_COPY, NOTES_EXPORT_COPY as X } from "./copy";
import { buildNotesMarkdown, lectureDateLabel, mdText, notesFileName, type NotesExportInput } from "./notesExport";

// Finish-backlog item 2: the Markdown builder and the file name. Synthetic data only. Dates are formatted in an explicit
// zone (Etc/UTC here, L-UTC) so the golden file is the same on a US-zoned dev machine and a UTC runner.

const here = path.dirname(fileURLToPath(import.meta.url));
const GOLDEN = readFileSync(path.join(here, "__fixtures__", "notes-export.golden.md"), "utf8").replace(/\r\n/g, "\n");
const ch = (code: number) => String.fromCharCode(code); // invisible characters built at run time, not typed into source

const READY: LectureNotes = {
  notesId: "nt_0123456789abcdef0123456789abcdef",
  revision: 1,
  status: "Ready",
  source: "mock",
  requestedAt: "2026-11-05T14:02:11Z",
  generatedAt: "2026-11-05T14:02:13Z",
  coveredFromMs: 1000,
  coveredToMs: 3_725_000,
  sections: [
    {
      heading: "Cell membranes",
      bullets: [
        { text: "The membrane is a lipid bilayer.", citations: [{ segmentId: "seg_1", startMs: 1000 }] },
        {
          text: "Proteins move some molecules across it.",
          citations: [
            { segmentId: "seg_2", startMs: 65_000 },
            { segmentId: "seg_1", startMs: 1000 },
          ],
        },
      ],
    },
    {
      heading: "Transport",
      bullets: [{ text: "Diffusion needs no energy; active transport does.", citations: [{ segmentId: "seg_9", startMs: 3_725_000 }] }],
    },
  ],
  finalSummaryRevision: 4,
  finalSummaryStatus: "Ready",
  truncated: false,
};
const FINAL: SummaryRevisionPayload = {
  revision: 4,
  basedOnSeq: "12",
  tldr: "Membranes control what enters a cell.",
  bullets: ["Bilayer structure", "Passive and active transport"],
  coveredFromMs: 0,
  coveredToMs: 3_725_000,
  status: "Final",
  citations: [
    { segmentId: "seg_1", startMs: 1000 },
    { segmentId: "seg_9", startMs: 3_725_000 },
  ],
  source: "mock",
};
const input = (o: Partial<NotesExportInput> = {}): NotesExportInput => ({
  notes: READY,
  summary: FINAL,
  lectureTitle: "Week 3: membranes",
  courseName: "Demo: Biology",
  startedAt: "2026-11-05T13:00:00Z",
  timeZone: "Etc/UTC",
  ...o,
});
const md = (o: Partial<NotesExportInput> = {}) => buildNotesMarkdown(input(o))!;

describe("buildNotesMarkdown", () => {
  it("matches the golden file (title, course, date, Final summary, notes with every citation time, the AI and local lines)", () => {
    expect(md()).toBe(GOLDEN);
  });

  it("keeps every citation time of every bullet, in order, including h:mm:ss past an hour and repeated segments", () => {
    const text = md();
    expect(text).toContain("- The membrane is a lipid bilayer. (00:01)");
    expect(text).toContain("- Proteins move some molecules across it. (01:05, 00:01)");
    expect(text).toContain("- Diffusion needs no energy; active transport does. (1:02:05)");
    expect(text).toContain("Notes cover 00:01 to 1:02:05 of the transcript.");
    expect(text).toContain(X.file.summaryTimes("00:01, 1:02:05"));
    // No segment ids, no notes id and no transcript text beyond the notes.
    expect(text).not.toMatch(/seg_|nt_/);
  });

  it("says plainly that the notes are AI-estimated, per the source field, and that nothing was sent", () => {
    expect(md()).toContain(X.file.mock);
    expect(md()).not.toContain(X.file.model);
    const real = md({ notes: { ...READY, source: "model" } });
    expect(real).toContain(X.file.model);
    expect(real).not.toContain(X.file.mock);
    expect(real).toContain("AI-estimated");
    for (const t of [md(), real]) expect(t).toContain("Nothing was sent anywhere.");
    // The mock line starts with the on-screen Demo notes tag word for word (no new Demo wording).
    expect(X.file.mock.startsWith(`${NOTES_COPY.demoTag}: ${DEMO_SUMMARY_COPY.note}`)).toBe(true);
  });

  it("empty and failed notes states export nothing", () => {
    for (const status of ["Pending", "Unavailable", "BudgetLimited", "NotNeeded"] as const) {
      expect(buildNotesMarkdown(input({ notes: { ...READY, status, sections: [] } })), status).toBeNull();
    }
    expect(buildNotesMarkdown(input({ notes: { ...READY, sections: [] } }))).toBeNull();
    // A Ready payload with a status other than Ready is never exported, even if sections are present.
    expect(buildNotesMarkdown(input({ notes: { ...READY, status: "Unavailable" } }))).toBeNull();
  });

  it("Final summary: included only when this press made it and the revision on hand is that one; otherwise a plain line", () => {
    expect(md()).toContain("## Final summary\n\nMembranes control what enters a cell.\n\n- Bilayer structure\n- Passive and active transport");
    const notMade = md({ notes: { ...READY, finalSummaryStatus: "Unavailable", finalSummaryRevision: null } });
    expect(notMade).toContain(X.file.summaryNotMade);
    expect(notMade).not.toContain("Membranes control");
    for (const summary of [null, { ...FINAL, status: "Draft" as const }, { ...FINAL, revision: 3 }]) {
      const t = md({ summary });
      expect(t).toContain(X.file.summaryNotLoaded);
      expect(t).not.toContain("Membranes control");
    }
  });

  it("missing facts drop their lines; no title gives the untitled heading", () => {
    const t = md({ lectureTitle: null, courseName: null, startedAt: null });
    expect(t.startsWith(`# ${X.file.untitled}\n`)).toBe(true);
    expect(t).not.toContain("Course:");
    expect(t).not.toContain("Lecture date:");
    expect(md({ lectureTitle: "   " }).startsWith(`# ${X.file.untitled}\n`)).toBe(true);
    expect(md({ startedAt: "not a date" })).not.toContain("Lecture date:");
  });

  it("truncated notes say where they stop", () => {
    expect(md({ notes: { ...READY, truncated: true } })).toContain(X.file.truncated("1:02:05"));
  });

  it("odd characters in titles and AI text stay text: no heading, link, image, HTML, code or list of their own", () => {
    const t = md({
      lectureTitle: "# <script>alert(1)</script> [x](javascript:alert(1)) ![i](http://e/x.png)\n## injected",
      courseName: "Bio | 101 *core* _a_ `b` ~c~ &amp;",
      notes: {
        ...READY,
        sections: [
          {
            heading: "1. Intro\r\n---",
            bullets: [{ text: "- nested\nline <img src=x onerror=alert(1)> \\ end", citations: [{ segmentId: "s", startMs: 0 }] }],
          },
        ],
      },
    });
    const lines = t.split("\n");
    expect(lines[0]).toBe("# \\# \\<script\\>alert(1)\\</script\\> \\[x\\](javascript:alert(1)) !\\[i\\](http://e/x.png) \\#\\# injected");
    expect(lines).toContain("- Course: Bio \\| 101 \\*core\\* \\_a\\_ \\`b\\` \\~c\\~ \\&amp;");
    expect(lines).toContain("### 1\\. Intro ---");
    expect(lines).toContain("- \\- nested line \\<img src=x onerror=alert(1)\\> \\\\ end (00:00)");
    // Exactly one h1, and every heading line is one the builder wrote.
    expect(lines.filter((l) => l.startsWith("# "))).toHaveLength(1);
    expect(lines.filter((l) => /^#{1,6} /.test(l)).map((l) => l.split(" ")[0])).toEqual(["#", "##", "##", "###"]);
    expect(t).not.toMatch(/(^|[^\\])<[a-z/]/i);
  });

  it("control and line-separator characters never split a line; disguising characters are dropped as on screen (plainText)", () => {
    // NUL and C1 NEL are dropped (as the sheet's PlainText does); LS, CR, LF and tab become spaces.
    const t = md({ lectureTitle: `A${ch(0)}B${ch(0x2028)}C${ch(0x85)}D${ch(9)}E${ch(13)}${ch(10)}F` });
    expect(t.split("\n")[0]).toBe("# AB CD E F");
    // A right-to-left override in AI text can not reorder the line in a viewer.
    const rlo = md({ notes: { ...READY, sections: [{ heading: `H${ch(0x202e)}eh`, bullets: [{ text: `x${ch(0x2066)}y`, citations: [{ segmentId: "s", startMs: 0 }] }] }] } });
    expect(rlo).toContain("### Heh\n");
    expect(rlo).toContain("- xy (00:00)");
  });
});

describe("mdText", () => {
  it.each([
    ["plain words stay as they are", "Plain words, with (brackets) and a full stop.", "Plain words, with (brackets) and a full stop."],
    ["Q&A is not an entity", "Q&A", "Q&A"],
    ["an entity-looking & is escaped", "&lt;b&gt;", "\\&lt;b\\&gt;"],
    ["a leading + or = is escaped", "+ plus", "\\+ plus"],
    ["a leading ordered-list marker is escaped", "2) second", "2\\) second"],
  ])("%s", (_n, inText, out) => {
    expect(mdText(inText)).toBe(out);
  });
});

describe("lectureDateLabel (L-UTC)", () => {
  it("uses the zone it is given, so a late-evening US lecture is the next day in UTC", () => {
    expect(lectureDateLabel("2026-11-06T03:30:00Z", "Etc/UTC")).toBe("November 6, 2026");
    expect(lectureDateLabel("2026-11-06T03:30:00Z", "America/New_York")).toBe("November 5, 2026");
    expect(lectureDateLabel("2026-11-06T03:30:00Z", "UTC")).toBe("November 6, 2026");
  });
  it("an unknown zone falls back to the browser's zone instead of throwing", () => {
    expect(lectureDateLabel("2026-11-05T12:00:00Z", "Not/AZone")).toMatch(/^November [45], 2026$/);
  });
});

describe("notesFileName", () => {
  it("title, notes, the lecture's day, .md", () => {
    expect(notesFileName("Week 3: membranes", "2026-11-05T13:00:00Z", "Etc/UTC")).toBe("Week 3 membranes notes 2026-11-05.md");
    expect(notesFileName("Demo lecture (synthetic replay)", "2026-11-06T03:30:00Z", "Etc/UTC")).toBe("Demo lecture (synthetic replay) notes 2026-11-06.md");
  });
  it("strips path separators and reserved characters, so the name can not leave the downloads folder", () => {
    const n = notesFileName("../../etc/passwd", null);
    expect(n).toBe("etc passwd notes.md");
    expect(notesFileName('a\\b/c:d*e?f"g<h>i|j', null)).toBe("a b c d e f g h i j notes.md");
    for (const t of ["../x", "..\\x", "C:\\Users\\x", "/abs", "a/../../b"]) expect(notesFileName(t, null)).not.toMatch(/[\\/:]|\.\./);
  });
  it("no title, only dots or spaces, Windows device names, control and direction-override characters", () => {
    expect(notesFileName(null, null)).toBe("lecture notes.md");
    expect(notesFileName(" ... ", null)).toBe("lecture notes.md");
    expect(notesFileName("CON", null)).toBe("lecture CON notes.md");
    expect(notesFileName("lpt1.txt", null)).toBe("lecture lpt1.txt notes.md");
    // U+202E (right-to-left override) can make "notes.md" display as "dm.seton"; it is removed, as are controls.
    expect(notesFileName(`evil${ch(0x202e)}gpj.exe`, null)).toBe("evilgpj.exe notes.md");
    expect(notesFileName(`a${ch(0)}b${ch(10)}c`, null)).toBe("ab c notes.md");
    expect(notesFileName(`a${ch(0x200b)}b${ch(0xfeff)}c`, null)).toBe("abc notes.md"); // zero-width characters
  });
  it("keeps non-Latin titles and caps the length at 80 characters before the suffix", () => {
    expect(notesFileName("Biología célula", null)).toBe("Biología célula notes.md");
    const long = notesFileName("x".repeat(300), null);
    expect(long).toBe(`${"x".repeat(80)} notes.md`);
  });
  it("carries no course name, user or id", () => {
    expect(notesFileName("Week 3", "2026-11-05T13:00:00Z", "Etc/UTC")).toBe("Week 3 notes 2026-11-05.md");
  });
});

describe("NOTES_EXPORT_COPY", () => {
  const strings = collectStrings(X);
  it.each(FORBIDDEN)("no %s", (_n, re) => {
    expect(strings.filter((s) => re.test(s))).toEqual([]);
  });
  it("sentences stay under 20 words and avoid the notes block's hand-check words", () => {
    expect(sentencesOver(strings, 20)).toEqual([]);
    expect(strings.filter((s) => /\b(still|just|right now|behind|missed|lost)\b/i.test(s))).toEqual([]);
  });
});
