"use client";

import { useEffect, useRef, useState } from "react";
import { loadUsualWindow, saveUsualWindow, type UsualWindow } from "./settings";
import { PlainText, type CatchUpPayload, type Citation, type ClaimBasis, type WindowMinutes } from "@/lib/lecture";
import { DEMO_SUMMARY_COPY, QUICK_RECAP_COPY as C } from "./copy";
import { formatMs } from "./format";
import type { LectureAi } from "./ports";
import { useQuickRecap } from "./useQuickRecap";

const WINDOWS: WindowMinutes[] = [2, 5, 10];

export function DemoSummaryTag({ className = "" }: { className?: string }) {
  return (
    <p data-testid="demo-summary-tag" className={`inline-block rounded-control border border-warning bg-warning-subtle px-2.5 py-1 text-xs font-bold text-warning ${className}`}>
      {DEMO_SUMMARY_COPY.tag}
      <span className="font-normal">: {DEMO_SUMMARY_COPY.note}</span>
    </p>
  );
}

/**
 * Citations as link-style timestamps (review S1): underlined, tabular, accent. Each keeps a 44px hit area. In `lead` mode they
 * form the left column of a hanging grid, like the transcript's timestamp column.
 */
function Cites({ citations, onCite, lead = false }: { citations: Citation[]; onCite(segmentId: string): void; lead?: boolean }) {
  if (citations.length === 0) return lead ? <span aria-hidden="true" /> : null;
  return (
    <span className={lead ? "flex flex-col items-start" : "ml-1 inline-flex flex-wrap gap-x-2 align-baseline"}>
      {citations.slice(0, 4).map((c) => (
        <button
          key={`${c.segmentId}-${c.startMs}`}
          type="button"
          className="inline-flex min-h-[44px] min-w-[44px] items-start pt-[0.2em] text-sm font-semibold tabular-nums text-accent underline underline-offset-[0.2em] hover:decoration-2"
          aria-label={`${formatMs(c.startMs)}, go to transcript line`}
          onClick={() => onCite(c.segmentId)}
        >
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
        <button type="button" className="btn-quiet btn-sm mt-2 -ml-3" aria-expanded={all} aria-controls="qr-said" onClick={() => setAll((a) => !a)}>
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
}: {
  r: CatchUpPayload;
  wait: "none" | "waiting" | "slow";
  focus: boolean;
  /** Set when a newer press failed: this result is the previous one and must say so. */
  earlierLabel: string | null;
  onFocused(): void;
  onCite(segmentId: string): void;
  onDone(): void;
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

  return (
    <section aria-labelledby="qr-result-h3" data-testid="catchup-result" data-kind={r.kind} className="mt-4 border-t border-line pt-4">
      <div className="flex items-start justify-between gap-3">
        <h3 id="qr-result-h3" ref={headingRef} tabIndex={-1} className="display text-xl">
          {C.resultHeading}
        </h3>
        {/* A visible way out without scrolling the sheet (review M1); same action as "Back to lecture". */}
        <button type="button" className="btn-quiet btn-sm -mr-2 -mt-1 shrink-0" onClick={onDone}>
          {C.backShort}
        </button>
      </div>
      {earlierLabel ? (
        <p data-testid="catchup-earlier" className="mt-1 text-sm font-semibold">
          {earlierLabel}
        </p>
      ) : null}
      <p className="mt-1 text-sm text-muted">{coverageLine(r)}</p>
      {ai && r.source === "mock" ? <DemoSummaryTag className="mt-2" /> : null}
      {notice ? (
        <p data-testid="catchup-notice" className="mt-2 text-sm text-muted">
          {notice}
        </p>
      ) : null}

      {ai ? (
        <>
          {r.nowDiscussing ? (
            <div className="mt-3 grid grid-cols-[3.25rem_minmax(0,1fr)] gap-x-2">
              <Cites lead citations={r.nowDiscussing.citations} onCite={onCite} />
              <p className="pt-[0.1em] text-lg leading-[1.45]">
                <span className="font-bold">{C.rightNow}: </span>
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
            <ul className="mt-2 space-y-1">
              {claims.map((c, i) => (
                <li key={i} className="grid grid-cols-[3.25rem_minmax(0,1fr)] gap-x-2">
                  <Cites lead citations={c.citations} onCite={onCite} />
                  <span className="pt-[0.15em]">
                    <PlainText value={c.text} />
                    <BasisTag basis={c.basis} />
                  </span>
                </li>
              ))}
            </ul>
          ) : null}
          {r.confidence ? <p className="mt-2 text-sm text-muted">{C.confidence(r.confidence)}</p> : null}
          {r.gaps && r.gaps.length > 0 ? (
            <div className="mt-2">
              <p className="font-semibold">{C.gaps}</p>
              <ul className="list-disc pl-5">
                {r.gaps.map((g, i) => (
                  <li key={i}>
                    <PlainText value={g} />
                  </li>
                ))}
              </ul>
            </div>
          ) : null}
          {hasDetail ? (
            <>
              <button type="button" className="btn-quiet btn-sm mt-2 -ml-3" aria-expanded={more} aria-controls="qr-more" onClick={() => setMore((m) => !m)}>
                {more ? C.hideDetail : C.showDetail}
              </button>
              {more ? (
                <div id="qr-more" className="mt-2 space-y-3">
                  {r.detail && r.detail.length > 0 ? (
                    <ul className="space-y-1">
                      {r.detail.map((d, i) => (
                        <li key={i} className="grid grid-cols-[3.25rem_minmax(0,1fr)] gap-x-2">
                          <Cites lead citations={d.citations} onCite={onCite} />
                          <span className="pt-[0.15em]">
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
                      <ul className="list-disc pl-5">
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
                      <ul className="list-disc pl-5">
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
            </>
          ) : null}
        </>
      ) : (
        <>
          <p className="mt-2 font-semibold">{C.said}</p>
          <p className="text-sm text-muted">{C.saidNote}</p>
          <SaidLines text={r.text} citations={r.citations} onCite={onCite} />
        </>
      )}
      <div className="mt-4">
        <button type="button" className="btn-secondary min-h-[48px]" onClick={onDone}>
          {C.done}
        </button>
      </div>
    </section>
  );
}

export interface QuickRecapPanelProps {
  /** Focus mode: stay a bottom dock at every width (the right rail is hidden). */
  docked?: boolean;
  ai: LectureAi | undefined;
  hubCatchUps: Record<string, CatchUpPayload>;
  onCite(segmentId: string): void;
  /** "Back to lecture": the screen moves focus back to the transcript. */
  onDone(): void;
}

/** Moves focus to the student's usual window button (skip link and Alt+R). Never starts a catch-up. */
export function focusQuickRecap(): void {
  document.getElementById("qr-usual")?.focus();
}

/**
 * Quick recap: window buttons run recovery at once (no confirm). Calm copy, plain text only, no live region of its own.
 * Always visible: a fixed bottom bar (bottom sheet when a result is open) below `lg`, a sticky right column from `lg`.
 */
export function QuickRecapPanel({ ai, hubCatchUps, onCite, onDone, docked = false }: QuickRecapPanelProps) {
  const qr = useQuickRecap({ ai, hubCatchUps });
  const [usual, setUsual] = useState<UsualWindow>(5);
  useEffect(() => setUsual(loadUsualWindow()), []);
  if (!ai) return null;
  const busy = qr.starting !== null;
  // Stable order (2, 5, 10): buttons never jump under a stressed thumb. The usual one is larger and text-marked.
  return (
    <section
      id="lecture-quick-recap"
      aria-labelledby="qr-h2"
      data-has-result={qr.result ? "true" : undefined}
      data-docked={docked ? "true" : undefined}
      className={
        docked
          ? "fixed inset-x-0 bottom-0 z-20 max-h-[60dvh] min-w-0 overflow-y-auto border-t-2 border-line bg-raised px-4 pb-[max(0.75rem,env(safe-area-inset-bottom))] pt-3 shadow-2 lg:mx-auto lg:max-w-[52rem] lg:rounded-t-sheet lg:border-x-2"
          : "fixed inset-x-0 bottom-0 z-20 max-h-[60dvh] min-w-0 overflow-y-auto border-t-2 border-line bg-raised px-4 pb-[max(0.75rem,env(safe-area-inset-bottom))] pt-3 shadow-2 lg:sticky lg:inset-x-auto lg:bottom-auto lg:top-[5.5rem] lg:z-auto lg:max-h-[calc(100vh-7rem)] lg:rounded-card lg:border lg:border-hair lg:bg-surface lg:p-5 lg:shadow-1"
      }
    >
      <div className="mx-auto max-w-[52rem]">
      <div className="flex items-baseline justify-between gap-3">
        <h2 id="qr-h2" className="display text-xl">
          {C.heading}
        </h2>
        <p className={`text-sm text-muted ${docked ? "hidden sm:block" : "hidden sm:block lg:hidden"}`} aria-hidden="true">
          {C.hint}
        </p>
      </div>
      <p className={`sr-only mt-1 text-sm text-muted ${docked ? "" : "lg:not-sr-only"}`}>{C.hint}</p>
      <div role="group" aria-label={C.groupLabel} className={`mt-2 grid grid-cols-3 gap-2 ${docked ? "" : "lg:mt-3 lg:grid-cols-1"}`} aria-busy={busy}>
        {WINDOWS.map((w) => {
          const isUsual = w === usual;
          return (
            <button
              key={w}
              id={isUsual ? "qr-usual" : undefined}
              type="button"
              className={`${isUsual ? "btn-primary min-h-[64px] text-base font-bold leading-tight sm:text-lg" : "btn-secondary min-h-[56px] self-end"} btn-steady min-w-[56px] px-2 sm:px-5 ${docked ? "" : "lg:self-auto"}`}
              aria-label={isUsual ? C.windowUsualLabel(w) : C.windowLabel(w)}
              aria-disabled={busy ? true : undefined}
              onClick={() => {
                if (busy) return;
                saveUsualWindow(w);
                setUsual(w);
                void qr.start(w);
              }}
            >
              {isUsual ? C.windowUsual(w) : C.window(w)}
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
        <p data-testid="qr-error" className="panel panel-notice mt-3">
          {C.errors[qr.error]}
        </p>
      ) : null}
      {qr.result ? (
        <Result
          key={qr.result.catchUpId}
          r={qr.result}
          wait={qr.wait}
          focus={qr.focusResult}
          earlierLabel={qr.error ? C.previousLabel(qr.result.windowMinutes) : null}
          onFocused={qr.clearFocusResult}
          onCite={onCite}
          onDone={() => {
            qr.dismiss();
            onDone();
          }}
        />
      ) : null}
      </div>
    </section>
  );
}
