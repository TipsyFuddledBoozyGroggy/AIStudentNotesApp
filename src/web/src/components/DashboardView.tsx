"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import type { Course } from "@/lib/api";
import { chooseNextStep, explainNextStep, MAX_DASHBOARD_COURSES, sortByRecentUse } from "@/lib/nextStep";
import { CourseCard } from "./CourseCard";
import { Chevron, Disclosure } from "./ui/Disclosure";

export type DashboardState =
  | { status: "loading" }
  | { status: "error"; message: string }
  | { status: "ready"; courses: Course[] };

interface Props {
  state: DashboardState;
  displayName?: string;
  onRetry: () => void;
}

function todayLabel(): string {
  return new Date().toLocaleDateString(undefined, { weekday: "long", month: "long", day: "numeric" });
}

const HOW_KEY = "tether.dashboard.howItWorks.v1";

/**
 * Three plain steps; not a list element, so the course list stays the only list on this screen. A disclosure under the
 * course list (redesign-round2 5.3), no box: open until the student has a lecture, then closed; their own open/close
 * choice is remembered on this device (review S5).
 */
function HowItWorks({ hasLecture }: { hasLecture: boolean }) {
  const [open, setOpen] = useState(!hasLecture);
  useEffect(() => {
    try {
      const v = window.localStorage.getItem(HOW_KEY);
      if (v === "open" || v === "closed") setOpen(v === "open");
    } catch {
      /* storage unavailable: default applies */
    }
  }, []);
  const steps: [string, string][] = [
    ["Open a course.", "Everything for that class lives there."],
    ["Start a lecture when you choose.", "Nothing is recorded until you press Start, and you can pause or stop at any time."],
    ["Want a recap? Press Quick recap.", "It shows what was said in the last 2, 5 or 10 minutes. The lecture keeps going."],
  ];
  return (
    <details
      open={open}
      onToggle={(e) => {
        const next = (e.currentTarget as HTMLDetailsElement).open;
        setOpen(next);
        try {
          window.localStorage.setItem(HOW_KEY, next ? "open" : "closed");
        } catch {
          /* ignore */
        }
      }}
      className="group"
    >
      <summary className="link inline-flex min-h-[44px] cursor-pointer list-none items-center gap-2 rounded-control [&::-webkit-details-marker]:hidden">
        <Chevron />
        How a lecture works
      </summary>
      <div className="mt-2 max-w-prose space-y-4">
        {steps.map(([title, body], i) => (
          <div key={title} className="flex gap-3">
            <span aria-hidden="true" className="w-6 shrink-0 font-semibold tabular-nums text-muted">
              {i + 1}
            </span>
            <p>
              <span className="block font-semibold">{title}</span>
              <span className="text-sm text-muted">{body}</span>
            </p>
          </div>
        ))}
      </div>
    </details>
  );
}

/** The date hangs in the margin beside the greeting from 1024 and sits above it below (redesign-round2 5.3). */
function Greeting({ children }: { children: string }) {
  return (
    <div className="desk-section">
      <p className="label desk-label">{todayLabel()}</p>
      <h1 className="desk-body display text-display break-words">{children}</h1>
    </div>
  );
}

/**
 * Today (redesign-round2 slice 4, wireframe 5.3), Desk template: no cards. Labels ("Next step", "Your courses") hang in
 * the margin column; the next step is a plain block with the screen's one filled button; the courses are a ledger of
 * whole-row links; "How a lecture works" is a closed-after-first-lecture disclosure under the list. Data fetching lives
 * in the page container.
 */
export function DashboardView({ state, displayName, onRetry }: Props) {
  if (state.status === "loading") {
    return (
      <div>
        <Greeting>Your dashboard</Greeting>
        <p role="status" className="mt-12 text-muted">
          Getting your courses.
        </p>
      </div>
    );
  }

  if (state.status === "error") {
    return (
      <div>
        <Greeting>Your dashboard</Greeting>
        <div role="alert" className="note note-notice mt-12 max-w-prose">
          <p className="note-title">{state.message}</p>
          <p className="mt-1 text-muted">Nothing was deleted. Try again when you like.</p>
          <button type="button" className="btn-primary mt-4" onClick={onRetry}>
            Try again
          </button>
        </div>
      </div>
    );
  }

  const { courses } = state;
  const step = chooseNextStep(courses);
  const visible = sortByRecentUse(courses).slice(0, MAX_DASHBOARD_COURSES);

  return (
    <div>
      <Greeting>{displayName ? `Hi, ${displayName}.` : "Welcome."}</Greeting>

      <section aria-labelledby="next-step-heading" className="desk-section mt-12">
        <h2 id="next-step-heading" className="label desk-label">
          Next step
        </h2>
        <div className="desk-body">
          {step.kind === "add-first-course" ? (
            <>
              <p className="display text-xl">Add your first course to get started.</p>
              <p className="mt-2 max-w-prose text-muted">A course holds its lectures, transcripts and notes. You can add the details later.</p>
              <Link href="/courses/new" className="btn-primary mt-4">
                Add your first course
              </Link>
            </>
          ) : (
            <>
              {/* States the basis plainly instead of implying a recommendation it cannot back (review S5). It shares the
                  margin label's baseline from 1024, so "Next step" and the basis read as one line of labels. */}
              <p className="text-sm text-muted">{step.reason === "recent-lecture" ? "Most recently used" : "Most recently added"}</p>
              <p className="display break-words text-xl">{step.course.name}</p>
              <p className="mt-2 max-w-prose text-muted">Open this course to start a lecture. Nothing is recorded until you say so.</p>
              <Link href={`/courses/${encodeURIComponent(step.course.id)}`} className="btn-primary mt-4">
                Open course<span className="sr-only"> {step.course.name}</span>
              </Link>
            </>
          )}
          <Disclosure summary="Why this?" className="mt-2 text-sm">
            <p className="max-w-prose pb-1">{explainNextStep(step)}</p>
          </Disclosure>
        </div>
      </section>

      {courses.length > 0 && (
        <section aria-labelledby="courses-heading" className="desk-section mt-12">
          {/* The label and its action share a row and a baseline (A6 / R7) at every width; the action's ink ends on the
              column edge (R6). Both are in the body face, so no heading face can change the row's height (R10). */}
          <h2 id="courses-heading" className="label desk-label">
            Your courses
          </h2>
          <Link href="/courses/new" className="desk-action btn-tertiary btn-sm optical-end">
            Add a course
          </Link>
          <div className="desk-body">
            <ul className="ledger">
              {visible.map((c) => (
                <CourseCard key={c.id} course={c} />
              ))}
            </ul>
            {courses.length > MAX_DASHBOARD_COURSES && (
              <Link href="/courses" className="btn-tertiary optical-start mt-2">
                See all courses
              </Link>
            )}
          </div>
        </section>
      )}

      <aside aria-label="About lectures" className={courses.length > 0 ? "mt-6" : "mt-12"}>
        <HowItWorks hasLecture={courses.some((c) => !!c.lastSessionAt)} />
      </aside>
    </div>
  );
}
