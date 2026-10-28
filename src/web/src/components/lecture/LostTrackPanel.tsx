"use client";

import { useEffect, useRef, useState } from "react";
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

function Result({
  r,
  wait,
  focus,
  onFocused,
  onCite,
  onDone,
}: {
  r: CatchUpPayload;
  wait: "none" | "waiting" | "slow";
  focus: boolean;
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
      <p className="mt-1 text-sm text-slate-700">{C.covers(r.windowMinutes)}</p>
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
          <p data-testid="catchup-extract" className="mt-1 whitespace-pre-wrap">
            <PlainText value={r.text} />
          </p>
          <Cites citations={r.citations.slice(-3)} onCite={onCite} />
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

/** I Lost Track: window buttons run recovery at once (no confirm). Calm copy, plain text only, no live region of its own. */
export function LostTrackPanel({ ai, hubCatchUps, onCite, onDone }: LostTrackPanelProps) {
  const lt = useLostTrack({ ai, hubCatchUps });
  if (!ai) return null;
  const busy = lt.starting !== null;
  return (
    <section id="lecture-lost-track" aria-labelledby="lt-h2" className="card min-w-0">
      <h2 id="lt-h2" className="text-lg font-bold">
        {C.heading}
      </h2>
      <p className="mt-1 text-sm text-slate-700">{C.hint}</p>
      <div role="group" aria-label={C.groupLabel} className="mt-3 flex flex-wrap gap-2" aria-busy={busy}>
        {WINDOWS.map((w) => (
          <button
            key={w}
            type="button"
            className="btn-primary min-h-[56px] min-w-[56px] px-5"
            aria-label={C.windowLabel(w)}
            aria-disabled={busy ? true : undefined}
            onClick={() => !busy && void lt.start(w)}
          >
            {C.window(w)}
          </button>
        ))}
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
