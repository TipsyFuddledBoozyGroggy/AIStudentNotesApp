"use client";

import { useEffect, useRef, useState } from "react";
import { api as defaultApi, type ApiClient } from "@/lib/api";
import { LECTURE_DELETE_COPY as C } from "../deleteCopy";
import { runDelete } from "../deleteFlow";
import { ConfirmDialog } from "../syllabus/ConfirmDialog";
import { useAnnouncer } from "./Announcer";

export interface LectureDeletion {
  sessionId: string;
  /** After the server's 204 (or a 404: already gone). The caller moves to the course page. */
  onDeleted(outcome: "deleted" | "gone"): void;
  api?: Pick<ApiClient, "deleteSession" | "getSession">;
}

/**
 * "Delete this lecture" (docs/ux/delete-wording-draft.md 5.1, PO-20by): a tertiary button in the stopped lecture's review
 * toolbar and the shared destructive ConfirmDialog (Keep it is filled and focused; Escape keeps; the page behind is inert;
 * focus returns to this button on Keep). The caller renders it only once the server says Stopped or Failed, never while
 * the lecture is Active or Paused, and not in focus mode. A failure keeps the dialog open with one line in its description
 * and says it once through the existing announcer (no new live region); focus moves to that line (UX review
 * docs/ux/demo-doc-and-export-ux-review.md section 7.3, which amends delete-wording-draft 6.3 "focus stays on the confirm
 * button").
 */
export function LectureDelete({ sessionId, onDeleted, api = defaultApi }: LectureDeletion) {
  const announcer = useAnnouncer();
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);
  const problemRef = useRef<HTMLParagraphElement>(null);

  // The problem line takes focus when it appears, so it is read even where the status region is not heard (UX review
  // section 7.3, S3); Tab from it reaches the dialog's buttons.
  useEffect(() => {
    if (problem) problemRef.current?.focus();
  }, [problem]);

  async function confirm() {
    if (busy) return;
    setBusy(true);
    setProblem(null);
    const r = await runDelete(
      () => api.deleteSession(sessionId),
      () => api.getSession(sessionId),
    );
    if (r.kind !== "problem") {
      // Stays busy: the page is about to move, and a second press must not send a second delete.
      onDeleted(r.kind);
      return;
    }
    setBusy(false);
    setProblem(r.text);
    announcer.status(r.text);
  }

  return (
    <>
      <button
        type="button"
        className="btn-tertiary optical-start"
        onClick={() => {
          setProblem(null);
          setOpen(true);
        }}
      >
        {C.button}
      </button>
      <ConfirmDialog
        open={open}
        heading={C.heading}
        confirmLabel={C.confirm}
        cancelLabel={C.keep}
        busy={busy}
        busyLabel={C.busy}
        onConfirm={() => void confirm()}
        onCancel={() => setOpen(false)}
      >
        <p>{C.body}</p>
        <p>{C.cannot}</p>
        <p>{C.kept}</p>
        {problem ? (
          <p ref={problemRef} tabIndex={-1} data-delete-problem className="font-semibold text-ink">
            {problem}
          </p>
        ) : null}
      </ConfirmDialog>
    </>
  );
}
