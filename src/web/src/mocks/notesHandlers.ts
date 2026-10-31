import { http, HttpResponse } from "msw";
import * as db from "./db";

// MSW stand-in for POST/GET /sessions/{id}/notes (PO-18d, docs/API_CONTRACTS.md section 17), faithful to the contract:
// JSON only (415), body exactly {"noticeVersion": "<known id>"} (400), Idempotency-Key required (400), 202 + Location for a
// new attempt, 200 for a press already running or finished, key replay 200 + Idempotency-Replayed: true, at most 5 attempts
// (429), a new attempt only after Unavailable or BudgetLimited, 409 invalid_state on GET before any press. Session ids pick
// the scenario (synthetic data only; NOT the real backend):
//   ses_missing            404 on both routes
//   ses_notes_409          409 invalid_state on POST (the session is not Stopped) and on GET
//   ses_notes_429          429 rate_limited on POST (per-minute cap, or another lecture already has a pending press)
//   ses_notes_pending      a press stays Pending
//   ses_notes_notneeded    a press is NotNeeded at once (no transcript text, nothing sent)
//   ses_notes_budget       the first press is BudgetLimited at once; a new press is Pending, then Ready
//   ses_notes_unavailable  the first press is Pending, then Unavailable; a new press is Pending, then Ready
//   ses_notes_ready_now    a press is Ready at once (an immediate decision)
//   anything else          a press is Pending; the next GET answers Ready (source "mock", a Final summary revision)

const P = "*/api/v1";
export const KNOWN_NOTICE_VERSIONS = new Set(["notes-notice-draft-1"]);
const KEY_RE = /^[A-Za-z0-9_-]{1,64}$/;
const NOTICE_RE = /^[A-Za-z0-9._-]{1,64}$/;
const MAX_ATTEMPTS = 5;
/** The Final summary revision a Ready press makes in this mock (the generic summary route serves revision 2 as Draft). */
export const MOCK_FINAL_REVISION = 3;

type Status = "Pending" | "Ready" | "Unavailable" | "BudgetLimited" | "NotNeeded";
interface Attempt {
  key: string;
  status: Status;
  revision: number;
  requestedAt: string;
}
const store = new Map<string, Attempt[]>();

export function resetNotesMock(): void {
  store.clear();
}

function problem(status: number, code: string, title: string) {
  return HttpResponse.json({ type: `https://errors.tether.invalid/${code}`, title, status, code }, { status, headers: { "Content-Type": "application/problem+json" } });
}

const hex32 = (n: number) => n.toString(16).padStart(32, "0");

function body(sessionId: string, a: Attempt) {
  const base = {
    notesId: `nt_${hex32(a.revision + sessionId.length * 1000)}`,
    revision: a.revision,
    status: a.status,
    source: null,
    requestedAt: a.requestedAt,
    generatedAt: null,
    coveredFromMs: null,
    coveredToMs: null,
    sections: [] as unknown[],
    finalSummaryRevision: null as number | null,
    finalSummaryStatus: null as string | null,
    truncated: false,
  };
  if (a.status !== "Ready") return base;
  return {
    ...base,
    source: "mock",
    generatedAt: a.requestedAt,
    coveredFromMs: 1000,
    coveredToMs: 60000,
    sections: [
      {
        heading: "Derivatives",
        bullets: [
          { text: "A derivative is a rate of change.", citations: [{ segmentId: "seg_1", startMs: 1000 }] },
          {
            text: "The power rule brings the exponent down.",
            citations: [
              { segmentId: "seg_2", startMs: 2000 },
              { segmentId: "seg_1", startMs: 1000 },
            ],
          },
        ],
      },
    ],
    finalSummaryRevision: MOCK_FINAL_REVISION,
    finalSummaryStatus: "Ready",
  };
}

