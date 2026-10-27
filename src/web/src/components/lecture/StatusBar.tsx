"use client";

import type { Ref } from "react";
import { DEMO_SOURCE_TAG, STATUS_COPY as S } from "./copy";
import { formatMs } from "./format";
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
  settings?: React.ReactNode;
}

function Shape({ kind }: { kind: "circle" | "bars" | "slash" | "square" }) {
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
          <rect x="4" y="3" width="4" height="14" fill="currentColor" />
          <rect x="12" y="3" width="4" height="14" fill="currentColor" />
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
          <rect x="4" y="4" width="12" height="12" fill="currentColor" />
        </svg>
      );
  }
}

/** Visible state label + shape for the recording indicator (never color-only). */
export function indicatorLabel(d: DisplayIndicator): { label: string; shape: "circle" | "bars" | "slash" | "square" } {
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

export function StatusBar(p: StatusBarProps) {
  const { label, shape } = indicatorLabel(p.display);
  const stopped = p.display.kind === "Stopped";
  const showResume = p.display.kind === "Paused" || (p.display.kind !== "Recording" && p.serverPaused);
  const enabled = p.display.kind === "Recording" || p.display.kind === "Paused";
  return (
    <header
      data-lecture-status-bar
      className="sticky top-[3.25rem] z-10 border-b-2 border-slate-400 bg-white px-3 py-2"
      aria-label="Recording status"
    >
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
        <h2 ref={p.headingRef} tabIndex={-1} className="flex items-center gap-2 text-lg font-bold">
          <Shape kind={shape} />
          <span data-testid="state-label">{label}</span>
        </h2>
        <span aria-hidden="true" className="tabular-nums" data-testid="elapsed">
          {formatMs(p.elapsedMs)}
        </span>
        <span data-testid="connection">{p.connected ? S.connected : S.notConnected}</span>
        {!stopped ? (
          <>
            <button
              ref={p.pauseRef}
              type="button"
              className="btn-primary min-h-[48px] min-w-[48px]"
              aria-disabled={enabled ? undefined : true}
              aria-describedby={enabled ? undefined : "bar-unavailable"}
              onClick={() => {
                if (!enabled) return;
                if (showResume) p.onResume();
                else p.onPause();
              }}
            >
              {showResume ? S.resume : S.pause}
            </button>
            <button ref={p.stopRef} type="button" className="btn-secondary min-h-[48px] min-w-[48px]" onClick={p.onStop}>
              {S.stop}
            </button>
            {!enabled ? (
              <span id="bar-unavailable" className="text-sm">
                {S.unavailable}
              </span>
            ) : null}
          </>
        ) : null}
        <span>{S.helpQuiet}</span>
        {p.demoSource ? (
          <span data-testid="demo-tag" className="rounded border-2 border-amber-800 px-2 text-sm font-semibold text-amber-900">
            {DEMO_SOURCE_TAG}
          </span>
        ) : null}
      </div>
      {p.settings}
    </header>
  );
}
