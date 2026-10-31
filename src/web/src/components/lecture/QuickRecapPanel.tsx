"use client";

import { useEffect, useRef, useState } from "react";
import { loadUsualWindow, saveUsualWindow, type UsualWindow } from "./settings";
import { PlainText, type CatchUpPayload, type Citation, type ClaimBasis, type WindowMinutes } from "@/lib/lecture";
import { DEMO_SUMMARY_COPY, NOTES_COPY, QUICK_RECAP_COPY as C } from "./copy";
import { formatMs } from "./format";
import type { LectureAi } from "./ports";
import type { QuickRecap } from "./useQuickRecap";

const WINDOWS: WindowMinutes[] = [2, 5, 10];

/**
 * Demo summary stamp (slice 2 Stamp): the tag beside its sentence. Wording and test id unchanged; shown wherever text came
 * from the deterministic mock, never hidden or dismissible. Sits inside a `.stamp-line` so the sentence is sm muted.
 */
// Presentation unchanged from before the redesign (PO-17b / gate G1: Demo-source labels are not restyled). One class string
// for both Demo tags, so the notes tag (slice 8a) is the Demo summary presentation by construction.
export const DEMO_TAG_CLASS = "inline-block rounded-control border border-warning bg-warning-subtle px-2.5 py-1 text-xs font-bold text-warning";

export function DemoSummaryTag({ className = "" }: { className?: string }) {
  // A span, so it nests inside the stamp line paragraph; inline-block keeps the old box.
  return (
    <span data-testid="demo-summary-tag" className={`${DEMO_TAG_CLASS} ${className}`}>
      {DEMO_SUMMARY_COPY.tag}
      <span className="font-normal">: {DEMO_SUMMARY_COPY.note}</span>
    </span>
  );
}

/**
 * Notes made by the deterministic mock (source === "mock", PO-18d): the Demo summary presentation with the word "Demo notes"
 * (Lead answer 2, a DRAFT Demo word, G1) and the unchanged sentence. Never hidden or dismissible.
 */
export function DemoNotesTag({ className = "" }: { className?: string }) {
  return (
    <span data-testid="demo-notes-tag" className={`${DEMO_TAG_CLASS} ${className}`}>
      {NOTES_COPY.demoTag}
      <span className="font-normal">: {DEMO_SUMMARY_COPY.note}</span>
    </span>
  );
}

/**
 * Citations as link-style timestamps (2.7 Link): accent, tabular, underlined, 44px hit area. In `lead` mode they form the
 * lead column of a hanging row (`.hang-row`), like the transcript's timestamp column, and hang in the margin from 1280.
 */
function Cites({ citations, onCite, lead = false }: { citations: Citation[]; onCite(segmentId: string): void; lead?: boolean }) {
  if (citations.length === 0) return lead ? <span aria-hidden="true" /> : null;
  return (
    <span className={lead ? "flex flex-col items-start" : "ml-1 inline-flex flex-wrap gap-x-2 align-baseline"}>
      {citations.slice(0, 4).map((c) => (
        <button key={`${c.segmentId}-${c.startMs}`} type="button" className="cite" aria-label={`${formatMs(c.startMs)}, go to transcript line`} onClick={() => onCite(c.segmentId)}>
          {formatMs(c.startMs)}
        </button>
      ))}
    </span>
  );
}

function BasisTag({ basis }: { basis: ClaimBasis }) {
  const label = C.basis[basis];
  return label ? <span className="tag tag-neutral ml-2 align-middle">{label}</span> : null;
}

function noticeFor(r: CatchUpPayload, wait: "none" | "waiting" | "slow"): string | null {
  if (r.kind === "Ai") return null;
  if (wait === "waiting") return C.pending;
  if (wait === "slow") return C.slow;
  if (r.aiState === "Unavailable") return C.unavailable;
  if (r.aiState === "BudgetLimited") return C.budget;
  return null;
}

const SAID_LINES = 5;

/** What the window really covers, from coveredFromMs/coveredToMs when the server sent them (PRD M2-S5 AC3/AC8). */
export function coverageLine(r: Pick<CatchUpPayload, "windowMinutes" | "coveredFromMs" | "coveredToMs">): string {
  const { coveredFromMs: from, coveredToMs: to, windowMinutes: w } = r;
  if (typeof from !== "number" || typeof to !== "number" || to < from) return C.covers(w);
  const span = to - from;
  if (span >= w * 60_000 - 1000) return C.covers(w);
  if (from === 0) return span < 60_000 ? C.coversShortBrief : C.coversShort(Math.round(span / 60_000));
  return C.coversRange(formatMs(from), formatMs(to));
}

