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

const WINDOW_KEY = "tether.lecture.lostTrackWindow.v1";
export type UsualWindow = 2 | 5 | 10;

/** Remembered I Lost Track window (PRD M2-S5 AC1). Storage may be missing or blocked: default 5. */
export function loadUsualWindow(): UsualWindow {
  try {
    const v = Number(window.localStorage.getItem(WINDOW_KEY));
    return v === 2 || v === 5 || v === 10 ? v : 5;
  } catch {
    return 5;
  }
}

export function saveUsualWindow(w: UsualWindow): void {
  try {
    window.localStorage.setItem(WINDOW_KEY, String(w));
  } catch {
    /* storage unavailable: the choice is just not remembered */
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
