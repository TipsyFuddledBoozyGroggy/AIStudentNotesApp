"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import type { Course } from "@/lib/api";
import { chooseNextStep, explainNextStep, MAX_DASHBOARD_COURSES, sortByRecentUse } from "@/lib/nextStep";
import { CourseCard } from "./CourseCard";
import { Disclosure } from "./ui/Disclosure";

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
 * Three plain steps; not a list element, so the course list stays the only list on this screen. A disclosure: open until the
 * student has a lecture, then closed; their own open/close choice is remembered on this device (review S5).
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
      className="group rounded-card border border-hair p-card"
    >
      <summary className="flex min-h-[44px] cursor-pointer list-none items-center justify-between gap-3 [&::-webkit-details-marker]:hidden">
        <span className="display text-xl">How a lecture works</span>
        <svg width="14" height="14" viewBox="0 0 14 14" aria-hidden="true" focusable="false" className="shrink-0 text-accent transition-transform duration-fast group-open:rotate-90">
          <path d="M5 2.5 9.5 7 5 11.5" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
        </svg>
      </summary>
      <div className="mt-4 space-y-4">
        {steps.map(([title, body], i) => (
          <div key={title} className="flex gap-3">
            <span aria-hidden="true" className="display w-6 shrink-0 text-xl leading-6 text-accent">
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

/** Presentational dashboard (M1-S3). Data fetching lives in the page container. */
export function DashboardView({ state, displayName, onRetry }: Props) {
  if (state.status === "loading") {
    return (
      <div>
        <h1 className="display text-3xl">Your dashboard</h1>
        <p role="status" className="mt-4 text-muted">
          Getting your courses.
        </p>
        {/* Static placeholder of the same height as the next-step card: no shimmer, no layout jump. */}
        <div aria-hidden="true" className="mt-6 h-44 rounded-card border border-hair bg-surface" />
      </div>
    );
  }

  if (state.status === "error") {
    return (
      <div>
        <h1 className="display text-3xl">Your dashboard</h1>
        <div role="alert" className="panel panel-notice mt-6 max-w-prose">
          <p className="font-semibold">{state.message}</p>
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
      <p className="eyebrow">{todayLabel()}</p>
      <h1 className="display mt-1 text-3xl">{displayName ? `Hi, ${displayName}.` : "Welcome."}</h1>

      <div className="mt-8 grid gap-block lg:grid-cols-[minmax(0,2fr)_minmax(0,1fr)] lg:gap-10">
        <div className="min-w-0 space-y-10">
          <section aria-labelledby="next-step-heading" className="card border-l-4 border-l-accent">
            <h2 id="next-step-heading" className="eyebrow">
              Next step
            </h2>
            {step.kind === "add-first-course" ? (
              <>
                <p className="display mt-3 text-2xl">Add your first course to get started.</p>
                <p className="mt-2 max-w-prose text-muted">A course holds its lectures, transcripts and notes. You can add the details later.</p>
                <Link href="/courses/new" className="btn-primary mt-5">
                  Add your first course
                </Link>
              </>
            ) : (
              <>
                {/* States the basis plainly instead of implying a recommendation it cannot back (review S5). */}
                <p className="mt-3 text-sm text-muted">{step.reason === "recent-lecture" ? "Most recently used" : "Most recently added"}</p>
                <p className="display break-words text-2xl">{step.course.name}</p>
                <p className="mt-2 max-w-prose text-muted">Open this course to start a lecture. Nothing is recorded until you say so.</p>
                <Link href={`/courses/${encodeURIComponent(step.course.id)}`} className="btn-primary mt-5">
                  Open course<span className="sr-only"> {step.course.name}</span>
                </Link>
              </>
            )}
            <Disclosure summary="Why this?" className="mt-3 text-sm">
              <p className="pb-1">{explainNextStep(step)}</p>
            </Disclosure>
          </section>

          {courses.length > 0 && (
            <section aria-labelledby="courses-heading">
              <div className="flex flex-wrap items-end justify-between gap-3">
                <h2 id="courses-heading" className="display text-2xl">
                  Your courses
                </h2>
                <Link href="/courses/new" className="btn-secondary btn-sm">
                  Add a course
                </Link>
              </div>
              <ul className="mt-4 divide-y divide-hair overflow-hidden rounded-card border border-hair bg-surface">
                {visible.map((c) => (
                  <CourseCard key={c.id} course={c} />
                ))}
              </ul>
              {courses.length > MAX_DASHBOARD_COURSES && (
                <Link href="/courses" className="btn-quiet mt-3 px-0">
                  See all courses
                </Link>
              )}
            </section>
          )}
        </div>
        <aside aria-label="About lectures" className="min-w-0">
          <HowItWorks hasLecture={courses.some((c) => !!c.lastSessionAt)} />
        </aside>
      </div>
    </div>
  );
}
