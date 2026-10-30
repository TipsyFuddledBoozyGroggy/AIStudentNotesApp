"use client";

import Link from "next/link";
import { useCallback, useEffect, useRef, useState } from "react";
import { ApiError } from "@/lib/api/errors";
import type { SyllabusApi } from "@/lib/syllabus/client";
import { IN_PROGRESS, type Syllabus } from "@/lib/syllabus/types";
import { useAnnouncer } from "../lecture/Announcer";
import { ActionProblem, problemKind, type ProblemKind } from "./ActionProblem";
import { ConfirmDialog } from "./ConfirmDialog";
import { DEMO_TAGS, NOT_READ_ACTIONS, PROCESS_COPY as C, STOP_COPY as STOP } from "./copy";
import { DemoLine } from "./DemoLine";
import { notReadCopy, offersPaste } from "./NotRead";

/** Bounds (UX 4, proposals): poll at most this many times, then stop and offer "Check again"; never loop silently. */
export const MAX_POLLS = 60;
export const MAX_POLL_FAILURES = 3;
export const SLOW_AFTER_SECONDS = 20;

export interface ProcessingProps {
  api: SyllabusApi;
  initial: Syllabus;
  courseId: string;
  /** Milliseconds per server "second" (pollAfterSeconds). 1000 in the app; tests pass a small value. */
  msPerSecond?: number;
  onReview(s: Syllabus): void;
  onPasteInstead(): void;
  /** "Stop reading" deleted the syllabus (DELETE cancels the running read; nothing is kept). */
  onStopped?: () => void;
}

const STEP_OF: Record<string, number> = { queued: 1, extracting: 2, proposing: 3 };

/**
 * Processing state (UX 4): plain text steps that change in place, no spinner, percentage, countdown or elapsed timer.
 * Polls GET /syllabi/{id} at the server's pollAfterSeconds, bounded. On completion the shared status region says "Your
 * items are ready." once and focus does not move; "Review items" appears beside "Back to course". Leaving the page
 * stops polling; returning resumes from the server's state.
 */
