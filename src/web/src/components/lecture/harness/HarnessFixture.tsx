"use client";

import { useEffect, useMemo, useState } from "react";
import * as b from "@/lib/lecture/builders";
import { AnnouncerProvider } from "../Announcer";
import { createHarnessRuntime } from "../harnessRuntime";
import { LectureScreen } from "../LectureScreen";
import { FakeMicSource, FakeMicTrack } from "../micSource";
import { FIXTURES, type Spec } from "./fixtures";

const SID = "ses_harness";
const noop = () => undefined;
/** PO-19: the harness's stand-in user id for the "Don't show this again" key (synthetic; no account). */
export const HARNESS_USER = "harness-user";

function LiveFixture({ spec }: { spec: NonNullable<Spec["live"]> }) {
  const parts = useMemo(() => {
    const { runtime, controls } = createHarnessRuntime({ sessionId: SID, events: spec.events, script: spec.script, ...spec.rt });
    const track = new FakeMicTrack();
    if (spec.track === "ended") track.readyState = "ended";
    if (spec.track === "stubborn") track.ignoreStop = true;
    return { runtime, controls, track, mic: new FakeMicSource("ok") };
  }, [spec]);
  const [remaining, setRemaining] = useState(spec.script?.length ?? 0);
  const [opened, setOpened] = useState(0);

  useEffect(() => {
    if (!spec.after) return;
    const h = setTimeout(() => spec.after?.({ controls: parts.controls, track: parts.track }), 300);
    return () => clearTimeout(h);
  }, [spec, parts]);

  return (
    <AnnouncerProvider>
      <LectureScreen
        runtime={parts.runtime}
        track={parts.track}
        mic={parts.mic}
        onBackToCourse={noop}
        initialLecture={parts.controls.initialLecture}
        settingsOverride={{ shortcuts: spec.shortcuts ?? false }}
        {...(spec.liveRoute
          ? { openPageHref: `/lectures/${SID}`, onOpenPage: () => setOpened((n) => n + 1), userId: HARNESS_USER }
          : {})}
      />
      <HarnessControlsBar
        opened={opened}
        remaining={remaining}
        onStep={() => {
          parts.controls.step();
          setRemaining((n) => Math.max(0, n - 1));
        }}
        onDrop={() => parts.controls.drop()}
        onRestore={() => parts.controls.restore()}
        onUnplug={() => parts.track.simulateDeviceLoss()}
        onServerPause={() => parts.controls.push(b.sessionStateEvent(SID, String(100 + Math.floor(Math.random() * 1e6)), "Paused", "user"))}
      />
    </AnnouncerProvider>
  );
}

/** Dev-only fixture controls. Deliberately outside the lecture view and outside the live regions. */
function HarnessControlsBar(p: {
  opened: number;
  remaining: number;
  onStep(): void;
  onDrop(): void;
  onRestore(): void;
  onUnplug(): void;
  onServerPause(): void;
}) {
  return (
    <section aria-label="Harness controls (development only)" className="mt-6 rounded-lg border-2 border-dashed border-line bg-surface p-3">
      <h2 className="text-base font-bold">Harness controls (dev only)</h2>
      <div className="mt-2 flex flex-wrap gap-2">
        <button type="button" className="btn-secondary" onClick={p.onStep} aria-disabled={p.remaining === 0 ? true : undefined}>
          Step next event ({p.remaining} left)
        </button>
        <button type="button" className="btn-secondary" onClick={p.onDrop}>
          Drop connection
        </button>
        <button type="button" className="btn-secondary" onClick={p.onRestore}>
          Allow reconnect
        </button>
        <button type="button" className="btn-secondary" onClick={p.onUnplug}>
          Unplug microphone
        </button>
        <button type="button" className="btn-secondary" onClick={p.onServerPause}>
          Server pauses (mic stays on)
        </button>
      </div>
      {p.opened > 0 ? (
        <p data-testid="harness-opened" className="mt-2 text-sm">
          Open the lecture&apos;s page pressed {p.opened} time(s). The real route replaces the URL with /lectures/{SID}.
        </p>
      ) : null}
    </section>
  );
}

export function HarnessFixture({ id }: { id: string }) {
  const spec = FIXTURES[id];
  if (!spec) return <p>Unknown fixture.</p>;
  return (
    <>
      <p className="mb-3 text-sm text-muted">
        Fixture: {spec.title} (UX section {spec.ux}). Synthetic data; no network, no microphone.
      </p>
      {spec.live ? <LiveFixture spec={spec.live} /> : spec.custom?.()}
    </>
  );
}
