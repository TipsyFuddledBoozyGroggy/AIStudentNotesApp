import Link from "next/link";
import type { Course } from "@/lib/api";

function lastLecture(c: Course): string {
  if (!c.lastSessionAt) return "No lectures yet";
  const d = new Date(c.lastSessionAt);
  return `Last lecture: ${d.toLocaleDateString(undefined, { month: "short", day: "numeric" })}`;
}

/** Name, last session date, one action. Deliberately no counts of unfinished work. */
export function CourseCard({ course }: { course: Course }) {
  return (
    <li className="card flex items-center justify-between gap-4">
      <div className="min-w-0">
        <h3 className="break-words text-lg font-semibold">{course.name}</h3>
        <p className="text-sm text-slate-700">{lastLecture(course)}</p>
      </div>
      <Link href={`/courses/${encodeURIComponent(course.id)}`} className="btn-secondary shrink-0">
        Open<span className="sr-only"> {course.name}</span>
      </Link>
    </li>
  );
}
