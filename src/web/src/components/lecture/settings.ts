"use client";

import { useCallback, useEffect, useState } from "react";

export interface LectureSettings {
  announceLines: boolean;
  announceTldr: boolean;
  reduceMotion: boolean;
  /** Alt+ shortcuts: OFF by default (contract section 12.5, unverified against screen readers). */
  shortcuts: boolean;
}

export const DEFAULT_SETTINGS: LectureSettings = {
  announceLines: false,
  announceTldr: false,
  reduceMotion: false,
  shortcuts: false,
};

const KEY = "tether.lecture.settings.v1";

function load(): LectureSettings {
  try {
    const raw = window.localStorage.getItem(KEY);
    if (!raw) return DEFAULT_SETTINGS;
    const p = JSON.parse(raw) as Partial<LectureSettings>;
    return {
      announceLines: p.announceLines === true,
      announceTldr: p.announceTldr === true,
      reduceMotion: p.reduceMotion === true,
      shortcuts: p.shortcuts === true,
    };
  } catch {
    return DEFAULT_SETTINGS;
  }
}

/** Per-viewer settings kept in localStorage (convenience only; failures fall back to defaults). */
export function useLectureSettings(initial?: Partial<LectureSettings>) {
  const [settings, setSettings] = useState<LectureSettings>({ ...DEFAULT_SETTINGS, ...initial });
  useEffect(() => {
    if (initial) return;
    setSettings(load());
  }, [initial]);
  const update = useCallback((patch: Partial<LectureSettings>) => {
    setSettings((s) => {
      const next = { ...s, ...patch };
      try {
        window.localStorage.setItem(KEY, JSON.stringify(next));
      } catch {
        /* storage unavailable */
      }
      return next;
    });
  }, []);
  return [settings, update] as const;
}
