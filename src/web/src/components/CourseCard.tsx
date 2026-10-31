import Link from "next/link";
import type { Course } from "@/lib/api";

function lastLecture(c: Course): string {
  if (!c.lastSessionAt) return "No lectures yet";
  const d = new Date(c.lastSessionAt);
  return `Last lecture: ${d.toLocaleDateString(undefined, { month: "short", day: "numeric" })}`;
}

/** One row: name, quiet meta, last session date, one action. Deliberately no counts of unfinished work. */
export function CourseCard({ course, headingLevel = 3 }: { course: Course; headingLevel?: 2 | 3 }) {
  const H = headingLevel === 2 ? "h2" : "h3";
  const meta = [course.code, course.term].filter(Boolean).join(" · ");
  return (
    // Row inset = card padding + border (R8): 24px like every card, not 20. Meta wraps without an orphan and the
    // "No lectures yet" line is hidden on phones (A12); a real last-lecture date always shows.
    <li className="flex items-center justify-between gap-4 px-card py-4">
      <div className="min-w-0">
        <H className="break-words text-lg font-semibold">{course.name}</H>
        <p className="mt-1 text-pretty text-sm text-muted">
          {meta ? <span>{meta}</span> : null}
          <span className={course.lastSessionAt ? "" : "hidden sm:inline"}>
            {meta ? " · " : ""}
            {lastLecture(course)}
          </span>
        </p>
      </div>
      <Link href={`/courses/${encodeURIComponent(course.id)}`} className="btn-secondary btn-sm shrink-0">
        Open<span className="sr-only"> {course.name}</span>
      </Link>
    </li>
  );
}
