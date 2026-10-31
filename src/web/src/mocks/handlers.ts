import { http, HttpResponse } from "msw";
import * as db from "./db";
import { finalSummaryFor, notesHandlers } from "./notesHandlers";
import { syllabusHandlers } from "./syllabusHandlers";

// Wildcard host so handlers match both relative (browser) and absolute (tests) URLs.
const P = "*/api/v1";

function problem(status: number, code: string, title: string, errors?: Record<string, string[]>) {
  return HttpResponse.json(
    { type: `https://errors.tether.invalid/${code}`, title, status, code, errors },
    { status, headers: { "Content-Type": "application/problem+json" } },
  );
}
const unauth = () => problem(401, "unauthenticated", "Please sign in.");

export const handlers = [
  http.post(`${P}/dev/login`, async ({ request }) => {
    const body = (await request.json().catch(() => ({}))) as { email?: string };
    const email = body.email?.trim() ?? "";
    if (!/^\S+@\S+\.\S+$/.test(email)) {
      return problem(400, "validation_failed", "Enter a valid email.", { email: ["Enter a valid email."] });
    }
    db.signIn(email.toLowerCase());
    return new HttpResponse(null, { status: 204 });
  }),

  http.post(`${P}/auth/logout`, () => {
    db.signOut();
    return new HttpResponse(null, { status: 204 });
  }),

  http.get(`${P}/me`, () => {
    const user = db.currentUser();
    return user ? HttpResponse.json(user) : unauth();
  }),

  http.get(`${P}/courses`, () => {
    if (!db.currentUser()) return unauth();
    return HttpResponse.json({ items: db.listCourses(), nextCursor: null });
  }),

  http.post(`${P}/courses`, async ({ request }) => {
    if (!db.currentUser()) return unauth();
    const body = (await request.json().catch(() => ({}))) as Record<string, unknown>;
    const name = typeof body.name === "string" ? body.name.trim() : "";
    if (name.length < 1 || name.length > 120) {
      return problem(400, "validation_failed", "Course name must be 1 to 120 characters.", {
        name: ["Course name must be 1 to 120 characters."],
      });
    }
    const opt = (k: string) => {
      const v = body[k];
      return typeof v === "string" && v.trim() ? v.trim() : undefined;
    };
    const course = db.addCourse({ name, code: opt("code"), term: opt("term"), instructor: opt("instructor"), color: opt("color") });
    return HttpResponse.json(course, { status: 201, headers: { Location: `/api/v1/courses/${course.id}` } });
  }),

  http.get(`${P}/courses/:id`, ({ params }) => {
    if (!db.currentUser()) return unauth();
    const course = db.findCourse(String(params.id));
    // ADR-002: missing and not-owned are both 404.
    return course ? HttpResponse.json(course) : problem(404, "not_found", "Course not found.");
  }),

  // --- AI routes (wire shapes from AiEndpoints.cs / CatchUpWire.cs). Session ids pick the scenario:
  // ses_missing -> 404, ses_ended -> 409 on quick-recap, ses_limited -> 429, ses_nosummary -> 404 on summary.
  http.get(`${P}/sessions/:id/summary`, ({ params }) => {
    if (!db.currentUser()) return unauth();
    if (params.id === "ses_missing") return problem(404, "not_found", "Session not found.");
    if (params.id === "ses_nosummary") return problem(404, "not_found", "No summary yet.");
    // After a Ready "Make notes" press the latest revision is the Final one it made (contract 17).
    const final = finalSummaryFor(String(params.id));
    if (final) return HttpResponse.json(final);
    return HttpResponse.json({
      revision: 2,
      basedOnSeq: "9",
      tldr: "The lecture is about derivatives.",
      bullets: ["A derivative is a rate of change."],
      coveredFromMs: 0,
      coveredToMs: 60000,
      status: "Draft",
      citations: [{ segmentId: "seg_1", startMs: 1000 }],
      source: "mock",
    });
  }),

  http.post(`${P}/sessions/:id/quick-recap`, async ({ params, request }) => {
    if (!db.currentUser()) return unauth();
    if (params.id === "ses_missing") return problem(404, "not_found", "Session not found.");
    if (params.id === "ses_ended") return problem(409, "invalid_state", "The session is not running.");
    if (params.id === "ses_limited") return problem(429, "rate_limited", "Too many requests.");
    const body = (await request.json().catch(() => ({}))) as { windowMinutes?: number };
    return HttpResponse.json({
      catchUpId: "cu_0123456789abcdef0123456789abcdef",
      windowMinutes: body.windowMinutes ?? 5,
      text: "[00:01] First line.\n[00:05] Second line.",
      citations: [{ segmentId: "seg_1", startMs: 1000 }],
      kind: "Extractive",
      aiState: "Pending",
      coveredFromMs: 0,
      coveredToMs: 60000,
    });
  }),

  http.get(`${P}/sessions/:id/catch-ups/:catchUpId`, ({ params }) => {
    if (!db.currentUser()) return unauth();
    if (params.id === "ses_missing" || params.catchUpId === "cu_missing") return problem(404, "not_found", "Recap not found.");
    return HttpResponse.json({
      catchUpId: params.catchUpId,
      windowMinutes: 5,
      text: "Most recent: derivatives.",
      citations: [{ segmentId: "seg_1", startMs: 1000 }],
      kind: "Ai",
      aiState: "Ready",
      coveredFromMs: 0,
      coveredToMs: 60000,
      source: "mock",
      nowDiscussing: { text: "Derivatives.", basis: "lecture", citations: [{ segmentId: "seg_1", startMs: 1000 }] },
      shortCatchUp: [{ text: "A derivative is a rate of change.", basis: "lecture", citations: [{ segmentId: "seg_1", startMs: 1000 }] }],
      detail: [],
      keyTerms: [],
      clarifyingQuestions: [],
      confidence: "low",
      gaps: [],
    });
  }),

  // Notes after Stop (PO-18d): see notesHandlers.ts for scenarios.
  ...notesHandlers,

  // syllabus.v1 (Phase 2, mock calendar only): see syllabusHandlers.ts for scenarios.
  ...syllabusHandlers,
];
