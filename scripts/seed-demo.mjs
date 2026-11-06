#!/usr/bin/env node
// Seeds LOCAL, SYNTHETIC demo data through the public API (dev login + the same routes the web app uses).
// No SQL, no provider, no real audio or document. Safe to re-run: every item is looked up by its fixed title first.
//
//   node scripts/seed-demo.mjs [--api http://localhost:5080]
//
// What it creates for the demo user (demo@dev.local):
//   - two courses, "Demo: Biology 101" and "Demo: Statistics 101"
//   - one finished lecture in Biology titled "Demo lecture (synthetic replay)": the backend's deterministic ReplaySttProvider
//     turns 14 s of SILENT audio frames into its built-in "Demo replay segment ..." transcript, the lecture is stopped, and the
//     mock AI writes the notes and the Final summary (TL;DR). Nothing here was spoken by anyone.
//   - one synthetic syllabus draft "Demo syllabus (synthetic)" in Biology, extracted by the mock extractor and left UNREVIEWED
//     (no decisions, no calendar sync): the student reviews it.
//
// Refuses to run against anything but localhost, and when the API reports a non-demo speech source.
// Consent / notice / attestation: the API requires an acknowledgement object on these routes. This script sends the same DRAFT
// version ids the web app sends (it does not change or invent wording) on behalf of the synthetic demo user only.

import { randomBytes } from "node:crypto";
import { pathToFileURL } from "node:url";

const DEMO_EMAIL = "demo@dev.local";
const WEB_ORIGIN = "http://localhost:3000"; // the API's allowed origin in the dev overlay
const CONSENT_TEXT_VERSION = "draft-2"; // web/src/components/lecture/copy.ts
const NOTES_NOTICE_VERSION = "notes-notice-draft-1"; // same file
const SYLLABUS_NOTICE_VERSION = "draft-2026-10"; // web/src/lib/syllabus/requests.ts

export const DEMO = {
  courses: [
    { name: "Demo: Biology 101", code: "BIO101", term: "Demo term", instructor: "Demo instructor (synthetic)" },
    { name: "Demo: Statistics 101", code: "STAT101", term: "Demo term", instructor: "Demo instructor (synthetic)" },
  ],
  lectureTitle: "Demo lecture (synthetic replay)",
  syllabusTitle: "Demo syllabus (synthetic)",
  // Synthetic text only. Dates are in the future of the project date so nothing reads as already past.
  syllabusText: [
    "Biology 101 syllabus (synthetic demo text, not a real course)",
    "Quiz 1 on February 2, 2027 in class",
    "Problem set 1 due Friday, February 5, 2027, 11:59 PM",
    "Midterm exam, Wednesday, March 3, 2027, 10:00 AM",
    "Lab report 2 due March 12, 2027 (tentative)",
    "Final exam: date TBA",
    "Late work policy: 10 percent per day",
    "Office hours: Tuesdays 2:00-3:00 PM",
  ].join("\n"),
};

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const log = (m) => console.log(`[seed] ${m}`);

export class SeedError extends Error {}

/** Only loopback hosts. Anything else is refused before a single request is made. */
export function assertLocalhost(apiUrl) {
  let u;
  try {
    u = new URL(apiUrl);
  } catch {
    throw new SeedError(`Not a URL: ${apiUrl}`);
  }
  const ok = ["localhost", "127.0.0.1", "[::1]"].includes(u.hostname);
  if (!ok || !/^https?:$/.test(u.protocol)) {
    throw new SeedError(`Refusing to seed ${u.origin}: the demo seeder only runs against localhost (dev-only data, dev login).`);
  }
  return u;
}

class Client {
  constructor(base) {
    this.base = base.replace(/\/$/, "");
    this.cookie = "";
  }
  async req(method, path, { body, headers = {}, ok = [200, 201, 202, 204] } = {}) {
    const h = { Origin: WEB_ORIGIN, ...headers };
    if (this.cookie) h.Cookie = this.cookie;
    if (body !== undefined) h["Content-Type"] = "application/json";
    let res;
    for (let attempt = 0; attempt < 8; attempt++) {
      res = await fetch(this.base + path, { method, headers: h, body: body === undefined ? undefined : JSON.stringify(body) });
      if (res.status !== 429) break;
      const wait = Math.min(15, Number(res.headers.get("retry-after")) || 5);
      log(`rate limited on ${method} ${path}; waiting ${wait}s`);
      await sleep(wait * 1000);
    }
    const set = res.headers.getSetCookie?.() ?? [];
    for (const c of set) {
      const pair = c.split(";")[0];
      if (pair.startsWith("cc.session=")) this.cookie = pair;
    }
    const text = await res.text();
    let json;
    try {
      json = text ? JSON.parse(text) : undefined;
    } catch {
      json = undefined;
    }
    if (!ok.includes(res.status)) {
      const code = json?.code ?? json?.title ?? "";
      throw new SeedError(`${method} ${path} -> HTTP ${res.status} ${code}`.trim());
    }
    return { status: res.status, json, headers: res.headers };
  }
}

