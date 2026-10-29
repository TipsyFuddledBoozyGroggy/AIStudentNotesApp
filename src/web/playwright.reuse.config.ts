import base from "./playwright.config";

// Same as playwright.config.ts, but reuses a dev:real server already listening on :3000 instead of starting one.
// Run: npx playwright test -c playwright.reuse.config.ts
// Own outputDir: the default (test-results/) is wiped on every run and would delete test-results/redesign-screens/.
export default { ...base, outputDir: "./test-results/e2e-artifacts", webServer: { ...base.webServer!, reuseExistingServer: true } } as typeof base;
