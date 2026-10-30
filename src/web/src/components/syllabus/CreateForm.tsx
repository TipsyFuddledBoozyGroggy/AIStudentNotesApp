"use client";

import Link from "next/link";
import { useEffect, useId, useRef, useState, type FormEvent } from "react";
import { ApiError } from "@/lib/api/errors";
import { SyllabusApiError, type SyllabusApi } from "@/lib/syllabus/client";
import { browserTimezone, isRealDate } from "@/lib/syllabus/format";
import { isLocalDate } from "@/lib/syllabus/parse";
import { newIdempotencyKey, NOTICE_VERSION } from "@/lib/syllabus/requests";
import type { Syllabus } from "@/lib/syllabus/types";
import { Disclosure } from "../ui/Disclosure";
import { CREATE_COPY as C, DEMO_TAGS, NOTICE_DRAFT } from "./copy";
import { DemoLine } from "./DemoLine";
import { isRecord, readLocal, removeLocal, writeLocal } from "./storage";

const MAX_CODE_POINTS = 50000;

export interface CreateFormProps {
  api: SyllabusApi;
  courseId: string;
  courseName: string;
  /** Show the Demo source line (mock extractor expected). */
  demoExpected: boolean;
  onCreated(s: Syllabus): void;
  onOpenExisting(id: string): void;
}

type ErrorKey = keyof typeof C.errors;
type Draft = { text: string; firstDay: string };
const isDraft = (v: unknown): v is Draft => isRecord(v) && typeof v.text === "string" && typeof v.firstDay === "string";

function textError(text: string): ErrorKey | null {
  if (text.trim().length === 0) return "textEmpty";
  if (text.includes("\u0000")) return "textNul";
  if ([...text].length > MAX_CODE_POINTS) return "textLong";
  return null;
}

function firstDayError(v: string): ErrorKey | null {
  if (!v) return null;
  if (!isLocalDate(v)) return "firstDayFormat";
  return isRealDate(v) ? null : "firstDayReal";
}

/**
 * Paste-text create (UX section 3; file upload is increment F2). One press = one POST with an Idempotency-Key; the
 * key is reused only to retry the same body after a network failure, so a double press or retry never creates two
 * syllabi. The draft text is kept locally and restored ("We kept what you pasted.").
 */