function SaidLines({ text, onCite, citations }: { text: string; onCite(segmentId: string): void; citations: Citation[] }) {
  const [all, setAll] = useState(false);
  const lines = text.split(/\r?\n/).filter((l) => l.trim() !== "");
  if (lines.length === 0) {
    return (
      <p data-testid="catchup-empty" className="mt-1">
        {C.emptyWindow}
      </p>
    );
  }
  const hidden = Math.max(0, lines.length - SAID_LINES);
  const shown = all ? lines : lines.slice(-SAID_LINES);
  return (
    <>
      <ul id="qr-said" data-testid="catchup-extract" className="mt-1 space-y-1">
        {shown.map((l, i) => (
          <li key={`${all ? "a" : "r"}${i}`} className="whitespace-pre-wrap border-l-2 border-hair pl-3">
            <PlainText value={l} />
          </li>
        ))}
      </ul>
      {hidden > 0 ? (
        <button type="button" className="btn-tertiary btn-sm optical-start mt-2" aria-expanded={all} aria-controls="qr-said" onClick={() => setAll((a) => !a)}>
          {all ? C.hideEarlier : C.showEarlier}
        </button>
      ) : null}
      <Cites citations={citations.slice(-3)} onCite={onCite} />
    </>
  );
}

function Result({
  r,
  wait,
  focus,
  earlierLabel,
  onFocused,
  onCite,
  onDone,
  level,
  sheet,
}: {
  r: CatchUpPayload;
  wait: "none" | "waiting" | "slow";
  focus: boolean;
  /** Set when a newer press failed: this result is the previous one and must say so. */
  earlierLabel: string | null;
  onFocused(): void;
  onCite(segmentId: string): void;
  onDone(): void;
  /** h2 as a sheet in the transcript column; h3 inside the Quick recap dock (phone). */
  level: 2 | 3;
  /** Desktop: a lecture sheet above the transcript (redesign-round2 5.2). Phone: a section inside the bottom sheet. */
  sheet: boolean;
}) {
  const [more, setMore] = useState(false);
  const headingRef = useRef<HTMLHeadingElement>(null);
  useEffect(() => {
    if (focus) {
      headingRef.current?.focus();
      onFocused();
    }
  }, [focus, onFocused]);

  const ai = r.kind === "Ai";
  const notice = noticeFor(r, wait);
  const hasDetail = ai && ((r.detail?.length ?? 0) > 0 || (r.keyTerms?.length ?? 0) > 0 || (r.clarifyingQuestions?.length ?? 0) > 0);
  const claims = r.shortCatchUp ?? [];
  const Heading = level === 2 ? "h2" : "h3";

  return (
    <section aria-labelledby="qr-result-h3" data-testid="catchup-result" data-kind={r.kind} className={sheet ? "lecture-sheet min-w-0" : "mt-4 border-t border-hair pt-4"}>
      <div className={sheet ? "sheet-head" : "flex items-baseline justify-between gap-3"}>
        <Heading id="qr-result-h3" ref={headingRef} tabIndex={-1} className="display text-xl">
          {C.resultHeading}
        </Heading>
        {/* A visible way out without scrolling (review M1); same action as "Back to lecture". */}
        <button type="button" className="btn-tertiary btn-sm optical-end shrink-0" onClick={onDone}>
          {C.backShort}
        </button>
      </div>
      <div className={sheet ? "sheet-body" : ""}>
        {earlierLabel ? (
          <p data-testid="catchup-earlier" className="mt-1 text-sm font-semibold">
            {earlierLabel}
          </p>
        ) : null}
        {/* Coverage and the Demo summary stamp share one line where it fits (wireframe 5.2). */}
        <p className="stamp-line mt-1">
          <span>{coverageLine(r)}</span>
          {ai && r.source === "mock" ? <DemoSummaryTag /> : null}
        </p>
        {notice ? (
          <p data-testid="catchup-notice" className="mt-2 text-sm text-muted">
            {notice}
          </p>
        ) : null}

        {ai ? (
          <>
            {r.nowDiscussing ? (
              // The lead sentence is synthesis: the serif speaks (PO-17); its time hangs in the lead column / margin.
              <div className="hang-row mt-3">
                <Cites lead citations={r.nowDiscussing.citations} onCite={onCite} />
                <p className="synthesis text-xl">
                  <span className="font-semibold">{C.rightNow}: </span>
                  <PlainText value={r.nowDiscussing.text} />
                  <BasisTag basis={r.nowDiscussing.basis} />
                </p>
              </div>
            ) : (
              <p className="mt-2 whitespace-pre-wrap">
                <PlainText value={r.text} />
              </p>
            )}
            {claims.length > 0 ? (
              <ul className="mt-3 space-y-1">
                {claims.map((c, i) => (
                  <li key={i} className="hang-row">
                    <Cites lead citations={c.citations} onCite={onCite} />
                    <span>
                      <PlainText value={c.text} />
                      <BasisTag basis={c.basis} />
                    </span>
                  </li>
                ))}
              </ul>
            ) : null}
            {r.confidence ? <p className="mt-2 text-sm text-muted">{C.confidence(r.confidence)}</p> : null}
            {r.gaps && r.gaps.length > 0 ? (
              <div className="mt-3">
                <p className="font-semibold">{C.gaps}</p>
                <ul className="list-disc pl-5 marker:text-muted">
                  {r.gaps.map((g, i) => (
                    <li key={i}>
                      <PlainText value={g} />
                    </li>
                  ))}
                </ul>
              </div>
            ) : null}
          </>
        ) : (
          <>
            <p className="mt-2 font-semibold">{C.said}</p>
            <p className="text-sm text-muted">{C.saidNote}</p>
            <SaidLines text={r.text} citations={r.citations} onCite={onCite} />
          </>
        )}
        {/* One row: the detail toggle on the left, the way back on the right. The detail opens BELOW the row, so the
            buttons stay where the student found them (nothing above moves). */}
        <div className="mt-4 flex flex-wrap items-center justify-between gap-3">
          <div className="flex flex-wrap items-center gap-x-3">
            {hasDetail ? (
              <button type="button" className="btn-tertiary btn-sm optical-start" aria-expanded={more} aria-controls="qr-more" onClick={() => setMore((m) => !m)}>
                {more ? C.hideDetail : C.showDetail}
              </button>
            ) : null}
          </div>
          <button type="button" className="btn-secondary" onClick={onDone}>
            {C.done}
          </button>
        </div>
        {hasDetail && more ? (
          <div id="qr-more" className="mt-3 space-y-3 border-t border-hair pt-3">
            {r.detail && r.detail.length > 0 ? (
              <ul className="space-y-1">
                {r.detail.map((d, i) => (
                  <li key={i} className="hang-row">
                    <Cites lead citations={d.citations} onCite={onCite} />
                    <span>
                      <PlainText value={d.text} />
                      <BasisTag basis={d.basis} />
                      {d.whyItMatters ? (
                        <span className="block text-sm text-muted">
                          {C.whyItMatters}: <PlainText value={d.whyItMatters} />
                        </span>
                      ) : null}
                    </span>
                  </li>
                ))}
              </ul>
            ) : null}
            {r.keyTerms && r.keyTerms.length > 0 ? (
              <div>
                <p className="font-semibold">{C.keyTerms}</p>
                <ul className="list-disc pl-5 marker:text-muted">
                  {r.keyTerms.map((t, i) => (
                    <li key={i}>
                      <span className="font-semibold">
                        <PlainText value={t.term} />
                      </span>
                      {t.definition ? (
                        <>
                          : <PlainText value={t.definition} />
                        </>
                      ) : null}
                      <BasisTag basis={t.basis} />
                      <Cites citations={t.citations} onCite={onCite} />
                    </li>
                  ))}
                </ul>
              </div>
            ) : null}
            {r.clarifyingQuestions && r.clarifyingQuestions.length > 0 ? (
              <div>
                <p className="font-semibold">{C.questions}</p>
                <ul className="list-disc pl-5 marker:text-muted">
                  {r.clarifyingQuestions.map((q, i) => (
                    <li key={i}>
                      <PlainText value={q} />
                    </li>
                  ))}
                </ul>
              </div>
            ) : null}
          </div>
        ) : null}
      </div>
    </section>
  );
}

