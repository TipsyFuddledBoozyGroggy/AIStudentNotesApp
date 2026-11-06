import { plainText, type LectureNotes, type SummaryRevisionPayload } from "@/lib/lecture";
import { NOTES_EXPORT_COPY } from "./copy";
import { formatMs } from "./format";

const F = NOTES_EXPORT_COPY.file;

/**
 * Notes export without Google (finish-backlog item 2). Pure functions: the Markdown text and its file name are built from
 * what the Notes sheet already shows (the Ready notes, the Final summary revision on screen) plus the lecture's title,
 * course name and start time. No transcript text beyond the notes, no segment ids, no network. Every untrusted string (AI
 * text, the title and course name the student typed) is reduced to one line and escaped, so a Markdown viewer shows it as
 * text: it can not open a link, an image, raw HTML or a heading of its own.
 */
/** What the page knows about the lecture, for the file's heading lines and name (all optional facts). */
export interface NotesExportInfo {
  lectureTitle: string | null;
  courseName: string | null;
  startedAt: string | null;
}

export interface NotesExportInput extends NotesExportInfo {
  notes: LectureNotes;
  /** The summary revision on screen; used only when it is the Final revision this notes press made. */
  summary: SummaryRevisionPayload | null;
  /** IANA zone for the date line and the file name; absent = the browser's own zone. */
  timeZone?: string;
}

/**
 * One line of plain text. First the same guard the screen uses (plainText, C-28: drops disguising control and
 * bidirectional characters), so the file says what the sheet shows; then line breaks, tabs and any remaining control
 * become spaces and runs of spaces collapse.
 */
function oneLine(s: string): string {
  return plainText(s)
    .replace(/[\u0000-\u001f\u007f\u0085\u2028\u2029]/g, " ")
    .replace(/\s+/g, " ")
    .trim();
}

/** A host name after the escapes above (letters, digits, hyphens, dots); its dots get escaped too. */
const HOST = String.raw`[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)*`;
const escDots = (host: string) => host.replace(/\./g, "\\.");

/**
 * Untrusted text as inline Markdown that renders as itself. Backslash-escapes the characters that start emphasis, code,
 * links, images, raw HTML, tables, strikethrough or headings, an entity-looking `&`, and a line-leading list or rule marker.
 *
 * It also breaks the GFM autolink literals (DXB-01), which need no markup at all: the `:` of a `scheme://` URL, the dot
 * after a word-initial `www`, and the `@` of an e-mail address are escaped, and so are the dots of the host that follows,
 * so a renderer that links bare host names (markdown-it's fuzzy linkify) does not find one either. Each escape renders as
 * the same character, so the text reads unchanged. A bare host name in ordinary prose ("example.com") is left alone: GFM
 * does not link it. The escapes break the literal for parsers that tokenise escapes separately (cmark-gfm, micromark,
 * markdown-it); a viewer that merges text before linking could still link, and no such viewer is tested here.
 */
