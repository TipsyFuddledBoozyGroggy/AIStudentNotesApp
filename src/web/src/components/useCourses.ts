"use client";

import { useCallback, useEffect, useState } from "react";
import { api, ApiError } from "@/lib/api";
import type { DashboardState } from "./DashboardView";

export function useCourses(enabled: boolean): { state: DashboardState; reload: () => void } {
  const [state, setState] = useState<DashboardState>({ status: "loading" });
  const [tick, setTick] = useState(0);

  useEffect(() => {
    if (!enabled) return;
    let cancelled = false;
    api
      .listCourses()
      .then((r) => !cancelled && setState({ status: "ready", courses: r.items }))
      .catch((e: unknown) => {
        if (cancelled) return;
        const msg =
          e instanceof ApiError && e.isNetwork
            ? "We can't reach the server right now."
            : "We couldn't load your courses.";
        setState({ status: "error", message: msg });
      });
    return () => {
      cancelled = true;
    };
  }, [enabled, tick]);

  const reload = useCallback(() => {
    setState({ status: "loading" });
    setTick((t) => t + 1);
  }, []);
  return { state, reload };
}
