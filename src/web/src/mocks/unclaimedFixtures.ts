import { buildUnclaimed, unclaimedRow } from "./syllabusData";

// Web fixtures for syllabus-unclaimed-date (PO-20b, docs/ux/unclaimed-dates-list.md 9). Synthetic text only. The shared
// contract fixtures (src/shared/contracts/fixtures) hold one valid row and two invalid ones; these add the shapes the UI
// has to cope with. unclaimed.test.ts checks every "valid" one against the real schema (ajv) and the strict parser,
// and every "invalid" one against the parser.

type Wire = Record<string, unknown>;

/** The line, the date span and offsets that agree (the normal case). */
export const ROW_LINE = unclaimedRow("unc_line", "Bring a calculator on Oct 5 for the in-class exercise.", "Oct 5", 120, 2);

/** Offsets that do not point at the snippet inside the context (schema-valid; the UI falls back to two blocks). */
export const ROW_OFFSET_MISMATCH: Wire = {
  id: "unc_mismatch",
  snippet: { text: "Nov 2", start: 300, end: 305, page: 3, precision: "exact", context: { text: "Field trip permission slips due Nov 2; see the course site.", start: 290, end: 349 } },
};

/** A very long line: the server keeps context equal to the hit (MockRuleExtractor AddUnclaimed). */
export const ROW_CONTEXT_IS_SNIPPET = unclaimedRow("unc_same", "Oct 19", "Oct 19", 900, null);

/** After the retention purge: text, offsets and context null together (R2-04). The UI never renders the list then. */
export const ROW_PURGED: Wire = { id: "unc_purged", snippet: { text: null, start: null, end: null, page: 4, precision: "exact", context: null } };

/** A 1,500-character line (the context cap) whose date sits in the middle. */
export function rowLongLine(): Wire {
  const left = "Long line ".repeat(74);
  const right = " and more words".repeat(Math.floor((1500 - left.length - 6) / 15));
  const line = `${left}Dec 12${right}`.slice(0, 1500);
  return unclaimedRow("unc_long", line, "Dec 12", 2000, 5);
}

export const page = (items: Wire[], nextCursor: string | null = null): Wire => ({ items, nextCursor });

export const VALID_PAGES: Record<string, Wire> = {
  "three-rows": page(buildUnclaimed("syl_fixture", 3)),
  "two-hundred-rows (cap)": page(buildUnclaimed("syl_fixture", 200)),
  "offset-mismatch": page([ROW_OFFSET_MISMATCH]),
  "context-equals-snippet": page([ROW_CONTEXT_IS_SNIPPET]),
  "purged-nulls": page([ROW_PURGED]),
  "long-line": page([rowLongLine()]),
  "with-cursor": page([ROW_LINE], "u1"),
  empty: page([]),
};

export const INVALID_PAGES: Record<string, Wire> = {
  "over-cap (201 rows)": page(buildUnclaimed("syl_fixture", 200).concat([unclaimedRow("unc_extra", "Bring snacks on Oct 1.", "Oct 1", 99999)])),
  "extra member on a row": page([{ ...ROW_LINE, title: "not in the schema" }]),
  "null snippet": page([{ id: "unc_x", snippet: null }]),
  "bad id": page([{ ...ROW_LINE, id: "unc 1" }]),
  "context without text (not null together)": page([{ id: "unc_y", snippet: { text: null, start: null, end: null, page: 1, precision: "exact", context: { text: "Oct 5", start: 1, end: 6 } } }]),
  "unknown page member": { items: [], nextCursor: null, total: 0 },
  "missing nextCursor": { items: [] },
};
