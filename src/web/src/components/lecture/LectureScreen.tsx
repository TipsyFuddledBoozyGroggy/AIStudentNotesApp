"use client";

import { useEffect, useRef, useState } from "react";
import type { LectureState } from "@/lib/lecture";
import { OFFLINE_COPY, TRANSCRIPT_COPY as T } from "./copy";
import { formatMs } from "./format";
import type { MicSource, MicTrack } from "./micSource";
import {
  AudioSkippedNote,
  DemoNotice,
  FatalPanel,
  MismatchPanel,
  OfflineBanner,
  PausedPanel,
  SettingsPanel,
  StopDialog,
  StoppedPanel,
  type PausedVariant,
} from "./Notices";
import type { LectureRuntime } from "./ports";
import { useLectureSettings, type LectureSettings } from "./settings";
import { StatusBar } from "./StatusBar";
import { TldrPanel } from "./TldrPanel";
import { Transcript, type TranscriptHandle } from "./Transcript";
import { useLectureSession } from "./useLectureSession";

export interface LectureScreenProps {
  runtime: LectureRuntime;
  track: MicTrack | null;
  mic: MicSource;
  onBackToCourse: () => void;
  initialLecture?: LectureState;
  initialElapsedMs?: number;
  justStarted?: boolean;
  /** Fixed settings (tests/harness); otherwise read from localStorage. */
  settingsOverride?: Partial<LectureSettings>;
}

