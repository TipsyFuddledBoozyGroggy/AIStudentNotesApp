import { ApiError } from "@/lib/api/errors";
import { DELETE_PROBLEM_COPY as P } from "./deleteCopy";

/** What a Delete press came to. `gone`: the server says it is not there (deleted elsewhere, or by this press). */
export type DeleteOutcome = { kind: "deleted" } | { kind: "gone" } | { kind: "problem"; text: string };

/**
 * Runs one delete and maps the answer to the draft's 6.3 rows. A network failure or a 5xx does not prove the delete did
 * not commit (the answer can be lost after the commit), so the resource is read once: 404 means it is gone (treated as
 * deleted), a successful read means nothing was deleted, and a failed read keeps the network line.
 */
export async function runDelete(del: () => Promise<unknown>, probe: () => Promise<unknown>): Promise<DeleteOutcome> {
  try {
    await del();
    return { kind: "deleted" };
  } catch (e) {
    if (!(e instanceof ApiError)) return { kind: "problem", text: P.generic };
    if (e.isNotFound) return { kind: "gone" };
    if (e.isUnauthenticated) return { kind: "problem", text: P.session };
    if (e.status === 429) return { kind: "problem", text: P.rate };
    if (e.status === 409) return { kind: "problem", text: P.conflict };
    if (e.isNetwork || e.status >= 500) {
      try {
        await probe();
      } catch (p) {
        if (p instanceof ApiError && p.isNotFound) return { kind: "gone" };
      }
      return { kind: "problem", text: P.network };
    }
    return { kind: "problem", text: P.generic };
  }
}

// One-shot marker (sessionStorage) written just before the move after a delete, read and removed by the page it moves to
// (the same pattern as the PO-19 arrival marker in lecture/settings.ts). It holds the note text only: the student's own
// lecture title or course name, on their own device, removed on the first read.
const KEY = "tether.deleted.v1";

export type DeletedMarker = { kind: "lecture"; courseId: string; note: string; announce: string } | { kind: "course"; note: string; announce: string };

export function markDeleted(m: DeletedMarker): void {
  try {
    window.sessionStorage.setItem(KEY, JSON.stringify(m));
  } catch {
    /* storage unavailable: the page still moves; only the note is lost */
  }
}

/**
 * Reads and removes the marker. Returns it only for the page it was written for (a lecture's course page, or the
 * dashboard after a course delete): a note is never shown on a page the student did not land on.
 */
export function takeDeleted(where: { kind: "lecture"; courseId: string } | { kind: "course" }): DeletedMarker | null {
  try {
    const raw = window.sessionStorage.getItem(KEY);
    if (raw === null) return null;
    window.sessionStorage.removeItem(KEY);
    const m = JSON.parse(raw) as Partial<DeletedMarker> | null;
    if (!m || typeof m.note !== "string" || typeof m.announce !== "string") return null;
    if (where.kind === "course") return m.kind === "course" ? (m as DeletedMarker) : null;
    return m.kind === "lecture" && (m as { courseId?: unknown }).courseId === where.courseId ? (m as DeletedMarker) : null;
  } catch {
    return null;
  }
}