export function mdText(s: string): string {
  return oneLine(s)
    .replace(/[\\`*_[\]<>|~#]/g, "\\$&")
    .replace(/&(?=#?[A-Za-z0-9]+;)/g, "\\&")
    .replace(new RegExp(String.raw`\b([A-Za-z][A-Za-z0-9+.-]*):\/\/(${HOST})?`, "g"), (_m, scheme: string, host = "") => `${scheme}\\://${escDots(host)}`)
    .replace(new RegExp(String.raw`(^|[^A-Za-z0-9])(www)\.(${HOST})?`, "gi"), (_m, pre: string, www: string, host = "") => `${pre}${www}\\.${escDots(host)}`)
    .replace(new RegExp(String.raw`([A-Za-z0-9._+-])@(${HOST})?`, "g"), (_m, last: string, host = "") => `${last}\\@${escDots(host)}`)
    .replace(/^([-+=])/, "\\$1")
    .replace(/^(\d+)([.)])/, "$1\\$2");
}

function dateIn(iso: string | null, timeZone: string | undefined, opts: Intl.DateTimeFormatOptions, locale = "en-US"): Intl.DateTimeFormatPart[] | null {
  if (!iso) return null;
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return null;
  try {
    return new Intl.DateTimeFormat(locale, { ...opts, timeZone }).formatToParts(d);
  } catch {
    // An unknown zone id: fall back to the browser's own zone rather than dropping the date.
    return new Intl.DateTimeFormat(locale, opts).formatToParts(d);
  }
}

/** "November 5, 2026" in the given zone, or null. */
export function lectureDateLabel(iso: string | null, timeZone?: string): string | null {
  const parts = dateIn(iso, timeZone, { year: "numeric", month: "long", day: "numeric" });
  return parts ? parts.map((p) => p.value).join("") : null;
}

/** "2026-11-05" in the given zone, or null. */
function isoDay(iso: string | null, timeZone?: string): string | null {
  const parts = dateIn(iso, timeZone, { year: "numeric", month: "2-digit", day: "2-digit" });
  if (!parts) return null;
  const get = (t: string) => parts.find((p) => p.type === t)?.value;
  const [y, m, d] = [get("year"), get("month"), get("day")];
  return y && m && d ? `${y}-${m}-${d}` : null;
}

const RESERVED = /^(con|prn|aux|nul|com[0-9]|lpt[0-9])(\..*)?$/i;
const MAX_BASE = 80;

/**
 * A safe download name: the student's own title (or "lecture"), " notes", the lecture's day when known, ".md". Strips path
 * separators and every character Windows, macOS or Linux refuse, control and bidirectional-override characters (which can
 * disguise the extension), leading and trailing dots and spaces, and Windows device names. No course name, user or id.
 */
export function notesFileName(lectureTitle: string | null, startedAt: string | null, timeZone?: string): string {
  let base = oneLine((lectureTitle ?? "").normalize("NFC")
    .replace(/[\u200b-\u200f\u202a-\u202e\u2060-\u2069\ufeff]/g, ""))
    .replace(/[<>:"/\\|?*]/g, " ")
    .replace(/\.{2,}/g, ".")
    .replace(/\s+/g, " ")
    .replace(/^[\s.]+|[\s.]+$/g, "");
  base = Array.from(base).slice(0, MAX_BASE).join("").replace(/[\s.]+$/g, "");
  if (!base) base = "lecture";
  if (RESERVED.test(base)) base = `lecture ${base}`;
  const day = isoDay(startedAt, timeZone);
  return `${base} notes${day ? ` ${day}` : ""}.md`;
}

const times = (cs: { startMs: number }[]) => cs.map((c) => formatMs(c.startMs)).join(", ");

/**
 * The Markdown text, or null when there is nothing to export (notes not Ready, or Ready without a section). The summary
 * is included only when this press made a Final summary and the revision on screen is that one (or newer).
 */
export function buildNotesMarkdown(input: NotesExportInput): string | null {
  const { notes, summary } = input;
  if (notes.status !== "Ready" || notes.sections.length === 0) return null;
  const out: string[] = [];
  const title = input.lectureTitle ? mdText(input.lectureTitle) : "";
  out.push(`# ${title || F.untitled}`, "");

  const meta: string[] = [];
  const course = input.courseName ? mdText(input.courseName) : "";
  if (course) meta.push(F.course(course));
  const date = lectureDateLabel(input.startedAt, input.timeZone);
  if (date) meta.push(F.date(date));
  const to = formatMs(notes.coveredToMs ?? 0);
  meta.push(F.covers(formatMs(notes.coveredFromMs ?? 0), to));
  if (notes.truncated) meta.push(F.truncated(to));
  out.push(...meta.map((m) => `- ${m}`), "");

  out.push(notes.source === "mock" ? F.mock : F.model, "", F.local, "", F.times, "");

  out.push(`## ${F.summaryHeading}`, "");
  const finalMade = notes.finalSummaryStatus === "Ready" && notes.finalSummaryRevision !== null;
  const finalOnHand = finalMade && summary !== null && summary.status === "Final" && summary.revision >= notes.finalSummaryRevision!;
  if (finalOnHand) {
    const tldr = mdText(summary.tldr);
    if (tldr) out.push(tldr, "");
    const bullets = summary.bullets.map(mdText).filter(Boolean);
    if (bullets.length) out.push(...bullets.map((b) => `- ${b}`), "");
    if (summary.citations.length) out.push(F.summaryTimes(times(summary.citations)), "");
  } else {
    out.push(finalMade ? F.summaryNotLoaded : F.summaryNotMade, "");
  }

  out.push(`## ${F.notesHeading}`, "");
  for (const s of notes.sections) {
    const heading = mdText(s.heading);
    if (heading) out.push(`### ${heading}`, "");
    for (const b of s.bullets) {
      const text = mdText(b.text);
      const at = b.citations.length ? ` (${times(b.citations)})` : "";
      if (text || at) out.push(`- ${text}${at}`);
    }
    out.push("");
  }
  return out.join("\n").replace(/\n+$/, "\n");
}
