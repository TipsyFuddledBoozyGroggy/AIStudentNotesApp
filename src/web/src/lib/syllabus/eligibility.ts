import type { Syllabus, SyllabusItem } from "./types";

// Client-side mirrors of the server's eligibility rules (contract 6.1, 6.3). They decide only what the UI OFFERS; the
// server re-checks every rule inside its locked transaction and is authoritative.

/** Batch approve is never offered for a suspect, hidden-content or purged document (schema-enforced on the server). */
export function batchAllowed(s: Syllabus): boolean {
  return s.batchApproveAllowed && !s.document.injectionSuspect && !s.document.hiddenContentDropped && !s.retention.canonicalTextPurged;
}

/**
 * A "clear" item for batch approve: dated, "Looks right", never edited, not yet decided, with source wording the student
 * can read (R3-01). "Probably right", "Please check", undated and policy items are never included.
 */
export function batchEligible(i: SyllabusItem): boolean {
  return (
    i.kind !== "policy" &&
    i.when.status === "resolved" &&
    i.confidence === "looks_right" &&
    !i.edited &&
    i.decision === "undecided" &&
    i.snippet !== null &&
    i.snippet.text !== null
  );
}

export const clearItems = (s: Syllabus, items: SyllabusItem[]) => (batchAllowed(s) ? items.filter(batchEligible) : []);

/** Approved, dated items: what the calendar step lists and sends (contract 6.3 eligibility). */
export const syncCandidates = (items: SyllabusItem[]) => items.filter((i) => i.kind !== "policy" && i.decision === "approved" && i.when.status === "resolved");

/** The item has events in the demo calendar (a first sync that did not go through has none: syncedRevision null). */
export const inDemoCalendar = (i: SyllabusItem) => i.syncState === "synced" || i.syncState === "changed_since_sync" || (i.syncState === "sync_failed" && i.syncedRevision !== null);

/** Items in date order for "one at a time" (UX 5.5): dated first by date, "Needs a date" last; policies are not decided. */
export function reviewOrder(items: SyllabusItem[]): SyllabusItem[] {
  const key = (i: SyllabusItem) => `${i.when.status === "resolved" ? "0" : "1"}|${i.when.date ?? "9999"}|${i.when.time ?? ""}|${i.title}|${i.id}`;
  return items.filter((i) => i.kind !== "policy").sort((a, b) => key(a).localeCompare(key(b)));
}
