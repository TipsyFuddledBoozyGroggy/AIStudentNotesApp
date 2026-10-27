import type { Course } from "@/lib/api/types";

export const MAX_DASHBOARD_COURSES = 6;

export type NextStep =
  | { kind: "add-first-course" }
  | { kind: "open-course"; course: Course; reason: "recent-lecture" | "newest-course" };

function lastUsed(c: Course): string {
  return c.lastSessionAt ?? c.createdAt;
}

/** Courses ordered most recently used first; ties broken by id so the result is deterministic. */
export function sortByRecentUse(courses: Course[]): Course[] {
  return [...courses].sort((a, b) => {
    const diff = lastUsed(b).localeCompare(lastUsed(a));
    return diff !== 0 ? diff : a.id.localeCompare(b.id);
  });
}

/**
 * Deterministic next step (student-experience.md section 3). Rules in order:
 * 1. in-progress session (not possible yet: lectures are not built)
 * 2. pinned course (not in the M1 contract yet)
 * 3. most recently used course
 * 4. "Add your first course"
 */
export function chooseNextStep(courses: Course[]): NextStep {
  const [first] = sortByRecentUse(courses);
  if (!first) return { kind: "add-first-course" };
  return { kind: "open-course", course: first, reason: first.lastSessionAt ? "recent-lecture" : "newest-course" };
}

export function explainNextStep(step: NextStep): string {
  switch (step.kind) {
    case "add-first-course":
      return "You have no courses yet, so adding one is the only step.";
    case "open-course":
      return step.reason === "recent-lecture"
        ? "This is the course you used most recently."
        : "This is the course you added most recently.";
  }
}
