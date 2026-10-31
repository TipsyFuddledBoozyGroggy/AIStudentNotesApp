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

const WINDOW_KEY = "tether.lecture.quickRecapWindow.v1";
export type UsualWindow = 2 | 5 | 10;

/** Remembered Quick recap window (PRD M2-S5 AC1). Storage may be missing or blocked: default 5. */
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

// --- PO-19: after Stop, move to the lecture's own page (docs/ux/stop-redirect.md section 4) ---------------------------
// A navigation preference, not a draft: no student content, so it is kept across sign-out and not purged by the syllabus
// owner switch (SB-06's reasons do not apply). It changes navigation, so it is user-scoped (one dot-free id segment, as in
// syllabus/storage.ts) and never device-wide. Not part of LOOK_KEYS: "Reset look to defaults" leaves it alone.

const segment = (id: string) => encodeURIComponent(id).replace(/\./g, "%2E"); // one dot-free key segment

/** `tether.lecture.u.<seg(userId)>.stopMove.v1`; value "direct" = open the page without asking. Absent = ask. */
export const stopMoveKey = (userId: string) => `tether.lecture.u.${segment(userId)}.stopMove.v1`;

/** True when the student chose "Don't show this again". No user id or no storage reads as "ask". */
export function loadStopMove(userId: string | null | undefined): boolean {
  if (!userId) return false;
  try {
    return window.localStorage.getItem(stopMoveKey(userId)) === "direct";
  } catch {
    return false;
  }
}

/** direct=true stores "direct"; false removes the key (back to asking). Never throws; no user id does nothing. */
export function saveStopMove(userId: string | null | undefined, direct: boolean): void {
  if (!userId) return;
  try {
    if (direct) window.localStorage.setItem(stopMoveKey(userId), "direct");
    else window.localStorage.removeItem(stopMoveKey(userId));
  } catch {
    /* storage unavailable: the dialog simply asks again next time */
  }
}

/** One-shot marker (sessionStorage) written just before the move; read and removed by the lecture's page on mount. */
export const ARRIVED_FROM_STOP_KEY = "tether.lecture.arrivedFromStop.v1";

export function markArrivedFromStop(sessionId: string): void {
  try {
    window.sessionStorage.setItem(ARRIVED_FROM_STOP_KEY, sessionId);
  } catch {
    /* no marker: the lecture's page opens as on a reload (no focus move, no announcement) */
  }
}

/** Reads and always removes the marker; true only when it names this session. */
export function takeArrivedFromStop(sessionId: string): boolean {
  try {
    const v = window.sessionStorage.getItem(ARRIVED_FROM_STOP_KEY);
    if (v !== null) window.sessionStorage.removeItem(ARRIVED_FROM_STOP_KEY);
    return v === sessionId;
  } catch {
    return false;
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