export function Processing({ api, initial, courseId, msPerSecond = 1000, onReview, onPasteInstead, onStopped }: ProcessingProps) {
  const announcer = useAnnouncer();
  const announcerRef = useRef(announcer);
  announcerRef.current = announcer;
  const headingRef = useRef<HTMLHeadingElement>(null);
  const [s, setS] = useState(initial);
  const [slow, setSlow] = useState(false);
  const [stopped, setStopped] = useState<null | "limit" | "offline">(null);
  const [attempt, setAttempt] = useState(0);
  const polls = useRef(0);
  const failures = useRef(0);
  const announced = useRef(false);
  const [confirmStop, setConfirmStop] = useState(false);
  const [stopping, setStopping] = useState(false);
  const [stopProblem, setStopProblem] = useState<ProblemKind | null>(null);
  const stopProblemRef = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (stopProblem) stopProblemRef.current?.focus();
  }, [stopProblem]);

  useEffect(() => {
    headingRef.current?.focus();
  }, []);

  const inProgress = IN_PROGRESS.has(s.status);

  useEffect(() => {
    if (!inProgress) return;
    const t = setTimeout(() => setSlow(true), SLOW_AFTER_SECONDS * msPerSecond);
    return () => clearTimeout(t);
  }, [inProgress, msPerSecond]);

  const finished = useCallback((next: Syllabus) => {
    if (announced.current) return;
    announced.current = true;
    const ok = next.status === "ready" || next.status === "partial";
    announcerRef.current.status(ok ? C.readyStatus : notReadCopy(next).heading);
  }, []);

  useEffect(() => {
    if (!inProgress || stopped) return;
    let cancelled = false;
    const wait = Math.max(1, s.pollAfterSeconds ?? 2) * msPerSecond;
    const t = setTimeout(async () => {
      polls.current += 1;
      try {
        const next = await api.getSyllabus(s.id);
        if (cancelled) return;
        failures.current = 0;
        setS(next);
        if (!IN_PROGRESS.has(next.status)) finished(next);
        else if (polls.current >= MAX_POLLS) setStopped("limit");
      } catch (e) {
        if (cancelled) return;
        failures.current += 1;
        if (failures.current >= MAX_POLL_FAILURES || polls.current >= MAX_POLLS) setStopped(e instanceof ApiError && e.isNetwork ? "offline" : "limit");
        else setAttempt((a) => a + 1); // schedule the next bounded try
      }
    }, wait);
    return () => {
      cancelled = true;
      clearTimeout(t);
    };
  }, [api, s, inProgress, stopped, attempt, msPerSecond, finished]);

  const checkAgain = () => {
    polls.current = 0;
    failures.current = 0;
    setStopped(null);
    setAttempt((a) => a + 1);
  };

  const step = STEP_OF[s.status] ?? 3;
  const notRead = !inProgress && s.status !== "ready" && s.status !== "partial";
  const nr = notReadCopy(s);

  async function stop() {
    if (stopping) return;
    setStopping(true);
    setStopProblem(null);
    try {
      await api.deleteSyllabus(s.id);
      setConfirmStop(false);
      announcerRef.current.status(STOP.done);
      onStopped?.();
    } catch (e) {
      const k = problemKind(e);
      setConfirmStop(false);
      if (k === "gone") {
        onStopped?.(); // already deleted (another tab): the outcome the student asked for
        return;
      }
      setStopProblem(k);
    } finally {
      setStopping(false);
    }
  }

  return (
    <div className="max-w-prose space-y-6">
      <div className="space-y-3">
        <h1 ref={headingRef} tabIndex={-1} className="display text-3xl">
          {C.h1}
        </h1>
        {s.demoSource && <DemoLine text={DEMO_TAGS.source} />}
        <p>{C.body}</p>
        <p className="text-muted">{C.leave}</p>
      </div>

      <div className="card space-y-3">
        {inProgress ? (
          <p className="font-semibold" data-testid="step-line">
            {C.step(step, C.steps[step - 1])}
          </p>
        ) : notRead ? (
          <div className="space-y-1">
            <p className="font-semibold">{nr.heading}</p>
            <p>{nr.body}</p>
          </div>
        ) : (
          <p className="font-semibold">{C.readyStatus}</p>
        )}
        <ol className="space-y-1">
          {C.steps.map((name, i) => {
            const done = !inProgress ? !notRead || i === 0 : i + 1 < step;
            return (
              <li key={name} className="flex items-center gap-2">
                <span className="tabular w-5 text-muted">{i + 1}.</span>
                <span>{name.charAt(0).toUpperCase() + name.slice(1)}</span>
                {done && (
                  <span className="inline-flex items-center gap-1 text-sm font-semibold text-success">
                    <svg width="14" height="14" viewBox="0 0 16 16" aria-hidden="true" focusable="false">
                      <path d="M3 8.5 6.5 12 13 4.5" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
                    </svg>
                    {C.done}
                  </span>
                )}
              </li>
            );
          })}
        </ol>
        {inProgress && slow && !stopped && <p className="text-muted">{C.slow}</p>}
        {stopped && (
          <div className="space-y-2">
            <p>{stopped === "offline" ? C.offline : C.stopped}</p>
            <button type="button" className="btn-secondary" onClick={checkAgain}>
              {C.checkAgain}
            </button>
          </div>
        )}
      </div>

      <div className="flex flex-wrap items-center gap-3">
        {!inProgress && !notRead && (
          <button type="button" className="btn-primary" onClick={() => onReview(s)}>
            {C.review}
          </button>
        )}
        {notRead && (
          <button type="button" className="btn-primary" onClick={onPasteInstead}>
            {offersPaste(s) ? NOT_READ_ACTIONS.paste : NOT_READ_ACTIONS.tryAgain}
          </button>
        )}
        <Link href={`/courses/${encodeURIComponent(courseId)}`} className="btn-secondary">
          {C.back}
        </Link>
        {inProgress && onStopped && (
          <button type="button" className="btn-quiet" onClick={() => setConfirmStop(true)}>
            {STOP.button}
          </button>
        )}
      </div>
      {stopProblem && <ActionProblem ref={stopProblemRef} kind={stopProblem} />}
      <ConfirmDialog
        open={confirmStop}
        heading={STOP.heading}
        confirmLabel={STOP.confirm}
        cancelLabel={STOP.keep}
        busy={stopping}
        busyLabel={STOP.busy}
        onConfirm={() => void stop()}
        onCancel={() => setConfirmStop(false)}
      >
        <p>{STOP.body}</p>
      </ConfirmDialog>
    </div>
  );
}
