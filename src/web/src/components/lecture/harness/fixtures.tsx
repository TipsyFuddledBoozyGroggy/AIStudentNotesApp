import type { ReactNode } from "react";
import * as b from "@/lib/lecture/builders";
import type { HubEvent } from "@/lib/lecture";
import { AnnouncerProvider } from "../Announcer";
import { ConsentStart } from "../ConsentStart";
import { createHarnessRuntime, type HarnessControls, type HarnessRuntimeOptions } from "../harnessRuntime";
import { MicBlocked } from "../MicBlocked";
import { FakeMicSource, FakeMicTrack } from "../micSource";
import type { LectureBackend } from "../ports";
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
      script: [
        b.partialEvent(SID, 4, 1, "so now the chain"),
        b.finalEvent(SID, "7", 4, "So now the chain rule: outer derivative times inner derivative.", 12000),
        b.summaryEvent(SID, 2, "Derivatives: power rule and chain rule.", { bullets: ["Chain rule: outer times inner."], coveredToMs: 16000 }),
        b.correctionEvent(SID, "8", 4, 2, "So now the chain rule: outer derivative times the inner derivative."),
      ],
    },
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
    title: "Stopped",
    ux: "3, 9",
    live: { events: lines("Stopped", "user"), track: "ended" },
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

