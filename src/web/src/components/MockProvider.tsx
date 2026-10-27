"use client";

import { useEffect, useState, type ReactNode } from "react";

const MOCKING = process.env.NEXT_PUBLIC_API_MOCKING === "enabled";

/**
 * Starts the MSW service worker before any child fetches. A no-op when
 * NEXT_PUBLIC_API_MOCKING is not "enabled", so the real backend can replace it
 * by changing configuration only.
 */
export function MockProvider({ children }: { children: ReactNode }) {
  const [ready, setReady] = useState(!MOCKING);

  useEffect(() => {
    if (!MOCKING) return;
    let cancelled = false;
    import("@/mocks/browser")
      .then((m) => m.startMockWorker())
      .catch(() => undefined)
      .finally(() => {
        if (!cancelled) setReady(true);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  if (!ready) return <p role="status" className="p-6">Loading...</p>;
  return <>{children}</>;
}
