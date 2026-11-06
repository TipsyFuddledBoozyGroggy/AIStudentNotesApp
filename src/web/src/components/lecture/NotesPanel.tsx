"use client";

import { useEffect, useRef, useState } from "react";
import { copyText, downloadTextFile } from "@/lib/download";
import { PlainText, type Citation, type LectureNotes, type SummaryRevisionPayload } from "@/lib/lecture";
import { useAnnouncer } from "./Announcer";
import { NOTES_COPY as C, NOTES_EXPORT_COPY as X } from "./copy";
import { formatMs } from "./format";
import { buildNotesMarkdown, notesFileName, type NotesExportInfo } from "./notesExport";
import type { LectureAi } from "./ports";
import { DemoNotesTag } from "./QuickRecapPanel";
import { canPressAgain, useNotes } from "./useNotes";

export interface NotesPanelProps {
  ai: LectureAi;
  /** The TL;DR revision on screen (reducer state). */
  summary: SummaryRevisionPayload | null;
  /** Feeds a fetched Final summary revision into the reducer (the same path a hub summary.revision takes). */
  onSummary(payload: SummaryRevisionPayload): void;
  onCite(segmentId: string): void;
  /** Title, course and start time for Export notes / Copy notes. Absent: the file has the untitled heading and no date. */
  exportInfo?: NotesExportInfo;
}

const NO_INFO: NotesExportInfo = { lectureTitle: null, courseName: null, startedAt: null };

type ExportResult = null | "saved" | "copied" | "saveFailed" | "copyFailed";

/**
 * Export notes / Copy notes (finish-backlog item 2). Both act on the student's own press only and use what is already on
 * the client: no request, no transcript beyond the notes. The result is said once through the existing status region (no
 * new live region) and shown as plain text; a press that did not work moves focus to its note (the ActionProblem pattern).
 */
function NotesExport({ notes, summary, info }: { notes: LectureNotes; summary: SummaryRevisionPayload | null; info: NotesExportInfo }) {
  const announcer = useAnnouncer();
  const [result, setResult] = useState<ExportResult>(null);
  const [seq, setSeq] = useState(0);
  const problemRef = useRef<HTMLParagraphElement>(null);
  const failed = result === "saveFailed" || result === "copyFailed";
  useEffect(() => {
    if (failed) problemRef.current?.focus();
  }, [failed, seq]);

  const text = () => buildNotesMarkdown({ notes, summary, ...info });
  const done = (r: Exclude<ExportResult, null>) => {
    setResult(r);
    setSeq((n) => n + 1);
    if (r === "saved" || r === "copied") announcer.status(X[r]);
  };
  const save = () => {
    const t = text();
    done(t !== null && downloadTextFile(t, notesFileName(info.lectureTitle, info.startedAt), "text/markdown;charset=utf-8") ? "saved" : "saveFailed");
  };
  const copy = async () => {
    const t = text();
    done(t !== null && (await copyText(t)) ? "copied" : "copyFailed");
  };

  return (
    <div className="mt-8" data-testid="notes-export">
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
        <button type="button" className="btn-secondary" aria-describedby="notes-export-help" onClick={save}>
          {X.exportButton}
        </button>
        <button type="button" className="btn-tertiary" aria-describedby="notes-export-help" onClick={() => void copy()}>
          {X.copyButton}
        </button>
      </div>
      <p id="notes-export-help" className="help mt-2 max-w-prose">
        {X.help}
      </p>
      {failed ? (
        <p ref={problemRef} tabIndex={-1} data-testid="notes-export-problem" className="note note-notice mt-3 max-w-prose">
          {X[result]}
        </p>
      ) : result ? (
        <p data-testid="notes-export-result" className="mt-2 max-w-prose text-sm text-muted">
          {X[result]}
        </p>
      ) : null}
    </div>
  );
}

/** Citation time as a link into the transcript (the recap's pattern and name). */
function Cite({ c, onCite }: { c: Citation; onCite(segmentId: string): void }) {
  return (
    <button type="button" className="cite" aria-label={`${formatMs(c.startMs)}, go to transcript line`} onClick={() => onCite(c.segmentId)}>
      {formatMs(c.startMs)}
    </button>
  );
}

