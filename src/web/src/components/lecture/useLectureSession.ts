"use client";

import { useCallback, useEffect, useReducer, useRef, useState } from "react";
import {
  deriveRecordingIndicator,
  initialLectureState,
  lectureReducer,
  ResumeController,
  resumeSeq,
  selectSegments,
  type LectureState,
  type RecordingIndicator,
} from "@/lib/lecture";
import { useAnnouncer } from "./Announcer";
import { FinalsBatcher, TLDR_INTERVAL_MS } from "./announcerQueue";
import { ANNOUNCE } from "./copy";
import type { MicFailure, MicSource, MicTrack } from "./micSource";
import type { LectureRuntime } from "./ports";
import type { LectureSettings } from "./settings";

export type Pending = null | "pausing" | "resuming" | "stopping";
export type FocusIntent = null | "status-heading" | "resume-in-panel" | "pause-button" | "stopped-heading";
export type DisplayIndicator = RecordingIndicator | { kind: "Pending"; pending: Exclude<Pending, null> };

export const PENDING_MAX_MS = 3000;
export const OFFLINE_AUTO_STOP_MS = 60_000;
const SYSTEM_REASONS = new Set(["auto_pause_no_audio", "auto_stop_paused_timeout", "hard_cap", "abandoned"]);

export interface UseLectureSessionOptions {
  runtime: LectureRuntime;
  track: MicTrack | null;
  mic: MicSource;
  settings: LectureSettings;
  initialLecture?: LectureState;
  initialElapsedMs?: number;
  /** Focus the status heading and say "Recording started." once (coming from the start flow). */
  justStarted?: boolean;
}

export interface PauseRecord {
  atMs: number;
  wallMs: number;
  resumedWallMs: number | null;
}

/**
 * Orchestrates one lecture screen: reducer + ResumeController + local mic track + user actions.
 * The visible indicator comes from deriveRecordingIndicator (server state AND track AND connection),
 * never from a button press (contract C-16). State changes are only believed when they arrive as events.
 */