async function login(c) {
  const r = await c.req("POST", "/api/v1/dev/login", { body: { email: DEMO_EMAIL }, ok: [204, 404] }).catch((e) => {
    throw new SeedError(`Could not reach the API at ${c.base} (${e.message}). Start it first: node scripts/start-local.mjs`);
  });
  if (r.status === 404) throw new SeedError("The API has no dev login (not a Development environment). Refusing to seed.");
  if (!c.cookie) throw new SeedError("Dev login did not return a session cookie.");
}

async function ensureCourse(c, spec) {
  const list = await c.req("GET", "/api/v1/courses?limit=100&includeArchived=true");
  const hit = list.json.items.find((x) => x.name === spec.name);
  if (hit) {
    log(`course exists: ${spec.name}`);
    return { id: hit.id, created: false };
  }
  const r = await c.req("POST", "/api/v1/courses", { body: spec });
  log(`course created: ${spec.name}`);
  return { id: r.json.id, created: true };
}

async function listSessions(c, courseId) {
  const out = [];
  let cursor = "";
  do {
    const r = await c.req("GET", `/api/v1/courses/${courseId}/sessions?limit=100${cursor ? `&cursor=${cursor}` : ""}`);
    out.push(...r.json.items);
    cursor = r.json.nextCursor ?? "";
  } while (cursor);
  return out;
}

async function transcriptCount(c, sessionId) {
  const r = await c.req("GET", `/api/v1/sessions/${sessionId}/transcript?limit=500`);
  const events = Array.isArray(r.json?.events) ? r.json.events : [];
  return events.filter((e) => typeof e.type === "string" && e.type.startsWith("transcript.")).length;
}

/** 16 kHz mono PCM, 200 ms frames, 12-byte little-endian header (speech.v1). Silence: the replay source is driven by audio position only. */
function silentFrame(seq) {
  const samples = 3200;
  const buf = Buffer.alloc(12 + samples * 2);
  buf.writeUInt32LE(seq, 0);
  buf.writeBigUInt64LE(BigInt(seq * 200), 4);
  return buf;
}

async function streamReplay(c, sessionId) {
  const t = await c.req("POST", `/api/v1/sessions/${sessionId}/audio-ticket`);
  const wsUrl = c.base.replace(/^http/, "ws") + `/ingest/sessions/${sessionId}/audio`;
  const ws = new WebSocket(wsUrl, { headers: { Origin: WEB_ORIGIN, Cookie: c.cookie } });
  ws.binaryType = "arraybuffer";
  let acked = false;
  let closeCode = null;
  ws.onmessage = (e) => {
    if (typeof e.data === "string" && e.data.includes('"ack"')) acked = true;
  };
  ws.onclose = (e) => {
    closeCode = e.code;
  };
  await new Promise((res, rej) => {
    ws.onopen = res;
    ws.onerror = () => rej(new SeedError("Audio socket could not open."));
  });
  ws.send(JSON.stringify({ type: "hello", version: "speech.v1", ticket: t.json.ticket, sampleRate: 16000, encoding: "pcm_s16le", channels: 1 }));
  // The server acks each accepted frame (not the hello), so send the first frame and wait for its ack before pacing the rest
  // (L-HELLO: the ticket is redeemed after the socket is open; a state change before the first ack races it).
  ws.send(silentFrame(0));
  for (let i = 0; i < 100 && !acked && closeCode === null; i++) await sleep(100);
  if (!acked) throw new SeedError(`Audio socket was not accepted (close ${closeCode ?? "none"}).`);
  // The replay script ends at 11.5 s and its correction lands at about 12 s of audio; send 14 s. Server allows ~1.1x real time
  // with a 5 s burst, so stay at 1.05x after a 4 s burst, as the web client does.
  const frames = 70;
  const startedAt = Date.now();
  for (let seq = 1; seq < frames; seq++) {
    const allowedMs = (Date.now() - startedAt) * 1.05 + 4000;
    if ((seq + 1) * 200 > allowedMs) await sleep((seq + 1) * 200 - allowedMs);
    if (closeCode !== null) throw new SeedError(`Audio socket closed early (${closeCode}).`);
    ws.send(silentFrame(seq));
  }
  await sleep(1500); // let the last events persist
  ws.send(JSON.stringify({ type: "stop" }));
  await sleep(300);
  try {
    ws.close(1000);
  } catch {
    /* already closed */
  }
}

