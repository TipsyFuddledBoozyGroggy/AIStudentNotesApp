"use client";

import { useState } from "react";
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
 * and says it once through the existing announcer (no new live region); focus stays on the confirm button.
 */
export function LectureDelete({ sessionId, onDeleted, api = defaultApi }: LectureDeletion) {
  const announcer = useAnnouncer();
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);

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
          <p data-delete-problem className="font-semibold text-ink">
            {problem}
          </p>
        ) : null}
      </ConfirmDialog>
    </>
  );
}
