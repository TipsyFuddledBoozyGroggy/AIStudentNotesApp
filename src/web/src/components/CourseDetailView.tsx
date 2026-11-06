"use client";

import Link from "next/link";
import { useEffect, useRef, type ReactNode } from "react";
import type { Course } from "@/lib/api";
import { DELETE_PROBLEM_COPY } from "./deleteCopy";
import { SyllabusCard } from "./syllabus/SyllabusCard";

interface Props {
  course: Course;
  justAdded?: boolean;
  /** After a lecture of this course was deleted (draft 6.2): one note under the heading, dismissable, no timer. */
  deletedNote?: string | null;
  onDismissDeleted?: () => void;
  /** "Delete this course" (CourseDelete), under the syllabus. The page supplies it; absent in isolated view tests. */
  deleteSection?: ReactNode;
}

/**
 * Course page (redesign-round2 slice 4), Desk template, no cards: back link, h1, meta, then two sections whose labels hang
 * in the margin column: "Lecture" (its sentence, unchanged, and the screen's one filled button) and "Syllabus" (one
 * ledger row). The lecture entry is a plain link: nothing starts, and no microphone is touched, until the consent
 * screen's Start button.
 */
export function CourseDetailView({ course, justAdded, deletedNote, onDismissDeleted, deleteSection }: Props) {
  const headingRef = useRef<HTMLHeadingElement>(null);
  useEffect(() => {
    headingRef.current?.focus(); // success flow: focus moves to the course heading
  }, []);

  const meta = [course.code, course.term, course.instructor].filter(Boolean).join(" · ");

  return (
    <div>
      <div>
        <Link href="/courses" className="link inline-flex min-h-[44px] items-center gap-1 text-sm no-underline hover:underline">
          <span aria-hidden="true">&larr;</span> Courses
        </Link>
        <h1 ref={headingRef} tabIndex={-1} className="display mt-1 break-words text-display">
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
        {deletedNote ? (
          <div data-deleted-note className="note note-neutral mt-3 flex max-w-prose flex-wrap items-center justify-between gap-x-4">
            <p className="min-w-0">{deletedNote}</p>
            <button
              type="button"
              className="btn-tertiary optical-end"
              onClick={() => {
                onDismissDeleted?.();
                headingRef.current?.focus(); // the button goes with the note; focus must not fall to <body>
              }}
            >
              {DELETE_PROBLEM_COPY.dismiss}
            </button>
          </div>
        ) : null}
      </div>

      <section aria-labelledby="lecture-heading" className="desk-section mt-12">
        <h2 id="lecture-heading" className="label desk-label">
          Lecture
        </h2>
        <div className="desk-body">
          <p className="max-w-prose text-muted">Nothing is recorded until you choose to start. You will see what Tether does first, and you can pause or stop at any time.</p>
          <Link href={`/courses/${encodeURIComponent(course.id)}/lecture`} className="btn-primary mt-4">
            Start a lecture
          </Link>
        </div>
      </section>

      <SyllabusCard courseId={course.id} className="mt-12" />

      {deleteSection}

      <Link href="/" className="btn-tertiary optical-start mt-12">
        Back to dashboard
      </Link>
    </div>
  );
}
