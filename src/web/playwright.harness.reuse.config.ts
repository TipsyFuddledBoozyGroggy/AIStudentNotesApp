import base from "./playwright.harness.config";

// Same as playwright.harness.config.ts, but reuses a server already answering on :3100 (for example a forwarder to a
// running `npm run dev:real`, whose MSW is also disabled) instead of starting a second `next dev` in this directory.
export default { ...base, outputDir: "./test-results/harness-artifacts", webServer: { ...base.webServer!, reuseExistingServer: true } } as typeof base;
