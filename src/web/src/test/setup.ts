import "@testing-library/jest-dom/vitest";
import { cleanup } from "@testing-library/react";
import { afterAll, afterEach, beforeAll } from "vitest";
import { resetMockDb } from "@/mocks/db";
import { server } from "@/mocks/server";

beforeAll(() => server.listen({ onUnhandledFrame: "error" }));
afterEach(() => {
  cleanup();
  server.resetHandlers();
  resetMockDb();
  window.localStorage.clear();
});
afterAll(() => server.close());
