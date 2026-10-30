import { weekStart } from "./format";
import type { EditableKind, SyllabusItem } from "./types";

export type GroupBy = "week" | "type";

export interface ItemGroup {
  /** Stable key, also used for expand state: "week:2026-10-12", "type:exam", "policy", "needs_date". */
  key: string;
  kind: "week" | "type" | "policy" | "needs_date";
  /** Monday (week groups) or kind (type groups). */
  value: string;
  items: SyllabusItem[];
}

const TYPE_ORDER: EditableKind[] = ["exam", "assignment", "reading", "office_hours", "other"];

const byWhen = (a: SyllabusItem, b: SyllabusItem) =>
  (a.when.date ?? "9999").localeCompare(b.when.date ?? "9999") ||
  (a.when.time ?? "").localeCompare(b.when.time ?? "") ||
  a.title.localeCompare(b.title) ||
  a.id.localeCompare(b.id);

/**
 * Groups for the review list (UX 5.1, 5.7): real calendar weeks (Monday start) or types, then "Course policies"
 * (informational, never approved), then "Needs a date", always last. Empty groups are omitted.
 */
export function groupItems(items: SyllabusItem[], by: GroupBy): ItemGroup[] {
  const dated = items.filter((i) => i.when.status === "resolved").sort(byWhen);
  const policies = items.filter((i) => i.kind === "policy").sort(byWhen);
  const undated = items.filter((i) => i.when.status === "needs_date").sort(byWhen);
  const groups: ItemGroup[] = [];
  if (by === "week") {
    const weeks = new Map<string, SyllabusItem[]>();
    for (const i of dated) {
      const k = weekStart(i.when.date as string);
      weeks.set(k, [...(weeks.get(k) ?? []), i]);
    }
    for (const [monday, list] of [...weeks].sort(([a], [b]) => a.localeCompare(b))) groups.push({ key: `week:${monday}`, kind: "week", value: monday, items: list });
  } else {
    for (const kind of TYPE_ORDER) {
      const list = dated.filter((i) => i.kind === kind);
      if (list.length) groups.push({ key: `type:${kind}`, kind: "type", value: kind, items: list });
    }
  }
  if (policies.length) groups.push({ key: "policy", kind: "policy", value: "policy", items: policies });
  if (undated.length) groups.push({ key: "needs_date", kind: "needs_date", value: "needs_date", items: undated });
  return groups;
}

/** Items the student decides on (policies are informational only and never counted). */
export const decidable = (items: SyllabusItem[]) => items.filter((i) => i.kind !== "policy");

/** The first group that has an undecided, decidable item: expanded by default (UX 5.1). */
export function firstOpenGroup(groups: ItemGroup[]): string | null {
  return groups.find((g) => g.kind !== "policy" && g.items.some((i) => i.decision === "undecided"))?.key ?? null;
}
