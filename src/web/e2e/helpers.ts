import { expect, type Page } from "@playwright/test";

export const uid = () => `${Date.now().toString(36)}${Math.random().toString(36).slice(2, 7)}`;

/** Real UI sign-in. Costs one /dev/login (rate limited to 10/min/IP): use sparingly. */
export async function signInViaUI(page: Page, email: string) {
  await page.goto("/signin");
  await page.getByLabel("Email").fill(email);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page).toHaveURL(/\/$/);
  await expect(page.getByRole("button", { name: "Sign out" })).toBeVisible();
}

/** Creates a course through the UI; returns its id from the redirect URL. */
export async function createCourseViaUI(page: Page, name: string) {
  await page.goto("/courses/new");
  await page.getByLabel(/Course name/).fill(name);
  await page.getByRole("button", { name: "Save course" }).click();
  await expect(page).toHaveURL(/\/courses\/[^/?]+\?added=1/);
  await expect(page.getByRole("heading", { level: 1, name })).toBeVisible();
  return new URL(page.url()).pathname.split("/").pop()!;
}
