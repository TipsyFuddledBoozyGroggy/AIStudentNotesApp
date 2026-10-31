import type { ReactNode } from "react";
import * as b from "@/lib/lecture/builders";
import type { HubEvent, LectureNotes } from "@/lib/lecture";
import { AnnouncerProvider } from "../Announcer";
import { ConsentStart } from "../ConsentStart";
import { createHarnessRuntime, type HarnessControls, type HarnessRuntimeOptions } from "../harnessRuntime";
import { MicBlocked } from "../MicBlocked";
import { FakeMicSource, FakeMicTrack } from "../micSource";
import type { LectureAi, LectureBackend } from "../ports";
import { StartFlow } from "../StartFlow";

const SID = "ses_harness";
const noop = () => undefined;

const lines = (state: "Active" | "Paused" | "Stopped" | "Failed" = "Active", reason?: string): HubEvent[] => [
  b.sessionStateEvent(SID, "1", "Active"),
  b.finalEvent(SID, "2", 1, "So the derivative of x squared is two x."),
  b.finalEvent(SID, "3", 2, "The power rule says bring the exponent down."),
  b.gapEvent(SID, "4", 5000, 8000, "network"),
  b.finalEvent(SID, "5", 3, "Now let us look at the chain rule.", 9000),
  b.summaryEvent(SID, 1, "Derivatives and the power rule.", {
    bullets: ["Power rule: bring the exponent down.", "Chain rule comes next."],
    coveredToMs: 9900,
    citations: [{ segmentId: "seg_2", startMs: 2000 }],
  }),
  ...(state === "Active" ? [] : [b.sessionStateEvent(SID, "6", state, reason)]),
];

const REDTEAM = '<img src=x onerror="alert(1)"> **bold** [click](https://evil.example/x) <script>alert(1)</script> javascript:alert(1)';

type TrackMode = "live" | "ended" | "stubborn";

export interface Spec {
  title: string;
  /** UX spec section(s) this state exercises. */
  ux: string;
  live?: {
    events: HubEvent[];
    track: TrackMode;
    rt?: Partial<HarnessRuntimeOptions>;
    script?: HubEvent[];
    /** Run once after mount (e.g. simulate unplugging the microphone). */
    after?: (c: { controls: HarnessControls; track: FakeMicTrack | null }) => void;
    shortcuts?: boolean;
  };
  custom?: () => ReactNode;
}

const fakeBackend = (): LectureBackend => ({
  startSession: async () => {
    const { runtime } = createHarnessRuntime({ sessionId: SID, events: [b.sessionStateEvent(SID, "1", "Active")] });
    return { ok: true, runtime };
  },
});

/** Deterministic stand-in for the AI routes (no network). Text is labelled as a demo by source: "mock". */
const fakeAi: LectureAi = {
  getSummary: async () => null,
  startQuickRecap: async (windowMinutes) => ({
    catchUpId: "cu_harness",
    windowMinutes,
    kind: "Ai",
    aiState: "Ready",
    source: "mock",
    text: "Most recent: the chain rule.",
    citations: [{ segmentId: "seg_3", startMs: 9000 }],
    coveredFromMs: 0,
    coveredToMs: 9900,
    nowDiscussing: { text: "The chain rule, right after the power rule.", basis: "lecture", citations: [{ segmentId: "seg_3", startMs: 9000 }] },
    shortCatchUp: [
      { text: "The derivative of x squared is two x.", basis: "lecture", citations: [{ segmentId: "seg_1", startMs: 1000 }] },
      { text: "Power rule: bring the exponent down.", basis: "lecture", citations: [{ segmentId: "seg_2", startMs: 2000 }] },
    ],
    gaps: ["Audio was not available from 00:05 to 00:08."],
  }),
  getCatchUp: async () => null,
};

// --- Notes after Stop (PO-18d, slice 8a): deterministic stand-ins for POST/GET /notes, demo-labelled (source "mock"). ---

const NOTES_BASE: LectureNotes = {
  notesId: "nt_0123456789abcdef0123456789abcdef",
  revision: 1,
  status: "Pending",
  source: null,
  requestedAt: "2026-10-31T14:02:11Z",
  generatedAt: null,
  coveredFromMs: null,
  coveredToMs: null,
  sections: [],
  finalSummaryRevision: null,
  finalSummaryStatus: null,
  truncated: false,
};

