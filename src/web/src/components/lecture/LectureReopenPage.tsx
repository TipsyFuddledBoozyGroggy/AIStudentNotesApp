"use client";

import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useEffect, useMemo, useRef, useState } from "react";
import { api, ApiError, type LectureSessionDto } from "@/lib/api";
import { useRequireUser } from "../AuthProvider";
import { LECTURE_DELETE_COPY as LD } from "../deleteCopy";
import { markDeleted } from "../deleteFlow";
import { AnnouncerProvider } from "./Announcer";
import { LectureScreen } from "./LectureScreen";
import { BrowserMicSource } from "./micSource";
import { createRealRuntime } from "./realRuntime";
import { takeArrivedFromStop } from "./settings";

type State = { s: "loading" } | { s: "notfound" } | { s: "error" } | { s: "ready"; session: LectureSessionDto };

/**
 * Reload or reopen an existing session. There is no live microphone here: an Active session without a live track
 * shows the mismatch panel (never silently recording), a Paused one can be resumed on a press.
 */
export function LectureReopenPage() {
  const params = useParams<{ sessionId: string }>();
  const sessionId = params?.sessionId;
  const router = useRouter();
  const { status } = useRequireUser();
  const [state, setState] = useState<State>({ s: "loading" });
  const mic = useMemo(() => new BrowserMicSource(), []);
  // PO-19: the one-shot marker the live route wrote just before the move, read and removed on the first mount. The ref
  // keeps the answer across StrictMode's simulated remount (whose second read would find nothing). A reload has none.
  const arrival = useRef<{ id: string; arrived: boolean } | null>(null);
  const [arrivedFromStop, setArrivedFromStop] = useState(false);
  useEffect(() => {
    if (!sessionId) return;
    if (arrival.current?.id !== sessionId) arrival.current = { id: sessionId, arrived: takeArrivedFromStop(sessionId) };
    setArrivedFromStop(arrival.current.arrived);
  }, [sessionId]);

  useEffect(() => {
    if (status !== "authenticated" || !sessionId) return;
    let cancelled = false;
    api
      .getSession(sessionId)
      .then((session) => !cancelled && setState({ s: "ready", session }))
      .catch((e: unknown) => {
        if (!cancelled) setState(e instanceof ApiError && e.isNotFound ? { s: "notfound" } : { s: "error" });
      });
    return () => {
      cancelled = true;
    };
  }, [status, sessionId]);

  const session = state.s === "ready" ? state.session : null;
  const runtime = useMemo(() => (session ? createRealRuntime({ api }, session) : null), [session]);

  // Finish-backlog item 2: the course's name for the notes file's "Course:" line, read with the existing GET /courses/{id}.
  // A failure only leaves that line out; nothing else on the page waits for it.
  const courseId = session?.courseId ?? null;
  const [courseName, setCourseName] = useState<string | null>(null);
  useEffect(() => {
    if (!courseId) return;
    let cancelled = false;
    Promise.resolve()
      .then(() => api.getCourse(courseId))
      .then((c) => !cancelled && setCourseName(c.name))
      .catch(() => undefined);
    return () => {
      cancelled = true;
    };
  }, [courseId]);
  const notesExport = useMemo(
    () => (session ? { lectureTitle: session.title, courseName, startedAt: session.startedAt ?? null } : undefined),
    [session, courseName],
  );

  if (state.s === "loading")
    return (
      <p role="status" className="text-muted">
        Getting this lecture.
      </p>
    );
  if (!session || !runtime) {
    return (
      <div className="max-w-prose space-y-4">
        <h1 className="display text-3xl">{state.s === "notfound" ? "We can't find that lecture" : "We couldn't load this lecture"}</h1>
        <Link href="/" className="btn-primary">
          Back to dashboard
        </Link>
      </div>
    );
  }
  return (
    <AnnouncerProvider>
      <LectureScreen
        runtime={runtime}
        track={null}
        mic={mic}
        courseName={session.title ?? undefined}
        arrivedFromStop={arrivedFromStop}
        notesExport={notesExport}
        onBackToCourse={() => router.push(`/courses/${encodeURIComponent(session.courseId)}`)}
        deletion={{
          sessionId: session.id,
          onDeleted: (outcome) => {
            // Never before the server answered (draft 6.2): the course page shows the note, says it once and focuses its h1.
            const note = outcome === "gone" ? LD.gone : session.title ? LD.doneNote(session.title) : LD.done;
            markDeleted({ kind: "lecture", courseId: session.courseId, note, announce: outcome === "gone" ? LD.gone : LD.done });
            router.push(`/courses/${encodeURIComponent(session.courseId)}`);
          },
        }}
      />
    </AnnouncerProvider>
  );
}
