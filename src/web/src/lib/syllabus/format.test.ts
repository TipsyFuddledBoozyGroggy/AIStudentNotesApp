import { formatDate, formatTime, isRealDate, weekRangeLabel, weekStart } from "./format";
import { groupItems, firstOpenGroup } from "./grouping";
import { loadFixtures } from "@/lib/lecture/schemas.node";
import { parseItem } from "./parse";
import type { SyllabusItem } from "./types";

it("isRealDate: real calendar dates in 2000-2100 only (jsdom date inputs sanitize, so this is the validation tested)", () => {
  expect(isRealDate("2028-02-29")).toBe(true);
  expect(isRealDate("2026-02-29")).toBe(false);
  expect(isRealDate("2026-02-30")).toBe(false);
  expect(isRealDate("1999-12-31")).toBe(false);
  expect(isRealDate("2026-10-14\n")).toBe(false);
});

it("formats wall-clock dates and times without shifting by the browser zone", () => {
  expect(formatDate("2026-10-14", 2026)).toBe("Wed, Oct 14");
  expect(formatDate("2027-01-04", 2026)).toBe("Mon, Jan 4, 2027");
  expect(formatTime("00:05")).toBe("12:05 AM");
  expect(formatTime("23:59")).toBe("11:59 PM");
  expect(weekStart("2026-10-18")).toBe("2026-10-12");
  expect(weekStart("2026-10-12")).toBe("2026-10-12");
  expect(weekRangeLabel("2026-10-26")).toBe("Oct 26 to Nov 1");
});

it("groups: weeks ascending, then Course policies, then Needs a date last; first undecided group opens", () => {
  const items = loadFixtures("valid")
    .filter((f) => f.schema === "syllabus-item")
    .map((f) => parseItem(f.json))
    .filter((x): x is SyllabusItem => x !== null);
  const g = groupItems(items, "week");
  const kinds = g.map((x) => x.kind);
  expect(kinds[kinds.length - 1]).toBe("needs_date");
  expect(kinds.indexOf("policy")).toBe(kinds.length - 2);
  const weeks = g.filter((x) => x.kind === "week").map((x) => x.value);
  expect([...weeks].sort()).toEqual(weeks);
  expect(firstOpenGroup(g)).not.toBe("policy");
  expect(groupItems(items, "type").every((x) => x.kind !== "week")).toBe(true);
});
