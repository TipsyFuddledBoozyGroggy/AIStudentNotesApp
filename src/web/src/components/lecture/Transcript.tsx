"use client";

import { useCallback, useImperativeHandle, useLayoutEffect, useRef, useState, type Ref } from "react";
import { PlainText, selectPartials, type GapRecord, type LectureState } from "@/lib/lecture";
import { TRANSCRIPT_COPY as T, PAUSED_COPY } from "./copy";
import { formatMs, spokenMs } from "./format";
import { useAnnouncer } from "./Announcer";
import type { PauseRecord } from "./useLectureSession";

export interface TranscriptHandle {
  jumpToLive(): void;
  /** Scrolls instantly to a line and moves focus to it. */
  focusSegment(segmentId: string): void;
}

interface Segment {
  segmentId: string;
  startMs: number;
  text: string;
  corrected: boolean;
}

type Item =
  | { kind: "seg"; ms: number; key: string; seg: Segment }
  | { kind: "gap"; ms: number; key: string; gap: GapRecord }
  | { kind: "pause"; ms: number; key: string; rec: PauseRecord };

export interface TranscriptProps {
  segments: Segment[];
  lecture: LectureState;
  pauses: PauseRecord[];
  /** Opt-in finals announcements: the finals region is polite instead of off. */
  announceLines: boolean;
  handleRef?: Ref<TranscriptHandle>;
  /** Review search (finished lectures only): show just the lines containing every word. Markers are hidden meanwhile. */
  query?: string;
}

export function matchesQuery(text: string, query: string): boolean {
  const words = query.trim().toLowerCase().split(/\s+/).filter(Boolean);
  const t = text.toLowerCase();
  return words.every((w) => t.includes(w));
}

const NEAR_BOTTOM_PX = 24;

export function Transcript({ segments, lecture, pauses, announceLines, handleRef, query = "" }: TranscriptProps) {
  const { finalsText } = useAnnouncer();
  const logRef = useRef<HTMLDivElement>(null);
  const following = useRef(true);
  const [showJump, setShowJump] = useState(false);

  const partial = selectPartials(lecture).at(-1);
  const searching = query.trim() !== "";
  const items: Item[] = (
    searching
      ? segments.filter((seg) => matchesQuery(seg.text, query)).map((seg): Item => ({ kind: "seg", ms: seg.startMs, key: `s-${seg.segmentId}`, seg }))
      : [
          ...segments.map((seg): Item => ({ kind: "seg", ms: seg.startMs, key: `s-${seg.segmentId}`, seg })),
          ...lecture.gaps.map((gap): Item => ({ kind: "gap", ms: gap.fromMs, key: `g-${gap.seq}`, gap })),
          ...pauses.map((rec, i): Item => ({ kind: "pause", ms: rec.atMs, key: `p-${i}`, rec })),
        ]
  ).sort((a, b) => a.ms - b.ms);

  const toBottom = useCallback(() => {
    const el = logRef.current;
    if (el) el.scrollTop = el.scrollHeight; // instant, never smooth
  }, []);

  useLayoutEffect(() => {
    if (following.current) toBottom();
  }, [items.length, partial?.text, toBottom]);

  const jumpToLive = useCallback(() => {
    following.current = true;
    setShowJump(false);
    toBottom();
  }, [toBottom]);

  useImperativeHandle(
    handleRef,
    () => ({
      jumpToLive,
      focusSegment(id: string) {
        const el = logRef.current?.querySelector<HTMLElement>(`[data-segment-id="${CSS.escape(id)}"]`);
        if (!el) return;
        following.current = false;
        setShowJump(true);
        el.scrollIntoView({ block: "center", behavior: "instant" });
        el.focus({ preventScroll: true });
      },
    }),
    [jumpToLive],
  );

  const onScroll = () => {
    const el = logRef.current;
    if (!el) return;
    const near = el.scrollHeight - el.scrollTop - el.clientHeight <= NEAR_BOTTOM_PX;
    following.current = near;
    setShowJump(!near);
  };

  return (
    <section aria-labelledby="transcript-h2" className="relative min-w-0">
      {/* The transcript is the sheet (redesign-round2 slice 3): heading row inside it (A9), no fixed empty height, and
          from 1280 the timestamps hang in the margin track beside it (globals.css). The scrollable log keeps its id,
          role, name and focusability. */}
      <div className="transcript-panel lecture-sheet">
        <div className="sheet-head">
          <h2 id="transcript-h2" className="display text-xl">
            {T.heading}
          </h2>
        </div>
      {/* Scrollable region: focusable (axe scrollable-region-focusable) and labelled. aria-live stays off;
          the opt-in finals region is a separate child so partials/corrections can never be announced. */}
      <div
        id="lecture-transcript"
        ref={logRef}
        role="log"
        aria-live="off"
        aria-label={T.logLabel}
        tabIndex={0}
        onScroll={onScroll}
        onKeyDown={(e) => {
          if (e.key === "End") {
            e.preventDefault();
            jumpToLive();
          }
        }}
        className="transcript mt-2 overflow-y-auto px-4 pb-4 sm:px-6"
      >
        {/* hang-indent (slice 8): from 1280 the empty / no-match line starts on the words' text edge inside the sheet, not
            in the margin track across the sheet's border (seen on the reopen route with no words). */}
        {items.length === 0 && !partial ? <p className="hang-indent text-muted">{searching ? "No line matches that. Try fewer words." : T.empty}</p> : null}
        {items.map((it) => {
          if (it.kind === "seg") {
            return (
              <p key={it.key} data-segment-id={it.seg.segmentId} tabIndex={-1} className="transcript-line">
                <span className="transcript-time">
                  <span aria-hidden="true">{formatMs(it.seg.startMs)}</span>
                  <span className="sr-only">{spokenMs(it.seg.startMs)}</span>
                </span>
                <span>
                  <PlainText value={it.seg.text} />
                  {it.seg.corrected ? <span className="ml-2 text-xs text-muted">({T.edited})</span> : null}
                </span>
              </p>
            );
          }
          if (it.kind === "gap") {
            const g = it.gap;
            return (
              <p key={it.key} data-gap className="transcript-divider">
                {T.gap(formatMs(g.fromMs), formatMs(g.toMs), T.gapReasons[g.reason])}
              </p>
            );
          }
          const r = it.rec;
          const to = r.resumedWallMs === null ? null : formatMs(r.atMs + (r.resumedWallMs - r.wallMs));
          return (
            <p key={it.key} data-pause-divider className="transcript-divider">
              {PAUSED_COPY.divider(formatMs(r.atMs), to)}
            </p>
          );
        })}
        {/* One reserved-height row: the partial is replaced in place by its final, nothing above moves. */}
        <p data-partial-row className="transcript-partial">
          {partial ? <PlainText value={partial.text} /> : null}
        </p>
        <div data-region="finals" aria-live={announceLines ? "polite" : "off"} aria-atomic="true" className="sr-only">
          {announceLines ? finalsText : ""}
        </div>
      </div>
      </div>
      {showJump ? (
        <button type="button" className="btn-secondary btn-sm hang-indent mt-2 shadow-2" onClick={jumpToLive}>
          {T.jumpToLive}
        </button>
      ) : null}
    </section>
  );
}