export const NOTES_READY: LectureNotes = {
  ...NOTES_BASE,
  status: "Ready",
  source: "mock",
  generatedAt: "2026-10-31T14:02:13Z",
  coveredFromMs: 1000,
  coveredToMs: 9900,
  sections: [
    {
      heading: "Derivatives and the power rule",
      bullets: [
        { text: "The derivative of x squared is two x.", citations: [{ segmentId: "seg_1", startMs: 1000 }] },
        {
          text: "The power rule says bring the exponent down.",
          citations: [
            { segmentId: "seg_2", startMs: 2000 },
            { segmentId: "seg_1", startMs: 1000 },
          ],
        },
      ],
    },
    { heading: "The chain rule", bullets: [{ text: "The chain rule comes next.", citations: [{ segmentId: "seg_3", startMs: 9000 }] }] },
  ],
  finalSummaryRevision: 2,
  finalSummaryStatus: "Ready",
};

const FINAL_SUMMARY = {
  revision: 2,
  basedOnSeq: "6",
  tldr: "Derivatives, the power rule and the chain rule.",
  bullets: ["Power rule: bring the exponent down.", "Chain rule: outer derivative times inner derivative."],
  coveredFromMs: 0,
  coveredToMs: 9900,
  status: "Final" as const,
  citations: [{ segmentId: "seg_2", startMs: 2000 }],
  source: "mock" as const,
};

/**
 * Notes stand-in. `initial` is what GET answers before any press (null = 409, nothing pressed). A press answers `onPress`
 * (default Pending, a new attempt), after which GET answers `afterPress` (default Ready with the Final summary).
 */
function notesAi(o: { initial: LectureNotes | null; onPress?: LectureNotes; afterPress?: LectureNotes }): LectureAi {
  let current = o.initial;
  let pressed = false;
  let attempt = o.initial?.revision ?? 0;
  return {
    ...fakeAi,
    getSummary: async () => (pressed || o.initial?.status === "Ready" ? FINAL_SUMMARY : null),
    getNotes: async () => current,
    requestNotes: async () => {
      await new Promise((r) => setTimeout(r, 300));
      if (current && current.status !== "Unavailable" && current.status !== "BudgetLimited") return current; // a replay
      attempt += 1;
      pressed = true;
      const answer = { ...(o.onPress ?? { ...NOTES_BASE, status: "Pending" as const }), revision: attempt };
      current = { ...(o.afterPress ?? NOTES_READY), revision: attempt };
      return answer;
    },
  };
}

const stoppedNotes = (ai: LectureAi, extra: HubEvent[] = []): Spec["live"] => ({
  events: [...lines("Stopped", "user"), ...extra],
  track: "ended",
  rt: { ai },
});

