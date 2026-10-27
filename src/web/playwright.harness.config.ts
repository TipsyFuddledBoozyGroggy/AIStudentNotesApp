import { defineConfig, devices } from "@playwright/test";

// Browser checks for the dev-only lecture harness (/dev/lecture-harness/*). Needs NO API, NO login and NO microphone:
// the fixtures use FakeMicSource and MockLectureSource.
// Run: npx playwright test -c playwright.harness.config.ts
export default defineConfig({
  testDir: "./e2e-harness",
  fullyParallel: true,
  retries: 0,
  timeout: 60_000,
  expect: { timeout: 15_000 },
  reporter: [["list"]],
  use: { baseURL: "http://localhost:3100", trace: "retain-on-failure" },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: {
    command: "npx next dev -p 3100",
    url: "http://localhost:3100/dev/lecture-harness",
    reuseExistingServer: false,
    timeout: 120_000,
    env: { NEXT_PUBLIC_API_MOCKING: "disabled" },
  },
});
