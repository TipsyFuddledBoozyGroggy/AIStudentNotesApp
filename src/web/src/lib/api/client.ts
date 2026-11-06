import { notesRequestBody } from "@/lib/lecture/aiPayloads";
import { ApiError } from "./errors";
import type { AudioTicket, Course, CourseList, CreateCourseInput, CreateSessionInput, LectureSessionDto, ProblemDetails, SessionList, UserProfile } from "./types";

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
  /**
   * Deletes the course and everything in it (`DELETE /courses/{id}?purge=true`; the plain form only hides the course and is
   * not offered, PO-20by). The server stops a live lecture first. 204; 404 gone or not yours; 409 lost a race (nothing deleted).
   */
  deleteCourse(id: string): Promise<void>;
  /** Every lecture of a course, all states, following `nextCursor` (server maximum 100 a page). 404 when not yours. */
  listSessions(courseId: string): Promise<LectureSessionDto[]>;
  createSession(input: CreateSessionInput): Promise<LectureSessionDto>;
  getSession(id: string): Promise<LectureSessionDto>;
  /** Pause and stop are never refused by the server. Resume can be 429 `rate_limited`. */
  pauseSession(id: string): Promise<LectureSessionDto>;
  resumeSession(id: string): Promise<LectureSessionDto>;
  stopSession(id: string): Promise<LectureSessionDto>;
  /** C-21 `DELETE /sessions/{id}`: stops a live lecture first, then deletes it. 204; 404 gone; 409 lost a race (nothing deleted). */
  deleteSession(id: string): Promise<void>;
  createAudioTicket(id: string): Promise<AudioTicket>;
  /** Raw page; the caller validates it. */
  getTranscriptPage(id: string, afterSeq: string | null, limit: number): Promise<unknown>;
  /** Latest summary revision (or a specific one). 404 `not_found` means no summary yet. Raw body; the caller parses it. */
  getSummary(id: string, revision?: number): Promise<unknown>;
  /**
   * Quick recap. One Idempotency-Key per press (reused only to retry the same press after a network failure).
   * Errors: 400 validation_failed, 404, 409 invalid_state (not Active/Paused), 429 rate_limited. Raw body.
   */
  postQuickRecap(id: string, windowMinutes: 2 | 5 | 10, idempotencyKey: string): Promise<unknown>;
  /** Poll fallback for catchup.ready. 404 when unknown. Raw body. */
  getCatchUp(id: string, catchUpId: string): Promise<unknown>;
  /**
   * Make notes after Stop (PO-18d, API_CONTRACTS section 17): body exactly `{"noticeVersion": "<id>"}`, Idempotency-Key
   * required (a fresh key per deliberate press; the SAME key only to retry that press after a network failure).
   * 202 + Location for a new attempt, 200 for a press already running or finished (key replay: Idempotency-Replayed).
   * Errors: 400 validation_failed, 401, 403 csrf_failed, 404, 409 invalid_state (not Stopped), 415, 429 rate_limited.
   * Raw body; the caller parses it strictly.
   */
  postNotes(id: string, noticeVersion: string, idempotencyKey: string): Promise<unknown>;
  /** Newest notes attempt. 404 foreign or missing; 409 invalid_state while nothing was pressed yet. Raw body. */
  getNotes(id: string): Promise<unknown>;
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
        // Never a status code: this text can reach the UI (language guide A26).
        problem?.detail ?? problem?.title ?? "That did not work. Nothing was changed.",
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
    async deleteCourse(id) {
      await request(`/courses/${encodeURIComponent(id)}?purge=true`, { method: "DELETE" });
    },
    async listSessions(courseId) {
      const out: LectureSessionDto[] = [];
      let cursor: string | null = null;
      // 50 pages of 100 is far beyond one course; the bound only guards against a cursor that never ends.
      for (let i = 0; i < 50; i++) {
        const q = new URLSearchParams({ limit: "100", ...(cursor ? { cursor } : {}) });
        const page = (await (await request(`/courses/${encodeURIComponent(courseId)}/sessions?${q}`)).json()) as SessionList;
        out.push(...page.items);
        cursor = page.nextCursor;
        if (!cursor) break;
      }
      return out;
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
    async deleteSession(id) {
      await request(`/sessions/${encodeURIComponent(id)}`, { method: "DELETE" });
    },
    async createAudioTicket(id) {
      return (await request(`/sessions/${encodeURIComponent(id)}/audio-ticket`, { method: "POST" })).json() as Promise<AudioTicket>;
    },
    async getTranscriptPage(id, afterSeq, limit) {
      const q = new URLSearchParams({ afterSeq: afterSeq ?? "0", limit: String(limit) });
      return (await request(`/sessions/${encodeURIComponent(id)}/transcript?${q}`)).json();
    },
    async getSummary(id, revision) {
      const q = revision === undefined ? "" : `?revision=${revision}`;
      return (await request(`/sessions/${encodeURIComponent(id)}/summary${q}`)).json();
    },
    async postQuickRecap(id, windowMinutes, idempotencyKey) {
      const res = await request(`/sessions/${encodeURIComponent(id)}/quick-recap`, {
        method: "POST",
        headers: { "Idempotency-Key": idempotencyKey },
        body: JSON.stringify({ windowMinutes }),
      });
      return res.json();
    },
    async getCatchUp(id, catchUpId) {
      return (await request(`/sessions/${encodeURIComponent(id)}/catch-ups/${encodeURIComponent(catchUpId)}`)).json();
    },
    async postNotes(id, noticeVersion, idempotencyKey) {
      const res = await request(`/sessions/${encodeURIComponent(id)}/notes`, {
        method: "POST",
        headers: { "Idempotency-Key": idempotencyKey },
        body: JSON.stringify(notesRequestBody(noticeVersion)),
      });
      return res.json();
    },
    async getNotes(id) {
      return (await request(`/sessions/${encodeURIComponent(id)}/notes`)).json();
    },
  };
}
