import Link from "next/link";
import type { Course } from "@/lib/api";
import { Chevron } from "./ui/Disclosure";

/** "Last lecture: Oct 27" when there is one; nothing otherwise ("No lectures yet" is dropped, redesign-round2 5.3 / N7). */
function lastLecture(c: Course): string | null {
  if (!c.lastSessionAt) return null;
  const d = new Date(c.lastSessionAt);
  return `Last lecture: ${d.toLocaleDateString(undefined, { month: "short", day: "numeric" })}`;
}

/**
 * One ledger row (redesign-round2 2.7 Ledger, slice 4): the course name is the row's one link and stretches over the whole
 * row; quiet meta beside it; a visual-only "Open" with a chevron at the end (aria-hidden, so the row has one tab stop and
 * one name). Deliberately no counts of unfinished work.
 */
export function CourseCard({ course, headingLevel = 3 }: { course: Course; headingLevel?: 2 | 3 }) {
  const H = headingLevel === 2 ? "h2" : "h3";
  const meta = [course.code, course.term, lastLecture(course)].filter(Boolean).join(" · ");
  return (
    // flex-nowrap: the meta wraps under the name inside the first column, so "Open" stays on the name's line on phones.
    <li className="ledger-row flex-nowrap">
      <div className="flex min-w-0 flex-wrap items-baseline gap-x-4">
        <H className="break-words text-lg font-semibold">
          <Link href={`/courses/${encodeURIComponent(course.id)}`} className="ledger-row-link">
            {course.name}
          </Link>
        </H>
        {meta ? <p className="text-pretty text-sm text-muted">{meta}</p> : null}
      </div>
      <span className="ledger-row-end" aria-hidden="true">
        Open
        <Chevron open={false} />
      </span>
    </li>
  );
}
