import { request, type FullConfig } from "@playwright/test";
import { mkdirSync } from "node:fs";
import { AUTH_DIR, USERS, authFile } from "./users";

// The API rate-limits /dev/login to 10/min per IP, so sign in once per user here and share
// the session via storageState. Waits out a 429 (e.g. a previous run within the same minute).
export default async function globalSetup(config: FullConfig) {
  const baseURL = config.projects[0].use.baseURL!;
  mkdirSync(AUTH_DIR, { recursive: true });

  // Warm the dev server so the first test does not pay route compile time.
  const warm = await request.newContext({ baseURL });
  for (const p of ["/signin", "/", "/courses/new"]) await warm.get(p).catch(() => undefined);
  await warm.dispose();

  for (const email of Object.values(USERS)) {
    let done = false;
    for (let attempt = 0; attempt < 3 && !done; attempt++) {
      const ctx = await request.newContext({ baseURL, extraHTTPHeaders: { Origin: baseURL } });
      const res = await ctx.post("/api/v1/dev/login", { data: { email } });
      if (res.status() === 429) {
        await ctx.dispose();
        await new Promise((r) => setTimeout(r, 61_000));
        continue;
      }
      if (res.status() !== 204) throw new Error(`dev login for ${email} returned ${res.status()}`);
      await ctx.storageState({ path: authFile(email) });
      await ctx.dispose();
      done = true;
    }
    if (!done) throw new Error(`dev login for ${email} stayed rate limited`);
  }
}
