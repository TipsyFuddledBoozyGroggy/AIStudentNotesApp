"use client";

import Link from "next/link";
import { useState } from "react";
import type { Course } from "@/lib/api";
import { sortByRecentUse } from "@/lib/nextStep";
import { useRequireUser } from "./AuthProvider";
import { CourseCard } from "./CourseCard";
import { useCourses } from "./useCourses";

/** Client-side course search: every word must appear in the name, code, term or instructor. */
export function searchCourses(courses: Course[], query: string): Course[] {
  const words = query.trim().toLowerCase().split(/\s+/).filter(Boolean);
  if (words.length === 0) return courses;
  return courses.filter((c) => {
    const hay = [c.name, c.code, c.term, c.instructor].filter(Boolean).join(" ").toLowerCase();
    return words.every((w) => hay.includes(w));
  });
}

/**
 * All courses (redesign-round2 slice 4), Desk template: the h1 and "Add a course" on one baseline, the search field, then
 * the courses as a ledger of whole-row links (no box). The search stays (behaviour), narrowed to the 28rem measure.
 */
export function AllCoursesPage() {
  const [query, setQuery] = useState("");
  const { status } = useRequireUser();
  const { state, reload } = useCourses(status === "authenticated");
  const matches = state.status === "ready" ? searchCourses(state.courses, query) : [];

  return (
    <div className="space-y-6">
      {/* Heading and its action share a baseline (A6 / R7); the button's ink ends on the column edge (R6). Fixed 48px row
          from 360px so no heading face can change its height (R10); below that it wraps. */}
      <div className="flex flex-wrap items-baseline justify-between gap-3 xs:h-12">
        <h1 className="display text-display">All courses</h1>
        <Link href="/courses/new" className="btn-tertiary btn-sm optical-end">
          Add a course
        </Link>
      </div>
      {state.status === "loading" && (
        <p role="status" className="text-muted">
          Getting your courses.
        </p>
      )}
      {state.status === "error" && (
        <div role="alert" className="note note-notice max-w-prose">
          <p className="note-title">{state.message}</p>
          <p className="mt-1 text-muted">Nothing was deleted. Try again when you like.</p>
          <button type="button" className="btn-primary mt-4" onClick={reload}>
            Try again
          </button>
        </div>
      )}
      {state.status === "ready" && state.courses.length > 0 && (
        <div className="max-w-narrow">
          <label htmlFor="course-search" className="field-label">
            Search your courses
          </label>
          <input
            id="course-search"
            type="search"
            className="field"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            aria-describedby="course-search-count"
            autoComplete="off"
          />
          <p id="course-search-count" className="help mt-1">
            {query.trim()
              ? `${matches.length} of ${state.courses.length} match.`
              : `${state.courses.length} ${state.courses.length === 1 ? "course" : "courses"}.`}
          </p>
        </div>
      )}
      {state.status === "ready" &&
        (matches.length > 0 ? (
          <ul className="ledger">
            {sortByRecentUse(matches).map((c) => (
              <CourseCard key={c.id} course={c} headingLevel={2} />
            ))}
          </ul>
        ) : (
          <p className="text-muted">{query.trim() ? "No course matches that. Try fewer words." : "No courses yet. Add one when you want to."}</p>
        ))}
      <Link href="/" className="btn-tertiary optical-start">
        Back to dashboard
      </Link>
    </div>
  );
}