function ReadySheet({
  notes,
  onCite,
  summary,
  info,
}: {
  notes: LectureNotes;
  onCite(segmentId: string): void;
  summary: SummaryRevisionPayload | null;
  info: NotesExportInfo;
}) {
  const to = formatMs(notes.coveredToMs ?? 0);
  return (
    <>
      <p className="stamp-line mt-1" data-testid="notes-stamp">
        <span>{C.covers(formatMs(notes.coveredFromMs ?? 0), to)}</span>
        {notes.source === "mock" ? <DemoNotesTag /> : null}
      </p>
      {notes.truncated ? (
        <p data-testid="notes-truncated" className="mt-2 max-w-prose text-sm text-muted">
          {C.truncated(to)}
        </p>
      ) : null}
      <p data-testid="notes-final" className="mt-2 max-w-prose text-sm text-muted">
        {notes.finalSummaryStatus === "Ready" && notes.finalSummaryRevision !== null ? C.finalDone : C.finalKept}
      </p>
      <div className="mt-6 space-y-6">
        {notes.sections.map((s, i) => (
          <div key={i} data-notes-section>
            {/* AI headings are synthesis (PO-17a): the serif; bullets stay in the verbatim face (spec 1.6). */}
            <h3 className="synthesis text-xl">
              <PlainText value={s.heading} />
            </h3>
            <ul className="mt-2 space-y-2">
              {s.bullets.map((bl, j) => (
                <li key={j} className="hang-row" data-notes-bullet>
                  <span className="flex flex-col items-start">
                    <Cite c={bl.citations[0]!} onCite={onCite} />
                  </span>
                  <span className="max-w-prose">
                    <PlainText value={bl.text} />
                    {bl.citations.length > 1 ? (
                      <span className="ml-2 inline-flex flex-wrap gap-x-2 align-baseline">
                        {bl.citations.slice(1).map((c, k) => (
                          <Cite key={`${c.segmentId}-${k}`} c={c} onCite={onCite} />
                        ))}
                      </span>
                    ) : null}
                  </span>
                </li>
              ))}
            </ul>
          </div>
        ))}
      </div>
      {notes.sections.length > 0 ? <NotesExport notes={notes} summary={summary} info={info} /> : null}
    </>
  );
}

/**
 * The Notes block after Stop (PO-18d option B; docs/ux/make-notes-rail-demo-notice.md section 1). Rendered by LectureScreen
 * only while the server state is Stopped. Before a press: the DRAFT disclosure sentence, the one filled "Make notes" and
 * "What is sent?". Pending: one sentence. Ready: a lecture sheet with cited bullets. Nothing here is automatic: the only
 * POST is the click handler of "Make notes". Adds no live region: announcements use the existing status region.
 */
