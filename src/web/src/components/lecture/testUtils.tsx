import { act, render } from "@testing-library/react";
import type { ReactElement } from "react";
import * as b from "@/lib/lecture/builders";
import type { HubEvent } from "@/lib/lecture";
import { AnnouncerProvider } from "./Announcer";
import { LectureScreen, type LectureScreenProps } from "./LectureScreen";
import { FakeMicSource, FakeMicTrack } from "./micSource";
import { createHarnessRuntime, type HarnessRuntimeOptions } from "./harnessRuntime";

export const SID = "ses_test";

export const baseEvents = (state: "Active" | "Paused" | "Stopped" = "Active", reason?: string): HubEvent[] => [
  b.sessionStateEvent(SID, "1", "Active"),
  b.finalEvent(SID, "2", 1, "First line of the lecture."),
  b.finalEvent(SID, "3", 2, "Second line of the lecture."),
  ...(state === "Active" ? [] : [b.sessionStateEvent(SID, "4", state, reason)]),
];

export interface Mounted {
  controls: ReturnType<typeof createHarnessRuntime>["controls"];
  runtime: ReturnType<typeof createHarnessRuntime>["runtime"];
  track: FakeMicTrack | null;
  mic: FakeMicSource;
  container: HTMLElement;
  statusLog: string[];
  alertLog: string[];
  onBack: () => void;
}

/** Mounts LectureScreen on the harness runtime and records every text the status/alert regions receive. */
export async function mountLecture(
  o: Partial<HarnessRuntimeOptions> & {
    track?: "live" | "ended" | "stubborn" | "none";
    settings?: Parameters<typeof LectureScreen>[0]["settingsOverride"];
    /** PO-19 props (live route: onOpenPage / openPageHref / userId; reopen page: arrivedFromStop). Absent: as before. */
    route?: Pick<LectureScreenProps, "onOpenPage" | "openPageHref" | "userId" | "arrivedFromStop" | "deletion" | "notesExport">;
  } = {},
): Promise<Mounted> {
  const { track: trackMode = "live", settings, route, ...rt } = o;
  const { runtime, controls } = createHarnessRuntime({ sessionId: SID, events: baseEvents(), ...rt });
  const mic = new FakeMicSource("ok");
  const track = trackMode === "none" ? null : new FakeMicTrack();
  if (track && trackMode === "ended") track.readyState = "ended";
  if (track && trackMode === "stubborn") track.ignoreStop = true;
  const onBack = vi.fn();
  const ui: ReactElement = (
    <AnnouncerProvider>
      <LectureScreen
        runtime={runtime}
        track={track}
        mic={mic}
        onBackToCourse={onBack}
        initialLecture={controls.initialLecture}
        settingsOverride={settings ?? {}}
        {...route}
      />
    </AnnouncerProvider>
  );
  const statusLog: string[] = [];
  const alertLog: string[] = [];
  // Observe before render so announcements emitted during mount are not missed.
  const obs = new MutationObserver((muts) => {
    const seen = new Set<string>(); // one record per region per batch (a swap is a remove plus an add)
    for (const m of muts) {
      const el = (m.target instanceof Element ? m.target : m.target.parentElement)?.closest("[data-region]");
      const kind = el?.getAttribute("data-region");
      if (!el || !el.textContent || !kind || seen.has(kind)) continue;
      seen.add(kind);
      if (kind === "status") statusLog.push(el.textContent);
      if (kind === "alert") alertLog.push(el.textContent);
    }
  });
  obs.observe(document.body, { childList: true, subtree: true, characterData: true });
  const { container } = render(ui);
  await flush();
  return { controls, runtime, track, mic, container, statusLog, alertLog, onBack };
}

export async function flush(): Promise<void> {
  await act(async () => {
    await Promise.resolve();
    await Promise.resolve();
  });
}
