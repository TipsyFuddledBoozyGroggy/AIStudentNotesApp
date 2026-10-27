import { http, HttpResponse } from "msw";
import * as db from "./db";

// Wildcard host so handlers match both relative (browser) and absolute (tests) URLs.
const P = "*/api/v1";

function problem(status: number, code: string, title: string, errors?: Record<string, string[]>) {
  return HttpResponse.json(
    { type: `https://errors.collegecompanion.app/${code}`, title, status, code, errors },
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
];
