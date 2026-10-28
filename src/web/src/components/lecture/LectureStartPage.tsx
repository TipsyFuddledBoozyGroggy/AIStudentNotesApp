"use client";

import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useEffect, useMemo, useState } from "react";
import { api, ApiError, type Course } from "@/lib/api";
import { useRequireUser } from "../AuthProvider";
import { BrowserMicSource } from "./micSource";
import { createRealBackend } from "./realRuntime";
import { StartFlow } from "./StartFlow";

type State = { s: "loading" } | { s: "notfound" } | { s: "error" } | { s: "ready"; course: Course };

/** Real route: consent screen -> POST session -> getUserMedia (on the press only) -> live screen on the real hub and audio socket. */
export function LectureStartPage() {
  const params = useParams<{ id: string }>();
  const id = params?.id;
  const router = useRouter();
  const { status } = useRequireUser();
  const [state, setState] = useState<State>({ s: "loading" });
  const backend = useMemo(() => createRealBackend({ api }), []);
  const mic = useMemo(() => new BrowserMicSource(), []);

  useEffect(() => {
    if (status !== "authenticated" || !id) return;
    let cancelled = false;
    api
      .getCourse(id)
      .then((course) => !cancelled && setState({ s: "ready", course }))
      .catch((e: unknown) => {
        if (!cancelled) setState(e instanceof ApiError && e.isNotFound ? { s: "notfound" } : { s: "error" });
      });
    return () => {
      cancelled = true;
    };
  }, [status, id]);

  if (state.s === "loading") return <p role="status">Loading...</p>;
  if (state.s !== "ready" || !id) {
    return (
      <div className="space-y-3">
        <h1 className="text-2xl font-bold">{state.s === "notfound" ? "We can't find that course" : "We couldn't load this course"}</h1>
        <Link href="/" className="btn-primary">
          Back to dashboard
        </Link>
      </div>
    );
  }
  return (
    <StartFlow
      courseId={id}
      courseName={state.course.name}
      backend={backend}
      mic={mic}
      demoExpected={process.env.NEXT_PUBLIC_EXPECT_DEMO_SOURCE !== "0"}
      onBackToCourse={() => router.push(`/courses/${encodeURIComponent(id)}`)}
    />
  );
}
