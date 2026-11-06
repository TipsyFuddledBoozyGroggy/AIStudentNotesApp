import { readFileSync } from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";
import { collectStrings, FORBIDDEN, sentencesOver } from "@/lib/copyLint";
import { DOCS_COPY as C } from "./copy";

// DX-F13: the Save to a Demo Doc wording is pinned word for word against docs/product/docs-export-tasks.md section 4
// (each pinned text must appear there in backticks, so a wording change in the doc trips this test), and every string in
// the registry passes the shared copy lint. Placeholders are called with the doc's own {name} text.

const doc = readFileSync(path.resolve(process.cwd(), "../../docs/product/docs-export-tasks.md"), "utf8");
const section = doc.slice(doc.indexOf("## 4. Exact UI states"), doc.indexOf("## 5. Open questions"));
const ticked = new Set([...section.matchAll(/`([^`]+)`/g)].map((m) => m[1]!));

const PINNED: [string, string][] = [
  ["entry.button", C.entry.button],
  ["entry.help", C.entry.help],
  ["entry.saved", C.entry.saved("{date}")],
  ["entry.open", C.entry.open],
  ["preview.heading", C.preview.heading],
  ["preview.line1", C.preview.line1],
  ["preview.where", C.preview.where],
  ["preview.title", C.preview.title("{title}")],
  ["preview.save", C.preview.save],
  ["preview.notNow", C.preview.notNow],
  ["preview.loading", C.preview.loading],
  ["preview.error", C.preview.error],
  ["preview.tryAgain", C.preview.tryAgain],
  ["preview.close", C.preview.close],
  ["changed.line", C.changed.line("{date}")],
  ["changed.legend", C.changed.legend],
  ["changed.update", C.changed.update],
  ["changed.another", C.changed.another],
  ["changed.hint", C.changed.hint],
  ["matches", C.matches],
  ["tooLarge", C.tooLarge],
  ["nothing", C.nothing],
  ["saving.button", C.saving.button],
  ["saving.announce", C.saving.announce],
  ["success.created", C.success.created],
  ["success.updated", C.success.updated],
  ["success.unchanged", C.success.unchanged],
  ["success.where", C.success.where],
  ["stale", C.stale],
  ["retry.wait", C.retry.wait("{n}" as unknown as number)],
  ["retry.tryAgain", C.retry.tryAgain],
  ["lastAttempt.body", C.lastAttempt.body],
  ["lastAttempt.startAgain", C.lastAttempt.startAgain],
  ["permanent", C.permanent],
  ["inProgress.body", C.inProgress.body],
  ["inProgress.checkAgain", C.inProgress.checkAgain],
  ["notAvailable", C.notAvailable],
  ["page.banner", C.page.banner],
  ["page.back", C.page.back],
  ["page.delete", C.page.delete],
];

describe("DOCS_COPY (item 3, DRAFT)", () => {
  it.each(PINNED)("%s is word for word in tasks section 4", (_k, text) => {
    expect(ticked.has(text), text).toBe(true);
  });

  it("the count line is section 4's plural form, with a derived singular", () => {
    expect(ticked.has("{n} sections, {m} bullets. Each bullet keeps its lecture time.")).toBe(true);
    expect(C.preview.count(3, 7)).toBe("3 sections, 7 bullets. Each bullet keeps its lecture time.");
    expect(C.preview.count(1, 1)).toBe("1 section, 1 bullet. Each bullet keeps its lecture time.");
  });

  it("the delete confirm text is section 4's, split into the dialog's heading and body", () => {
    expect(ticked.has(`${C.page.deleteHeading} ${C.page.deleteBody}`)).toBe(true);
  });

  it("the one departure: section 4's retry line fails the shared lint, so the derived line replaces only that clause", () => {
    const original = "That did not save. Nothing was saved and nothing was lost. You can try again.";
    expect(ticked.has(original)).toBe(true);
    expect(FORBIDDEN.some(([, re]) => re.test(original))).toBe(true);
    expect(C.retry.body).toBe("That did not save. Nothing was saved and your notes are fine. You can try again.");
  });

  const all = collectStrings(C);
  it.each(FORBIDDEN)("no %s", (_name, re) => {
    expect(all.filter((s) => re.test(s))).toEqual([]);
  });

  it("no sentence is longer than 20 words", () => {
    expect(sentencesOver(all, 20)).toEqual([]);
  });

  it("never names Google except in the three lines that say a Demo Doc is not in Google", () => {
    const google = all.filter((s) => /google/i.test(s)).sort();
    expect(google).toEqual([C.entry.help, C.page.banner, C.preview.where].sort());
    for (const s of google) expect(s).toMatch(/Nothing is sent to Google\.|not Google Docs|not in Google Docs/);
  });
});
