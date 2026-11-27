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
    let cancelled = false;
    // The env value is compared INLINE (not through MOCKING): Next replaces it with a literal at build time, so in a build with mocking
    // disabled this whole branch is constant-false and webpack never emits the mock-handler chunk (QA D3). Through a variable it would.
    if (process.env.NEXT_PUBLIC_API_MOCKING === "enabled") {
      import("@/mocks/browser")
        .then((m) => m.startMockWorker())
        .catch(() => undefined)
        .finally(() => {
          if (!cancelled) setReady(true);
        });
    }
    return () => {
      cancelled = true;
    };
  }, []);

  if (!ready) return <p role="status" className="p-6">Getting things ready.</p>;
  return <>{children}</>;
}
