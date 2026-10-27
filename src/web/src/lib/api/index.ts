import { createApiClient } from "./client";

export * from "./types";
export * from "./errors";
export type { ApiClient } from "./client";
export { createApiClient } from "./client";

/** Singleton used by the app. Base URL is same-origin by default (ADR-006). */
export const api = createApiClient({
  baseUrl: process.env.NEXT_PUBLIC_API_BASE_URL ?? "/api/v1",
});
