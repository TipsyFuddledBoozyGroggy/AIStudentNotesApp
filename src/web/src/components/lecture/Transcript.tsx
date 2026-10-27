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
}

const NEAR_BOTTOM_PX = 24;

export function Transcript({ segments, lecture, pauses, announceLines, handleRef }: TranscriptProps) {
  const { finalsText } = useAnnouncer();
  const logRef = useRef<HTMLDivElement>(null);
  const following = useRef(true);
  const [showJump, setShowJump] = useState(false);

  const partial = selectPartials(lecture).at(-1);
  const items: Item[] = [
    ...segments.map((seg): Item => ({ kind: "seg", ms: seg.startMs, key: `s-${seg.segmentId}`, seg })),
    ...lecture.gaps.map((gap): Item => ({ kind: "gap", ms: gap.fromMs, key: `g-${gap.seq}`, gap })),
    ...pauses.map((rec, i): Item => ({ kind: "pause", ms: rec.atMs, key: `p-${i}`, rec })),
  ].sort((a, b) => a.ms - b.ms);

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
    <section aria-labelledby="transcript-h2" className="min-w-0">
      <h2 id="transcript-h2" className="text-lg font-bold">
        {T.heading}
      </h2>
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
        className="mt-2 h-[50vh] min-h-[12rem] overflow-y-auto rounded-lg border-2 border-slate-400 bg-white p-3"
      >
        {items.length === 0 && !partial ? <p className="text-slate-700">{T.empty}</p> : null}
        {items.map((it) => {
          if (it.kind === "seg") {
            return (
              <p key={it.key} data-segment-id={it.seg.segmentId} tabIndex={-1} className="mb-2">
                <span className="mr-2 text-sm text-slate-700">
                  <span aria-hidden="true">{formatMs(it.seg.startMs)}</span>
                  <span className="sr-only">{spokenMs(it.seg.startMs)}</span>
                </span>
                <PlainText value={it.seg.text} />
                {it.seg.corrected ? <span className="ml-2 text-xs text-slate-700">({T.edited})</span> : null}
              </p>
            );
          }
          if (it.kind === "gap") {
            const g = it.gap;
            return (
              <p key={it.key} data-gap className="mb-2 border-l-4 border-slate-500 pl-2 text-sm text-slate-800">
                {T.gap(formatMs(g.fromMs), formatMs(g.toMs), T.gapReasons[g.reason])}
              </p>
            );
          }
          const r = it.rec;
          const to = r.resumedWallMs === null ? null : formatMs(r.atMs + (r.resumedWallMs - r.wallMs));
          return (
            <p key={it.key} data-pause-divider className="mb-2 border-y border-slate-400 py-1 text-center text-sm text-slate-800">
              {PAUSED_COPY.divider(formatMs(r.atMs), to)}
            </p>
          );
        })}
        {/* One reserved-height row: the partial is replaced in place by its final, nothing above moves. */}
        <p data-partial-row className="min-h-[1.5rem] italic text-slate-700">
          {partial ? <PlainText value={partial.text} /> : null}
        </p>
        <div data-region="finals" aria-live={announceLines ? "polite" : "off"} aria-atomic="true" className="sr-only">
          {announceLines ? finalsText : ""}
        </div>
      </div>
      {showJump ? (
        <button type="button" className="btn-secondary mt-2 min-h-[44px]" onClick={jumpToLive}>
          {T.jumpToLive}
        </button>
      ) : null}
    </section>
  );
}
