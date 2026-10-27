import { setupWorker } from "msw/browser";
import { enablePersistence } from "./db";
import { handlers } from "./handlers";

export async function startMockWorker(): Promise<void> {
  enablePersistence();
  await setupWorker(...handlers).start({ onUnhandledFrame: "bypass", quiet: true });
}