/** The Final summary revision for a session whose newest press is Ready (read by the summary route). */
export function finalSummaryFor(sessionId: string) {
  const newest = store.get(sessionId)?.at(-1);
  if (!newest || newest.status !== "Ready") return null;
  return {
    revision: MOCK_FINAL_REVISION,
    basedOnSeq: "9",
    tldr: "The lecture covered derivatives and the power rule.",
    bullets: ["A derivative is a rate of change.", "The power rule brings the exponent down."],
    coveredFromMs: 0,
    coveredToMs: 60000,
    status: "Final",
    citations: [{ segmentId: "seg_1", startMs: 1000 }],
    source: "mock",
  };
}

/** First status of a new attempt for the scenario (n = 1 for the first press of the session). */
function firstStatus(sessionId: string, n: number): Status {
  if (sessionId === "ses_notes_notneeded") return "NotNeeded";
  if (sessionId === "ses_notes_ready_now") return "Ready";
  if (sessionId === "ses_notes_budget" && n === 1) return "BudgetLimited";
  return "Pending";
}

/** What a Pending attempt becomes when it is read. */
function settle(sessionId: string, a: Attempt): void {
  if (a.status !== "Pending" || sessionId === "ses_notes_pending") return;
  a.status = sessionId === "ses_notes_unavailable" && a.revision === 1 ? "Unavailable" : "Ready";
}

export const notesHandlers = [
  http.post(`${P}/sessions/:id/notes`, async ({ params, request }) => {
    if (!db.currentUser()) return problem(401, "unauthenticated", "Please sign in.");
    const id = String(params.id);
    if (!(request.headers.get("Content-Type") ?? "").toLowerCase().startsWith("application/json")) return problem(415, "unsupported_media_type", "Send JSON.");
    let parsed: unknown;
    try {
      parsed = await request.json();
    } catch {
      return problem(400, "validation_failed", "The request body is not valid.");
    }
    const ok =
      typeof parsed === "object" &&
      parsed !== null &&
      !Array.isArray(parsed) &&
      Object.keys(parsed).length === 1 &&
      typeof (parsed as { noticeVersion?: unknown }).noticeVersion === "string" &&
      NOTICE_RE.test((parsed as { noticeVersion: string }).noticeVersion) &&
      KNOWN_NOTICE_VERSIONS.has((parsed as { noticeVersion: string }).noticeVersion);
    if (!ok) return problem(400, "validation_failed", "The request body is not valid.");
    const key = request.headers.get("Idempotency-Key");
    if (!key || !KEY_RE.test(key)) return problem(400, "validation_failed", "Idempotency-Key is required.");
    if (id === "ses_missing") return problem(404, "not_found", "Session not found.");
    if (id === "ses_notes_409") return problem(409, "invalid_state", "The session is not stopped.");
    if (id === "ses_notes_429") return problem(429, "rate_limited", "Too many requests.");

    const attempts = store.get(id) ?? [];
    const replay = attempts.find((a) => a.key === key);
    if (replay) return HttpResponse.json(body(id, replay), { status: 200, headers: { "Idempotency-Replayed": "true" } });
    const newest = attempts.at(-1);
    if (newest && newest.status !== "Unavailable" && newest.status !== "BudgetLimited") return HttpResponse.json(body(id, newest), { status: 200 });
    if (attempts.length >= MAX_ATTEMPTS) return problem(429, "rate_limited", "Too many requests.");
    const a: Attempt = { key, revision: attempts.length + 1, status: firstStatus(id, attempts.length + 1), requestedAt: "2026-10-31T14:02:11Z" };
    store.set(id, [...attempts, a]);
    return HttpResponse.json(body(id, a), { status: 202, headers: { Location: `/api/v1/sessions/${encodeURIComponent(id)}/notes` } });
  }),

  http.get(`${P}/sessions/:id/notes`, ({ params }) => {
    if (!db.currentUser()) return problem(401, "unauthenticated", "Please sign in.");
    const id = String(params.id);
    if (id === "ses_missing") return problem(404, "not_found", "Session not found.");
    const newest = store.get(id)?.at(-1);
    if (!newest) return problem(409, "invalid_state", "Notes have not been requested.");
    settle(id, newest);
    return HttpResponse.json(body(id, newest));
  }),
];
