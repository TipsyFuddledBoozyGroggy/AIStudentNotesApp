"use client";

import { useEffect, useRef, useState } from "react";
import type { LectureState } from "@/lib/lecture";
import { useAnnouncer } from "./Announcer";
import { ANNOUNCE, OFFLINE_COPY, TRANSCRIPT_COPY as T } from "./copy";
import { formatMs } from "./format";
import type { MicSource, MicTrack } from "./micSource";
import {
  AudioSkippedNote,
  DemoNotice,
  FatalPanel,
  GUTTERS,
  MismatchPanel,
  NOTICE_SLOT,
  OfflineBanner,
  PausedPanel,
  SettingsPanel,
  StopDialog,
  StoppedPanel,
  type PausedVariant,
} from "./Notices";
import { NotesPanel } from "./NotesPanel";
import type { LectureRuntime } from "./ports";
import { usePrefs } from "../PrefsProvider";
import { loadStopMove, saveStopMove, useLectureSettings, type LectureSettings } from "./settings";
import { StatusBar } from "./StatusBar";
import { StopMoveDialog } from "./StopMoveDialog";
import { focusQuickRecap, QuickRecapPanel, QuickRecapResult } from "./QuickRecapPanel";
import { TldrPanel } from "./TldrPanel";
import { matchesQuery, Transcript, type TranscriptHandle } from "./Transcript";
import { useLectureSession } from "./useLectureSession";
import { useQuickRecap } from "./useQuickRecap";
import { useWide } from "./useWide";

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
  /** Course name for the page heading (the only h1 on the live screen). */
  courseName?: string;
  /**
   * PO-19, live route only (docs/ux/stop-redirect.md): moves to the lecture's own page (the caller writes the arrival
   * marker, then replaces the URL). Absent (the reopen page): the student's Stop never opens the move notice.
   */
  onOpenPage?: () => void;
  /** PO-19, live route only: the lecture's own page, linked from the Stopped note (push semantics). */
  openPageHref?: string;
  /** PO-19: the signed-in user's id, which scopes the "Don't show this again" preference. Unknown: always ask. */
  userId?: string | null;
  /** PO-19: this page was opened by the move (one-shot marker): focus the Stopped heading and say so once. */
  arrivedFromStop?: boolean;
}

/** A Stopped reason the student's own Stop produces (system reasons never open the move notice). */
const userStopReason = (r: string | null) => r === null || r === "" || r === "user";

/** Live view and every state that replaces or decorates it (Paused, Stopped, mismatch, offline). */
const SKIP =
  "sr-only focus:not-sr-only focus:fixed focus:left-2 focus:top-2 focus:z-50 focus:rounded-control focus:border-2 focus:border-accent focus:bg-surface focus:px-4 focus:py-3 focus:font-semibold focus:text-accent";

