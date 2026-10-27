"use client";

import { DashboardView } from "./DashboardView";
import { useRequireUser } from "./AuthProvider";
import { useCourses } from "./useCourses";

export function DashboardPage() {
  const { status, user } = useRequireUser();
  const { state, reload } = useCourses(status === "authenticated");

  if (status === "loading" || status === "unauthenticated") {
    return <p role="status">Loading...</p>;
  }
  if (status === "error") {
    return (
      <div role="alert" className="card">
        <p>We can&apos;t reach the server right now.</p>
        <button type="button" className="btn-primary mt-3" onClick={() => window.location.reload()}>
          Try again
        </button>
      </div>
    );
  }
  return <DashboardView state={state} displayName={user?.displayName} onRetry={reload} />;
}
