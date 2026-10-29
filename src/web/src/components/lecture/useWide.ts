"use client";

import { useEffect, useState } from "react";

const QUERY = "(min-width: 1024px)";

/**
 * True at the two-column breakpoint (>= 1024px). Where matchMedia is missing (jsdom) it reports wide, so every panel is
 * expanded and component tests see the full content. Layout-only: never used for anything about recording state.
 */
export function useWide(): boolean {
  const [wide, setWide] = useState(true);
  useEffect(() => {
    if (typeof window.matchMedia !== "function") return;
    const mq = window.matchMedia(QUERY);
    setWide(mq.matches);
    const h = (e: MediaQueryListEvent) => setWide(e.matches);
    mq.addEventListener?.("change", h);
    return () => mq.removeEventListener?.("change", h);
  }, []);
  return wide;
}