export const FIXTURES: Record<string, Spec> = {
  "consent": {
    title: "Consent and start (unchecked)",
    ux: "1",
    custom: () => (
      <AnnouncerProvider>
        <ConsentStart courseName="Biology 101" onStart={noop} onCancel={noop} />
      </AnnouncerProvider>
    ),
  },
  "consent-attested": {
    title: "Consent with course-level attestation",
    ux: "1",
    custom: () => (
      <AnnouncerProvider>
        <ConsentStart courseName="Biology 101" onStart={noop} onCancel={noop} attestation={{ dateLabel: "Oct 20" }} />
      </AnnouncerProvider>
    ),
  },
  "consent-error": {
    title: "Consent, 403 consent_required",
    ux: "1",
    custom: () => (
      <AnnouncerProvider>
        <ConsentStart courseName="Biology 101" onStart={noop} onCancel={noop} error="consent_required" />
      </AnnouncerProvider>
    ),
  },
  "start-flow": {
    title: "Full start flow (fake backend, fake mic)",
    ux: "1, 2",
    custom: () => <StartFlow courseId="c1" courseName="Biology 101" backend={fakeBackend()} mic={new FakeMicSource("ok")} onBackToCourse={noop} />,
  },
  "start-flow-denied": {
    title: "Full start flow, microphone denied",
    ux: "1, 5",
    custom: () => <StartFlow courseId="c1" courseName="Biology 101" backend={fakeBackend()} mic={new FakeMicSource("denied")} onBackToCourse={noop} />,
  },
  "mic-denied": {
    title: "Mic denied (blocked screen)",
    ux: "5",
    custom: () => (
      <AnnouncerProvider>
        <MicBlocked reason="denied" nothingRecordedConfirmed onTryAgain={noop} onBack={noop} />
      </AnnouncerProvider>
    ),
  },
  "live": {
    title: "Live view (use the controls to step events)",
    ux: "2",
    live: {
      events: lines(),
      track: "live",
      // The real runtime always has the AI routes (realRuntime.ts); without them this fixture hid Quick recap entirely.
      rt: { ai: fakeAi },
      script: [
        b.partialEvent(SID, 4, 1, "so now the chain"),
        b.finalEvent(SID, "7", 4, "So now the chain rule: outer derivative times inner derivative.", 12000),
        b.summaryEvent(SID, 2, "Derivatives: power rule and chain rule.", { bullets: ["Chain rule: outer times inner."], coveredToMs: 16000 }),
        b.correctionEvent(SID, "8", 4, 2, "So now the chain rule: outer derivative times the inner derivative."),
      ],
    },
  },
  "live-quick-recap": {
    title: "Live view with Quick recap (deterministic fake AI, demo-labelled)",
    ux: "4",
    live: { events: lines(), track: "live", rt: { ai: fakeAi } },
  },
  "live-redteam": {
    title: "Live view with red-team text (must render inert)",
    ux: "2 AC4",
    live: {
      events: [
        b.sessionStateEvent(SID, "1", "Active"),
        b.finalEvent(SID, "2", 1, REDTEAM),
        b.summaryEvent(SID, 1, REDTEAM, { bullets: [REDTEAM], coveredToMs: 1000 }),
      ],
      track: "live",
    },
  },
  "paused": {
    title: "Paused (persistent panel)",
    ux: "3",
    live: { events: lines("Paused", "user"), track: "ended" },
  },
  "paused-mic-lost": {
    title: "Microphone stopped mid-session (unplugged after mount)",
    ux: "5",
    live: { events: lines(), track: "live", after: ({ track }) => track?.simulateDeviceLoss() },
  },
  "paused-auto": {
    title: "Paused automatically (no audio)",
    ux: "9",
    live: { events: lines("Paused", "auto_pause_no_audio"), track: "ended" },
  },
  "stopped": {
    // Slice 8a: the real runtime always has the AI routes, so a Stopped lecture offers "Make notes" (and keeps the Quick
    // recap dock, as built). Pressing it here runs the deterministic stand-in: Pending, then Ready on the next poll.
    title: "Stopped (Make notes offered)",
    ux: "3, 9; notes 1",
    live: stoppedNotes(notesAi({ initial: null })),
  },
  "stopped-notes-pending": {
    title: "Stopped, notes being written",
    ux: "notes 1.3 pending",
    live: stoppedNotes(notesAi({ initial: { ...NOTES_BASE, status: "Pending" } })),
  },
  "stopped-notes-ready": {
    title: "Stopped, notes ready (demo notes, final summary)",
    ux: "notes 1.4 ready",
    live: stoppedNotes(notesAi({ initial: NOTES_READY }), [b.summaryEvent(SID, 2, FINAL_SUMMARY.tldr, FINAL_SUMMARY)]),
  },
  "stopped-notes-ready-truncated": {
    title: "Stopped, notes ready but cut at the end; the final summary step stopped",
    ux: "notes 1.3 truncated, finalKept",
    live: stoppedNotes(
      notesAi({ initial: { ...NOTES_READY, coveredToMs: 9000, truncated: true, finalSummaryRevision: null, finalSummaryStatus: "Unavailable" } }),
    ),
  },
  "stopped-notes-unavailable": {
    title: "Stopped, notes could not be made (press again offered)",
    ux: "notes 1.3 unavailable",
    live: stoppedNotes(notesAi({ initial: { ...NOTES_BASE, status: "Unavailable" } })),
  },
  "stopped-notes-not-needed": {
    title: "Stopped, no words so no notes",
    ux: "notes 1.3 not needed",
    live: stoppedNotes(notesAi({ initial: { ...NOTES_BASE, status: "NotNeeded" } })),
  },
  "stopped-auto": {
    title: "Stopped automatically (paused too long)",
    ux: "9",
    live: { events: lines("Stopped", "auto_stop_paused_timeout"), track: "ended" },
  },
  "disconnected": {
    title: "Disconnected (not connected, audio not being saved)",
    ux: "6",
    live: { events: lines(), track: "live", rt: { online: false } },
  },
  "mismatch": {
    title: "Mic/server mismatch (server paused, microphone still on)",
    ux: "8",
    live: { events: lines("Paused", "user"), track: "live" },
  },
};

export const FIXTURE_IDS = Object.keys(FIXTURES);

