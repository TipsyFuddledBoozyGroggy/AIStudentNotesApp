import { ITEM_COPY } from "@/components/syllabus/copy";
import { browserTimezone, dateRange, formatDate, formatTime, isRealDate, normalizeZone, weekdayList, weekRangeLabel, weekStart } from "./format";
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

describe("browser time zone (docs/ops/ci-first-run-e2e-diagnosis.md 2)", () => {
  const withZone = (timeZone: string | undefined) => {
    const real = Intl.DateTimeFormat;
    vi.spyOn(Intl, "DateTimeFormat").mockImplementation(((...a: ConstructorParameters<typeof Intl.DateTimeFormat>) => {
      const f = new real(...a);
      const ro = f.resolvedOptions();
      return { ...f, resolvedOptions: () => ({ ...ro, timeZone: timeZone as string }) };
    }) as unknown as typeof Intl.DateTimeFormat);
  };
  afterEach(() => vi.restoreAllMocks());

  it("a browser reporting UTC sends Etc/UTC (Chromium on a TZ=UTC host reports bare UTC)", () => {
    withZone("UTC");
    expect(browserTimezone()).toBe("Etc/UTC");
  });

  it("an Area/Location zone is sent as reported", () => {
    withZone("America/New_York");
    expect(browserTimezone()).toBe("America/New_York");
  });

  it("falls back to Etc/UTC when Intl throws or reports nothing", () => {
    withZone(undefined);
    expect(browserTimezone()).toBe("Etc/UTC");
    vi.restoreAllMocks();
    vi.spyOn(Intl, "DateTimeFormat").mockImplementation(() => {
      throw new RangeError("no Intl");
    });
    expect(browserTimezone()).toBe("Etc/UTC");
  });

  it("normalizeZone: every zero-offset alias maps to Etc/UTC; real zones pass; junk falls back", () => {
    for (const z of ["UTC", "Etc/UTC", "utc", "GMT", "Etc/GMT", "Etc/GMT0", "GMT+0", "Etc/GMT-0", "UCT", "Etc/Universal", "Zulu", "Etc/Greenwich", " UTC "]) expect(normalizeZone(z), z).toBe("Etc/UTC");
    for (const z of ["America/New_York", "Europe/London", "America/Argentina/Buenos_Aires", "Asia/Kolkata"]) expect(normalizeZone(z), z).toBe(z);
    // Etc/GMT+5 is a real fixed-offset zone, not UTC: it is not mapped to UTC (the API decides; it is not on the allow-list).
    expect(normalizeZone("Etc/GMT+5")).toBe("Etc/GMT+5");
    for (const z of ["EST", "", "-05:00", "America/New_York\n", null, 42]) expect(normalizeZone(z), String(z)).toBe("Etc/UTC");
  });
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
