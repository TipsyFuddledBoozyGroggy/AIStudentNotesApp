"use client";

import { useState } from "react";
import { PlainText, type SummaryJobState, type SummaryRevisionPayload } from "@/lib/lecture";
import { SUMMARY_STATUS_COPY as S, TLDR_COPY as C } from "./copy";
import { formatMs } from "./format";
import { DemoSummaryTag } from "./LostTrackPanel";

export interface TldrPanelProps {
  summary: SummaryRevisionPayload | null;
  /** Job state from summary.status. Only Delayed and Failed are mentioned, calmly. */
  status?: { state: SummaryJobState; lagSeconds: number } | null;
  onCite(segmentId: string): void;
}

/** Placeholder TL;DR panel fed by summary.revision. Plain text only (C-28). aria-live stays off. */
export function TldrPanel({ summary, status, onCite }: TldrPanelProps) {
  const [hold, setHold] = useState(false);
  const [frozen, setFrozen] = useState<SummaryRevisionPayload | null>(null);
  const [more, setMore] = useState(false);

  const shown = hold ? frozen : summary;
  const newer = hold && summary !== null && (frozen === null || summary.revision > frozen.revision);
  const bullets = shown ? shown.bullets.slice(0, more ? 7 : 3) : [];
  const canMore = shown !== null && shown.bullets.length > 3;

  return (
    <aside id="lecture-tldr" aria-labelledby="tldr-h2" aria-live="off" tabIndex={-1} className="card min-w-0">
      <h2 id="tldr-h2" className="text-lg font-bold">
        {C.heading}
      </h2>
      {shown?.source === "mock" ? <DemoSummaryTag className="mt-2" /> : null}
      {status?.state === "Delayed" || status?.state === "Failed" ? (
        <p data-testid="summary-status" className="mt-2 text-sm">
          {status.state === "Delayed" ? S.delayed : S.failed}
          {shown ? ` ${S.asOf(formatMs(shown.coveredToMs))}.` : ""}
        </p>
      ) : null}
      {shown ? (
        <>
          <p className="mt-2 font-semibold">
            <PlainText value={shown.tldr} />
          </p>
          {bullets.length > 0 ? (
            <ul className="mt-2 list-disc space-y-1 pl-5">
              {bullets.map((b, i) => (
                <li key={i}>
                  <PlainText value={b} />
                </li>
              ))}
            </ul>
          ) : null}
          {canMore ? (
            <button type="button" className="btn-secondary mt-2" aria-expanded={more} onClick={() => setMore((m) => !m)}>
              {more ? C.showLess : C.showMore}
            </button>
          ) : null}
          <p className="mt-2 text-sm text-slate-700">{C.updated(formatMs(shown.coveredToMs), shown.revision)}</p>
          {shown.citations.length > 0 ? (
            <p className="mt-2 flex flex-wrap gap-2">
              {shown.citations.slice(0, 5).map((c) => (
                <button key={`${c.segmentId}-${c.startMs}`} type="button" className="btn-secondary" onClick={() => onCite(c.segmentId)}>
                  {C.from(formatMs(c.startMs))}
                </button>
              ))}
            </p>
          ) : null}
        </>
      ) : (
        <p className="mt-2 text-slate-700">{C.empty}</p>
      )}
      <div className="mt-3 flex flex-wrap items-center gap-3">
        <button
          type="button"
          className="btn-secondary"
          onClick={() => {
            setFrozen(summary);
            setHold((h) => !h);
          }}
        >
          {hold ? C.resume : C.hold}
        </button>
        {hold ? (
          <span data-testid="tldr-paused" className="text-sm">
            {C.paused(frozen ? formatMs(frozen.coveredToMs) : "earlier")}
          </span>
        ) : null}
        {newer ? (
          <>
            <span className="text-sm">{C.newer}</span>
            <button type="button" className="btn-secondary" onClick={() => setFrozen(summary)}>
              {C.showLatest}
            </button>
          </>
        ) : null}
      </div>
    </aside>
  );
}
