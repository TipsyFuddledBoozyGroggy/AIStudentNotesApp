"use client";

import { useEffect, useRef, useState } from "react";
import { api as defaultApi, type ApiClient, type LectureSessionDto } from "@/lib/api";
import { syllabusApi as defaultSyllabusApi, type SyllabusApi } from "@/lib/syllabus/client";
import type { Syllabus } from "@/lib/syllabus/types";
import { COURSE_DELETE_COPY as C, DELETE_PROBLEM_COPY as P } from "./deleteCopy";
import { runDelete } from "./deleteFlow";
import { useAnnouncer } from "./lecture/Announcer";
import { ConfirmDialog } from "./syllabus/ConfirmDialog";
import { clearSyllabusDrafts } from "./syllabus/storage";

/** A lecture the server would stop before deleting (PO-20by: Delete is hidden while a lecture in the course is recording). */
export const isLive = (s: Pick<LectureSessionDto, "state">) => s.state === "Active" || s.state === "Paused";

interface Contents {
  lectures: number;
  syllabi: Syllabus[];
}

export interface CourseDeleteProps {
  courseId: string;
  /** After the server's 204 (or a 404: already gone). The caller moves to the dashboard. */
  onDeleted(outcome: "deleted" | "gone"): void;
  api?: Pick<ApiClient, "listSessions" | "deleteCourse" | "getCourse">;
  syllabi?: Pick<SyllabusApi, "listSyllabi">;
}

type View = { s: "checking" } | { s: "ready" } | { s: "recording" };

/**
 * "Delete this course" (docs/ux/delete-wording-draft.md 5.2a, PO-20by: Delete only, no Hide). A Desk section under the
 * syllabus with one tertiary button. While any lecture of the course is Active or Paused the button is not shown; one
 * plain line says why. The lecture list is read on load and again on each press (so the dialog's counts and the recording
 * check are fresh) and once more just before the delete is sent. The dialog is the shared destructive ConfirmDialog.
 */
export function CourseDelete({ courseId, onDeleted, api = defaultApi, syllabi = defaultSyllabusApi }: CourseDeleteProps) {
  const announcer = useAnnouncer();
  const [view, setView] = useState<View>({ s: "checking" });
  const [opening, setOpening] = useState(false);
  const [contents, setContents] = useState<Contents | null>(null);
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);
  const [outside, setOutside] = useState<string | null>(null);
  const blockedRef = useRef<HTMLParagraphElement>(null);
  const focusBlocked = useRef(false);

  useEffect(() => {
    let cancelled = false;
    api
      .listSessions(courseId)
      .then((list) => !cancelled && setView({ s: list.some(isLive) ? "recording" : "ready" }))
      // Unknown: show the button; the press checks again before anything opens.
      .catch(() => !cancelled && setView({ s: "ready" }));
    return () => {
      cancelled = true;
    };
  }, [api, courseId]);

  useEffect(() => {
    if (view.s === "recording" && focusBlocked.current) {
      focusBlocked.current = false;
      blockedRef.current?.focus();
    }
  }, [view.s]);

  /** The student pressed Delete but a lecture is live: the button goes, focus and one announcement go to the reason. */
  function showRecording() {
    setOpen(false);
    focusBlocked.current = true;
    setView({ s: "recording" });
    announcer.status(C.blocked);
  }

  async function press() {
    if (opening) return;
    setOpening(true);
    setOutside(null);
    try {
      const [sessions, list] = await Promise.all([api.listSessions(courseId), syllabi.listSyllabi(courseId)]);
      if (sessions.some(isLive)) return showRecording();
      setContents({ lectures: sessions.length, syllabi: list });
      setProblem(null);
      setOpen(true);
    } catch {
      setOutside(P.network);
      announcer.status(P.network);
    } finally {
      setOpening(false);
    }
  }

  async function confirm() {
    if (busy) return;
    setBusy(true);
    setProblem(null);
    try {
      if ((await api.listSessions(courseId)).some(isLive)) {
        setBusy(false);
        return showRecording();
      }
    } catch {
      /* the delete below answers for itself */
    }
    const r = await runDelete(
      () => api.deleteCourse(courseId),
      () => api.getCourse(courseId),
    );
    if (r.kind !== "problem") {
      // SB-06: the deleted syllabi's drafts on this device go too (as for a syllabus delete), and the course's paste draft.
      const ids = contents?.syllabi.map((s) => s.id) ?? [];
      for (const id of ids) clearSyllabusDrafts(id, courseId);
      if (ids.length === 0) clearSyllabusDrafts("", courseId); // no syllabus: only the course's paste draft (an empty id matches no item key)
      onDeleted(r.kind); // stays busy: the page is about to move
      return;
    }
    setBusy(false);
    setProblem(r.text);
    announcer.status(r.text);
  }

  if (view.s === "checking") return null;

  const items = contents
    ? [
        contents.lectures > 0 ? C.lectures(contents.lectures) : null,
        contents.syllabi.length > 0 ? C.syllabi(contents.syllabi.length) : null,
        // counts.synced counts only syncState "synced" (SyllabusCore.cs), not "changed_since_sync" or a failed re-sync, which
        // still hold demo events; only approved items can have one, so "any approved" is the superset that never hides one.
        contents.syllabi.some((s) => s.counts.approved > 0) ? C.demoCalendar : null,
      ].filter((x): x is string => x !== null)
    : [];

  return (
    <section aria-labelledby="course-data-heading" className="desk-section mt-12">
      <h2 id="course-data-heading" className="label desk-label">
        {C.section}
      </h2>
      <div className="desk-body">
        {view.s === "recording" ? (
          <p ref={blockedRef} tabIndex={-1} className="max-w-prose text-muted">
            {C.blocked}
          </p>
        ) : (
          <button type="button" className="btn-tertiary optical-start" aria-busy={opening ? true : undefined} onClick={() => void press()}>
            {C.button}
          </button>
        )}
        {outside ? (
          <div className="note note-notice mt-3 max-w-prose">
            <p className="font-semibold">{outside}</p>
          </div>
        ) : null}
      </div>
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
        <p>{items.length > 0 ? C.body : C.bodyEmpty}</p>
        {items.length > 0 ? (
          <ul className="list-disc pl-5 marker:text-muted">
            {items.map((t) => (
              <li key={t}>{t}</li>
            ))}
          </ul>
        ) : null}
        <p>{C.cannot}</p>
        <p>{C.kept}</p>
        {problem ? (
          <p data-delete-problem className="font-semibold text-ink">
            {problem}
          </p>
        ) : null}
      </ConfirmDialog>
    </section>
  );
}
