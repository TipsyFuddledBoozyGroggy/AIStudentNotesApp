"use client";

import { useEffect, useRef, useState } from "react";
import { loadUsualWindow, saveUsualWindow, type UsualWindow } from "./settings";
import { PlainText, type CatchUpPayload, type Citation, type ClaimBasis, type WindowMinutes } from "@/lib/lecture";
import { DEMO_SUMMARY_COPY, LOST_TRACK_COPY as C } from "./copy";
import { formatMs, spokenMs } from "./format";
import type { LectureAi } from "./ports";
import { useLostTrack } from "./useLostTrack";

const WINDOWS: WindowMinutes[] = [2, 5, 10];

export function DemoSummaryTag({ className = "" }: { className?: string }) {
  return (
    <p data-testid="demo-summary-tag" className={`inline-block rounded border-2 border-slate-700 bg-slate-100 px-2 py-0.5 text-sm font-semibold ${className}`}>
      {DEMO_SUMMARY_COPY.tag}
      <span className="font-normal">: {DEMO_SUMMARY_COPY.note}</span>
    </p>
  );
}

function Cites({ citations, onCite }: { citations: Citation[]; onCite(segmentId: string): void }) {
  if (citations.length === 0) return null;
  return (
    <span className="ml-1 inline-flex flex-wrap gap-1 align-middle">
      {citations.slice(0, 4).map((c) => (
        <button
          key={`${c.segmentId}-${c.startMs}`}
          type="button"
          className="btn-secondary min-h-[44px] min-w-[44px] px-2 py-0 text-sm"
          aria-label={`${C.from(spokenMs(c.startMs))}, go to transcript line`}
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
  return label ? <span className="ml-2 rounded border border-slate-600 px-1 text-sm">{label}</span> : null;
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
      <ul id="lt-said" data-testid="catchup-extract" className="mt-1 space-y-1">
        {shown.map((l, i) => (
          <li key={`${all ? "a" : "r"}${i}`} className="whitespace-pre-wrap">
            <PlainText value={l} />
          </li>
        ))}
      </ul>
      {hidden > 0 ? (
        <button type="button" className="btn-secondary mt-2 min-h-[44px]" aria-expanded={all} aria-controls="lt-said" onClick={() => setAll((a) => !a)}>
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
    <section aria-labelledby="lt-result-h3" data-testid="catchup-result" data-kind={r.kind} className="mt-3 rounded-lg border-2 border-slate-700 bg-white p-3">
      <h3 id="lt-result-h3" ref={headingRef} tabIndex={-1} className="text-base font-bold">
        {C.resultHeading}
      </h3>
      {earlierLabel ? (
        <p data-testid="catchup-earlier" className="mt-1 text-sm font-semibold">
          {earlierLabel}
        </p>
      ) : null}
      <p className="mt-1 text-sm text-slate-700">{coverageLine(r)}</p>
      {ai && r.source === "mock" ? <DemoSummaryTag className="mt-2" /> : null}
      {notice ? (
        <p data-testid="catchup-notice" className="mt-2">
          {notice}
        </p>
      ) : null}

      {ai ? (
        <>
          {r.nowDiscussing ? (
            <p className="mt-2">
              <span className="font-semibold">{C.rightNow}: </span>
              <PlainText value={r.nowDiscussing.text} />
              <BasisTag basis={r.nowDiscussing.basis} />
              <Cites citations={r.nowDiscussing.citations} onCite={onCite} />
            </p>
          ) : (
            <p className="mt-2 whitespace-pre-wrap">
              <PlainText value={r.text} />
            </p>
          )}
          {claims.length > 0 ? (
            <ul className="mt-2 list-disc space-y-1 pl-5">
              {claims.map((c, i) => (
                <li key={i}>
                  <PlainText value={c.text} />
                  <BasisTag basis={c.basis} />
                  <Cites citations={c.citations} onCite={onCite} />
                </li>
              ))}
            </ul>
          ) : null}
          {r.confidence ? <p className="mt-2 text-sm">{C.confidence(r.confidence)}</p> : null}
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
              <button type="button" className="btn-secondary mt-2 min-h-[44px]" aria-expanded={more} aria-controls="lt-more" onClick={() => setMore((m) => !m)}>
                {more ? C.hideDetail : C.showDetail}
              </button>
              {more ? (
                <div id="lt-more" className="mt-2 space-y-3">
                  {r.detail && r.detail.length > 0 ? (
                    <ul className="list-disc space-y-2 pl-5">
                      {r.detail.map((d, i) => (
                        <li key={i}>
                          <PlainText value={d.text} />
                          <BasisTag basis={d.basis} />
                          <Cites citations={d.citations} onCite={onCite} />
                          {d.whyItMatters ? (
                            <span className="block text-sm text-slate-700">
                              {C.whyItMatters}: <PlainText value={d.whyItMatters} />
                            </span>
                          ) : null}
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
          <p className="text-sm text-slate-700">{C.saidNote}</p>
          <SaidLines text={r.text} citations={r.citations} onCite={onCite} />
        </>
      )}
      <div className="mt-3">
        <button type="button" className="btn-primary min-h-[48px]" onClick={onDone}>
          {C.done}
        </button>
      </div>
    </section>
  );
}

export interface LostTrackPanelProps {
  ai: LectureAi | undefined;
  hubCatchUps: Record<string, CatchUpPayload>;
  onCite(segmentId: string): void;
  /** "Got it": the screen moves focus back to the transcript. */
  onDone(): void;
}

/** Moves focus to the student's usual window button (skip link and Alt+L). Never starts a catch-up. */
export function focusLostTrack(): void {
  document.getElementById("lt-usual")?.focus();
}

/**
 * I Lost Track: window buttons run recovery at once (no confirm). Calm copy, plain text only, no live region of its own.
 * Always visible: a fixed bottom bar (bottom sheet when a result is open) below `lg`, a sticky right column from `lg`.
 */
export function LostTrackPanel({ ai, hubCatchUps, onCite, onDone }: LostTrackPanelProps) {
  const lt = useLostTrack({ ai, hubCatchUps });
  const [usual, setUsual] = useState<UsualWindow>(5);
  useEffect(() => setUsual(loadUsualWindow()), []);
  if (!ai) return null;
  const busy = lt.starting !== null;
  // Stable order (2, 5, 10): buttons never jump under a stressed thumb. The usual one is larger and text-marked.
  return (
    <section
      id="lecture-lost-track"
      aria-labelledby="lt-h2"
      className="fixed inset-x-0 bottom-0 z-20 max-h-[70vh] min-w-0 overflow-y-auto border-t-4 border-slate-700 bg-white p-3 shadow-[0_-2px_8px_rgba(0,0,0,0.2)] lg:sticky lg:inset-x-auto lg:bottom-auto lg:top-4 lg:z-auto lg:max-h-[calc(100vh-2rem)] lg:rounded-xl lg:border lg:border-t lg:border-slate-300 lg:p-5 lg:shadow-sm"
    >
      <h2 id="lt-h2" className="text-base font-bold lg:text-lg">
        {C.heading}
      </h2>
      <p className="sr-only mt-1 text-sm text-slate-700 lg:not-sr-only">{C.hint}</p>
      <div role="group" aria-label={C.groupLabel} className="mt-2 flex flex-wrap gap-2 lg:mt-3" aria-busy={busy}>
        {WINDOWS.map((w) => {
          const isUsual = w === usual;
          return (
            <button
              key={w}
              id={isUsual ? "lt-usual" : undefined}
              type="button"
              className={`btn-primary min-w-[56px] px-5 ${isUsual ? "min-h-[64px] text-lg font-bold" : "min-h-[56px]"}`}
              aria-label={isUsual ? C.windowUsualLabel(w) : C.windowLabel(w)}
              aria-disabled={busy ? true : undefined}
              onClick={() => {
                if (busy) return;
                saveUsualWindow(w);
                setUsual(w);
                void lt.start(w);
              }}
            >
              {isUsual ? C.windowUsual(w) : C.window(w)}
            </button>
          );
        })}
      </div>
      {busy ? (
        <p data-testid="lt-working" className="mt-2 text-sm">
          {C.working}
        </p>
      ) : null}
      {lt.error ? (
        <p data-testid="lt-error" className="mt-3 rounded border-2 border-slate-700 p-3">
          {C.errors[lt.error]}
        </p>
      ) : null}
      {lt.result ? (
        <Result
          key={lt.result.catchUpId}
          r={lt.result}
          wait={lt.wait}
          focus={lt.focusResult}
          earlierLabel={lt.error ? C.previousLabel(lt.result.windowMinutes) : null}
          onFocused={lt.clearFocusResult}
          onCite={onCite}
          onDone={() => {
            lt.dismiss();
            onDone();
          }}
        />
      ) : null}
    </section>
  );
}
