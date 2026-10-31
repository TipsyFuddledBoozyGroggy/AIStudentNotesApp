"use client";

import type { ReactNode, Ref } from "react";
import { DEMO_SOURCE_TAG, STATUS_COPY as S } from "./copy";
import { formatMs } from "./format";
import { GUTTERS, LECTURE_CONTAINER } from "./Notices";
import type { DisplayIndicator } from "./useLectureSession";

export interface StatusBarProps {
  display: DisplayIndicator;
  /** Last known server state, to choose Pause vs Resume while the state label is "Not recording". */
  serverPaused: boolean;
  elapsedMs: number;
  connected: boolean;
  demoSource: boolean;
  onPause(): void;
  onResume(): void;
  onStop(): void;
  headingRef?: Ref<HTMLHeadingElement>;
  pauseRef?: Ref<HTMLButtonElement>;
  stopRef?: Ref<HTMLButtonElement>;
  settings?: ReactNode;
  /** Focus mode toggle, rendered after Stop (design-direction 5.4). */
  focusToggle?: ReactNode;
  /** "Leave" control, only passed when the lecture is neither Active nor Paused (Lead decision, 2026-10-29). */
  leave?: ReactNode;
  /** Show mm:ss instead of whole minutes (Preferences; default off so the pill does not tick every second). */
  showSeconds?: boolean;
}

/** Whole minutes by default ("0 min", "12 min"); mm:ss on request. */
export function elapsedLabel(ms: number, showSeconds: boolean): string {
  if (showSeconds) return formatMs(ms);
  const m = Math.floor(Math.max(0, ms) / 60_000);
  return `${m} min`;
}

type ShapeKind = "circle" | "bars" | "slash" | "square";

function Shape({ kind }: { kind: ShapeKind }) {
  const common = { width: 20, height: 20, viewBox: "0 0 20 20", "aria-hidden": true, focusable: false } as const;
  switch (kind) {
    case "circle":
      return (
        <svg {...common}>
          <circle cx="10" cy="10" r="7" fill="currentColor" />
        </svg>
      );
    case "bars":
      return (
        <svg {...common}>
          <rect x="4" y="3" width="4" height="14" rx="1" fill="currentColor" />
          <rect x="12" y="3" width="4" height="14" rx="1" fill="currentColor" />
        </svg>
      );
    case "slash":
      return (
        <svg {...common}>
          <circle cx="10" cy="10" r="7" fill="none" stroke="currentColor" strokeWidth="2" />
          <line x1="4" y1="16" x2="16" y2="4" stroke="currentColor" strokeWidth="2" />
        </svg>
      );
    case "square":
      return (
        <svg {...common}>
          <rect x="4" y="4" width="12" height="12" rx="1" fill="currentColor" />
        </svg>
      );
  }
}

/** Visible state label + shape for the recording indicator (never color-only). */
export function indicatorLabel(d: DisplayIndicator): { label: string; shape: ShapeKind } {
  switch (d.kind) {
    case "Recording":
      return { label: S.recording, shape: "circle" };
    case "Paused":
      return { label: S.paused, shape: "bars" };
    case "Stopped":
      return { label: S.stopped, shape: "square" };
    case "Pending":
      return { label: d.pending === "pausing" ? S.pausing : d.pending === "resuming" ? S.resuming : S.stopping, shape: "slash" };
    default:
      return { label: S.notRecording, shape: "slash" };
  }
}

/** Pill colours per state (section 4): recording red only for live capture; never animated. */
const PILL: Record<ShapeKind, string> = {
  circle: "border-recording bg-recording-subtle text-recording",
  bars: "border-warning bg-warning-subtle text-warning",
  slash: "border-line bg-surface text-ink",
  square: "border-line bg-sunken text-muted",
};

const icon = (d: string) => (
  <svg width="16" height="16" viewBox="0 0 16 16" aria-hidden="true" focusable="false" className="shrink-0">
    <path d={d} fill="currentColor" />
  </svg>
);
const PAUSE_ICON = icon("M4 2.5h3v11H4zM9 2.5h3v11H9z");
const RESUME_ICON = icon("M4.5 2.5v11l9-5.5z");
const STOP_ICON = icon("M3.5 3.5h9v9h-9z");

