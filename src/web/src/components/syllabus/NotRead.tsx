"use client";

import Link from "next/link";
import { useEffect, useRef } from "react";
import type { Syllabus } from "@/lib/syllabus/types";
import { DEMO_TAGS, NOT_READ_ACTIONS as A, NOT_READ_COPY, NOT_READ_FILE_NOT_YET } from "./copy";
import { DemoLine } from "./DemoLine";

/** Heading and body for a finished syllabus with nothing to review. A PDF or Word upload ends `disabled` in this increment. */
export function notReadCopy(s: Pick<Syllabus, "statusReason" | "fileType">): { heading: string; body: string } {
  if (s.statusReason === "disabled" && (s.fileType === "pdf" || s.fileType === "docx")) return NOT_READ_FILE_NOT_YET;
  return NOT_READ_COPY[s.statusReason ?? "default"] ?? NOT_READ_COPY.default;
}

/** The primary action offers pasting whenever the text could not be read from a file. */
export const offersPaste = (s: Pick<Syllabus, "status" | "statusReason" | "fileType">) =>
  s.status === "needs_text" || (s.statusReason === "disabled" && (s.fileType === "pdf" || s.fileType === "docx"));

/** A finished syllabus with nothing to review (needs_text or failed; UX section 8). Says what happened and the next step. */
export function NotRead({ syllabus, courseId, onPasteInstead }: { syllabus: Syllabus; courseId: string; onPasteInstead(): void }) {
  const ref = useRef<HTMLHeadingElement>(null);
  useEffect(() => ref.current?.focus(), []);
  const c = notReadCopy(syllabus);
  return (
    <div className="max-w-prose space-y-4">
      <h1 ref={ref} tabIndex={-1} className="display text-3xl">
        {c.heading}
      </h1>
      {syllabus.demoSource && <DemoLine text={DEMO_TAGS.source} />}
      <p>{c.body}</p>
      <div className="flex flex-wrap gap-3">
        <button type="button" className="btn-primary" onClick={onPasteInstead}>
          {offersPaste(syllabus) ? A.paste : A.tryAgain}
        </button>
        <Link href={`/courses/${encodeURIComponent(courseId)}`} className="btn-secondary">
          {A.back}
        </Link>
      </div>
    </div>
  );
}
