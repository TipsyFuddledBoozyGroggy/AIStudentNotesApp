"use client";

import Link from "next/link";
import { sortByRecentUse } from "@/lib/nextStep";
import { useRequireUser } from "./AuthProvider";
import { CourseCard } from "./CourseCard";
import { useCourses } from "./useCourses";

export function AllCoursesPage() {
  const { status } = useRequireUser();
  const { state, reload } = useCourses(status === "authenticated");

  return (
    <div className="space-y-4">
      <h1 className="text-2xl font-bold">All courses</h1>
      {state.status === "loading" && <p role="status">Loading your courses...</p>}
      {state.status === "error" && (
        <div role="alert" className="card">
          <p>{state.message}</p>
          <button type="button" className="btn-primary mt-3" onClick={reload}>
            Try again
          </button>
        </div>
      )}
      {state.status === "ready" && (
        <ul className="space-y-3">
          {sortByRecentUse(state.courses).map((c) => (
            <CourseCard key={c.id} course={c} />
          ))}
        </ul>
      )}
      <Link href="/" className="btn-secondary">
        Back to dashboard
      </Link>
    </div>
  );
}
