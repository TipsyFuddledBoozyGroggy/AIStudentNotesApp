import { test, expect, type Request } from "@playwright/test";
import { createCourseViaUI, uid } from "./helpers";
import { stateFor } from "./users";

// docs/ops/ci-first-run-e2e-diagnosis.md 2: a browser that reports UTC (Chromium on a TZ=UTC host reports "UTC") must be
// able to create a syllabus. The configs pin America/New_York; this one case overrides it to UTC so the regression stays
// covered whatever the host zone is. Real stack (Next dev on :3000 proxying the API on :5080), synthetic text only.
// Needs the API allow-list that accepts Etc/UTC (Support.cs ZoneAllowList).

test.use({ storageState: stateFor("sylUtc"), timezoneId: "UTC" });

test("a browser in UTC sends Etc/UTC and the syllabus is created", async ({ page }) => {
  test.setTimeout(120_000);
  const creates: Request[] = [];
  page.on("request", (r) => {
    if (r.method() === "POST" && /\/api\/v1\/courses\/[^/]+\/syllabi$/.test(new URL(r.url()).pathname)) creates.push(r);
  });
  const courseId = await createCourseViaUI(page, `UTC ${uid()}`);
  expect(await page.evaluate(() => Intl.DateTimeFormat().resolvedOptions().timeZone)).toMatch(/^(Etc\/)?UTC$/);

  await page.goto(`/courses/${courseId}/syllabus`);
  await expect(page.getByRole("heading", { level: 1, name: "Add your syllabus" })).toBeFocused();
  await expect(page.getByText("Times will use your time zone: Etc/UTC.")).toBeVisible();
  await page.getByLabel("Syllabus text").fill("Quiz 1 on September 14, 2026\nEssay due October 30, 2026, 11:59 PM");
  await page.getByRole("checkbox", { name: "I read the notice above. (Draft placeholder wording.)" }).check();
  await page.getByRole("button", { name: "Read my syllabus" }).click();

  await expect(page.getByRole("heading", { level: 1, name: "Reading your syllabus" })).toBeVisible();
  expect(creates).toHaveLength(1);
  expect(creates[0].postDataJSON()).toMatchObject({ defaultTimezone: "Etc/UTC" });
  expect((await creates[0].response())?.status()).toBe(202);
  await expect(page.getByText(/time zone, .* yet/)).toHaveCount(0);
  await page.getByRole("button", { name: "Review items" }).click({ timeout: 30_000 });
  await expect(page.getByRole("heading", { level: 1, name: "Review your syllabus items" })).toBeFocused();
});
