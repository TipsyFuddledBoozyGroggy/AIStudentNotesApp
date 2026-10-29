"use client";

import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { applyPrefs, DEFAULT_PREFS, loadPrefs, savePrefs, type Prefs } from "@/lib/design/prefs";

interface PrefsValue {
  prefs: Prefs;
  update(patch: Partial<Prefs>): void;
}

const Ctx = createContext<PrefsValue | null>(null);

/** Hook body shared by the provider and the standalone fallback. */
function usePrefsState(enabled: boolean): PrefsValue {
  const [prefs, setPrefs] = useState<Prefs>(DEFAULT_PREFS);
  // Until the stored prefs are read, do not touch <html>: the head script already set the right attributes.
  const [loaded, setLoaded] = useState(false);
  useEffect(() => {
    if (!enabled) return;
    setPrefs(loadPrefs());
    setLoaded(true);
  }, [enabled]);

  // Apply to <html> whenever they change, and follow the OS theme while the choice is "System".
  useEffect(() => {
    if (!enabled || !loaded) return;
    applyPrefs(prefs);
    if (prefs.theme !== "system" || typeof window.matchMedia !== "function") return;
    const mq = window.matchMedia("(prefers-color-scheme: dark)");
    const h = () => applyPrefs(prefs);
    mq.addEventListener?.("change", h);
    return () => mq.removeEventListener?.("change", h);
  }, [enabled, loaded, prefs]);

  const update = useCallback((patch: Partial<Prefs>) => {
    setPrefs((p) => {
      const next = { ...p, ...patch };
      if (enabled) savePrefs(next);
      return next;
    });
  }, [enabled]);
  return useMemo(() => ({ prefs, update }), [prefs, update]);
}

/** Student-owned look preferences, applied to <html> (the head script already applied them before first paint). */
export function PrefsProvider({ children }: { children: ReactNode }) {
  const value = usePrefsState(true);
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

/**
 * Preferences from the provider. Outside one (isolated component tests, harness pieces) it falls back to in-memory
 * defaults that are neither loaded, saved nor applied to <html>, so tests stay hermetic.
 */
export function usePrefs(): PrefsValue {
  const ctx = useContext(Ctx);
  const local = usePrefsState(false);
  return ctx ?? local;
}
