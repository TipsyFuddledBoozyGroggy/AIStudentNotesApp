export const COURSE_NAME_MAX = 120;

/** Returns an error message, or null when the name is valid (M1-S2 AC1/AC4). */
export function validateCourseName(raw: string): string | null {
  const name = raw.trim();
  if (name.length === 0) return "Enter a course name.";
  if (name.length > COURSE_NAME_MAX) return `Use ${COURSE_NAME_MAX} characters or fewer.`;
  return null;
}

export function isDuplicateName(raw: string, existing: string[]): boolean {
  const n = raw.trim().toLowerCase();
  return n.length > 0 && existing.some((e) => e.trim().toLowerCase() === n);
}
