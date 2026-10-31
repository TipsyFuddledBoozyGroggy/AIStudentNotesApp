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
import { Segmented } from "../ui/Segmented";
import { ActionProblem } from "./ActionProblem";
import { CREATE_COPY as C, DEMO_TAGS, FILE_COPY as F, NOTICE_DRAFT } from "./copy";
import { DemoLine } from "./DemoLine";
import { isRecord, readLocal, removeLocal, writeLocal } from "./storage";

const MAX_CODE_POINTS = 50000;
export const MAX_FILE_BYTES = 10 * 1024 * 1024;

export type Method = "paste" | "file";

export interface CreateFormProps {
  api: SyllabusApi;
  courseId: string;
  courseName: string;
  /** Show the Demo source line (mock extractor expected). */
  demoExpected: boolean;
  initialMethod?: Method;
  onCreated(s: Syllabus): void;
  onOpenExisting(id: string): void;
  /** When the course already has syllabi, Cancel goes back to them instead of to the course page. */
  onCancel?: () => void;
}

type ErrorKey = keyof typeof C.errors;
type FileErrorKey = keyof typeof F.errors;
type SubmitError = { key: ErrorKey; existingId?: string } | { file: FileErrorKey } | { session: true };
type Draft = { text: string; firstDay: string; lastDay?: string };
const isDraft = (v: unknown): v is Draft =>
  isRecord(v) && typeof v.text === "string" && typeof v.firstDay === "string" && (v.lastDay === undefined || typeof v.lastDay === "string");

function textError(text: string): ErrorKey | null {
  if (text.trim().length === 0) return "textEmpty";
  if (text.includes("\u0000")) return "textNul";
  if ([...text].length > MAX_CODE_POINTS) return "textLong";
  return null;
}

function dayError(v: string): ErrorKey | null {
  if (!v) return null;
  if (!isLocalDate(v)) return "firstDayFormat";
  return isRealDate(v) ? null : "firstDayReal";
}

/** Both term dates or neither (the real server rejects one alone), and the end on or after the start. */
function termError(first: string, last: string): FileErrorKey | null {
  if (Boolean(first) !== Boolean(last)) return "termPair";
  if (first && last && last < first) return "termOrder";
  return null;
}

/**
 * Checks a chosen file at selection time (UX 3: validate on selection, not on submit). The backend reads plain text
 * only in this increment (PDF and Word end `failed`/`disabled`), so those are refused here with a plain reason instead
 * of spending one of the student's daily reads on a file that cannot be read. The server sniffs the bytes anyway.
 */
export function fileError(f: { name: string; size: number; type: string }): FileErrorKey | null {
  const name = f.name.toLowerCase();
  const ext = name.includes(".") ? name.slice(name.lastIndexOf(".")) : "";
  if ([".pdf", ".docx", ".doc"].includes(ext) || f.type === "application/pdf" || /word/.test(f.type)) return "notYet";
  if (!(ext === ".txt" || (ext === "" && (f.type === "text/plain" || f.type === "")))) return "type";
  if (f.size > MAX_FILE_BYTES) return "size";
  if (f.size === 0) return "empty";
  return null;
}

