import { expect, test } from "@playwright/test";
import { createCourseViaUI, signInViaUI, uid } from "./helpers";

// auth.v1 (docs/API_CONTRACTS_AUTH.md, ADR-022) against the real stack: Next proxying /api to the API. No request reaches Google: the
// callback probe uses a random state, which the API refuses before any token exchange.

test.describe("sign-in page states (auth.v1 section 4)", () => {
  for (const [outcome, title] of [
    ["not_invited", "Not on the invite list"],
    ["access_denied", "Sign-in cancelled"],
    ["school_account", "Personal accounts only"],
    ["state_invalid", "Sign-in did not work"],
    ["bogus-value", "Sign-in did not work"],
  ] as const) {
    test(`outcome ${outcome}: one message, focus on its heading, the query is dropped`, async ({ page }) => {
      await page.goto(`/signin?purpose=signin&outcome=${outcome}`);
      const heading = page.getByRole("heading", { level: 2, name: title });
      await expect(heading).toBeVisible();
      await expect(heading).toBeFocused();
      await expect(page).toHaveURL(/\/signin$/);
      await expect(page.getByText("bogus-value")).toHaveCount(0);
    });
  }

  test("the page offers exactly what the API says", async ({ page, request }) => {
    const methods = (await (await request.get("/api/v1/auth/methods")).json()) as { google: boolean; devLogin: boolean };
    await page.goto("/signin");
    await expect(page.getByRole("button", { name: "Continue with Google" })).toHaveCount(methods.google ? 1 : 0);
    await expect(page.getByLabel("Email")).toHaveCount(methods.devLogin ? 1 : 0);
  });
});

test.describe("callback through the web proxy", () => {
  test("a bogus state answers 302 with the relative outcome Location and expires the binder", async ({ request }) => {
    const methods = (await (await request.get("/api/v1/auth/methods")).json()) as { google: boolean };
    test.skip(!methods.google, "Google sign-in is not configured on this API (set Auth__Google__* for the dev API to run this)");
    const state = "A".repeat(43);
    const res = await request.get(`/api/v1/auth/google/callback?state=${state}&code=x`, { maxRedirects: 0 });
    expect(res.status()).toBe(302);
    expect(res.headers()["location"]).toBe("/signin?purpose=signin&outcome=state_invalid");
    expect(res.headers()["set-cookie"] ?? "").toMatch(/cc\.signin=;/);
  });
});

// S1 item 8: after Sign out, the Back button does not bring the signed-in data back. Uses the dev form, so it runs where dev login exists.
test.describe("sign out and Back", () => {
  test.use({ storageState: { cookies: [], origins: [] } });
  test("Back after sign-out shows the sign-in page, not the course", async ({ page, request }) => {
    const methods = (await (await request.get("/api/v1/auth/methods")).json()) as { devLogin: boolean };
    test.skip(!methods.devLogin, "needs the dev form to sign in (local stack)");
    await signInViaUI(page, `e2e-back-${uid()}@dev.local`);
    const name = `Back check ${uid()}`;
    await createCourseViaUI(page, name);
    await page.getByRole("button", { name: "Sign out" }).click();
    await expect(page).toHaveURL(/\/signin$/);
    await page.goBack();
    await expect(page).toHaveURL(/\/signin$/);
    await expect(page.getByText(name)).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Sign out" })).toHaveCount(0);
  });
});
