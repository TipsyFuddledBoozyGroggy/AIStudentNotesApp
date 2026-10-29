"use client";

import Link from "next/link";
import { useEffect, useRef } from "react";
import type { Course } from "@/lib/api";

interface Props {
  course: Course;
  justAdded?: boolean;
}

/** Course detail. The lecture entry is a plain link: nothing starts, and no microphone is touched, until the consent screen's Start button. */
export function CourseDetailView({ course, justAdded }: Props) {
  const headingRef = useRef<HTMLHeadingElement>(null);
  useEffect(() => {
    headingRef.current?.focus(); // success flow: focus moves to the course heading
  }, []);

  const meta = [course.code, course.term, course.instructor].filter(Boolean).join(" · ");

  return (
    <div className="max-w-3xl space-y-8">
      <div>
        <Link href="/courses" className="link inline-flex min-h-[44px] items-center gap-1 text-sm no-underline hover:underline">
          <span aria-hidden="true">&larr;</span> Courses
        </Link>
        <h1 ref={headingRef} tabIndex={-1} className="display mt-1 break-words text-3xl">
          {course.name}
        </h1>
        {meta && <p className="mt-1 text-muted">{meta}</p>}
        {justAdded && (
          <p role="status" className="mt-3 inline-flex items-center gap-2 font-semibold text-success">
            <svg width="18" height="18" viewBox="0 0 18 18" aria-hidden="true" focusable="false">
              <path d="M3.5 9.5 7 13l7.5-8" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round" />
            </svg>
            {course.name} added.
          </p>
        )}
      </div>

      <section aria-labelledby="lecture-heading" className="card border-l-4 border-l-accent">
        <h2 id="lecture-heading" className="display text-2xl">
          Lecture
        </h2>
        <p className="mt-2 max-w-prose text-muted">Nothing is recorded until you choose to start. You will see what Tether does first, and you can pause or stop at any time.</p>
        <Link href={`/courses/${encodeURIComponent(course.id)}/lecture`} className="btn-primary mt-5">
          Start a lecture
        </Link>
      </section>

      <Link href="/" className="btn-quiet px-0">
        Back to dashboard
      </Link>
    </div>
  );
}
