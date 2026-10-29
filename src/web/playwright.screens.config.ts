import { defineConfig, devices } from "@playwright/test";

// Redesign verification: full-page screenshots of each screen at 390px and 1280px in the four looks
// (Paper, Night desk, Fog, Dusk) plus an axe scan of every one. Needs `npm run dev:real` on :3000 (real API for the
// signed-in pages; the lecture states come from the dev-only harness, synthetic data, no microphone).
// Run: npx playwright test -c playwright.screens.config.ts      Output: test-results/redesign-screens/ (gitignored)
export default defineConfig({
  testDir: "./e2e-screens",
  outputDir: "./test-results/redesign-screens-artifacts",
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 600_000,
  expect: { timeout: 30_000 },
  reporter: [["list"]],
  use: { baseURL: "http://localhost:3000", trace: "off" },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: { command: "npm run dev:real", url: "http://localhost:3000/signin", reuseExistingServer: true, timeout: 120_000 },
});
