import { defineConfig, devices } from "@playwright/test";

// Real-stack E2E: Next dev server (MSW disabled, /api proxied to the API on :5080).
export default defineConfig({
  testDir: "./e2e",
  fullyParallel: true,
  retries: 0,
  globalSetup: "./e2e/global-setup.ts",
  timeout: 60_000,
  expect: { timeout: 30_000 }, // first hit of each route compiles in next dev
  reporter: [["list"], ["html", { open: "never" }]],
  use: { baseURL: "http://localhost:3000", trace: "retain-on-failure" },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: {
    command: "npm run dev:real",
    url: "http://localhost:3000/signin",
    reuseExistingServer: false,
    timeout: 120_000,
  },
});
