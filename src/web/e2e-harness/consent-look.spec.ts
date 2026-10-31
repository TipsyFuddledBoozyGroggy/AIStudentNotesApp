import { expect, test, type Page } from "@playwright/test";

// Gate G1 (redesign-round2 slice 2): the consent page and the teacher-permission pop-up keep their pre-slice-2 computed
// look while every other dialog, quiet button, link and disabled button moved to the v2 primitives. The values below are
// the ones the consent surfaces rendered before slice 2 (Paper, light): scrim .55, raised dialog box 28rem / 16px radius /
// 24px padding / 30px serif heading, accent quiet buttons ("Not now", "Cancel"), the dashed border-strong disabled
// style on an unticked "Confirm and start recording", the uppercase 13px bold eyebrow and the Draft tag. Dev harness
// only: FakeMicSource, no API, no login, no microphone.

const DIALOG_NAME = "Did your teacher say you can record?";
const PAPER = { accent: "rgb(46, 60, 140)", borderStrong: "rgb(138, 133, 121)", raised: "rgb(255, 255, 255)", warning: "rgb(122, 67, 0)", muted: "rgb(87, 83, 74)" };

const style = (page: Page, locator: ReturnType<Page["locator"]>, props: string[]) =>
  locator.evaluate((el, ps) => Object.fromEntries(ps.map((p) => [p, getComputedStyle(el).getPropertyValue(p)])), props);

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => window.localStorage.setItem("tether.prefs.v1", JSON.stringify({ theme: "light" })));
  await page.goto("/dev/lecture-harness/consent");
  await expect(page.getByText(/^Fixture:/)).toBeVisible();
});

test("consent page: eyebrow, Draft tag, 'Not now' and the Start button keep the legacy look", async ({ page }) => {
  const eyebrow = page.getByText("Before you start", { exact: true });
  expect(await style(page, eyebrow, ["text-transform", "font-size", "font-weight", "letter-spacing", "color"])).toEqual({
    "text-transform": "uppercase",
    "font-size": "13px",
    "font-weight": "700",
    "letter-spacing": "1.04px",
    color: PAPER.muted,
  });
  const notNow = page.getByRole("button", { name: "Not now" });
  const n = await style(page, notNow, ["color", "border-top-color", "border-top-width", "border-top-style", "background-color", "min-height"]);
  expect(n.color).toBe(PAPER.accent);
  expect(n["border-top-color"]).toBe("rgba(0, 0, 0, 0)");
  expect(n["border-top-width"]).toBe("2px");
  expect(n["background-color"]).toBe("rgba(0, 0, 0, 0)");
  expect(n["min-height"]).toBe("48px");
  const tag = page.getByText("Draft", { exact: true });
  const t = await style(page, tag, ["color", "border-top-color", "border-top-left-radius", "padding-left", "padding-top"]);
  expect(t.color).toBe(PAPER.warning);
  expect(t["border-top-color"]).toBe(PAPER.warning);
  expect(t["border-top-left-radius"]).toBe("9999px");
  expect(t["padding-left"]).toBe("10px");
  expect(t["padding-top"]).toBe("2px");
  const start = page.getByRole("button", { name: "Start recording", exact: true });
  expect(await style(page, start, ["min-height", "background-color"])).toEqual({ "min-height": "48px", "background-color": PAPER.accent });
});

test("teacher-permission pop-up: scrim .55, raised 28rem box, 30px heading, dashed disabled confirm, accent Cancel", async ({ page }) => {
  await page.getByRole("button", { name: "Start recording", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: DIALOG_NAME });
  await expect(dialog).toBeVisible();
  const overlay = page.locator("[data-consent-dialog-overlay]");
  expect((await style(page, overlay, ["background-color"]))["background-color"]).toBe("rgba(20, 22, 27, 0.55)");
  await expect(overlay).not.toHaveClass(/dialog-scrim/);
  await expect(dialog).not.toHaveClass(/(?<![\w-])(dialog|sheet)(?![\w-])/); // its own classes (rounded-sheet, bg-raised), not .dialog / .sheet
  expect(await style(page, dialog, ["max-width", "background-color", "border-top-left-radius", "padding-left", "border-top-width", "border-top-color"])).toEqual({
    "max-width": "448px",
    "background-color": PAPER.raised,
    "border-top-left-radius": "16px",
    "padding-left": "24px",
    "border-top-width": "1px",
    "border-top-color": "rgb(218, 213, 200)",
  });
  const heading = dialog.getByRole("heading", { name: DIALOG_NAME });
  expect((await style(page, heading, ["font-size"]))["font-size"]).toBe("30px");
  const confirm = dialog.getByRole("button", { name: "Confirm and start recording" });
  await expect(confirm).toHaveAttribute("aria-disabled", "true");
  expect(await style(page, confirm, ["border-top-style", "border-top-color", "min-height", "color"])).toEqual({
    "border-top-style": "dashed",
    "border-top-color": PAPER.borderStrong,
    "min-height": "48px",
    color: PAPER.muted,
  });
  const cancel = dialog.getByRole("button", { name: "Cancel" });
  const c = await style(page, cancel, ["color", "background-color", "min-height"]);
  expect(c.color).toBe(PAPER.accent);
  expect(c["background-color"]).toBe("rgba(0, 0, 0, 0)");
  expect(c["min-height"]).toBe("48px");
  // The box starts unticked and nothing has started.
  await expect(dialog.getByRole("checkbox")).not.toBeChecked();
  await page.keyboard.press("Escape");
  await expect(dialog).toHaveCount(0);
});

test("by contrast, the Stop dialog in the lecture takes the v2 dialog treatment (scrim .6, sheet, 24px heading)", async ({ page }) => {
  await page.goto("/dev/lecture-harness/live");
  await expect(page.getByText(/^Fixture:/)).toBeVisible();
  await page.getByRole("button", { name: "Stop", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: /Stop/ });
  await expect(dialog).toBeVisible();
  await expect(dialog).toHaveClass(/\bdialog\b/);
  const scrim = page.locator(".dialog-scrim");
  expect((await style(page, scrim, ["background-color"]))["background-color"]).toBe("rgba(20, 22, 27, 0.6)");
  expect(await style(page, dialog, ["max-width", "background-color", "padding-left"])).toEqual({ "max-width": "448px", "background-color": "rgb(255, 255, 255)", "padding-left": "24px" });
  expect((await style(page, dialog.getByRole("heading"), ["font-size"]))["font-size"]).toBe("24px");
  await page.keyboard.press("Escape");
  await expect(dialog).toHaveCount(0);
});
