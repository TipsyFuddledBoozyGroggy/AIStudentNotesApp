import type { Course, CreateCourseInput, UserProfile } from "@/lib/api/types";

// In-memory fake backend state, used only by MSW handlers (dev + tests).
interface State {
  signedInEmail: string | null;
  coursesByEmail: Record<string, Course[]>;
}

const KEY = "cc.mockdb.v1";
let persist = false;
let state: State = { signedInEmail: null, coursesByEmail: {} };

export function enablePersistence(): void {
  persist = true;
  try {
    const raw = window.localStorage.getItem(KEY);
    if (raw) state = JSON.parse(raw) as State;
  } catch {
    /* ignore corrupt mock state */
  }
}

function save(): void {
  if (!persist) return;
  try {
    window.localStorage.setItem(KEY, JSON.stringify(state));
  } catch {
    /* ignore */
  }
}

export function resetMockDb(): void {
  state = { signedInEmail: null, coursesByEmail: {} };
  save();
}

export function seedCourses(email: string, courses: Course[]): void {
  state.coursesByEmail[email] = courses;
  save();
}

export function signIn(email: string): void {
  state.signedInEmail = email;
  save();
}
export function signOut(): void {
  state.signedInEmail = null;
  save();
}

export function currentUser(): UserProfile | null {
  const email = state.signedInEmail;
  if (!email) return null;
  const local = email.split("@")[0] ?? "student";
  return { id: `user-${local}`, email, displayName: local.charAt(0).toUpperCase() + local.slice(1) };
}

export function listCourses(): Course[] {
  const email = state.signedInEmail;
  return email ? [...(state.coursesByEmail[email] ?? [])] : [];
}

export function addCourse(input: CreateCourseInput): Course {
  const email = state.signedInEmail as string;
  const now = new Date().toISOString();
  const course: Course = {
    id: `course-${Math.random().toString(36).slice(2, 10)}`,
    ...input,
    createdAt: now,
    updatedAt: now,
  };
  state.coursesByEmail[email] = [...(state.coursesByEmail[email] ?? []), course];
  save();
  return course;
}

export function findCourse(id: string): Course | undefined {
  return listCourses().find((c) => c.id === id);
}