/** Live view and every state that replaces or decorates it (Paused, Stopped, mismatch, offline). */
export function LectureScreen(props: LectureScreenProps) {
  const [settings, updateSettings] = useLectureSettings(props.settingsOverride);
  const s = useLectureSession({
    runtime: props.runtime,
    track: props.track,
    mic: props.mic,
    settings,
    initialLecture: props.initialLecture,
    initialElapsedMs: props.initialElapsedMs,
    justStarted: props.justStarted,
  });
  const [stopOpen, setStopOpen] = useState(false);
  const transcript = useRef<TranscriptHandle>(null);
  const headingRef = useRef<HTMLHeadingElement>(null);
  const pauseRef = useRef<HTMLButtonElement>(null);
  const resumeRef = useRef<HTMLButtonElement>(null);
  const stoppedRef = useRef<HTMLHeadingElement>(null);
  const fatalRef = useRef<HTMLHeadingElement>(null);

  const kind = s.raw.kind;
  const serverState = s.lecture.sessionState;

  // Focus rules (UX 3): only after the student's own action; system changes never move focus.
  useEffect(() => {
    if (!s.focusIntent || s.pending) return;
    const target =
      s.focusIntent === "status-heading"
        ? headingRef.current
        : s.focusIntent === "resume-in-panel" && kind === "Paused"
          ? resumeRef.current
          : s.focusIntent === "pause-button" && kind === "Recording"
            ? pauseRef.current
            : s.focusIntent === "stopped-heading" && kind === "Stopped"
              ? stoppedRef.current
              : null;
    if (target) {
      target.focus();
      s.clearFocusIntent();
    }
  });

  // A system auto-stop removes the focused Paused panel: move focus to the Stopped heading only then.
  const prevKind = useRef(kind);
  useEffect(() => {
    if (prevKind.current !== "Stopped" && kind === "Stopped" && (document.activeElement === document.body || document.activeElement === null)) {
      stoppedRef.current?.focus();
    }
    prevKind.current = kind;
  }, [kind]);

  // Optional Alt+ shortcuts: OFF by default (contract 12.5). Alt+P never starts a first recording.
  useEffect(() => {
    if (!settings.shortcuts) return;
    const h = (e: KeyboardEvent) => {
      if (!e.altKey || e.ctrlKey || e.metaKey) return;
      if (e.code === "KeyP") {
        e.preventDefault();
        if (kind === "Recording") void s.pause();
        else if (kind === "Paused") void s.resume();
      } else if (e.code === "KeyJ") {
        e.preventDefault();
        transcript.current?.jumpToLive();
      }
    };
    window.addEventListener("keydown", h);
    return () => window.removeEventListener("keydown", h);
  });

  const lastPause = s.pauses.at(-1);
  const sinceLabel = serverState === "Paused" && lastPause && lastPause.resumedWallMs === null ? formatMs(lastPause.atMs) : null;
  const offline = s.fatal === null && s.connection === "disconnected" && serverState !== "Stopped" && serverState !== "Failed" && serverState !== null;
  const stopped = kind === "Stopped";
  const variant: PausedVariant = s.micLost ? "mic-stopped" : s.reason === "auto_pause_no_audio" ? "auto" : "user";

  let notice: React.ReactNode = null;
  if (s.fatal) {
    notice = <FatalPanel kind={s.fatal} onBack={props.onBackToCourse} headingRef={fatalRef} />;
  } else if (kind === "Mismatch" && !(s.micLost && s.raw.kind === "Mismatch" && s.raw.reason === "server-active-without-live-track") && !s.pending) {
    notice = <MismatchPanel micOff={!s.raw.stopCapture} onCheck={s.checkAgain} onStop={() => setStopOpen(true)} />;
  } else if (stopped) {
    notice = <StoppedPanel reason={s.reason} failed={serverState === "Failed"} onBack={props.onBackToCourse} headingRef={stoppedRef} />;
  } else if (serverState === "Paused" || (s.micLost && kind === "Mismatch")) {
    notice = (
      <PausedPanel
        variant={variant}
        sinceLabel={sinceLabel}
        micIssue={s.micIssue}
        busy={s.pending !== null}
        resumeDisabledReason={offline ? OFFLINE_COPY.resumeDisabledReason : serverState !== "Paused" ? OFFLINE_COPY.resumeDisabledReason : null}
        onResume={() => void s.resume()}
        onStop={() => setStopOpen(true)}
        resumeRef={resumeRef}
      />
    );
  }

  return (
    <div data-lecture-root data-reduce-motion={settings.reduceMotion ? "true" : "false"} className="lecture-root -mx-4">
      <a href="#lecture-transcript" className="sr-only focus:not-sr-only focus:absolute focus:z-30 focus:bg-white focus:p-2">
        {T.skipToTranscript}
      </a>
      <StatusBar
        display={s.display}
        serverPaused={serverState === "Paused"}
        elapsedMs={s.elapsedMs}
        connected={s.connection === "connected"}
        demoSource={props.runtime.demoSource}
        onPause={() => void s.pause()}
        onResume={() => void s.resume()}
        onStop={() => setStopOpen(true)}
        headingRef={headingRef}
        pauseRef={pauseRef}
        settings={<SettingsPanel settings={settings} onChange={updateSettings} />}
      />
      {s.actionError ? (
        <p data-testid="action-error" className="mx-3 mt-3 rounded border-2 border-slate-700 bg-white p-3">
          {s.actionError}
        </p>
      ) : null}
      {props.runtime.demoSource ? <DemoNotice /> : null}
      {s.audioSkippedMs > 0 ? <AudioSkippedNote ms={s.audioSkippedMs} /> : null}
      {offline ? <OfflineBanner stopQueued={s.stopQueued} /> : null}
      {notice}
      <div className="mt-3 grid gap-4 px-3 lg:grid-cols-[minmax(0,2fr)_minmax(0,1fr)]">
        <Transcript
          segments={s.segments}
          lecture={s.lecture}
          pauses={s.pauses}
          announceLines={settings.announceLines}
          handleRef={transcript}
        />
        <TldrPanel summary={s.lecture.summary} onCite={(id) => transcript.current?.focusSegment(id)} />
      </div>
      <StopDialog
        open={stopOpen}
        paused={serverState === "Paused"}
        onCancel={() => setStopOpen(false)}
        onConfirm={() => {
          setStopOpen(false);
          void s.stop();
        }}
      />
    </div>
  );
}
