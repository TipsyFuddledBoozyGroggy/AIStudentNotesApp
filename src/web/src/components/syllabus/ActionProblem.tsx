"use client";

import Link from "next/link";
import { forwardRef } from "react";
import { ApiError } from "@/lib/api/errors";
import { ACTION_COPY as C } from "./copy";

export type ProblemKind = "session" | "network" | "rate" | "gone" | "generic";

/** Maps a failed press to one of the UX 8 states. The server's `detail` is never shown. */
export function problemKind(e: unknown): ProblemKind {
  if (e instanceof ApiError) {
    if (e.isUnauthenticated) return "session";
    if (e.isNetwork) return "network";
    if (e.status === 429) return "rate";
    if (e.isNotFound) return "gone";
  }
  return "generic";
}

export const problemText = (k: Exclude<ProblemKind, "session">) =>
  k === "network" ? C.network : k === "rate" ? C.rateLimited : k === "gone" ? C.gone : C.generic;

/**
 * A press that did not go through. Session expiry says what is kept and links to sign-in (UX 8); everything else is one
 * sentence that says nothing was changed. Focusable (tabIndex -1) so the caller can move focus here after the student's
 * own press. Not a live region: callers announce once through the shared announcer when they need to.
 */
export const ActionProblem = forwardRef<HTMLDivElement, { kind: ProblemKind; className?: string }>(function ActionProblem({ kind, className = "" }, ref) {
  if (kind === "session")
    return (
      <div ref={ref} tabIndex={-1} className={`note note-notice space-y-2 ${className}`} data-problem="session">
        <p className="font-semibold">{C.sessionHeading}</p>
        <p>{C.sessionBody}</p>
        <Link href="/signin" className="btn-secondary">
          {C.signIn}
        </Link>
      </div>
    );
  return (
    <div ref={ref} tabIndex={-1} className={`note note-notice ${className}`} data-problem={kind}>
      <p className="font-semibold">{problemText(kind)}</p>
    </div>
  );
});
