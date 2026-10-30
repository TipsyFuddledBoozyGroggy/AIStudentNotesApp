import { act, render } from "@testing-library/react";
import type { ReactElement } from "react";
import * as db from "@/mocks/db";
import { AnnouncerProvider } from "../lecture/Announcer";

export const EMAIL = "alice@example.edu";
export const COURSE = "course-bio101";

export function signedInWithCourse(): void {
  db.signIn(EMAIL);
  db.seedCourses(EMAIL, [{ id: COURSE, name: "Biology 101", createdAt: "2026-01-01T00:00:00Z", updatedAt: "2026-01-01T00:00:00Z" }]);
}

/** Renders inside the shared announcer and records every text the status and alert regions receive. */
export function renderWithAnnouncer(ui: ReactElement) {
  const statusLog: string[] = [];
  const alertLog: string[] = [];
  const obs = new MutationObserver((muts) => {
    const seen = new Set<string>();
    for (const m of muts) {
      const el = (m.target instanceof Element ? m.target : m.target.parentElement)?.closest("[data-region]");
      const kind = el?.getAttribute("data-region");
      if (!el || !el.textContent || !kind || seen.has(kind)) continue;
      seen.add(kind);
      (kind === "status" ? statusLog : alertLog).push(el.textContent);
    }
  });
  obs.observe(document.body, { childList: true, subtree: true, characterData: true });
  const r = render(<AnnouncerProvider>{ui}</AnnouncerProvider>);
  return { ...r, statusLog, alertLog, stop: () => obs.disconnect() };
}

export async function flush(): Promise<void> {
  await act(async () => {
    await Promise.resolve();
    await Promise.resolve();
  });
}
