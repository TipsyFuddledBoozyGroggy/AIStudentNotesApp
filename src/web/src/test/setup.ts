import "@testing-library/jest-dom/vitest";
import { cleanup } from "@testing-library/react";
import { afterAll, afterEach, beforeAll } from "vitest";
import { resetMockDb } from "@/mocks/db";
import { server } from "@/mocks/server";
import { resetSyllabusDb } from "@/mocks/syllabusDb";

beforeAll(() => server.listen({ onUnhandledFrame: "error" }));
afterEach(() => {
  cleanup();
  server.resetHandlers();
  resetMockDb();
  resetSyllabusDb();
  window.localStorage.clear();
});
afterAll(() => server.close());
