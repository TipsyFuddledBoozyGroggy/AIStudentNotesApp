"use client";

import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { api, ApiError, type Course } from "@/lib/api";
import { useRequireUser } from "./AuthProvider";
import { CourseDelete } from "./CourseDelete";
import { CourseDetailView } from "./CourseDetailView";
import { COURSE_DELETE_COPY as CD } from "./deleteCopy";
import { markDeleted, takeDeleted, type DeletedMarker } from "./deleteFlow";
import { AnnouncerProvider, useAnnouncer } from "./lecture/Announcer";

type State = { s: "loading" } | { s: "notfound" } | { s: "error" } | { s: "ready"; course: Course };

export function CourseDetailPage() {
  const params = useParams<{ id: string }>();
  const id = params?.id;
  const { status } = useRequireUser();
  const [state, setState] = useState<State>({ s: "loading" });
  const [justAdded, setJustAdded] = useState(false);

  useEffect(() => {
    setJustAdded(new URLSearchParams(window.location.search).get("added") === "1");
  }, []);

  useEffect(() => {
    if (status !== "authenticated" || !id) return;
    let cancelled = false;
    api
      .getCourse(id)
      .then((course) => !cancelled && setState({ s: "ready", course }))
      .catch((e: unknown) => {
        if (!cancelled) setState(e instanceof ApiError && e.isNotFound ? { s: "notfound" } : { s: "error" });
      });
    return () => {
      cancelled = true;
    };
  }, [status, id]);

  if (state.s === "loading")
    return (
      <p role="status" className="text-muted">
        Getting this course.
      </p>
    );
  if (state.s === "notfound" || state.s === "error") {
    return (
      <div className="max-w-prose space-y-4">
        <h1 className="display text-display">{state.s === "notfound" ? "We can't find that course" : "We couldn't load this course"}</h1>
        <p className="text-muted">{state.s === "notfound" ? "It may have been removed, or it belongs to another account." : "Nothing was deleted. Try again in a moment."}</p>
        <Link href="/" className="btn-primary">
          Back to dashboard
        </Link>
      </div>
    );
  }
  // The existing announcer (two regions, Announcer.tsx) says the after-delete line once; no new live region.
  return (
    <AnnouncerProvider>
      <CourseReady course={state.course} justAdded={justAdded} />
    </AnnouncerProvider>
  );
}

function CourseReady({ course, justAdded }: { course: Course; justAdded: boolean }) {
  const router = useRouter();
  const announcer = useAnnouncer();
  // One-shot marker from the lecture page's delete, read and removed on the first mount; the ref keeps the answer across
  // StrictMode's simulated remount (whose second read would find nothing), as the PO-19 arrival marker does.
  const taken = useRef<{ id: string; m: DeletedMarker | null } | null>(null);
  const [note, setNote] = useState<string | null>(null);
  useEffect(() => {
    if (taken.current?.id !== course.id) {
      taken.current = { id: course.id, m: takeDeleted({ kind: "lecture", courseId: course.id }) };
      if (taken.current.m) announcer.status(taken.current.m.announce);
    }
    setNote(taken.current.m?.note ?? null);
  }, [course.id, announcer]);

  return (
    <CourseDetailView
      course={course}
      justAdded={justAdded}
      deletedNote={note}
      onDismissDeleted={() => {
        setNote(null);
        if (taken.current) taken.current.m = null;
      }}
      deleteSection={
        <CourseDelete
          courseId={course.id}
          onDeleted={(outcome) => {
            markDeleted({ kind: "course", note: outcome === "gone" ? CD.gone : CD.doneNote(course.name), announce: outcome === "gone" ? CD.gone : CD.done });
            router.push("/");
          }}
        />
      }
    />
  );
}
