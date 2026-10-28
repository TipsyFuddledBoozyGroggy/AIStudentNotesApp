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
    <div className="space-y-6">
      <div>
        <h1 ref={headingRef} tabIndex={-1} className="break-words text-2xl font-bold">
          {course.name}
        </h1>
        {meta && <p className="text-slate-700">{meta}</p>}
        {justAdded && (
          <p role="status" className="mt-2 font-medium">
            {course.name} added.
          </p>
        )}
      </div>

      <section aria-labelledby="lecture-heading" className="card">
        <h2 id="lecture-heading" className="text-xl font-bold">
          Lecture
        </h2>
        <p className="mt-1 text-slate-700">Nothing is recorded until you choose to start.</p>
        <Link href={`/courses/${encodeURIComponent(course.id)}/lecture`} className="btn-primary mt-4">
          Start a lecture
        </Link>
      </section>

      <Link href="/" className="btn-secondary">
        Back to dashboard
      </Link>
    </div>
  );
}
