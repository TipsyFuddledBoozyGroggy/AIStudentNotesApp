import Link from "next/link";
import type { Course } from "@/lib/api";
import { chooseNextStep, explainNextStep, MAX_DASHBOARD_COURSES, sortByRecentUse } from "@/lib/nextStep";
import { CourseCard } from "./CourseCard";

export type DashboardState =
  | { status: "loading" }
  | { status: "error"; message: string }
  | { status: "ready"; courses: Course[] };

interface Props {
  state: DashboardState;
  displayName?: string;
  onRetry: () => void;
}

/** Presentational dashboard (M1-S3). Data fetching lives in the page container. */
export function DashboardView({ state, displayName, onRetry }: Props) {
  if (state.status === "loading") {
    return (
      <div>
        <h1 className="text-2xl font-bold">Your dashboard</h1>
        <p role="status" className="mt-4">Loading your courses...</p>
      </div>
    );
  }

  if (state.status === "error") {
    return (
      <div>
        <h1 className="text-2xl font-bold">Your dashboard</h1>
        <div role="alert" className="card mt-4">
          <p>{state.message}</p>
          <p className="mt-1 text-slate-700">Nothing is lost. Try again when you are ready.</p>
          <button type="button" className="btn-primary mt-3" onClick={onRetry}>
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
    <div className="space-y-8">
      <h1 className="text-2xl font-bold">{displayName ? `Hi, ${displayName}.` : "Welcome."}</h1>

      <section aria-labelledby="next-step-heading" className="card">
        <h2 id="next-step-heading" className="text-sm font-bold uppercase tracking-wide text-slate-700">
          Next step
        </h2>
        {step.kind === "add-first-course" ? (
          <>
            <p className="mt-2 text-lg">Add your first course to get started.</p>
            <Link href="/courses/new" className="btn-primary mt-4">
              Add your first course
            </Link>
          </>
        ) : (
          <>
            <p className="mt-2 text-xl font-semibold">{step.course.name}</p>
            <p className="text-slate-700">Ready when you are. Lecture capture is coming next.</p>
            <Link href={`/courses/${encodeURIComponent(step.course.id)}`} className="btn-primary mt-4">
              Open course<span className="sr-only"> {step.course.name}</span>
            </Link>
          </>
        )}
        <details className="mt-3 text-sm">
          <summary className="cursor-pointer font-medium text-blue-900 underline">Why this?</summary>
          <p className="mt-1">{explainNextStep(step)}</p>
        </details>
      </section>

      {courses.length > 0 && (
        <section aria-labelledby="courses-heading">
          <h2 id="courses-heading" className="text-xl font-bold">
            Your courses
          </h2>
          <ul className="mt-3 space-y-3">
            {visible.map((c) => (
              <CourseCard key={c.id} course={c} />
            ))}
          </ul>
          <div className="mt-4 flex flex-wrap gap-3">
            <Link href="/courses/new" className="btn-secondary">
              Add a course
            </Link>
            {courses.length > MAX_DASHBOARD_COURSES && (
              <Link href="/courses" className="btn-secondary">
                See all courses
              </Link>
            )}
          </div>
        </section>
      )}

      <details>
        <summary className="cursor-pointer font-medium text-slate-800">Coming later</summary>
        <p className="mt-1 text-slate-700">Planner and study mode are not built yet.</p>
      </details>
    </div>
  );
}
