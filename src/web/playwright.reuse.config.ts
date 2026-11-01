import base from "./playwright.config";

// Same as playwright.config.ts, but reuses a dev:real server already listening on :3000 instead of starting one.
// Run: npx playwright test -c playwright.reuse.config.ts
// Own outputDir: the default (test-results/) is wiped on every run and would delete test-results/redesign-screens/.
// timezoneId restated so the pin holds even if the base `use` changes (docs/ops/ci-first-run-e2e-diagnosis.md 2).
export default {
  ...base,
  use: { ...base.use, timezoneId: "America/New_York" },
  outputDir: "./test-results/e2e-artifacts",
  webServer: { ...base.webServer!, reuseExistingServer: true },
} as typeof base;
