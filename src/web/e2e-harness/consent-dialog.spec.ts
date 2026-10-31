import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";

// Teacher-permission consent dialog (PO-16; wording is DRAFT, gate G1 BLOCKED). Dev harness only: FakeMicSource and a
// fake backend, so no API, no login and no real microphone or audio is involved.

const DIALOG_NAME = "Did your teacher say you can record?";
const dialog = (page: Page) => page.getByRole("dialog", { name: DIALOG_NAME });
const startButton = (page: Page) => page.getByRole("button", { name: "Start recording", exact: true });

async function spyMic(page: Page) {
  await page.addInitScript(() => {
    const w = window as unknown as { __gum: number };
    w.__gum = 0;
    const md = navigator.mediaDevices;
    if (md) {
      md.getUserMedia = async () => {
        w.__gum++;
        throw new DOMException("x", "NotAllowedError");
      };
    }
  });
}
const gumCalls = (page: Page) => page.evaluate(() => (window as unknown as { __gum?: number }).__gum ?? 0);

async function openFixture(page: Page, id: string) {
  await page.goto(`/dev/lecture-harness/${id}`);
  await expect(page.getByText(/^Fixture:/)).toBeVisible();
  await expect(startButton(page)).toBeVisible();
}

test("axe (WCAG 2.2 AA tags, incl. colour contrast) has no violations with the dialog open", async ({ page }) => {
  await openFixture(page, "consent");
  await startButton(page).click();
  await expect(dialog(page)).toBeVisible();
  await page.waitForTimeout(300);
  const r = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"]).analyze();
  expect(r.violations.map((v) => `${v.id}: ${v.nodes.map((n) => n.html).join(" | ")}`)).toEqual([]);
});

test("keyboard: Enter opens, focus starts on the unticked box, Tab stays inside, Escape closes and returns focus", async ({ page }) => {
  await openFixture(page, "consent");
  await startButton(page).focus();
  await page.keyboard.press("Enter");
  const d = dialog(page);
  await expect(d).toBeVisible();
  const box = d.getByRole("checkbox", { name: "My teacher said I can record this class." });
  await expect(box).toBeFocused();
  await expect(box).not.toBeChecked();
  for (let i = 0; i < 7; i++) {
    await page.keyboard.press(i % 3 === 2 ? "Shift+Tab" : "Tab");
    expect(await d.evaluate((el) => el.contains(document.activeElement))).toBe(true);
  }
  await page.keyboard.press("Escape");
  await expect(d).toHaveCount(0);
  await expect(startButton(page)).toBeFocused();
});

test("the page behind the dialog is inert while it is open, and usable again after Cancel", async ({ page }) => {
  await openFixture(page, "consent");
  await startButton(page).click();
  await expect(dialog(page)).toBeVisible();
  const notNowInert = () =>
    page.getByRole("button", { name: "Not now", includeHidden: true }).evaluate((el) => el.closest("[inert]") !== null);
  expect(await notNowInert()).toBe(true);
  expect(await dialog(page).evaluate((el) => el.closest("[inert]") === null)).toBe(true);
  await dialog(page).getByRole("button", { name: "Cancel" }).click();
  await expect(dialog(page)).toHaveCount(0);
  expect(await notNowInert()).toBe(false);
});

test("confirm with the box unticked does nothing but point to the hint; no auto-dismiss", async ({ page }) => {
  await openFixture(page, "consent");
  await startButton(page).click();
  const confirm = dialog(page).getByRole("button", { name: "Confirm and start recording" });
  await expect(confirm).toHaveAttribute("aria-disabled", "true");
  await confirm.click({ force: true }); // aria-disabled stays focusable and clickable; the press only moves focus
  await expect(dialog(page).getByText("Check the box above to continue.")).toBeFocused();
  await page.waitForTimeout(3000);
  await expect(dialog(page)).toBeVisible();
});

test("start flow: Cancel and Escape keep the microphone off; confirming shows the Recording status", async ({ page }) => {
  await spyMic(page);
  await openFixture(page, "start-flow");
  await startButton(page).click();
  await dialog(page).getByRole("checkbox").check();
  await dialog(page).getByRole("button", { name: "Cancel" }).click();
  await startButton(page).click();
  await dialog(page).getByRole("checkbox").check();
  await page.keyboard.press("Escape");
  await expect(dialog(page)).toHaveCount(0);
  await expect(page.getByTestId("state-label")).toHaveCount(0);
  expect(await gumCalls(page)).toBe(0);

  await startButton(page).click();
  await dialog(page).getByRole("checkbox").check();
  await dialog(page).getByRole("button", { name: "Confirm and start recording" }).click();
  await expect(page.getByTestId("state-label")).toHaveText("Recording");
  await expect(page.getByRole("heading", { name: "Recording" })).toBeFocused();
});

test("320px: the dialog fits with no horizontal scroll and both buttons can be reached", async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 640 });
  await openFixture(page, "consent");
  await startButton(page).click();
  const d = dialog(page);
  await expect(d).toBeVisible();
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBeLessThanOrEqual(0);
  const box = await d.boundingBox();
  expect(box!.x).toBeGreaterThanOrEqual(0);
  expect(box!.x + box!.width).toBeLessThanOrEqual(320);
  for (const name of ["Confirm and start recording", "Cancel"]) {
    const b = d.getByRole("button", { name });
    await b.scrollIntoViewIfNeeded();
    await expect(b).toBeInViewport();
  }
});

test("forced colours: the dialog and its controls stay visible", async ({ browser }) => {
  const ctx = await browser.newContext({ forcedColors: "active" });
  const page = await ctx.newPage();
  await page.goto("http://localhost:3100/dev/lecture-harness/consent");
  await page.getByRole("button", { name: "Start recording", exact: true }).click();
  const d = page.getByRole("dialog", { name: DIALOG_NAME });
  await expect(d.getByRole("checkbox")).toBeVisible();
  await expect(d.getByRole("button", { name: "Confirm and start recording" })).toBeVisible();
  await ctx.close();
});