export function CreateForm({ api, courseId, courseName, demoExpected, onCreated, onOpenExisting }: CreateFormProps) {
  const ids = { text: useId(), textHint: useId(), textErr: useId(), day: useId(), dayHint: useId(), dayErr: useId(), notice: useId(), submitHint: useId() };
  const draftKey = `draft.${courseId}`;
  const headingRef = useRef<HTMLHeadingElement>(null);
  const errorRef = useRef<HTMLDivElement>(null);
  const textRef = useRef<HTMLTextAreaElement>(null);
  const [text, setText] = useState("");
  const [firstDay, setFirstDay] = useState("");
  const [kept, setKept] = useState(false);
  const [attestedAt, setAttestedAt] = useState<string | null>(null);
  const [textErr, setTextErr] = useState<ErrorKey | null>(null);
  const [dayErr, setDayErr] = useState<ErrorKey | null>(null);
  const [submitErr, setSubmitErr] = useState<{ key: ErrorKey; existingId?: string } | null>(null);
  const [busy, setBusy] = useState(false);
  const busyRef = useRef(false);
  const keyRef = useRef<{ fingerprint: string; key: string } | null>(null);
  const [tz] = useState(() => browserTimezone());

  useEffect(() => {
    headingRef.current?.focus();
    const d = readLocal(draftKey, isDraft);
    if (d && (d.text || d.firstDay)) {
      setText(d.text);
      setFirstDay(d.firstDay);
      setKept(Boolean(d.text));
    }
  }, [draftKey]);

  // Draft autosave (debounced). Only while there is something to keep.
  useEffect(() => {
    const t = setTimeout(() => {
      if (text || firstDay) writeLocal(draftKey, { text, firstDay });
      else removeLocal(draftKey);
    }, 400);
    return () => clearTimeout(t);
  }, [draftKey, text, firstDay]);

  useEffect(() => {
    if (submitErr) errorRef.current?.focus(); // the student's own press caused it: focus the message (UX 8)
  }, [submitErr]);

  const ready = text.trim().length > 0 && attestedAt !== null;
  const hint = text.trim().length === 0 ? C.hintNeedText : attestedAt === null ? C.hintNeedNotice : null;

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!ready || busyRef.current) return;
    const te = textError(text);
    const de = firstDayError(firstDay);
    setTextErr(te);
    setDayErr(de);
    if (te || de) {
      const day = document.getElementById(ids.day);
      if (!te && day) day.closest("details")?.setAttribute("open", ""); // the optional field may be collapsed
      (te ? textRef.current : day)?.focus();
      return;
    }
    const termStart = firstDay || null;
    const fingerprint = JSON.stringify([courseId, text, termStart, attestedAt]);
    if (keyRef.current?.fingerprint !== fingerprint) keyRef.current = { fingerprint, key: newIdempotencyKey() };
    busyRef.current = true;
    setBusy(true);
    setSubmitErr(null);
    try {
      const { syllabus } = await api.createFromText(
        courseId,
        { text, termStart, defaultTimezone: tz, noticeVersion: NOTICE_VERSION, acceptedAt: attestedAt as string },
        keyRef.current.key,
      );
      removeLocal(draftKey);
      onCreated(syllabus);
    } catch (err) {
      setSubmitErr(mapError(err));
      if (!(err instanceof ApiError && err.isNetwork)) keyRef.current = null; // the server answered: a retry is a new request
    } finally {
      busyRef.current = false;
      setBusy(false);
    }
  }

  const textDescribedBy = [ids.textHint, kept ? `${ids.textHint}-kept` : null, textErr ? ids.textErr : null].filter(Boolean).join(" ");

  return (
    <div className="max-w-prose space-y-6">
      <div className="space-y-3">
        <h1 ref={headingRef} tabIndex={-1} className="display text-3xl">
          {C.h1}
        </h1>
        <p className="text-muted">{C.intro(courseName)}</p>
        {demoExpected && <DemoLine text={DEMO_TAGS.source} />}
      </div>

      <form onSubmit={submit} noValidate className="space-y-6">
        <div>
          <label htmlFor={ids.text} className="label">
            {C.pasteLabel}
          </label>
          <p id={ids.textHint} className="help mt-1">
            {C.pasteHint}
          </p>
          {kept && (
            <p id={`${ids.textHint}-kept`} className="help mt-1">
              {C.pasteKept}
            </p>
          )}
          <textarea
            ref={textRef}
            id={ids.text}
            rows={12}
            value={text}
            onChange={(e) => {
              setText(e.target.value);
              setKept(false);
              if (textErr) setTextErr(null);
            }}
            onBlur={() => setTextErr(text ? textError(text) : null)}
            aria-invalid={textErr ? true : undefined}
            aria-describedby={textDescribedBy}
            className="field resize-y"
          />
          {textErr && (
            <p id={ids.textErr} className="mt-1 font-semibold text-danger">
              {C.errors[textErr]}
            </p>
          )}
        </div>

        <Disclosure summary={C.optionsToggle}>
          <div className="mt-2">
            <label htmlFor={ids.day} className="label">
              {C.firstDayLabel}
            </label>
            <p id={ids.dayHint} className="help mt-1">
              {C.firstDayHint}
            </p>
            <input
              id={ids.day}
              type="date"
              min="2000-01-01"
              max="2100-12-31"
              value={firstDay}
              onChange={(e) => {
                setFirstDay(e.target.value);
                if (dayErr) setDayErr(null);
              }}
              onBlur={() => setDayErr(firstDayError(firstDay))}
              aria-invalid={dayErr ? true : undefined}
              aria-describedby={[ids.dayHint, dayErr ? ids.dayErr : null].filter(Boolean).join(" ")}
              className="field max-w-xs"
            />
            {dayErr && (
              <p id={ids.dayErr} className="mt-1 font-semibold text-danger">
                {C.errors[dayErr]}
              </p>
            )}
          </div>
        </Disclosure>

        <p className="help">{C.timezone(tz)}</p>

        <section aria-labelledby={ids.notice} className="panel panel-notice space-y-3">
          <h2 id={ids.notice} className="flex flex-wrap items-center gap-2 font-semibold text-ink">
            <span className="tag tag-warning">{NOTICE_DRAFT.tag}</span>
            {NOTICE_DRAFT.heading}
          </h2>
          <p>{NOTICE_DRAFT.body}</p>
          <label className="flex min-h-[44px] cursor-pointer items-start gap-3">
            <input
              type="checkbox"
              checked={attestedAt !== null}
              onChange={(e) => setAttestedAt(e.target.checked ? new Date().toISOString() : null)}
              className="mt-1 h-5 w-5 shrink-0"
            />
            <span>{NOTICE_DRAFT.checkbox}</span>
          </label>
        </section>

        {submitErr && (
          <div ref={errorRef} tabIndex={-1} className="panel panel-notice">
            <p className="font-semibold">{C.errors[submitErr.key]}</p>
            {submitErr.existingId && (
              <button type="button" className="btn-secondary mt-3" onClick={() => onOpenExisting(submitErr.existingId as string)}>
                {C.errors.openExisting}
              </button>
            )}
          </div>
        )}

        <div className="flex flex-wrap items-center gap-3">
          <button
            type="submit"
            className="btn-primary"
            aria-disabled={!ready || busy ? true : undefined}
            aria-describedby={hint && !busy ? ids.submitHint : undefined}
          >
            {busy ? C.submitBusy : C.submit}
          </button>
          <Link href={`/courses/${encodeURIComponent(courseId)}`} className="btn-quiet">
            {C.cancel}
          </Link>
        </div>
        {hint && !busy && (
          <p id={ids.submitHint} className="help">
            {hint}
          </p>
        )}
      </form>
    </div>
  );
}

function mapError(err: unknown): { key: ErrorKey; existingId?: string } {
  if (err instanceof ApiError && err.isNetwork) return { key: "network" };
  if (err instanceof SyllabusApiError) {
    if (err.code === "duplicate_syllabus") return err.existingSyllabusId ? { key: "duplicate", existingId: err.existingSyllabusId } : { key: "duplicate" };
    if (err.code === "extraction_in_progress") return { key: "inProgress" };
    if (err.code === "syllabus_limit_reached") return { key: "limit" };
    if (err.status === 429) return { key: "rateLimited" };
    if (err.status === 413) return { key: "textLong" };
    if (err.status === 400) return { key: "invalid" };
  }
  return { key: "generic" };
}