export function fileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} bytes`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

/**
 * Create a syllabus (UX section 3): paste text, or upload a plain text file (multipart). One press = one POST with an
 * Idempotency-Key; the key is reused only to retry the same body after a network failure, so a double press or retry
 * never creates two syllabi. The pasted text and the options are kept locally and restored ("We kept what you pasted.").
 */
export function CreateForm({ api, courseId, courseName, demoExpected, initialMethod = "paste", onCreated, onOpenExisting, onCancel }: CreateFormProps) {
  const ids = {
    text: useId(),
    textHint: useId(),
    textErr: useId(),
    file: useId(),
    fileHint: useId(),
    fileErr: useId(),
    day: useId(),
    dayHint: useId(),
    dayErr: useId(),
    last: useId(),
    lastErr: useId(),
    termErr: useId(),
    notice: useId(),
    submitHint: useId(),
  };
  const headingId = useId();
  const draftKey = `draft.${courseId}`;
  const headingRef = useRef<HTMLHeadingElement>(null);
  const errorRef = useRef<HTMLDivElement>(null);
  const textRef = useRef<HTMLTextAreaElement>(null);
  const fileRef = useRef<HTMLInputElement>(null);
  const [method, setMethod] = useState<Method>(initialMethod);
  const [text, setText] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const [fileErr, setFileErr] = useState<FileErrorKey | null>(null);
  const [firstDay, setFirstDay] = useState("");
  const [lastDay, setLastDay] = useState("");
  const [kept, setKept] = useState(false);
  const [attestedAt, setAttestedAt] = useState<string | null>(null);
  const [textErr, setTextErr] = useState<ErrorKey | null>(null);
  const [dayErr, setDayErr] = useState<ErrorKey | null>(null);
  const [lastErr, setLastErr] = useState<ErrorKey | null>(null);
  const [termErr, setTermErr] = useState<FileErrorKey | null>(null);
  const [submitErr, setSubmitErr] = useState<SubmitError | null>(null);
  const [busy, setBusy] = useState(false);
  const busyRef = useRef(false);
  const keyRef = useRef<{ fingerprint: string; key: string } | null>(null);
  const [tz] = useState(() => browserTimezone());

  useEffect(() => {
    headingRef.current?.focus();
    const d = readLocal(draftKey, isDraft);
    if (d && (d.text || d.firstDay || d.lastDay)) {
      setText(d.text);
      setFirstDay(d.firstDay);
      setLastDay(d.lastDay ?? "");
      setKept(Boolean(d.text));
    }
  }, [draftKey]);

  // Draft autosave (debounced). Only while there is something to keep. A chosen file cannot be kept.
  useEffect(() => {
    const t = setTimeout(() => {
      if (text || firstDay || lastDay) writeLocal(draftKey, { text, firstDay, lastDay });
      else removeLocal(draftKey);
    }, 400);
    return () => clearTimeout(t);
  }, [draftKey, text, firstDay, lastDay]);

  useEffect(() => {
    if (submitErr) errorRef.current?.focus(); // the student's own press caused it: focus the message (UX 8)
  }, [submitErr]);

  const hasInput = method === "paste" ? text.trim().length > 0 : file !== null && fileErr === null;
  const ready = hasInput && attestedAt !== null;
  const hint = !hasInput ? (method === "paste" ? C.hintNeedText : file ? F.hintOtherFile : F.hintNeedFile) : attestedAt === null ? C.hintNeedNotice : null;

  function chooseFile(list: FileList | null) {
    const f = list?.[0] ?? null;
    setSubmitErr(null);
    keyRef.current = null;
    if (!f) {
      setFile(null);
      setFileErr(null);
      return;
    }
    setFile(f);
    setFileErr(fileError(f));
  }

  function removeFile() {
    setFile(null);
    setFileErr(null);
    keyRef.current = null;
    if (fileRef.current) fileRef.current.value = "";
    fileRef.current?.focus();
  }

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!ready || busyRef.current) return;
    const te = method === "paste" ? textError(text) : null;
    const de = dayError(firstDay);
    const le = dayError(lastDay);
    const pe = de || le ? null : termError(firstDay, lastDay);
    setTextErr(te);
    setDayErr(de);
    setLastErr(le);
    setTermErr(pe);
    if (te || de || le || pe) {
      const day = document.getElementById(de || pe ? ids.day : ids.last);
      if (!te && day) day.closest("details")?.setAttribute("open", ""); // the optional fields may be collapsed
      (te ? textRef.current : day)?.focus();
      return;
    }
    const termStart = firstDay || null;
    const termEnd = lastDay || null;
    const meta = { termStart, termEnd, defaultTimezone: tz, noticeVersion: NOTICE_VERSION, acceptedAt: attestedAt as string };
    const fingerprint = JSON.stringify(
      method === "paste" ? [courseId, "paste", text, termStart, termEnd, attestedAt] : [courseId, "file", file?.name, file?.size, file?.lastModified, termStart, termEnd, attestedAt],
    );
    if (keyRef.current?.fingerprint !== fingerprint) keyRef.current = { fingerprint, key: newIdempotencyKey() };
    busyRef.current = true;
    setBusy(true);
    setSubmitErr(null);
    try {
      const { syllabus } =
        method === "paste"
          ? await api.createFromText(courseId, { text, ...meta }, keyRef.current.key)
          : await api.createFromFile(courseId, { file: file as File, fileName: (file as File).name, ...meta }, keyRef.current.key);
      removeLocal(draftKey);
      onCreated(syllabus);
    } catch (err) {
      setSubmitErr(mapError(err, method));
      if (!(err instanceof ApiError && err.isNetwork)) keyRef.current = null; // the server answered: a retry is a new request
    } finally {
      busyRef.current = false;
      setBusy(false);
    }
  }

  const textDescribedBy = [ids.textHint, kept ? `${ids.textHint}-kept` : null, textErr ? ids.textErr : null].filter(Boolean).join(" ");
  const cancelLink = `/courses/${encodeURIComponent(courseId)}`;

  // Sheet template (redesign-round2 2.3, slice 6; deferred from slice 4): the h1, its sentence and the Demo line on the
  // canvas above the one sheet, which holds what is filled in. The Draft notice panel and its checkbox inside the sheet
  // are unchanged in wording and presentation (counsel-pending placeholder, P2-D5; gate G1).
  return (
    <div className="max-w-sheet">
      <h1 id={headingId} ref={headingRef} tabIndex={-1} className="display text-3xl">
        {C.h1}
      </h1>
      <p className="mt-1 text-muted">{C.intro(courseName)}</p>
      {demoExpected && (
        <div className="mt-3">
          <DemoLine text={DEMO_TAGS.source} />
        </div>
      )}

      <form onSubmit={submit} noValidate aria-labelledby={headingId} className="sheet sheet-page mt-6 space-y-6">
        <Segmented
          legend={F.methodLegend}
          value={method}
          onChange={(m) => {
            setMethod(m);
            setSubmitErr(null);
          }}
          options={[
            { value: "paste", label: F.methodPaste },
            { value: "file", label: F.methodFile },
          ]}
        />

        {method === "paste" ? (
          <div>
            <label htmlFor={ids.text} className="field-label">
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
        ) : (
          <div>
            <label htmlFor={ids.file} className="field-label">
              {F.label}
            </label>
            <p id={ids.fileHint} className="help mt-1">
              {F.hint}
            </p>
            <input
              ref={fileRef}
              id={ids.file}
              type="file"
              accept=".txt,text/plain"
              onChange={(e) => chooseFile(e.target.files)}
              aria-invalid={fileErr ? true : undefined}
              aria-describedby={[ids.fileHint, fileErr ? ids.fileErr : null].filter(Boolean).join(" ")}
              className="mt-2 block min-h-[44px] w-full text-sm file:mr-3 file:min-h-[44px] file:cursor-pointer file:rounded-control file:border-2 file:border-line file:bg-surface file:px-4 file:font-semibold file:text-ink"
            />
            {file && (
              <p className="mt-2 flex flex-wrap items-center gap-3 text-sm">
                <span className="break-all">{F.selected(file.name, fileSize(file.size))}</span>
                <button type="button" className="btn-tertiary btn-sm" aria-label={F.removeName} onClick={removeFile}>
                  {F.remove}
                </button>
              </p>
            )}
            {fileErr && (
              <p id={ids.fileErr} className="mt-1 font-semibold text-danger">
                {F.errors[fileErr]}
              </p>
            )}
          </div>
        )}

        <Disclosure summary={C.optionsToggle}>
          <div className="mt-2 space-y-4">
            <p id={ids.dayHint} className="help">
              {C.firstDayHint} {F.termHint}
            </p>
            <div>
              <label htmlFor={ids.day} className="field-label">
                {C.firstDayLabel}
              </label>
              <input
                id={ids.day}
                type="date"
                min="2000-01-01"
                max="2100-12-31"
                value={firstDay}
                onChange={(e) => {
                  setFirstDay(e.target.value);
                  if (dayErr) setDayErr(null);
                  if (termErr) setTermErr(null);
                }}
                onBlur={() => setDayErr(dayError(firstDay))}
                aria-invalid={dayErr || termErr ? true : undefined}
                aria-describedby={[ids.dayHint, dayErr ? ids.dayErr : null, termErr ? ids.termErr : null].filter(Boolean).join(" ")}
                className="field max-w-xs"
              />
              {dayErr && (
                <p id={ids.dayErr} className="mt-1 font-semibold text-danger">
                  {C.errors[dayErr]}
                </p>
              )}
            </div>
            <div>
              <label htmlFor={ids.last} className="field-label">
                {F.lastDayLabel}
              </label>
              <input
                id={ids.last}
                type="date"
                min="2000-01-01"
                max="2100-12-31"
                value={lastDay}
                onChange={(e) => {
                  setLastDay(e.target.value);
                  if (lastErr) setLastErr(null);
                  if (termErr) setTermErr(null);
                }}
                onBlur={() => setLastErr(dayError(lastDay))}
                aria-invalid={lastErr || termErr ? true : undefined}
                aria-describedby={[ids.dayHint, lastErr ? ids.lastErr : null, termErr ? ids.termErr : null].filter(Boolean).join(" ")}
                className="field max-w-xs"
              />
              {lastErr && (
                <p id={ids.lastErr} className="mt-1 font-semibold text-danger">
                  {C.errors[lastErr]}
                </p>
              )}
            </div>
            {termErr && (
              <p id={ids.termErr} className="font-semibold text-danger">
                {F.errors[termErr]}
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

        {submitErr && "session" in submitErr && <ActionProblem ref={errorRef} kind="session" />}
        {submitErr && !("session" in submitErr) && (
          <div ref={errorRef} tabIndex={-1} className="note note-notice">
            <p className="font-semibold">{"file" in submitErr ? F.errors[submitErr.file] : C.errors[submitErr.key]}</p>
            {"key" in submitErr && submitErr.existingId && (
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
          {onCancel ? (
            <button type="button" className="btn-tertiary" onClick={onCancel}>
              {C.cancel}
            </button>
          ) : (
            <Link href={cancelLink} className="btn-tertiary">
              {C.cancel}
            </Link>
          )}
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

function mapError(err: unknown, method: Method): SubmitError {
  if (err instanceof ApiError && err.isUnauthenticated) return { session: true };
  if (err instanceof ApiError && err.isNetwork) return method === "file" ? { file: "network" } : { key: "network" };
  if (err instanceof SyllabusApiError) {
    if (err.code === "duplicate_syllabus") return err.existingSyllabusId ? { key: "duplicate", existingId: err.existingSyllabusId } : { key: "duplicate" };
    if (err.code === "extraction_in_progress") return { key: "inProgress" };
    if (err.code === "syllabus_limit_reached") return { key: "limit" };
    if (err.status === 429) return { key: "rateLimited" };
    if (err.status === 415) return { file: "type" };
    if (err.status === 413) return method === "file" ? { file: "size" } : { key: "textLong" };
    if (err.status === 400) return { key: "invalid" };
  }
  return { key: "generic" };
}
