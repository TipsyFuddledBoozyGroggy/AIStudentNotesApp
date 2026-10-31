import { expect, test, type Page } from "@playwright/test";
import { uid } from "./helpers";
import { stateFor } from "./users";

// Slice 8x (1): the reopen route's hub lifecycle on the real stack (`next dev`, React StrictMode, real API hub). Before the
// fix, StrictMode's effect run / cleanup / run left two open hub sockets per page (one joined, one orphaned and never
// closed); with another tab of the same user open, the joining socket was the 4th and the server's per-user cap (C-26: 3)
// refused it on every retry, so the bar said "Not recording" although the server says Stopped. Here a second tab stays
// open on the same lecture for the whole test, and the route is loaded 5 times in a fresh page each time: every load must
// reach "Stopped" (never left on "Not recording") and open exactly one hub socket.
// No audio, no microphone, no AI: the session is started with the draft consent attestation and stopped at once over the API.
test.use({ storageState: stateFor("reopen") });

const hubSockets = (page: Page) => {
  const open = new Set<object>();
  page.on("websocket", (ws) => {
    if (!ws.url().includes("/hubs/lecture")) return;
    open.add(ws);
    ws.on("close", () => open.delete(ws));
  });
  return open;
};

test("reopen route: the bar shows Stopped after load (5 loads, another tab open) with one hub socket per page", async ({ page, context }) => {
  const origin = { Origin: "http://localhost:3000" };
  const course = await page.request.post("/api/v1/courses", { headers: origin, data: { name: `Reopen ${uid()}` } });
  expect(course.status()).toBe(201);
  const courseId = ((await course.json()) as { id: string }).id;
  const s = await page.request.post(`/api/v1/courses/${courseId}/sessions`, {
    headers: { ...origin, "Idempotency-Key": `reopen-${uid()}` },
    data: { consent: { acknowledged: true, textVersion: "draft-2" } },
  });
  expect(s.status(), await s.text()).toBe(201);
  const sid = ((await s.json()) as { id: string }).id;
  expect((await page.request.post(`/api/v1/sessions/${sid}/stop`, { headers: origin })).status()).toBeLessThan(300);

  const otherTab = hubSockets(page);
  await page.goto(`/lectures/${sid}`);
  await expect(page.getByTestId("state-label")).toHaveText("Stopped");
  await expect.poll(() => otherTab.size).toBe(1);

  for (let load = 1; load <= 5; load++) {
    const p = await context.newPage();
    const sockets = hubSockets(p);
    await p.goto(`/lectures/${sid}`);
    const label = p.getByTestId("state-label");
    await expect(label, `load ${load}`).toHaveText("Stopped", { timeout: 15_000 });
    await expect(label, `load ${load}`).not.toHaveText("Not recording");
    await expect.poll(() => sockets.size, { message: `load ${load}: hub sockets open` }).toBe(1);
    await p.close();
  }
  await expect(page.getByTestId("state-label")).toHaveText("Stopped"); // the other tab was never disturbed
});
