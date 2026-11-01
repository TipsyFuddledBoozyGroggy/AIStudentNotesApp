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
  // timezoneId pinned so results do not depend on the host zone (a UTC CI runner broke the syllabus flow before the API
  // accepted Etc/UTC; docs/ops/ci-first-run-e2e-diagnosis.md 2). e2e/syllabus-utc.spec.ts covers a UTC browser explicitly.
  use: { baseURL: "http://localhost:3000", trace: "retain-on-failure", timezoneId: "America/New_York" },
  projects: [
    {
      name: "chromium",
      use: {
        ...devices["Desktop Chrome"],
        // Synthetic microphone: Chromium feeds a generated beep, so no real device or real audio is involved. The
        // auto-accept flag only grants the prompt; tests assert getUserMedia is not called before the Start press.
        launchOptions: { args: ["--use-fake-device-for-media-stream", "--use-fake-ui-for-media-stream"] },
      },
    },
  ],
  webServer: {
    command: "npm run dev:real",
    url: "http://localhost:3000/signin",
    reuseExistingServer: false,
    timeout: 120_000,
  },
});