/**
 * The open Quick recap result. On desktop the lecture screen renders it as a sheet in the transcript column, above the
 * transcript (redesign-round2 5.2: the recap is the thing being read, so it takes the sheet at reading width and the
 * transcript stays visible beneath); on phones the panel renders it inside its bottom sheet. One instance at a time.
 */
export function QuickRecapResult({ qr, level, sheet, onCite, onDone }: { qr: QuickRecap; level: 2 | 3; sheet: boolean; onCite(segmentId: string): void; onDone(): void }) {
  const r = qr.result;
  if (!r) return null;
  return (
    <Result
      key={r.catchUpId}
      r={r}
      wait={qr.wait}
      focus={qr.focusResult}
      earlierLabel={qr.error ? C.previousLabel(r.windowMinutes) : null}
      onFocused={qr.clearFocusResult}
      onCite={onCite}
      onDone={() => {
        qr.dismiss();
        onDone();
      }}
      level={level}
      sheet={sheet}
    />
  );
}

export interface QuickRecapPanelProps {
  /** Focus mode: stay a bottom dock at every width (the right rail is hidden). */
  docked?: boolean;
  ai: LectureAi | undefined;
  qr: QuickRecap;
  /** Phone: the result opens inside this panel as a bottom sheet. Desktop: the screen renders it in the transcript column. */
  resultInside: boolean;
  onCite(segmentId: string): void;
  /** "Back to lecture": the screen moves focus back to the transcript. */
  onDone(): void;
}

