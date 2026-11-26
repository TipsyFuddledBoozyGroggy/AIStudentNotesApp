import base from "./playwright.config";

// Runs the real-stack specs against the PRODUCTION containers (docker-compose.poc.yml: standalone Next server on :3100,
// API image behind it). No webServer: nothing is started, `next dev` is never involved. Start the stack first, with the
// audio-ticket limit raised for the run: TETHER_POC_AUDIO_TICKETS_PER_MINUTE=600 docker compose -f docker-compose.poc.yml up -d
// (at the shipped 6 a minute the sixth lecture start in this spec is answered 429 and the run fails).
// Run: E2E_BASE_URL=http://localhost:3100 E2E_USERS=alice,bob npx playwright test -c playwright.poc.config.ts --workers=1 e2e/lecture.spec.ts
// Only specs that read their origin from E2E_BASE_URL are valid here (today: e2e/lecture.spec.ts); the others still hard-code :3000.
const baseURL = process.env.E2E_BASE_URL ?? "http://localhost:3100";
process.env.E2E_BASE_URL = baseURL; // the spec module reads it for its Origin header and absolute API URLs
export default {
  ...base,
  use: { ...base.use, baseURL, timezoneId: "America/New_York" },
  outputDir: "./test-results/e2e-poc-artifacts",
  webServer: undefined,
} as typeof base;
