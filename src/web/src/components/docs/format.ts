import type { DocRef } from "@/lib/docs";
import { isSessionId } from "@/lib/docs";

/** "November 6" in the given IANA zone (the browser's own when absent). Zone-dependent: tests run it in UTC (L-UTC). */
export function formatDay(iso: string, timeZone?: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "";
  try {
    return new Intl.DateTimeFormat("en-US", { month: "long", day: "numeric", timeZone }).format(d);
  } catch {
    return new Intl.DateTimeFormat("en-US", { month: "long", day: "numeric", timeZone: "UTC" }).format(d);
  }
}

/**
 * The app route of a Demo Doc, with its lecture so the page can link back (docs-demo-doc carries no session id). The path
 * is the parsed in_app `openPath`, which the parser has already checked names this document; only a canonical session id
 * is appended.
 */
export function openHref(ref: DocRef, sessionId: string): string {
  return isSessionId(sessionId) ? `${ref.openPath}?lecture=${sessionId}` : ref.openPath;
}
