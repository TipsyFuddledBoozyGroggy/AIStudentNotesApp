"use client";

import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useEffect, useMemo, useState } from "react";
import { api, ApiError, type LectureSessionDto } from "@/lib/api";
import { useRequireUser } from "../AuthProvider";
import { AnnouncerProvider } from "./Announcer";
import { LectureScreen } from "./LectureScreen";
import { BrowserMicSource } from "./micSource";
import { createRealRuntime } from "./realRuntime";

type State = { s: "loading" } | { s: "notfound" } | { s: "error" } | { s: "ready"; session: LectureSessionDto };

/**
 * Reload or reopen an existing session. There is no live microphone here: an Active session without a live track
 * shows the mismatch panel (never silently recording), a Paused one can be resumed on a press.
 */
export function LectureReopenPage() {
  const params = useParams<{ sessionId: string }>();
  const sessionId = params?.sessionId;
  const router = useRouter();
  const { status } = useRequireUser();
  const [state, setState] = useState<State>({ s: "loading" });
  const mic = useMemo(() => new BrowserMicSource(), []);

  useEffect(() => {
    if (status !== "authenticated" || !sessionId) return;
    let cancelled = false;
    api
      .getSession(sessionId)
      .then((session) => !cancelled && setState({ s: "ready", session }))
      .catch((e: unknown) => {
        if (!cancelled) setState(e instanceof ApiError && e.isNotFound ? { s: "notfound" } : { s: "error" });
      });
    return () => {
      cancelled = true;
    };
  }, [status, sessionId]);

  const session = state.s === "ready" ? state.session : null;
  const runtime = useMemo(() => (session ? createRealRuntime({ api }, session) : null), [session]);

  if (state.s === "loading")
    return (
      <p role="status" className="text-muted">
        Getting this lecture.
      </p>
    );
  if (!session || !runtime) {
    return (
      <div className="max-w-prose space-y-4">
        <h1 className="display text-3xl">{state.s === "notfound" ? "We can't find that lecture" : "We couldn't load this lecture"}</h1>
        <Link href="/" className="btn-primary">
          Back to dashboard
        </Link>
      </div>
    );
  }
  return (
    <AnnouncerProvider>
      <LectureScreen
        runtime={runtime}
        track={null}
        mic={mic}
        courseName={session.title ?? undefined}
        onBackToCourse={() => router.push(`/courses/${encodeURIComponent(session.courseId)}`)}
      />
    </AnnouncerProvider>
  );
}
