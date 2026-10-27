import { ApiError } from "./errors";
import type { Course, CourseList, CreateCourseInput, ProblemDetails, UserProfile } from "./types";

/**
 * The only module that knows how to talk HTTP to the backend. UI code depends on
 * the ApiClient interface, so MSW (today) and the ASP.NET Core API (later) are
 * interchangeable. TODO(backend): send X-CSRF-TOKEN on unsafe methods once the
 * antiforgery endpoint is specified (backend-foundation.md section 4).
 */
export interface ApiClient {
  getMe(): Promise<UserProfile>;
  devLogin(email: string): Promise<void>;
  logout(): Promise<void>;
  listCourses(): Promise<CourseList>;
  createCourse(input: CreateCourseInput): Promise<Course>;
  getCourse(id: string): Promise<Course>;
}

export interface ApiClientOptions {
  baseUrl: string;
  fetchImpl?: typeof fetch;
}

export function createApiClient({ baseUrl, fetchImpl }: ApiClientOptions): ApiClient {
  const base = baseUrl.replace(/\/$/, "");

  async function request(path: string, init: RequestInit = {}): Promise<Response> {
    const doFetch = fetchImpl ?? fetch;
    let res: Response;
    try {
      res = await doFetch(`${base}${path}`, {
        credentials: "include",
        ...init,
        headers: {
          Accept: "application/json, application/problem+json",
          ...(init.body ? { "Content-Type": "application/json" } : {}),
          ...init.headers,
        },
      });
    } catch {
      throw new ApiError(0, "network_error", "Could not reach the server.");
    }
    if (!res.ok) {
      let problem: ProblemDetails | undefined;
      try {
        problem = (await res.json()) as ProblemDetails;
      } catch {
        problem = undefined;
      }
      throw new ApiError(
        res.status,
        problem?.code ?? "http_error",
        problem?.detail ?? problem?.title ?? `Request failed (${res.status}).`,
        problem,
      );
    }
    return res;
  }

  return {
    async getMe() {
      return (await request("/me")).json() as Promise<UserProfile>;
    },
    async devLogin(email) {
      await request("/dev/login", { method: "POST", body: JSON.stringify({ email }) });
    },
    async logout() {
      await request("/auth/logout", { method: "POST" });
    },
    async listCourses() {
      return (await request("/courses")).json() as Promise<CourseList>;
    },
    async createCourse(input) {
      return (await request("/courses", { method: "POST", body: JSON.stringify(input) })).json() as Promise<Course>;
    },
    async getCourse(id) {
      return (await request(`/courses/${encodeURIComponent(id)}`)).json() as Promise<Course>;
    },
  };
}