export function NotesPanel({ ai, summary, onSummary, onCite, exportInfo = NO_INFO }: NotesPanelProps) {
  const n = useNotes(ai);
  const [whatOpen, setWhatOpen] = useState(false);
  const headingRef = useRef<HTMLHeadingElement>(null);
  const problemRef = useRef<HTMLParagraphElement>(null);
  const { focus, clearFocus } = n;

  // Focus moves only after the student's own press (spec 1.7); poll arrivals never move focus or scroll.
  useEffect(() => {
    if (!focus) return;
    const el = focus === "heading" ? headingRef.current : problemRef.current;
    if (el) {
      el.focus({ preventScroll: false });
      clearFocus();
    }
  });

  // Ready with a Final summary: read it once over REST unless the TL;DR on screen is already that revision (the hub may
  // have delivered it to this joined connection). A failure is silent: the TL;DR keeps the revision it shows.
  const notes = n.view.kind === "notes" ? n.view.notes : null;
  const finalRev = notes?.status === "Ready" && notes.finalSummaryStatus === "Ready" ? notes.finalSummaryRevision : null;
  const haveFinal = summary !== null && summary.status === "Final" && finalRev !== null && summary.revision >= finalRev;
  const fetchedFor = useRef<string | null>(null);
  const notesId = notes?.notesId ?? null;
  useEffect(() => {
    if (finalRev === null || haveFinal || !notesId || fetchedFor.current === notesId) return;
    fetchedFor.current = notesId;
    ai.getSummary()
      .then((p) => {
        if (p) onSummary(p);
      })
      .catch(() => undefined);
  }, [finalRev, haveFinal, notesId, ai, onSummary]);

  if (!n.enabled) return null;
  const v = n.view;
  if (v.kind === "checking") return null; // one GET in flight; nothing is offered before the truth is known

  const ready = notes?.status === "Ready";
  const offer = v.kind === "none" || v.kind === "checkFailed" || (notes !== null && canPressAgain(notes));
  const statusNote =
    v.kind === "notFound"
      ? C.errors.not_found
      : v.kind === "checkFailed"
        ? C.checkFailed
        : notes?.status === "Unavailable"
          ? C.unavailable
          : notes?.status === "BudgetLimited"
            ? C.budget
            : null;

  // One tree for every state: the wrapper, the section, the head and the h2 stay the same elements when Pending turns into
  // Ready (only classes change), so a focused "Notes" heading keeps focus when the poll brings the notes (spec 1.7: system
  // arrivals never move focus; a remounted heading would drop it to <body>).
  return (
    <div className={`min-w-0 ${ready ? "xl:col-span-2" : "xl:col-start-2"}`}>
      <section id="lecture-notes" aria-labelledby="notes-h2" data-notes-status={notes?.status ?? v.kind} className={ready ? "lecture-sheet min-w-0" : "min-w-0"}>
        <div className={ready ? "sheet-head" : undefined}>
          <h2 id="notes-h2" ref={headingRef} tabIndex={-1} className="display text-xl">
            {C.heading}
          </h2>
        </div>
        {ready && notes ? (
          <div key="ready" className="sheet-body">
            <ReadySheet notes={notes} onCite={onCite} summary={summary} info={exportInfo} />
          </div>
        ) : (
          <div key="block">
            {statusNote ? (
              <div data-testid="notes-status" className="note note-notice mt-2 max-w-prose">
                <p>{statusNote}</p>
                {v.kind === "checkFailed" ? (
                  <button type="button" className="btn-tertiary btn-sm optical-start mt-1" onClick={() => void n.recheck()}>
                    {C.checkAgain}
                  </button>
                ) : null}
              </div>
            ) : null}
            {notes?.status === "Pending" ? (
              <p data-testid="notes-pending" className="mt-2 max-w-prose">
                {C.pending}
              </p>
            ) : null}
            {notes?.status === "NotNeeded" ? (
              <p data-testid="notes-not-needed" className="mt-2 max-w-prose">
                {C.notNeeded}
              </p>
            ) : null}
            {offer ? (
              <div>
                <p id="notes-intro" className="mt-2 max-w-prose">
                  {C.intro}
                </p>
                <div className="mt-3 flex flex-wrap items-center gap-x-4 gap-y-2">
                  <button
                    type="button"
                    className="btn-primary btn-steady"
                    aria-describedby="notes-intro"
                    aria-busy={n.busy ? true : undefined}
                    onClick={() => void n.press()}
                  >
                    {C.make}
                  </button>
                  <button type="button" className="btn-tertiary" aria-expanded={whatOpen} aria-controls="notes-what-body" onClick={() => setWhatOpen((o) => !o)}>
                    {C.whatIsSent}
                  </button>
                </div>
                {n.problem ? (
                  <p ref={problemRef} tabIndex={-1} data-testid="notes-problem" className="note note-notice mt-3 max-w-prose">
                    {C.errors[n.problem]}
                  </p>
                ) : null}
                <p id="notes-what-body" className="mt-2 max-w-prose text-sm text-muted" hidden={!whatOpen}>
                  {C.whatIsSentBody}
                </p>
              </div>
            ) : null}
          </div>
        )}
      </section>
    </div>
  );
}
