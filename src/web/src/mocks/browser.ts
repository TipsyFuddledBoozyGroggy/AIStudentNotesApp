import { setupWorker } from "msw/browser";
import { enablePersistence } from "./db";
import { handlers } from "./handlers";
import { enableSyllabusPersistence } from "./syllabusDb";

export async function startMockWorker(): Promise<void> {
  enablePersistence();
  enableSyllabusPersistence();
  await setupWorker(...handlers).start({ onUnhandledFrame: "bypass", quiet: true });
}
