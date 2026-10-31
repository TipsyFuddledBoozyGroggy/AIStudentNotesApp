"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { ApiError } from "@/lib/api/errors";
import type { LectureNotes } from "@/lib/lecture";
import { useAnnouncer } from "./Announcer";
import { NOTES_COPY as C } from "./copy";
import type { LectureAi } from "./ports";

/** Poll cadence while Pending (spec 1.5): every 3 s for the first minute, then every 10 s. No visible timer. */
export const NOTES_POLL_FAST_MS = 3000;
export const NOTES_POLL_SLOW_MS = 10_000;
export const NOTES_POLL_FAST_FOR_MS = 60_000;
/**
 * Upper bound on one poll run. The server ends every press itself (a queued press after 1,800 s, a running one after the
 * stale limit, 820 s at the defaults), so this bound is never the normal exit; it only stops a client that was left open
 * on a server that never answers. After it the block offers "Try again" (a fresh GET).
 */
export const NOTES_POLL_MAX_MS = 45 * 60_000;

export type NotesProblem = keyof typeof C.errors;

export type NotesView =
  | { kind: "checking" }
  /** 409 invalid_state on GET: nothing pressed yet, the entry point. */
  | { kind: "none" }
  /** The GET could not be answered: the entry point plus a calm note and "Try again". */
  | { kind: "checkFailed" }
  | { kind: "notFound" }
  | { kind: "notes"; notes: LectureNotes };

export function notesProblem(e: unknown): NotesProblem {
  if (e instanceof ApiError) {
    if (e.isNetwork) return "network";
    if (e.status === 429) return "rate_limited";
    if (e.status === 409) return "invalid_state";
    if (e.status === 404) return "not_found";
  }
  return "generic";
}

const announceFor = (n: LectureNotes): string | null =>
  n.status === "Pending"
    ? C.pendingAnnounce
    : n.status === "Ready"
      ? C.readyAnnounce
      : n.status === "NotNeeded"
        ? C.notNeededAnnounce
        : C.unavailableAnnounce;

/** A new press is offered only after these (contract 17 item 4); Ready and NotNeeded are final for the session. */
export const canPressAgain = (n: LectureNotes) => n.status === "Unavailable" || n.status === "BudgetLimited";

export type NotesFocus = null | "heading" | "problem";

/**
 * Make notes after Stop (PO-18d). Mount: exactly one GET. A POST happens ONLY from `press()`, i.e. from a deliberate click;
 * nothing here schedules, retries or repeats a POST. One press = one fresh Idempotency-Key; the same key is kept only after
 * a network failure (the request may have landed: the next press of the same intent replays it), and dropped after any
 * answered error or any answer at all. Pending is polled with GET (3 s, then 10 s, bounded); leaving the page stops the
 * poll and coming back starts from a fresh GET. Announcements go through the existing status region only, once per press
 * and once per transition seen from the poll; system arrivals never move focus.
 */
export function useNotes(ai: LectureAi | undefined) {
  const announcer = useAnnouncer();
  const announcerRef = useRef(announcer);
  announcerRef.current = announcer;
  const enabled = !!ai?.getNotes && !!ai?.requestNotes;

  const [view, setView] = useState<NotesView>({ kind: "checking" });
  const viewRef = useRef(view);
  viewRef.current = view;
  const [busy, setBusy] = useState(false);
  const busyRef = useRef(false);
  const [problem, setProblem] = useState<NotesProblem | null>(null);
  const [focus, setFocus] = useState<NotesFocus>(null);
  const keyRef = useRef<string | null>(null);
  const announced = useRef(new Set<string>());
  const mounted = useRef(true);
  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);

  const announceOnce = useCallback((n: LectureNotes) => {
    const text = announceFor(n);
    const id = `${n.notesId}:${n.status}`;
    if (!text || announced.current.has(id)) return;
    announced.current.add(id);
    announcerRef.current.status(text);
  }, []);

  const check = useCallback(async () => {
    if (!ai?.getNotes) return;
    setView({ kind: "checking" });
    try {
      const n = await ai.getNotes();
      if (!mounted.current) return;
      setView(n ? { kind: "notes", notes: n } : { kind: "none" });
    } catch (e) {
      if (!mounted.current) return;
      setView(e instanceof ApiError && e.isNotFound ? { kind: "notFound" } : { kind: "checkFailed" });
    }
  }, [ai]);

  // Mount (Stopped only; the caller renders this hook's panel only then): one GET, never a POST.
  useEffect(() => {
    if (enabled) void check();
  }, [enabled, check]);

  // Poll while the newest attempt is Pending. A remount starts over from the mount GET above.
  const pendingId = view.kind === "notes" && view.notes.status === "Pending" ? view.notes.notesId : null;
  useEffect(() => {
    if (!pendingId || !ai?.getNotes) return;
    const getNotes = ai.getNotes;
    const started = Date.now();
    let stopped = false;
    let h: ReturnType<typeof setTimeout> | null = null;
    const schedule = () => {
      const elapsed = Date.now() - started;
      if (elapsed >= NOTES_POLL_MAX_MS) {
        stopped = true;
        setView({ kind: "checkFailed" });
        return;
      }
      h = setTimeout(tick, elapsed < NOTES_POLL_FAST_FOR_MS ? NOTES_POLL_FAST_MS : NOTES_POLL_SLOW_MS);
    };
    const tick = async () => {
      if (stopped) return;
      try {
        const n = await getNotes();
        if (stopped || !mounted.current) return;
        if (!n) {
          setView({ kind: "none" });
          return;
        }
        if (n.status !== "Pending") {
          announceOnce(n); // a system arrival: announced once, focus and scroll untouched
          setView({ kind: "notes", notes: n });
          return;
        }
        if (n.notesId !== pendingId) setView({ kind: "notes", notes: n });
        else schedule();
      } catch (e) {
        if (stopped || !mounted.current) return;
        if (e instanceof ApiError && e.isNotFound) {
          setView({ kind: "notFound" });
          return;
        }
        schedule(); // a transient failure: keep the cadence; the bound above still applies
      }
    };
    schedule();
    return () => {
      stopped = true;
      if (h) clearTimeout(h);
    };
  }, [pendingId, ai, announceOnce]);

  const press = useCallback(async () => {
    if (!ai?.requestNotes || busyRef.current) return;
    const v = viewRef.current;
    if (v.kind === "notes" && !canPressAgain(v.notes)) return; // final or already running: no button, no POST
    busyRef.current = true;
    setBusy(true);
    setProblem(null);
    setFocus(null);
    keyRef.current ??= crypto.randomUUID();
    const key = keyRef.current;
    try {
      const n = await ai.requestNotes(key);
      keyRef.current = null; // answered: the next press is a new intent with a new key
      if (!mounted.current) return;
      announceOnce(n);
      setView({ kind: "notes", notes: n });
      // The pressed control is gone unless a new press is offered again: focus would fall to body, so it moves to "Notes".
      setFocus(canPressAgain(n) ? null : "heading");
    } catch (e) {
      const kind = notesProblem(e);
      if (kind !== "network") keyRef.current = null;
      if (!mounted.current) return;
      setProblem(kind);
      setFocus("problem"); // the student caused it (the ActionProblem pattern); the button stays
    } finally {
      busyRef.current = false;
      if (mounted.current) setBusy(false);
    }
  }, [ai, announceOnce]);

  return {
    enabled,
    view,
    busy,
    problem,
    press,
    recheck: check,
    focus,
    clearFocus: () => setFocus(null),
  };
}

export type NotesState = ReturnType<typeof useNotes>;
