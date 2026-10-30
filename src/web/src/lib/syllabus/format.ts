import { isLocalDate, isZone } from "./parse";
import type { ItemKind, SyllabusItem, Weekday } from "./types";

// Wall-clock helpers (contract 2: dates are YYYY-MM-DD + HH:MM + an IANA zone; never converted to UTC here).
// Formatting is done on a UTC Date built from the parts, so the student's own zone can never shift a date.

function ymdParts(ymd: string): [number, number, number] {
  const [y, m, d] = ymd.split("-").map(Number);
  return [y, m, d];
}

const utc = (ymd: string) => {
  const [y, m, d] = ymdParts(ymd);
  return new Date(Date.UTC(y, m - 1, d));
};

const toYmd = (dt: Date) => dt.toISOString().slice(0, 10);

/** A real calendar date in the contract window (2000-2100). Feb 29 only in leap years. */
export function isRealDate(ymd: string): boolean {
  if (!isLocalDate(ymd)) return false;
  const [y, m, d] = ymdParts(ymd);
  const dt = new Date(Date.UTC(y, m - 1, d));
  return dt.getUTCFullYear() === y && dt.getUTCMonth() === m - 1 && dt.getUTCDate() === d;
}

/** "Tue, Oct 14" (plus the year when it is not `thisYear`). */
export function formatDate(ymd: string, thisYear = new Date().getFullYear()): string {
  const dt = utc(ymd);
  const opts: Intl.DateTimeFormatOptions = { weekday: "short", month: "short", day: "numeric", timeZone: "UTC" };
  if (dt.getUTCFullYear() !== thisYear) opts.year = "numeric";
  return new Intl.DateTimeFormat("en-US", opts).format(dt);
}

/** "10:00 AM" from "10:00". */
export function formatTime(hhmm: string): string {
  const [h, m] = hhmm.split(":").map(Number);
  const suffix = h < 12 ? "AM" : "PM";
  const h12 = h % 12 === 0 ? 12 : h % 12;
  return `${h12}:${String(m).padStart(2, "0")} ${suffix}`;
}

/** Monday of the week containing `ymd`. */
export function weekStart(ymd: string): string {
  const dt = utc(ymd);
  const dow = (dt.getUTCDay() + 6) % 7; // Monday = 0
  dt.setUTCDate(dt.getUTCDate() - dow);
  return toYmd(dt);
}

/** "Oct 12 to 18" or "Oct 26 to Nov 1" for the week starting `monday`. */
export function weekRangeLabel(monday: string): string {
  const a = utc(monday);
  const b = new Date(a);
  b.setUTCDate(a.getUTCDate() + 6);
  const mon = (d: Date) => new Intl.DateTimeFormat("en-US", { month: "short", timeZone: "UTC" }).format(d);
  return a.getUTCMonth() === b.getUTCMonth() ? `${mon(a)} ${a.getUTCDate()} to ${b.getUTCDate()}` : `${mon(a)} ${a.getUTCDate()} to ${mon(b)} ${b.getUTCDate()}`;
}

/** Today's wall-clock date in the browser's zone. */
export function todayYmd(now = new Date()): string {
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}-${String(now.getDate()).padStart(2, "0")}`;
}

/** The browser's IANA zone if it matches the contract's Area/Location form, else a safe default. */
export function browserTimezone(): string {
  try {
    const z = Intl.DateTimeFormat().resolvedOptions().timeZone;
    if (isZone(z)) return z;
  } catch {
    /* fall through */
  }
  return "Etc/UTC";
}

const WEEKDAY_NAMES: Record<Weekday, string> = { MO: "Monday", TU: "Tuesday", WE: "Wednesday", TH: "Thursday", FR: "Friday", SA: "Saturday", SU: "Sunday" };
const WEEK_ORDER: Weekday[] = ["MO", "TU", "WE", "TH", "FR", "SA", "SU"];

/** "Tuesday", "Tuesday and Thursday", "Monday, Wednesday and Friday" (Monday-first order, no duplicates). */
export function weekdayList(days: Weekday[]): string {
  const names = WEEK_ORDER.filter((d) => days.includes(d)).map((d) => WEEKDAY_NAMES[d]);
  return names.length <= 1 ? (names[0] ?? "") : `${names.slice(0, -1).join(", ")} and ${names[names.length - 1]}`;
}

/** "Aug 25" (no weekday), plus the year when it is not `thisYear`. */
function monthDay(ymd: string, withYear: boolean): string {
  const opts: Intl.DateTimeFormatOptions = { month: "short", day: "numeric", timeZone: "UTC" };
  if (withYear) opts.year = "numeric";
  return new Intl.DateTimeFormat("en-US", opts).format(utc(ymd));
}

/**
 * A date range without weekdays, for a line that already names the weekday: "Aug 25 to Dec 11";
 * "Aug 25 to Dec 11, 2027" when both are in another year; each date carries its year when the years differ.
 */
export function dateRange(from: string, until: string, thisYear = new Date().getFullYear()): string {
  const fy = utc(from).getUTCFullYear();
  const uy = utc(until).getUTCFullYear();
  if (fy === uy) return `${monthDay(from, false)} to ${monthDay(until, uy !== thisYear)}`;
  return `${monthDay(from, true)} to ${monthDay(until, true)}`;
}

export const KIND_LABEL: Record<ItemKind, string> = {
  exam: "Exam",
  assignment: "Assignment",
  reading: "Reading",
  office_hours: "Office hours",
  other: "Other",
  policy: "Course policy",
};

/** Plain-language "when" line for a resolved item: "Tue, Oct 14 · 10:00 AM" or "Tue, Oct 14 · All day". */
export function whenLine(item: SyllabusItem, allDayWord: string): string | null {
  const w = item.when;
  if (w.status !== "resolved" || !w.date) return null;
  const parts = [formatDate(w.date)];
  if (w.endDate && w.endDate !== w.date) parts[0] = `${parts[0]} to ${formatDate(w.endDate)}`;
  if (w.allDay) parts.push(allDayWord);
  else if (w.time) parts.push(w.endTime ? `${formatTime(w.time)} to ${formatTime(w.endTime)}` : formatTime(w.time));
  return parts.join(" · ");
}
