"use client";

import { DashboardView } from "./DashboardView";
import { useRequireUser } from "./AuthProvider";
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
  return <DashboardView state={state} displayName={user?.displayName} onRetry={reload} />;
}