export function useLectureSession(opts: UseLectureSessionOptions) {
  const { runtime, mic, settings } = opts;
  const announcer = useAnnouncer();
  const announcerRef = useRef(announcer);
  announcerRef.current = announcer;

  const [lecture, dispatch] = useReducer(lectureReducer, undefined, () => opts.initialLecture ?? initialLectureState(runtime.sessionId));
  const lectureRef = useRef(lecture);
  lectureRef.current = lecture;

  const [snap, setSnap] = useState<{ status: string }>({ status: "idle" });
  const rcRef = useRef<ResumeController | null>(null);

  const [track, setTrackState] = useState<MicTrack | null>(opts.track);
  const [, bump] = useReducer((x: number) => x + 1, 0);
  const trackRef = useRef(track);
  trackRef.current = track;

  const [pending, setPendingState] = useState<Pending>(null);
  const pendingRef = useRef<Pending>(null);
  const setPending = useCallback((p: Pending) => {
    pendingRef.current = p;
    setPendingState(p);
  }, []);
  const [micLost, setMicLost] = useState(false);
  const [micIssue, setMicIssue] = useState<MicFailure | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [stopQueued, setStopQueued] = useState(false);
  const [focusIntent, setFocusIntent] = useState<FocusIntent>(opts.justStarted ? "status-heading" : null);
  const [elapsedMs, setElapsedMs] = useState(opts.initialElapsedMs ?? 0);
  const elapsedRef = useRef(elapsedMs);
  const [pauses, setPauses] = useState<PauseRecord[]>([]);

  const stopTrack = useCallback(() => {
    trackRef.current?.stop();
    bump();
  }, []);

  // --- transport / resume ---------------------------------------------------------------------
  useEffect(() => {
    const t = runtime.transport;
    const rc = new ResumeController({
      sessionId: runtime.sessionId,
      connect: () => t.connect(),
      join: (r) => t.join(r),
      fetchTranscript: (a, l) => t.fetchTranscript(a, l),
      resumePoint: () => ({ lastSeq: resumeSeq(lectureRef.current), lastSummaryRevision: lectureRef.current.summary?.revision ?? null }),
      onJoined: (response, lastSeqSent) => dispatch({ kind: "joined", response, lastSeqSent }),
      onEvents: (events) => dispatch({ kind: "events", events }),
      onFastForward: (throughSeq) => dispatch({ kind: "fastForward", throughSeq }),
      onStatus: (s) => setSnap({ status: s.status }),
      setTimeout: (fn, ms) => setTimeout(fn, ms),
      clearTimeout: (h) => clearTimeout(h as ReturnType<typeof setTimeout>),
      random: Math.random,
    });
    rcRef.current = rc;
    const unsub = t.subscribe((event) => dispatch({ kind: "event", event }));
    const unDrop = t.onDisconnect(() => rc.notifyDisconnected());
    rc.start();
    return () => {
      unsub();
      unDrop();
      rc.stop();
    };
  }, [runtime]);

  const connection = snap.status === "connected" ? "connected" : "disconnected";
  const trackSnap = track ? { readyState: track.readyState, enabled: track.enabled, muted: track.muted } : null;
  // A server-initiated end with a documented system reason (contract 12.1) is expected: the hot track is
  // stopped below, and the indicator is derived as if it were already off so no false mismatch flashes.
  const systemEnded = lecture.sessionState !== null && lecture.sessionState !== "Active" && SYSTEM_REASONS.has(lecture.sessionStateReason ?? "");
  const derived = deriveRecordingIndicator({ serverState: lecture.sessionState, connection, track: systemEnded ? null : trackSnap });
  // A mismatch stays on screen (after capture is stopped the indicator alone would look consistent)
  // until the student presses "Check again" (UX section 8).
  const [latched, setLatched] = useState<Extract<RecordingIndicator, { kind: "Mismatch" }> | null>(null);
  const raw: RecordingIndicator = latched ?? derived;
  const display: DisplayIndicator = pending ? { kind: "Pending", pending } : raw;
  const trackLive = trackSnap?.readyState === "live";

  // --- device loss ----------------------------------------------------------------------------
  useEffect(() => {
    if (!track) return;
    return track.onEnded(() => {
      bump();
      if (pendingRef.current === null && lectureRef.current.sessionState === "Active") {
        setMicLost(true);
        announcerRef.current.alert("mic-lost", ANNOUNCE.micStopped);
        void runtime.actions.pause().catch(() => undefined);
      }
    });
  }, [track, runtime]);

  useEffect(() => {
    if (systemEnded && trackRef.current?.readyState === "live") stopTrack();
  }, [systemEnded, trackLive, stopTrack]);

  // --- mismatch: stop capture immediately, alert once -----------------------------------------
  useEffect(() => {
    if (pending) return;
    const a = announcerRef.current;
    if (derived.kind === "Mismatch") {
      if (derived.stopCapture && trackRef.current?.readyState === "live") stopTrack();
      if (!(micLost && derived.reason === "server-active-without-live-track")) {
        a.alert("mismatch", ANNOUNCE.mismatch);
        setLatched((l) => l ?? derived);
      }
    } else if (!latched) {
      a.clearEpisode("mismatch");
    }
  }, [derived.kind, derived.kind === "Mismatch" ? derived.reason : null, pending, micLost, stopTrack, latched]); // eslint-disable-line react-hooks/exhaustive-deps

  // --- disconnect episode, reconnect, 60 s client auto-stop of the local track -----------------
  const wasOffline = useRef(false);
  useEffect(() => {
    const a = announcerRef.current;
    if (connection === "disconnected") {
      if (lecture.sessionState === "Active" && !pending && (snap.status === "reconnecting" || snap.status === "failed")) {
        a.alert("disconnect", ANNOUNCE.disconnected);
        wasOffline.current = true;
      }
    } else {
      a.clearEpisode("disconnect");
      if (wasOffline.current && raw.kind === "Recording") {
        wasOffline.current = false;
        a.status(ANNOUNCE.backOnline);
      } else if (wasOffline.current && raw.kind !== "Recording") {
        wasOffline.current = false;
      }
    }
  }, [connection, snap.status, lecture.sessionState, pending, raw.kind]);

  useEffect(() => {
    if (connection !== "disconnected" || !trackLive) return;
    const h = setTimeout(stopTrack, OFFLINE_AUTO_STOP_MS);
    return () => clearTimeout(h);
  }, [connection, trackLive, stopTrack]);

  // --- queued stop (offline) ------------------------------------------------------------------
  useEffect(() => {
    if (!stopQueued || connection !== "connected") return;
    setStopQueued(false);
    setPending("stopping");
    setFocusIntent("stopped-heading");
    runtime.actions.stop().catch(() => fail());
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [stopQueued, connection, runtime]);

  // --- pending resolution ---------------------------------------------------------------------
  useEffect(() => {
    const s = lecture.sessionState;
    if (pending === "pausing" && s !== "Active") setPending(null);
    if (pending === "resuming" && s === "Active" && trackLive) setPending(null);
    if (pending === "stopping" && (s === "Stopped" || s === "Failed")) setPending(null);
    if (s === "Stopped" || s === "Failed") setLatched(null); // an ended session has nothing left to mismatch
  }, [pending, lecture.sessionState, trackLive, setPending]);

  useEffect(() => {
    if (!pending) return;
    const h = setTimeout(() => setPending(null), PENDING_MAX_MS); // then show what is true
    return () => clearTimeout(h);
  }, [pending, setPending]);

  // --- announcements for state changes the student caused or the system caused ----------------
  const prevKind = useRef<DisplayIndicator["kind"] | null>(null);
  const startedAnnounced = useRef(false);
  useEffect(() => {
    const a = announcerRef.current;
    const prev = prevKind.current;
    prevKind.current = display.kind;
    if (display.kind === "Pending") return;
    if (opts.justStarted && !startedAnnounced.current && display.kind === "Recording") {
      startedAnnounced.current = true;
      a.status(ANNOUNCE.started);
    }
    if (prev === "Pending" || prev === "Recording" || prev === "Paused") {
      if (display.kind === "Paused" && focusIntent === "resume-in-panel") a.status(ANNOUNCE.paused);
      if (display.kind === "Recording" && focusIntent === "pause-button") a.status(ANNOUNCE.resumed);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [display.kind]);

  const reason = lecture.sessionStateReason;
  useEffect(() => {
    const a = announcerRef.current;
    if (lecture.sessionState === "Paused" && reason === "auto_pause_no_audio") a.alert("auto-pause", ANNOUNCE.autoPaused);
    if (lecture.sessionState === "Stopped" && (reason === "auto_stop_paused_timeout" || reason === "hard_cap" || reason === "abandoned"))
      a.alert("auto-stop", ANNOUNCE.autoStopped);
  }, [lecture.sessionState, reason]);

  // --- pause records (divider in transcript) --------------------------------------------------
  const prevRaw = useRef<RecordingIndicator["kind"] | null>(null);
  useEffect(() => {
    const prev = prevRaw.current;
    prevRaw.current = raw.kind;
    if (raw.kind === "Paused" && prev !== "Paused" && prev !== null)
      setPauses((p) => [...p, { atMs: elapsedRef.current, wallMs: Date.now(), resumedWallMs: null }]);
    if (raw.kind === "Recording" && prev === "Paused")
      setPauses((p) => p.map((r, i) => (i === p.length - 1 && r.resumedWallMs === null ? { ...r, resumedWallMs: Date.now() } : r)));
    if (raw.kind === "Recording") setMicLost(false);
  }, [raw.kind]);

  // --- elapsed active time --------------------------------------------------------------------
  useEffect(() => {
    if (display.kind !== "Recording") return;
    let last = Date.now();
    const h = setInterval(() => {
      const n = Date.now();
      elapsedRef.current += n - last;
      last = n;
      setElapsedMs(elapsedRef.current);
    }, 1000);
    return () => clearInterval(h);
  }, [display.kind]);

  // --- opt-in announcements: finals (batched) and TL;DR (rate limited) ------------------------
  const batcher = useRef(new FinalsBatcher());
  const announcedIds = useRef<Set<string> | null>(null);
  const segments = selectSegments(lecture);
  useEffect(() => {
    const a = announcerRef.current;
    if (!settings.announceLines) {
      announcedIds.current = new Set(segments.map((s) => s.segmentId));
      a.setFinalsText("");
      return;
    }
    if (announcedIds.current === null) announcedIds.current = new Set(segments.map((s) => s.segmentId));
    const fresh = segments.filter((s) => !announcedIds.current!.has(s.segmentId));
    fresh.forEach((s) => announcedIds.current!.add(s.segmentId));
    if (fresh.length) batcher.current.add(fresh.map((s) => s.text));
    const flush = () => {
      const text = batcher.current.take(Date.now());
      if (text) announcerRef.current.setFinalsText(text);
    };
    flush();
    const due = batcher.current.dueIn(Date.now());
    if (due === null) return;
    const h = setTimeout(flush, due);
    return () => clearTimeout(h);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [segments.length, settings.announceLines, lecture.segments]);

  const lastTldrAt = useRef(-Infinity);
  const lastRevision = useRef<number | null>(lecture.summary?.revision ?? null);
  useEffect(() => {
    const rev = lecture.summary?.revision ?? null;
    if (rev === lastRevision.current) return;
    lastRevision.current = rev;
    if (!settings.announceTldr || rev === null) return;
    const wait = lastTldrAt.current + TLDR_INTERVAL_MS - Date.now();
    const fire = () => {
      lastTldrAt.current = Date.now();
      announcerRef.current.status(ANNOUNCE.tldrUpdated);
    };
    if (wait <= 0) return fire();
    const h = setTimeout(fire, wait);
    return () => clearTimeout(h);
  }, [lecture.summary?.revision, settings.announceTldr]);

  // --- actions --------------------------------------------------------------------------------
  const fail = useCallback(() => {
    setPending(null);
    setFocusIntent(null);
    setActionError(ANNOUNCE.actionFailed);
    announcerRef.current.status(ANNOUNCE.actionFailed);
  }, [setPending]);

  const pause = useCallback(async () => {
    if (pendingRef.current || lectureRef.current.sessionState !== "Active") return;
    setActionError(null);
    setPending("pausing");
    setFocusIntent("resume-in-panel");
    stopTrack(); // local track first (contract section 9)
    try {
      await runtime.actions.pause();
    } catch {
      fail();
    }
  }, [runtime, stopTrack, fail, setPending]);

  const resume = useCallback(async () => {
    if (pendingRef.current || lectureRef.current.sessionState !== "Paused") return;
    setActionError(null);
    setMicIssue(null);
    setPending("resuming");
    setFocusIntent("pause-button");
    try {
      await runtime.actions.resume();
    } catch {
      fail();
      setFocusIntent(null);
      return;
    }
    const r = await mic.request(); // only ever from this press
    if (!r.ok) {
      setMicIssue(r.reason);
      setPending(null);
      setFocusIntent(null);
      await runtime.actions.pause().catch(() => undefined);
      return;
    }
    setTrackState(r.track);
    trackRef.current = r.track;
    setMicLost(false);
  }, [runtime, mic, fail, setPending]);

  const stop = useCallback(async () => {
    if (pendingRef.current === "stopping") return;
    stopTrack(); // mic off immediately, even offline
    setFocusIntent("stopped-heading");
    if (rcRef.current?.snapshot().status !== "connected") {
      setStopQueued(true);
      return;
    }
    setPending("stopping");
    try {
      await runtime.actions.stop();
    } catch {
      fail();
    }
  }, [runtime, stopTrack, fail, setPending]);

  /** Rebuild state from the server (Join without a resume point); never restarts capture. */
  const checkAgain = useCallback(() => {
    stopTrack();
    setLatched(null);
    announcerRef.current.clearEpisode("mismatch");
    dispatch({ kind: "reset", sessionId: runtime.sessionId });
    rcRef.current?.start();
  }, [runtime, stopTrack]);

  // --- title prefix ---------------------------------------------------------------------------
  useEffect(() => {
    const label = display.kind === "Recording" ? "Recording | " : display.kind === "Paused" ? "Paused | " : display.kind === "Stopped" ? "" : "Not recording | ";
    const base = document.title.replace(/^(Recording|Paused|Not recording) [|]\s*/, "");
    document.title = label + base;
  }, [display.kind]);

  return {
    lecture,
    segments,
    raw,
    display,
    pending,
    connection: connection as "connected" | "disconnected",
    trackLive,
    elapsedMs,
    micLost,
    micIssue,
    actionError,
    stopQueued,
    pauses,
    reason,
    focusIntent,
    clearFocusIntent: () => setFocusIntent(null),
    pause,
    resume,
    stop,
    checkAgain,
  };
}

export type LectureSession = ReturnType<typeof useLectureSession>;
