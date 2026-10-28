"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { ApiError } from "@/lib/api/errors";
import type { CatchUpPayload, WindowMinutes } from "@/lib/lecture";
import { useAnnouncer } from "./Announcer";
import { LOST_TRACK_COPY as C } from "./copy";
import type { LectureAi } from "./ports";

export const CATCHUP_POLL_MS = 2000;
export const CATCHUP_POLL_MAX = 10; // about 20 s, then the extractive view simply stays

export type LostTrackErrorKind = keyof typeof C.errors;

export function lostTrackErrorKind(e: unknown): LostTrackErrorKind {
  if (e instanceof ApiError) {
    if (e.isNetwork) return "network";
    if (e.status === 409) return "invalid_state";
    if (e.status === 429) return "rate_limited";
    if (e.status === 404) return "not_found";
  }
  return "generic";
}

/** An AI version replaces the extractive one for the same id; the reverse never happens. */
export function mergeCatchUp(prev: CatchUpPayload | null, next: CatchUpPayload): CatchUpPayload {
  if (!prev || prev.catchUpId !== next.catchUpId) return next;
  if (prev.kind === "Ai" && next.kind === "Extractive") return prev;
  return next;
}

export type RecapWait = "none" | "waiting" | "slow";

export interface UseLostTrackOptions {
  ai: LectureAi | undefined;
  /** catchup.ready events already reduced from the hub. */
  hubCatchUps: Record<string, CatchUpPayload>;
}

/**
 * I Lost Track: one press = one POST with a fresh Idempotency-Key. The extractive answer comes back at once; if the
 * AI version is pending, the hub's catchup.ready delivers it and a short bounded poll is the fallback. There is no
 * automatic retry of a failed press: the student presses again when ready.
 */
export function useLostTrack({ ai, hubCatchUps }: UseLostTrackOptions) {
  const announcer = useAnnouncer();
  const announcerRef = useRef(announcer);
  announcerRef.current = announcer;

  const [starting, setStarting] = useState<WindowMinutes | null>(null);
  const [result, setResult] = useState<CatchUpPayload | null>(null);
  const [error, setError] = useState<LostTrackErrorKind | null>(null);
  const [pollStopped, setPollStopped] = useState(false);
  const [focusResult, setFocusResult] = useState(false);

  const keyRef = useRef<{ window: WindowMinutes; key: string } | null>(null);
  const startingRef = useRef(false);
  const mounted = useRef(true);
  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);

  const resultRef = useRef<CatchUpPayload | null>(null);
  resultRef.current = result;
  const accept = useCallback((next: CatchUpPayload) => {
    const prev = resultRef.current;
    const merged = mergeCatchUp(prev, next);
    if (merged === prev) return;
    resultRef.current = merged;
    if (prev && prev.catchUpId === merged.catchUpId && prev.kind !== "Ai" && merged.kind === "Ai") announcerRef.current.status(C.recapReady);
    setResult(merged);
  }, []);

  const start = useCallback(
    async (windowMinutes: WindowMinutes) => {
      if (!ai || startingRef.current) return;
      startingRef.current = true;
      setStarting(windowMinutes);
      setError(null);
      setPollStopped(false);
      // Same key only to retry the same press after a network failure; otherwise a fresh one per press.
      if (keyRef.current?.window !== windowMinutes) keyRef.current = { window: windowMinutes, key: crypto.randomUUID() };
      const key = keyRef.current.key;
      try {
        const payload = await ai.startLostTrack(windowMinutes, key);
        keyRef.current = null;
        if (!mounted.current) return;
        setResult(payload);
        setFocusResult(true);
        announcerRef.current.status(C.catchUpReady);
      } catch (e) {
        const kind = lostTrackErrorKind(e);
        if (kind !== "network") keyRef.current = null;
        if (!mounted.current) return;
        setError(kind);
        announcerRef.current.status(C.errors[kind]);
      } finally {
        startingRef.current = false;
        if (mounted.current) setStarting(null);
      }
    },
    [ai],
  );

  // Hub push (catchup.ready) for the catch-up on screen.
  const hubVersion = result ? hubCatchUps[result.catchUpId] : undefined;
  useEffect(() => {
    if (hubVersion) accept(hubVersion);
  }, [hubVersion, accept]);

  // Bounded poll fallback while the AI version is pending.
  const pollId = result && result.kind === "Extractive" && result.aiState === "Pending" ? result.catchUpId : null;
  useEffect(() => {
    if (!ai || !pollId) return;
    let n = 0;
    let stopped = false;
    let inFlight = false;
    const h = setInterval(async () => {
      if (inFlight || stopped) return;
      if (++n > CATCHUP_POLL_MAX) {
        stopped = true;
        clearInterval(h);
        setPollStopped(true);
        return;
      }
      inFlight = true;
      try {
        const next = await ai.getCatchUp(pollId);
        if (stopped) return;
        if (next === null) {
          stopped = true;
          setPollStopped(true);
        } else accept(next);
      } catch {
        if (!stopped) {
          stopped = true;
          setPollStopped(true); // no retry loop: the extractive view stays
        }
      } finally {
        inFlight = false;
      }
    }, CATCHUP_POLL_MS);
    return () => {
      stopped = true;
      clearInterval(h);
    };
  }, [ai, pollId, accept]);

  const dismiss = useCallback(() => {
    setResult(null);
    setError(null);
    setPollStopped(false);
    setFocusResult(false);
  }, []);

  const wait: RecapWait = !result || result.kind === "Ai" || result.aiState !== "Pending" ? "none" : pollStopped ? "slow" : "waiting";

  return { starting, result, error, wait, start, dismiss, focusResult, clearFocusResult: () => setFocusResult(false) };
}
