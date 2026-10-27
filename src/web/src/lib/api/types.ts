// Mirrors docs/architecture/backend-foundation.md section 6 (Milestone 1).
// ASSUMPTIONS (not yet in the written contract; confirm with backend-engineer):
//  - GET /courses returns an envelope { items, nextCursor }.
//  - Course includes id, createdAt, updatedAt and optional lastSessionAt.
//  - GET /me returns { id, email, displayName }.

export interface UserProfile {
  id: string;
  email: string;
  displayName: string;
}

export interface Course {
  id: string;
  name: string;
  code?: string;
  term?: string;
  instructor?: string;
  color?: string;
  createdAt: string;
  updatedAt: string;
  /** ISO timestamp of the most recent lecture session, if any. */
  lastSessionAt?: string;
}

export interface CreateCourseInput {
  name: string;
  code?: string;
  term?: string;
  instructor?: string;
  color?: string;
}

export interface CourseList {
  items: Course[];
  nextCursor: string | null;
}

/** RFC 9457 ProblemDetails with the contract extensions. */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status: number;
  detail?: string;
  code?: string;
  traceId?: string;
  errors?: Record<string, string[]>;
}