async function ensureLecture(c, course) {
  let sessions = await listSessions(c, course.id);
  let hit = sessions.find((s) => s.title === DEMO.lectureTitle);
  if (hit) {
    const n = hit.state === "Active" || hit.state === "Paused" ? 0 : await transcriptCount(c, hit.id);
    if (n === 0) {
      log("lecture exists but has no transcript (earlier run interrupted); removing it and redoing");
      await c.req("DELETE", `/api/v1/sessions/${hit.id}`);
      hit = undefined;
    }
  }
  if (!hit) {
    const key = "seed-" + randomBytes(8).toString("hex");
    const created = await c.req("POST", `/api/v1/courses/${course.id}/sessions`, {
      headers: { "Idempotency-Key": key },
      body: { title: DEMO.lectureTitle, consent: { acknowledged: true, textVersion: CONSENT_TEXT_VERSION } },
    });
    if (created.json.demoSource !== true) {
      await c.req("POST", `/api/v1/sessions/${created.json.id}/stop`).catch(() => undefined);
      await c.req("DELETE", `/api/v1/sessions/${created.json.id}`).catch(() => undefined);
      throw new SeedError("The API reports a NON-demo speech source. Refusing to stream anything into it.");
    }
    log("streaming the synthetic replay (about 12 s)...");
    try {
      await streamReplay(c, created.json.id);
    } finally {
      await c.req("POST", `/api/v1/sessions/${created.json.id}/stop`, { ok: [200, 409] });
    }
    hit = created.json;
    log(`lecture recorded from the replay source: ${await transcriptCount(c, hit.id)} transcript entries`);
  } else {
    log(`lecture exists: ${DEMO.lectureTitle}`);
  }
  // Notes + Final summary: one deliberate press, a fixed key per lecture so a re-run replays it instead of making another.
  const key = "seed-notes-" + hit.id.replace(/[^A-Za-z0-9]/g, "").slice(0, 40);
  let notes = await c.req("POST", `/api/v1/sessions/${hit.id}/notes`, {
    headers: { "Idempotency-Key": key },
    body: { noticeVersion: NOTES_NOTICE_VERSION },
  });
  for (let i = 0; i < 60 && notes.json?.status !== "Ready" && notes.json?.status !== "Failed"; i++) {
    await sleep(1000);
    notes = await c.req("GET", `/api/v1/sessions/${hit.id}/notes`);
  }
  if (notes.json?.status !== "Ready") throw new SeedError(`Notes did not become Ready (status ${notes.json?.status}).`);
  log("notes and Final summary are Ready (mock AI)");
  return hit.id;
}

async function ensureSyllabus(c, course) {
  const list = await c.req("GET", `/api/v1/courses/${course.id}/syllabi?limit=50`);
  let hit = list.json.items.find((s) => s.title === DEMO.syllabusTitle);
  if (!hit) {
    const key = "seed-syl-" + randomBytes(8).toString("hex");
    const r = await c.req("POST", `/api/v1/courses/${course.id}/syllabi`, {
      headers: { "Idempotency-Key": key },
      ok: [200, 201, 202, 409],
      body: {
        title: DEMO.syllabusTitle,
        text: DEMO.syllabusText,
        defaultTimezone: "America/New_York",
        noticeVersion: SYLLABUS_NOTICE_VERSION,
        attestation: { accepted: true, acceptedAt: new Date().toISOString() },
      },
    });
    if (r.status === 409) {
      log("a syllabus with this exact text already exists in the course; keeping it");
      const again = await c.req("GET", `/api/v1/courses/${course.id}/syllabi?limit=50`);
      hit = again.json.items.find((s) => s.title === DEMO.syllabusTitle) ?? again.json.items[0];
    } else {
      hit = r.json;
      log("syllabus draft created");
    }
  } else {
    log(`syllabus exists: ${DEMO.syllabusTitle}`);
  }
  for (let i = 0; i < 60; i++) {
    const g = await c.req("GET", `/api/v1/syllabi/${hit.id}`);
    if (g.json.status === "ready") return { id: hit.id, status: "ready" };
    if (g.json.status === "failed") throw new SeedError(`Syllabus extraction failed (${g.json.statusReason ?? "no reason"}).`);
    await sleep(1000);
  }
  throw new SeedError("Syllabus extraction did not finish in 60 s.");
}

export async function seed(apiUrl = "http://localhost:5080") {
  const u = assertLocalhost(apiUrl);
  const c = new Client(u.origin);
  await login(c);
  const bio = await ensureCourse(c, DEMO.courses[0]);
  await ensureCourse(c, DEMO.courses[1]);
  const lectureId = await ensureLecture(c, bio);
  const syl = await ensureSyllabus(c, bio);
  const summary = { email: DEMO_EMAIL, biologyCourseId: bio.id, lectureId, syllabusId: syl.id, syllabusStatus: syl.status };
  log(`done: ${JSON.stringify(summary)}`);
  return summary;
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? "").href) {
  const i = process.argv.indexOf("--api");
  const api = i > 0 ? process.argv[i + 1] : (process.env.TETHER_API_URL ?? "http://localhost:5080");
  seed(api).catch((e) => {
    console.error(`[seed] FAILED: ${e instanceof SeedError ? e.message : (e?.stack ?? e)}`);
    process.exit(1);
  });
}