/**
 * The lecture bar. On the live screen it replaces the site header (one sticky bar). Always shows the true state as shape +
 * word, elapsed active time and connection, with Pause/Resume and Stop in fixed places (SC 3.2.6).
 */
export function StatusBar(p: StatusBarProps) {
  const { label, shape } = indicatorLabel(p.display);
  const stopped = p.display.kind === "Stopped";
  const showResume = p.display.kind === "Paused" || (p.display.kind !== "Recording" && p.serverPaused);
  const enabled = p.display.kind === "Recording" || p.display.kind === "Paused";
  return (
    // 56px bar (redesign-round2 3.3 item 2): 44px pill and controls, 4px padding, one rule; wraps to two rows on phones (<= 112px).
    // Gutters and container as the lecture columns under it (R3), 80rem from 1536px.
    <header data-lecture-status-bar className="sticky top-0 z-20 border-b border-line bg-surface" aria-label="Recording status">
      <div className={`${LECTURE_CONTAINER} flex min-h-12 flex-wrap items-center gap-x-3 gap-y-2 py-1 ${GUTTERS}`}>
        <div className={`inline-flex min-h-[44px] items-center gap-3 rounded-full border-2 py-1 pl-3 pr-4 ${PILL[shape]}`}>
          <h2 ref={p.headingRef} tabIndex={-1} className="flex items-center gap-2 text-lg font-bold leading-none">
            <Shape kind={shape} />
            <span data-testid="state-label">{label}</span>
          </h2>
          <span aria-hidden="true" className="tabular text-base font-semibold" data-testid="elapsed">
            {elapsedLabel(p.elapsedMs, p.showSeconds ?? false)}
          </span>
        </div>
        <span data-testid="connection" className={`inline-flex items-center gap-2 text-sm font-semibold ${p.connected ? "text-muted" : "text-warning"}`}>
          <svg width="12" height="12" viewBox="0 0 12 12" aria-hidden="true" focusable="false">
            {p.connected ? (
              <circle cx="6" cy="6" r="4.5" fill="currentColor" />
            ) : (
              <>
                <circle cx="6" cy="6" r="4.5" fill="none" stroke="currentColor" strokeWidth="1.6" />
                <line x1="2.5" y1="9.5" x2="9.5" y2="2.5" stroke="currentColor" strokeWidth="1.6" />
              </>
            )}
          </svg>
          {p.connected ? S.connected : S.notConnected}
        </span>
        <div className="flex flex-wrap items-center gap-2 sm:ml-auto">
          {!stopped ? (
            <>
              <button
                ref={p.pauseRef}
                type="button"
                className="btn-secondary min-w-target px-3 sm:px-5"
                aria-disabled={enabled ? undefined : true}
                aria-describedby={enabled ? undefined : "bar-unavailable"}
                onClick={() => {
                  if (!enabled) return;
                  if (showResume) p.onResume();
                  else p.onPause();
                }}
              >
                {showResume ? RESUME_ICON : PAUSE_ICON}
                {showResume ? S.resume : S.pause}
              </button>
              <button ref={p.stopRef} type="button" className="btn-secondary min-w-target px-3 sm:px-5" onClick={p.onStop}>
                {STOP_ICON}
                {S.stop}
              </button>
              {!enabled ? (
                <span id="bar-unavailable" className="sr-only text-sm text-muted sm:not-sr-only">
                  {S.unavailable}
                </span>
              ) : null}
            </>
          ) : null}
          {p.focusToggle}
          {p.settings}
          {p.leave}
        </div>
        {p.demoSource ? (
          <span data-testid="demo-tag" className="tag tag-warning hidden sm:inline-flex">
            {DEMO_SOURCE_TAG}
          </span>
        ) : null}
      </div>
    </header>
  );
}
