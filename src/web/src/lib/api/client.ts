import { ApiError } from "./errors";
import type { AudioTicket, Course, CourseList, CreateCourseInput, CreateSessionInput, LectureSessionDto, ProblemDetails, UserProfile } from "./types";

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
  createSession(input: CreateSessionInput): Promise<LectureSessionDto>;
  getSession(id: string): Promise<LectureSessionDto>;
  /** Pause and stop are never refused by the server. Resume can be 429 `rate_limited`. */
  pauseSession(id: string): Promise<LectureSessionDto>;
  resumeSession(id: string): Promise<LectureSessionDto>;
  stopSession(id: string): Promise<LectureSessionDto>;
  createAudioTicket(id: string): Promise<AudioTicket>;
  /** Raw page; the caller validates it. */
  getTranscriptPage(id: string, afterSeq: string | null, limit: number): Promise<unknown>;
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
    async createSession(input) {
      const body = { consent: input.consent, ...(input.title ? { title: input.title } : {}) };
      const res = await request(`/courses/${encodeURIComponent(input.courseId)}/sessions`, {
        method: "POST",
        headers: { "Idempotency-Key": input.idempotencyKey },
        body: JSON.stringify(body),
      });
      return res.json() as Promise<LectureSessionDto>;
    },
    async getSession(id) {
      return (await request(`/sessions/${encodeURIComponent(id)}`)).json() as Promise<LectureSessionDto>;
    },
    async pauseSession(id) {
      return (await request(`/sessions/${encodeURIComponent(id)}/pause`, { method: "POST" })).json() as Promise<LectureSessionDto>;
    },
    async resumeSession(id) {
      return (await request(`/sessions/${encodeURIComponent(id)}/resume`, { method: "POST" })).json() as Promise<LectureSessionDto>;
    },
    async stopSession(id) {
      return (await request(`/sessions/${encodeURIComponent(id)}/stop`, { method: "POST" })).json() as Promise<LectureSessionDto>;
    },
    async createAudioTicket(id) {
      return (await request(`/sessions/${encodeURIComponent(id)}/audio-ticket`, { method: "POST" })).json() as Promise<AudioTicket>;
    },
    async getTranscriptPage(id, afterSeq, limit) {
      const q = new URLSearchParams({ afterSeq: afterSeq ?? "0", limit: String(limit) });
      return (await request(`/sessions/${encodeURIComponent(id)}/transcript?${q}`)).json();
    },
  };
}
