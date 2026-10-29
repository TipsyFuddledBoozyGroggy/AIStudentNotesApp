"use client";

import { useRef, useState } from "react";
import { CONSENT_TEXT_VERSION } from "./copy";
import { AnnouncerProvider } from "./Announcer";
import { ConsentStart } from "./ConsentStart";
import { LectureScreen } from "./LectureScreen";
import { MicBlocked } from "./MicBlocked";
import type { MicFailure, MicSource, MicTrack } from "./micSource";
import type { LectureBackend, LectureRuntime, StartError } from "./ports";

export interface StartFlowProps {
  courseId: string;
  courseName: string;
  backend: LectureBackend;
  mic: MicSource;
  onBackToCourse: () => void;
  attestation?: { dateLabel: string };
  demoExpected?: boolean;
}

type Phase =
  | { name: "consent"; error: StartError | null }
  | { name: "starting" }
  | { name: "blocked"; reason: MicFailure; confirmed: boolean }
  | { name: "live"; runtime: LectureRuntime; track: MicTrack };

/**
 * Consent -> create session -> getUserMedia (only on press) -> live. The audio ticket, audio socket and hub
 * join are NOT part of F2. If the mic is refused the session is stopped; Try again starts a fresh one.
 */
export function StartFlow(props: StartFlowProps) {
  return (
    <AnnouncerProvider>
      <StartFlowInner {...props} />
    </AnnouncerProvider>
  );
}

function StartFlowInner({ courseId, courseName, backend, mic, onBackToCourse, attestation, demoExpected }: StartFlowProps) {
  const [phase, setPhase] = useState<Phase>({ name: "consent", error: null });
  const busy = useRef(false);

  const start = async () => {
    if (busy.current) return;
    busy.current = true;
    setPhase({ name: "starting" });
    try {
      const res = await backend.startSession({ courseId, consent: { acknowledged: true, textVersion: CONSENT_TEXT_VERSION } });
      if (!res.ok) {
        setPhase({ name: "consent", error: res.error });
        return;
      }
      const m = await mic.request();
      if (!m.ok) {
        let confirmed = true;
        try {
          await res.runtime.actions.stop();
        } catch {
          confirmed = false;
        }
        setPhase({ name: "blocked", reason: m.reason, confirmed });
        return;
      }
      setPhase({ name: "live", runtime: res.runtime, track: m.track });
    } catch {
      setPhase({ name: "consent", error: "network" });
    } finally {
      busy.current = false;
    }
  };

  switch (phase.name) {
    case "consent":
    case "starting":
      return (
        <ConsentStart
          courseName={courseName}
          attestation={attestation}
          demoExpected={demoExpected}
          error={phase.name === "consent" ? phase.error : null}
          busy={phase.name === "starting"}
          onStart={() => void start()}
          onCancel={onBackToCourse}
        />
      );
    case "blocked":
      return (
        <MicBlocked
          reason={phase.reason}
          nothingRecordedConfirmed={phase.confirmed}
          onTryAgain={() => void start()}
          onBack={onBackToCourse}
        />
      );
    case "live":
      return (
        <LectureScreen runtime={phase.runtime} track={phase.track} mic={mic} onBackToCourse={onBackToCourse} courseName={courseName} justStarted />
      );
  }
}
