import base from "./playwright.harness.config";

// Same as playwright.harness.config.ts, but reuses a server already answering on :3100 (for example a forwarder to a
// running `npm run dev:real`, whose MSW is also disabled) instead of starting a second `next dev` in this directory.
export default {
  ...base,
  use: { ...base.use, timezoneId: "America/New_York" }, // host-zone independent (docs/ops/ci-first-run-e2e-diagnosis.md 2)
  outputDir: "./test-results/harness-artifacts",
  webServer: { ...base.webServer!, reuseExistingServer: true },
} as typeof base;
