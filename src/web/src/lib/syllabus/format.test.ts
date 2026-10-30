import { ITEM_COPY } from "@/components/syllabus/copy";
import { dateRange, formatDate, formatTime, isRealDate, weekdayList, weekRangeLabel, weekStart } from "./format";
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

describe("recurrence line (weekday named once)", () => {
  it("weekdayList: full names, Monday-first, 'and' before the last, no duplicates", () => {
    expect(weekdayList(["TU"])).toBe("Tuesday");
    expect(weekdayList(["TH", "TU"])).toBe("Tuesday and Thursday");
    expect(weekdayList(["FR", "MO", "WE", "MO"])).toBe("Monday, Wednesday and Friday");
  });

  it("dateRange: no weekdays; year only when not this year, once when both share it", () => {
    expect(dateRange("2026-08-25", "2026-12-11", 2026)).toBe("Aug 25 to Dec 11");
    expect(dateRange("2027-01-12", "2027-05-04", 2026)).toBe("Jan 12 to May 4, 2027");
    expect(dateRange("2026-08-25", "2027-01-15", 2026)).toBe("Aug 25, 2026 to Jan 15, 2027");
  });

  it("ITEM_COPY.repeats reads naturally and never repeats the weekday", () => {
    const line = ITEM_COPY.repeats(weekdayList(["TU"]), dateRange("2026-08-25", "2026-12-11", 2026));
    expect(line).toBe("Repeats every Tuesday, Aug 25 to Dec 11.");
    expect(line.match(/Tue/g)).toHaveLength(1);
    expect(line).not.toMatch(/\b(Mon|Tue|Wed|Thu|Fri|Sat|Sun),/);
    expect(ITEM_COPY.repeats(weekdayList(["TU", "TH"]), dateRange("2026-08-25", "2026-12-10", 2026))).toBe("Repeats every Tuesday and Thursday, Aug 25 to Dec 10.");
  });
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
