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
    <li className="flex items-center justify-between gap-4 px-5 py-4">
      <div className="min-w-0">
        <H className="break-words text-lg font-semibold leading-snug">{course.name}</H>
        <p className="mt-0.5 text-sm text-muted">
          {meta ? <span>{meta} · </span> : null}
          {lastLecture(course)}
        </p>
      </div>
      <Link href={`/courses/${encodeURIComponent(course.id)}`} className="btn-secondary btn-sm shrink-0">
        Open<span className="sr-only"> {course.name}</span>
      </Link>
    </li>
  );
}
