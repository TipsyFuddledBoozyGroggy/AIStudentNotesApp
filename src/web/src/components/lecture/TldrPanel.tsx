"use client";

import { useState } from "react";
import { PlainText, type SummaryJobState, type SummaryRevisionPayload } from "@/lib/lecture";
import { SUMMARY_STATUS_COPY as S, TLDR_COPY as C } from "./copy";
import { formatMs } from "./format";
import { DemoSummaryTag } from "./QuickRecapPanel";

export interface TldrPanelProps {
  summary: SummaryRevisionPayload | null;
  /** Job state from summary.status. Only Delayed and Failed are mentioned, calmly. */
  status?: { state: SummaryJobState; lagSeconds: number } | null;
  onCite(segmentId: string): void;
  /**
   * Phone and Focus mode: a Note above the transcript with the heading and the one-line TL;DR, the rest one press away
   * (expands in place). Collapsing never hides the Demo summary stamp or the summary status note.
   */
  collapsible?: boolean;
  /** False once the lecture has ended: "Pause updates" is hidden because nothing new will arrive (review S2). */
  live?: boolean;
}

/**
 * TL;DR fed by summary.revision. Plain text only (C-28). aria-live stays off; updates swap text in place. On desktop it is
 * the second row of the rail ledger; the one-line summary is synthesis and speaks in the serif (PO-17).
 */
export function TldrPanel({ summary, status, onCite, collapsible = false, live = true }: TldrPanelProps) {
  const [hold, setHold] = useState(false);
  const [frozen, setFrozen] = useState<SummaryRevisionPayload | null>(null);
  const [more, setMore] = useState(false);
  const [open, setOpen] = useState(false);

  const shown = hold ? frozen : summary;
  const newer = hold && summary !== null && (frozen === null || summary.revision > frozen.revision);
  const bullets = shown ? shown.bullets.slice(0, more ? 7 : 3) : [];
  const canMore = shown !== null && shown.bullets.length > 3;
  const collapsed = collapsible && !open;

  return (
    <aside id="lecture-tldr" aria-labelledby="tldr-h2" aria-live="off" tabIndex={-1} className={`min-w-0 ${collapsible ? "note note-neutral" : "rail-row p-4"}`}>
      <div className="flex items-baseline justify-between gap-3">
        <h2 id="tldr-h2" className="rail-title">
          {C.heading}
        </h2>
        {collapsible ? (
          <button
            type="button"
            className="btn-tertiary btn-sm optical-end"
            aria-expanded={open}
            aria-controls="tldr-body"
            aria-label={open ? "Less of the TL;DR" : "More of the TL;DR"}
            onClick={() => setOpen((o) => !o)}
          >
            {open ? "Less" : "More"}
          </button>
        ) : null}
      </div>
      {shown?.source === "mock" ? (
        <p className="stamp-line mt-2">
          <DemoSummaryTag />
        </p>
      ) : null}
      {status?.state === "Delayed" || status?.state === "Failed" ? (
        <p data-testid="summary-status" className="mt-2 text-sm text-muted">
          {status.state === "Delayed" ? S.delayed : S.failed}
          {shown ? ` ${S.asOf(formatMs(shown.coveredToMs))}.` : ""}
        </p>
      ) : null}
      {shown ? (
        // Collapsed: two lines reserved (clamp + min height), so the note is the same height in every heading face and a
        // revision that grows from one line to two never moves the transcript under it (landmark gate; Principle 3).
        <p className={`synthesis mt-2 text-xl ${collapsed ? "line-clamp-2 min-h-16" : ""}`}>
          <PlainText value={shown.tldr} />
        </p>
      ) : (
        <p className="mt-2 text-muted">{C.empty}</p>
      )}
      <div id="tldr-body" hidden={collapsed}>
        {shown ? (
          <>
            {bullets.length > 0 ? (
              <ul className="mt-3 list-disc space-y-2 pl-5 marker:text-muted">
                {bullets.map((b, i) => (
                  <li key={i}>
                    <PlainText value={b} />
                  </li>
                ))}
              </ul>
            ) : null}
            {canMore ? (
              <button type="button" className="btn-tertiary btn-sm optical-start mt-2" aria-expanded={more} onClick={() => setMore((m) => !m)}>
                {more ? C.showLess : C.showMore}
              </button>
            ) : null}
            {shown.citations.length > 0 ? (
              <p className="mt-3 flex flex-wrap gap-x-4 gap-y-2">
                {shown.citations.slice(0, 5).map((c) => (
                  <button key={`${c.segmentId}-${c.startMs}`} type="button" className="cite" onClick={() => onCite(c.segmentId)}>
                    {C.from(formatMs(c.startMs))}
                  </button>
                ))}
              </p>
            ) : null}
            <p className="tabular mt-3 text-xs text-muted">{C.updated(formatMs(shown.coveredToMs), shown.revision)}</p>
          </>
        ) : null}
        <div hidden={!(live || hold)} className="mt-3 flex flex-wrap items-center gap-x-3 gap-y-2 border-t border-hair pt-3">
          <button
            type="button"
            className="btn-tertiary btn-sm optical-start"
            onClick={() => {
              setFrozen(summary);
              setHold((h) => !h);
            }}
          >
            {hold ? C.resume : C.hold}
          </button>
          {hold ? (
            <span data-testid="tldr-paused" className="text-sm text-muted">
              {C.paused(frozen ? formatMs(frozen.coveredToMs) : "earlier")}
            </span>
          ) : null}
          {newer ? (
            <>
              <span className="text-sm font-semibold">{C.newer}</span>
              <button type="button" className="btn-secondary btn-sm" onClick={() => setFrozen(summary)}>
                {C.showLatest}
              </button>
            </>
          ) : null}
        </div>
      </div>
    </aside>
  );
}
