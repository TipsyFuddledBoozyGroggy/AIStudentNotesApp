// Per-viewer conveniences only (drafts, view settings). Every access is wrapped: storage can be missing or throw
// (private windows, blocked site data), and the screens must work without it. Server state is never stored here.

const PREFIX = "tether.syllabus.";

export function readLocal<T>(key: string, check: (v: unknown) => v is T): T | null {
  try {
    const raw = window.localStorage.getItem(PREFIX + key);
    if (raw === null) return null;
    const v: unknown = JSON.parse(raw);
    return check(v) ? v : null;
  } catch {
    return null;
  }
}

export function writeLocal(key: string, value: unknown): void {
  try {
    window.localStorage.setItem(PREFIX + key, JSON.stringify(value));
  } catch {
    /* a lost draft is acceptable; the page keeps working */
  }
}

export function removeLocal(key: string): void {
  try {
    window.localStorage.removeItem(PREFIX + key);
  } catch {
    /* ignore */
  }
}

export const isRecord = (v: unknown): v is Record<string, unknown> => typeof v === "object" && v !== null && !Array.isArray(v);