/** Moves focus to the student's usual window button (skip link and Alt+R). Never starts a catch-up. */
export function focusQuickRecap(): void {
  document.getElementById("qr-usual")?.focus();
}

const DOCK = "fixed inset-x-0 bottom-0 z-20 max-h-[60dvh] min-w-0 overflow-y-auto border-t-2 border-line bg-raised px-4 pb-[max(0.75rem,env(safe-area-inset-bottom))] pt-3 shadow-2";

/**
 * Quick recap: window buttons run recovery at once (no confirm). Calm copy, plain text only, no live region of its own.
 * Always visible: a fixed bottom bar (bottom sheet when a result is open) below `lg`; from `lg` the first row of the
 * sticky rail ledger (the screen owns the rail), or the Focus dock.
 */
export function QuickRecapPanel({ ai, qr, docked = false, resultInside, onCite, onDone }: QuickRecapPanelProps) {
  const [usual, setUsual] = useState<UsualWindow>(5);
  useEffect(() => setUsual(loadUsualWindow()), []);
  if (!ai) return null;
  const busy = qr.starting !== null;
  // Stable order (2, 5, 10): buttons never jump under a stressed thumb. The usual one is larger, filled and text-marked.
  return (
    <section
      id="lecture-quick-recap"
      aria-labelledby="qr-h2"
      data-has-result={resultInside && qr.result ? "true" : undefined}
      data-docked={docked ? "true" : undefined}
      className={
        docked
          ? // Focus dock: the same 52rem token as the Focus column, whose box has no padding of its own, so the dock's edges equal the column's (A11).
            `${DOCK} lg:mx-auto lg:max-w-focus lg:rounded-t-sheet lg:border-x-2`
          : `${DOCK} rail-row lg:static lg:inset-auto lg:z-auto lg:max-h-none lg:overflow-visible lg:border-0 lg:bg-transparent lg:p-4 lg:shadow-none`
      }
    >
      <div className="mx-auto max-w-focus">
        <div className="flex items-baseline justify-between gap-3">
          <h2 id="qr-h2" className="rail-title">
            {C.heading}
          </h2>
          <p className={`text-sm text-muted ${docked ? "hidden sm:block" : "hidden sm:block lg:hidden"}`} aria-hidden="true">
            {C.hint}
          </p>
        </div>
        <p className={`sr-only mt-1 text-sm text-muted ${docked ? "" : "lg:not-sr-only"}`}>{C.hint}</p>
        <div role="group" aria-label={C.groupLabel} className="mt-2 grid grid-cols-3 items-end gap-2" aria-busy={busy}>
          {WINDOWS.map((w) => {
            const isUsual = w === usual;
            return (
              <button
                key={w}
                id={isUsual ? "qr-usual" : undefined}
                type="button"
                // The usual one stacks its two lines (flex-col: inside an inline-flex button a block span would sit beside the
                // text, not under it). Labels are 15px where a column is about 104px (phone dock, rail) so "Last 10 min" holds one line.
                className={`${isUsual ? "btn-primary min-h-[64px] flex-col gap-0 text-base font-bold leading-tight" : "btn-secondary min-h-[56px] text-sm sm:text-base"} btn-steady min-w-[56px] px-1 ${docked ? "sm:px-5" : `sm:px-5 lg:px-1 ${isUsual ? "" : "lg:text-sm"}`}`}
                aria-label={isUsual ? C.windowUsualLabel(w) : C.windowLabel(w)}
                aria-disabled={busy ? true : undefined}
                onClick={() => {
                  if (busy) return;
                  saveUsualWindow(w);
                  setUsual(w);
                  void qr.start(w);
                }}
              >
                {C.window(w)}
                {isUsual ? (
                  // Second line: the usual marker in a smaller weight, so the label never breaks mid-phrase in a narrow column.
                  <>
                    {" "}
                    <span className="block text-sm font-semibold leading-tight">{C.windowUsualNote}</span>
                  </>
                ) : null}
              </button>
            );
          })}
        </div>
        {busy ? (
          <p data-testid="qr-working" className="mt-2 text-sm text-muted">
            {C.working}
          </p>
        ) : null}
        {qr.error ? (
          <p data-testid="qr-error" className="note note-notice mt-3">
            {C.errors[qr.error]}
          </p>
        ) : null}
        {resultInside ? <QuickRecapResult qr={qr} level={3} sheet={false} onCite={onCite} onDone={onDone} /> : null}
      </div>
    </section>
  );
}
