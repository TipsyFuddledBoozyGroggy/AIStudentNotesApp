"use client";

import { useEffect, useRef, useState } from "react";
import { DashboardView } from "./DashboardView";
import { useRequireUser } from "./AuthProvider";
import { takeDeleted, type DeletedMarker } from "./deleteFlow";
import { AnnouncerProvider, useAnnouncer } from "./lecture/Announcer";
import { useCourses } from "./useCourses";

export function DashboardPage() {
  const { status, user } = useRequireUser();
  const { state, reload } = useCourses(status === "authenticated");

  if (status === "loading" || status === "unauthenticated") {
    return (
      <p role="status" className="text-muted">
        Getting your courses.
      </p>
    );
  }
  if (status === "error") {
    return (
      <div role="alert" className="note note-notice max-w-prose">
        <p className="note-title">We can&apos;t reach the server at the moment.</p>
        <p className="mt-1 text-muted">Nothing was deleted. Try again when you like.</p>
        <button type="button" className="btn-primary mt-4" onClick={() => window.location.reload()}>
          Try again
        </button>
      </div>
    );
  }
  // The existing announcer says the after-delete line once (no new live region).
  return (
    <AnnouncerProvider>
      <AfterDelete>{(note, dismiss) => <DashboardView state={state} displayName={user?.displayName} onRetry={reload} deletedNote={note} onDismissDeleted={dismiss} />}</AfterDelete>
    </AnnouncerProvider>
  );
}

/** Reads the one-shot "course deleted" marker on the first mount (kept across StrictMode's simulated remount by a ref). */
function AfterDelete({ children }: { children: (note: string | null, dismiss: () => void) => React.ReactNode }) {
  const announcer = useAnnouncer();
  const taken = useRef<{ m: DeletedMarker | null } | null>(null);
  const [note, setNote] = useState<string | null>(null);
  useEffect(() => {
    if (taken.current === null) {
      taken.current = { m: takeDeleted({ kind: "course" }) };
      if (taken.current.m) announcer.status(taken.current.m.announce);
    }
    setNote(taken.current.m?.note ?? null);
  }, [announcer]);
  return (
    <>
      {children(note, () => {
        setNote(null);
        if (taken.current) taken.current.m = null;
      })}
    </>
  );
}
