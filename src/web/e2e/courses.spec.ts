import { test, expect, type Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { createCourseViaUI, signInViaUI, uid } from "./helpers";
import { stateFor } from "./users";

// Sessions come from global-setup (storageState) because /dev/login is rate limited.
const asUser = (k: Parameters<typeof stateFor>[0]) => test.use({ storageState: stateFor(k) });

test.describe("sign in via the UI", () => {
  test.use({ storageState: { cookies: [], origins: [] } });
  test("alice signs in through the form", async ({ page }) => {
    await signInViaUI(page, "alice@dev.local");
    await expect(page.getByRole("heading", { level: 1, name: "Hi, Alice." })).toBeVisible();
  });
});

test.describe("empty dashboard", () => {
  asUser("empty");
  test("shows add-first-course next step", async ({ page }) => {
    await page.goto("/");
    await expect(page.getByText("Add your first course to get started.")).toBeVisible();
    await page.getByRole("link", { name: "Add your first course" }).click();
    await expect(page).toHaveURL(/\/courses\/new$/);
  });
});

test.describe("persistence", () => {
  asUser("reload");
  test("created course appears on dashboard and survives reload", async ({ page }) => {
    const name = `Bio ${uid()}`;
    await createCourseViaUI(page, name);
    await page.goto("/");
    await expect(page.getByRole("heading", { name: "Your courses" })).toBeVisible();
    await expect(page.getByText(name).first()).toBeVisible();
    await page.reload();
    await expect(page.getByText(name).first()).toBeVisible();
  });
});

test.describe("session end", () => {
  asUser("signout");
  test("sign out redirects to /signin and protects pages", async ({ page }) => {
    await page.goto("/");
    await page.getByRole("button", { name: "Sign out" }).click();
    await expect(page).toHaveURL(/\/signin$/);
    await page.goto("/");
    await expect(page).toHaveURL(/\/signin$/);
  });

  test.describe("lost session", () => {
  // Own user: the sign-out test above revokes the server session that "signout" shares through storageState.
  asUser("lostSession");
  test("lost session (401) redirects to /signin", async ({ page, context }) => {
    await page.goto("/");
    await expect(page.getByRole("button", { name: "Sign out" })).toBeVisible();
    await context.clearCookies();
    await page.goto("/courses/new");
    await expect(page).toHaveURL(/\/signin$/);
  });
  });
});

test("bob cannot see or open alice's course", async ({ browser }) => {
  const aliceCtx = await browser.newContext({ storageState: stateFor("alice") });
  const bobCtx = await browser.newContext({ storageState: stateFor("bob") });
  try {
    const alice = await aliceCtx.newPage();
    const bob = await bobCtx.newPage();
    const name = `Alice private ${uid()}`;
    const id = await createCourseViaUI(alice, name);

    await bob.goto("/");
    await expect(bob.getByRole("button", { name: "Sign out" })).toBeVisible();
    await expect(bob.getByText(name)).toHaveCount(0);
    await bob.goto(`/courses/${id}`);
    await expect(bob.getByRole("heading", { name: "We can't find that course" })).toBeVisible();
    await expect(bob.getByText(name)).toHaveCount(0);
  } finally {
    await aliceCtx.close();
    await bobCtx.close();
  }
});

test.describe("keyboard", () => {
  asUser("keyboard");
  test("course creation using only the keyboard", async ({ page }) => {
    await page.goto("/courses/new");
    const name = `Kbd ${uid()}`;
    const nameInput = page.getByLabel(/Course name/);
    await expect(nameInput).toBeVisible();
    // Tab from the document start until the name field is focused (header controls come first).
    for (let i = 0; i < 10 && !(await nameInput.evaluate((el) => el === document.activeElement)); i++) {
      await page.keyboard.press("Tab");
    }
    await expect(nameInput).toBeFocused();
    await page.keyboard.type(name);
    await page.keyboard.press("Tab"); // Show options
    await page.keyboard.press("Tab"); // Save course (options hidden)
    await expect(page.getByRole("button", { name: "Save course" })).toBeFocused();
    await page.keyboard.press("Enter");
    await expect(page).toHaveURL(/\/courses\/[^/?]+\?added=1/);
    await expect(page.getByRole("heading", { level: 1, name })).toBeFocused();
  });
});

const scan = async (page: Page) => {
  const r = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21aa", "wcag22aa", "best-practice"]).analyze();
  expect(r.violations.map((v) => `${v.id} (${v.impact}): ${v.nodes.map((n) => n.target.join(" ")).join(" | ")}`)).toEqual([]);
};

test.describe("axe incl. color-contrast", () => {
  test.describe("signed out", () => {
    test.use({ storageState: { cookies: [], origins: [] } });
    test("/signin", async ({ page }) => {
      await page.goto("/signin");
      await expect(page.getByRole("heading", { name: "Sign in" })).toBeVisible();
      await scan(page);
    });
  });

  test.describe("empty user", () => {
    asUser("axeEmpty");
    test("dashboard empty", async ({ page }) => {
      await page.goto("/");
      await expect(page.getByRole("link", { name: "Add your first course" })).toBeVisible();
      await scan(page);
    });
    test("/courses/new", async ({ page }) => {
      await page.goto("/courses/new");
      await expect(page.getByLabel(/Course name/)).toBeVisible();
      await scan(page);
    });
  });

  test.describe("user with a course", () => {
    asUser("axe");
    test("course detail and populated dashboard", async ({ page }) => {
      await createCourseViaUI(page, `A11y ${uid()}`);
      await scan(page);
      await page.goto("/");
      await expect(page.getByRole("heading", { name: "Your courses" })).toBeVisible();
      await scan(page);
    });
  });
});