export function LectureScreen(props: LectureScreenProps) {
  const [settings, updateSettings] = useLectureSettings(props.settingsOverride);
  const { prefs, update: updatePrefs } = usePrefs();
  const reduceMotion = settings.reduceMotion || prefs.reduceMotion;
  // Focus mode (section 4): student-chosen, remembered, never automatic. Removes chrome, never lowers text contrast.
  const focus = prefs.focusMode;
  const wide = useWide();
  const tldrInline = !wide || focus;
  const s = useLectureSession({
    runtime: props.runtime,
    track: props.track,
    mic: props.mic,
    settings,
    initialLecture: props.initialLecture,
    initialElapsedMs: props.initialElapsedMs,
    justStarted: props.justStarted,
    arrivedFromStop: props.arrivedFromStop,
  });
  const announcer = useAnnouncer();
  const [stopOpen, setStopOpen] = useState(false);
  // PO-19 move notice (stop-redirect.md sections 1 and 3). Armed only by the confirm press of a Stop that is not queued
  // (connected, no fatal state); dropped when the pending window closes without Stopped, on a queued stop, a fatal state
  // or Failed. No timer of its own: the existing PENDING_MAX_MS bound on `pending` is the window.
  const moveArmed = useRef(false);
  const moveFired = useRef(false);
  const pageOpened = useRef(false);
  const arrivalToAnnounce = useRef(!!props.arrivedFromStop);
  const [moveOpen, setMoveOpen] = useState(false);
  const openPage = () => {
    if (pageOpened.current) return; // one move per screen, whatever is pressed twice
    pageOpened.current = true;
    props.onOpenPage?.();
  };
  const [query, setQuery] = useState("");
  const transcript = useRef<TranscriptHandle>(null);
  // Quick recap state lives here because its two halves render in different places: the window buttons in the rail or
  // the dock, the result as a sheet in the transcript column (desktop) or inside the dock's bottom sheet (phone).
  const qr = useQuickRecap({ ai: props.runtime.ai, hubCatchUps: s.lecture.catchUps });
  const resultInColumn = wide && props.runtime.ai !== undefined;
  const onCite = (id: string) => transcript.current?.focusSegment(id);
  const onRecapDone = () => document.getElementById("lecture-transcript")?.focus();
  const headingRef = useRef<HTMLHeadingElement>(null);
  const pauseRef = useRef<HTMLButtonElement>(null);
  const resumeRef = useRef<HTMLButtonElement>(null);
  const stoppedRef = useRef<HTMLHeadingElement>(null);
  const fatalRef = useRef<HTMLHeadingElement>(null);

  const kind = s.raw.kind;
  const serverState = s.lecture.sessionState;

  // PO-19: the armed Stop reached Stopped (declared before the focus rules so it decides first in the same commit). The
  // notice replaces the Stopped-heading focus that press would otherwise get; with "Don't show this again" saved, the
  // page moves at once and no dialog renders at all.
  useEffect(() => {
    if (!moveArmed.current) return;
    if (kind === "Stopped" && serverState === "Stopped" && s.fatal === null && userStopReason(s.reason)) {
      moveArmed.current = false;
      moveFired.current = true;
      s.clearFocusIntent();
      if (loadStopMove(props.userId)) openPage();
      else setMoveOpen(true);
    } else if (s.pending === null || s.stopQueued || s.fatal !== null || serverState === "Failed") {
      moveArmed.current = false;
    }
    // Re-checked on every change that can settle an armed Stop (the arming press itself changes `pending` or `stopQueued`).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [kind, serverState, s.fatal, s.reason, s.pending, s.stopQueued, props.userId]);

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
      // PO-19: arriving through the move, one status announcement when focus lands on the Stopped heading.
      if (target === stoppedRef.current && arrivalToAnnounce.current) {
        arrivalToAnnounce.current = false;
        announcer.status(ANNOUNCE.movedAfterStop);
      }
    }
  });

  // A system auto-stop removes the focused Paused panel: move focus to the Stopped heading only then. PO-19: only for a
  // lecture this screen has seen running (Active or Paused); loading an already-stopped lecture (a reload or a reopen, where
  // the hub's Join turns "not connected" into Stopped) is not a stop and moves no focus (stop-redirect.md sections 3, 5).
  const seenRunning = useRef(false);
  useEffect(() => {
    if (serverState === "Active" || serverState === "Paused") seenRunning.current = true;
  }, [serverState]);
  const prevKind = useRef(kind);
  useEffect(() => {
    if (moveFired.current) {
      // The move notice (or the move) takes this Stop's focus instead (PO-19).
      moveFired.current = false;
      prevKind.current = kind;
      return;
    }
    if (seenRunning.current && prevKind.current !== "Stopped" && kind === "Stopped" && (document.activeElement === document.body || document.activeElement === null)) {
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
      } else if (e.code === "KeyR" && props.runtime.ai) {
        e.preventDefault();
        focusQuickRecap();
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

  const tldr = (
    <TldrPanel
      summary={s.lecture.summary}
      status={s.lecture.summaryStatus}
      collapsible={tldrInline}
      live={!stopped && s.fatal === null}
      onCite={(id) => transcript.current?.focusSegment(id)}
    />
  );

  let notice: React.ReactNode = null;
  if (s.fatal) {
    notice = <FatalPanel kind={s.fatal} onBack={props.onBackToCourse} headingRef={fatalRef} />;
  } else if (kind === "Mismatch" && !(s.micLost && s.raw.kind === "Mismatch" && s.raw.reason === "server-active-without-live-track") && !s.pending) {
    notice = <MismatchPanel micOff={!s.raw.stopCapture} onCheck={s.checkAgain} onStop={() => setStopOpen(true)} />;
  } else if (stopped) {
    notice = <StoppedPanel reason={s.reason} failed={serverState === "Failed"} onBack={props.onBackToCourse} headingRef={stoppedRef} openPageHref={props.openPageHref} />;
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
    <div data-lecture-root data-reduce-motion={reduceMotion ? "true" : "false"} className="lecture-root pb-8">
      <a href="#lecture-transcript" className={SKIP}>
        {T.skipToTranscript}
      </a>
      {props.runtime.ai ? (
        <a
          href="#lecture-quick-recap"
          className={SKIP}
          onClick={(e) => {
            e.preventDefault();
            focusQuickRecap();
          }}
        >
          {T.skipToQuickRecap}
        </a>
      ) : null}
      <a href="#lecture-tldr" className={SKIP}>
        {T.skipToTldr}
      </a>
      <StatusBar
        display={s.display}
        serverPaused={serverState === "Paused"}
        elapsedMs={s.elapsedMs}
        connected={s.connection === "connected"}
        onPause={() => void s.pause()}
        onResume={() => void s.resume()}
        onStop={() => setStopOpen(true)}
        headingRef={headingRef}
        pauseRef={pauseRef}
        settings={<SettingsPanel settings={settings} onChange={updateSettings} />}
        focusToggle={
          stopped ? null : (
          <button type="button" aria-pressed={focus} className="btn-tertiary hidden lg:inline-flex" onClick={() => updatePrefs({ focusMode: !focus })}>
            <svg width="16" height="16" viewBox="0 0 16 16" aria-hidden="true" focusable="false">
              {focus ? (
                <path d="M3 8.5 6.5 12 13 4.5" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
              ) : (
                <rect x="4" y="2.5" width="8" height="11" rx="1" fill="none" stroke="currentColor" strokeWidth="1.8" />
              )}
            </svg>
            Focus
          </button>
          )
        }
        // Review S2: the Stopped/Failed/fatal panels already offer "Back to course"; a second "Leave" exit was removed.
        showSeconds={prefs.showSeconds}
      />
      <h1 className="sr-only">{props.courseName ? `Lecture: ${props.courseName}` : "Lecture"}</h1>
      {s.actionError ? (
        <div className={NOTICE_SLOT}>
          <p data-testid="action-error" className="note note-notice">
            {s.actionError}
          </p>
        </div>
      ) : null}
      {/* Safety state first: directly under the lecture bar (design-direction 5.4), then informational notes, 24px apart.
          The Demo note is one line with its body behind "Details" in every state (PO-18c); it never comes first in a
          safety state. */}
      {offline ? <OfflineBanner stopQueued={s.stopQueued} /> : null}
      {notice}
      {props.runtime.demoSource ? <DemoNotice /> : null}
      {s.audioSkippedMs > 0 ? <AudioSkippedNote ms={s.audioSkippedMs} /> : null}
      {/* Lecture template (redesign-round2 2.3): gutters 16 / 24 / 32 on the wrapper; the inner element's box IS the column,
          so the fixed/sticky dock with the same max-width shares its edges (A11). Standard: transcript 1fr | aside 22.5rem,
          gap 40, 72rem (80rem from 1536px); from 1280 a 5rem margin track leads, where the sheets hang their timestamps
          (slice 3). Focus: one 52rem column, margin track from 1280 too. The left column is a subgrid so a sheet spans the
          margin and the transcript tracks while the notes and the toolbar start on the transcript track. */}
      <div className={`lecture-columns mt-4 ${GUTTERS} ${props.runtime.ai ? (focus ? "pb-44" : "pb-44 lg:pb-0") : ""}`}>
      <div
        data-layout={focus ? "focus" : "standard"}
        className={`mx-auto grid gap-6 ${
          focus
            ? "max-w-focus xl:grid-cols-[var(--margin-col)_minmax(0,1fr)] xl:gap-x-10"
            : "max-w-content lg:grid-cols-[minmax(0,1fr)_var(--lecture-aside-w)] lg:gap-10 xl:grid-cols-[var(--margin-col)_minmax(0,1fr)_var(--lecture-aside-w)] 2xl:max-w-lecture"
        }`}
      >
        <div className="min-w-0 space-y-6 xl:col-span-2 xl:grid xl:grid-cols-subgrid xl:gap-y-6 xl:space-y-0">
          {tldrInline ? <div className="min-w-0 xl:col-start-2">{tldr}</div> : null}
          {/* Make notes (PO-18d, slice 8a): only while the server says Stopped (never Failed, Active, Paused, a mismatch or a
              fatal state), first in the transcript column before the search tools. It places itself on the grid: the
              block on the transcript track, the Ready sheet across the margin and transcript tracks like the recap sheet. */}
          {stopped && serverState === "Stopped" && s.fatal === null && props.runtime.ai ? (
            <NotesPanel ai={props.runtime.ai} summary={s.lecture.summary} onSummary={s.ingestSummary} onCite={onCite} />
          ) : null}
          {stopped ? (
            // Review tools (design-direction slice 8, narrow): only once the lecture has ended, so nothing moves during it.
            // Toolbar grid (A10 / R9): the search field takes the column, the action ends on the column edge, helper below.
            <section aria-label="Review this lecture" className="grid gap-x-6 gap-y-3 sm:grid-cols-[minmax(0,1fr)_auto] xl:col-start-2">
              <div className="min-w-0">
                <label htmlFor="lecture-search" className="field-label">
                  Search this lecture
                </label>
                <input id="lecture-search" type="search" className="field" value={query} autoComplete="off" aria-describedby="lecture-search-count" onChange={(e) => setQuery(e.target.value)} />
                <p id="lecture-search-count" className="help mt-1">
                  {query.trim()
                    ? `${s.segments.filter((x) => matchesQuery(x.text, query)).length} of ${s.segments.length} lines match.`
                    : `${s.segments.length} ${s.segments.length === 1 ? "line" : "lines"}.`}
                </p>
              </div>
              {/* Sits level with the field: self-end minus the help line (24) and its gap (4). */}
              <div className="sm:self-end sm:pb-7">
                <button type="button" className="btn-secondary" aria-disabled="true" aria-describedby="export-reason">
                  Export to Google Docs
                </button>
              </div>
              <p id="export-reason" className="help max-w-prose sm:col-span-2">
                Not built yet. Nothing is sent anywhere. When it exists it will ask you first.
              </p>
            </section>
          ) : null}
          {/* Desktop: the open recap is a sheet above the transcript, in the student's reading column (5.2). */}
          {resultInColumn && qr.result ? (
            <div className="min-w-0 xl:col-span-2">
              <QuickRecapResult qr={qr} level={2} sheet onCite={onCite} onDone={onRecapDone} />
            </div>
          ) : null}
          <div className="min-w-0 xl:col-span-2">
            <Transcript
              query={stopped ? query : ""}
              segments={s.segments}
              lecture={s.lecture}
              pauses={s.pauses}
              announceLines={settings.announceLines}
              handleRef={transcript}
            />
          </div>
        </div>
        {focus ? (
          <QuickRecapPanel ai={props.runtime.ai} qr={qr} docked resultInside={!resultInColumn} onCite={onCite} onDone={onRecapDone} />
        ) : (
          // The rail: one ledger with Quick recap and the TL;DR as its rows (desktop). Below lg it has no box of its own
          // (`contents`): the dock inside is fixed to the bottom and the TL;DR sits above the transcript.
          <div data-lecture-rail className="lecture-rail contents min-w-0 lg:block">
            <QuickRecapPanel ai={props.runtime.ai} qr={qr} resultInside={!resultInColumn} onCite={onCite} onDone={onRecapDone} />
            {tldrInline ? null : tldr}
          </div>
        )}
      </div>
      </div>
      <StopDialog
        open={stopOpen}
        paused={serverState === "Paused"}
        onCancel={() => setStopOpen(false)}
        onConfirm={() => {
          setStopOpen(false);
          // PO-19: arm the move notice only for a Stop that goes to the server now (not queued offline, no fatal state).
          moveArmed.current = props.onOpenPage !== undefined && s.connection === "connected" && s.fatal === null && s.pending !== "stopping";
          void s.stop();
        }}
      />
      {moveOpen ? (
        <StopMoveDialog
          onOpen={(remember) => {
            if (remember) saveStopMove(props.userId, true);
            openPage();
          }}
          onStay={() => {
            // The checkbox value is discarded: staying is not "move me without asking next time".
            setMoveOpen(false);
            stoppedRef.current?.focus();
          }}
        />
      ) : null}
    </div>
  );
}
