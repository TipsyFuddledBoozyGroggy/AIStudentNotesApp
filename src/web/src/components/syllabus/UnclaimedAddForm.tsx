"use client";

import { useEffect, useId, useRef, useState, type KeyboardEvent } from "react";
import { ApiError } from "@/lib/api/errors";
import { plainText } from "@/lib/lecture/plainText";
import type { SyllabusApi } from "@/lib/syllabus/client";
import { newIdempotencyKey } from "@/lib/syllabus/requests";
import type { EditableKind, ItemCreate, SyllabusItem, UnclaimedDate } from "@/lib/syllabus/types";
import { useAnnouncer } from "../lecture/Announcer";
import { ActionProblem } from "./ActionProblem";
import { ACTION_COPY, EDIT_COPY as E, UNCLAIMED_COPY as U } from "./copy";
import { isValues, validate, type EditValues, type Errors, type Field } from "./ItemEditForm";
import { readDraft, removeDraft, writeDraft } from "./storage";

const TITLE_MAX = 200;

/**
 * The prefilled Title (unclaimed-dates-list.md 4.10): the line (or the date itself when there is no line) with whitespace
 * collapsed, trimmed, and cut to 200 code points at the last space before the cap. Characters that plainText removes at
 * render are removed here too, so the field starts with what the student sees on the row.
 */
export function prefillTitle(u: UnclaimedDate): string {
  const src = plainText(u.snippet.context?.text ?? u.snippet.text ?? "");
  const t = src.replace(/\s+/g, " ").trim();
  const cps = [...t];
  if (cps.length <= TITLE_MAX) return t;
  if (cps[TITLE_MAX] === " ") return cps.slice(0, TITLE_MAX).join("");
  const head = cps.slice(0, TITLE_MAX).join("");
  const space = head.lastIndexOf(" ");
  return space > 0 ? head.slice(0, space) : head;
}

/** The add form's draft key. A row with a draft does not open by itself; pressing "Add as item" restores it (spec 4.10). */
export const addDraftKey = (syllabusId: string, unclaimedId: string) => `unclaimed-add.${syllabusId}.${unclaimedId}`;

export interface UnclaimedAddFormProps {
  api: SyllabusApi;
  syllabusId: string;
  row: UnclaimedDate;
  onCreated(item: SyllabusItem): void;
  onCancel(): void;
}

type Problem = { text: string } | { session: true };

/**
 * "Add as item" (unclaimed-dates-list.md 4.10): the ItemEditForm fields in a sheet under the row. Title starts as the
 * line, Type starts as Other, Date starts empty and is required (the client never derives a date the server did not),
 * All day is checked. Validation on blur and on save; a failed save focuses the first invalid field. One fresh
 * Idempotency-Key per press, reused only to retry the same body after a network failure. Server text is never shown.
 */
export function UnclaimedAddForm({ api, syllabusId, row, onCreated, onCancel }: UnclaimedAddFormProps) {
  const announcer = useAnnouncer();
  const uid = useId();
  const id = (f: string) => `${uid}-${f}`;
  const draftKey = addDraftKey(syllabusId, row.id);
  const [restored] = useState(() => readDraft(draftKey, isValues));
  const [v, setV] = useState<EditValues>(() => restored ?? { title: prefillTitle(row), kind: "other", date: "", time: "", allDay: true });
  const [errors, setErrors] = useState<Errors>({});
  const [problem, setProblem] = useState<Problem | null>(null);
  const [busy, setBusy] = useState(false);
  // A non-400 failure moves focus to its message (the student's own press caused it; 14.2 R2b, the Review.tsx
  // dataProblemRef pattern). A counter, so the same failure twice still moves focus.
  const [focusProblem, setFocusProblem] = useState(0);
  const problemRef = useRef<HTMLElement | null>(null);
  const setProblemEl = (el: HTMLElement | null) => {
    problemRef.current = el;
  };
  const pendingKey = useRef<{ key: string; body: string } | null>(null);
  const refs = { title: useRef<HTMLInputElement>(null), date: useRef<HTMLInputElement>(null), time: useRef<HTMLInputElement>(null) };
  const name = row.snippet.text ?? "";

  useEffect(() => {
    refs.title.current?.focus();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- focus once, on open
  }, []);

  useEffect(() => {
    if (focusProblem) problemRef.current?.focus();
  }, [focusProblem]);

  const update = (patch: Partial<EditValues>) => {
    setV((prev) => {
      const next = { ...prev, ...patch };
      writeDraft(draftKey, next);
      return next;
    });
  };

  const blurCheck = (f: Field) => {
    const e = validate(v, true);
    setErrors((prev) => ({ ...prev, [f]: e[f] }));
  };

  async function save() {
    if (busy) return;
    const e = validate(v, true);
    setErrors(e);
    const first = (["title", "date", "time"] as const).find((f) => e[f]);
    if (first) {
      refs[first].current?.focus();
      return;
    }
    const allDay = v.allDay || !v.time;
    const body: ItemCreate = { kind: v.kind, title: v.title.trim().replace(/\s+/g, " "), date: v.date, allDay, time: allDay ? null : v.time };
    const fingerprint = JSON.stringify(body);
    // Reuse the key only to retry the very same body after a network failure; any answered request discarded it.
    const key = pendingKey.current?.body === fingerprint ? pendingKey.current.key : newIdempotencyKey();
    pendingKey.current = { key, body: fingerprint };
    setBusy(true);
    setProblem(null);
    try {
      const item = await api.createItem(syllabusId, body, key);
      pendingKey.current = null;
      removeDraft(draftKey);
      onCreated(item);
    } catch (err) {
      const network = err instanceof ApiError && err.isNetwork;
      if (!network) pendingKey.current = null;
      const invalid = err instanceof ApiError && err.status === 400;
      if (err instanceof ApiError && err.isUnauthenticated) {
        setProblem({ session: true });
        announcer.alert("syllabus-session", ACTION_COPY.sessionHeading);
      } else {
        // 409 invalid_state is both "not ready" and the item caps on the server (one code), so any 409 gets
        // form.conflict, which is true for both, rather than a guess at which one it was (14.2 R2a).
        const text = invalid
          ? E.errors.server
          : network
            ? ACTION_COPY.network
            : err instanceof ApiError && err.status === 429
              ? ACTION_COPY.rateLimited
              : err instanceof ApiError && err.isNotFound
                ? ACTION_COPY.gone
                : err instanceof ApiError && err.status === 409
                  ? U.form.conflict
                  : ACTION_COPY.generic;
        setProblem({ text });
        if (!invalid) announcer.status(text);
      }
      // A 400 is about what was typed, so Title takes focus; anything else is not, so its message does (14.2 R2b).
      if (invalid) refs.title.current?.focus();
      else setFocusProblem((n) => n + 1);
    } finally {
      setBusy(false);
    }
  }

  const cancel = () => {
    removeDraft(draftKey);
    onCancel();
  };

  const onKeyDown = (e: KeyboardEvent) => {
    if (e.key === "Escape") {
      e.preventDefault();
      cancel();
    }
  };

  const described = (f: Field, ...hints: string[]) => [...hints, errors[f] ? id(`${f}-err`) : null].filter(Boolean).join(" ") || undefined;

  return (
    <form
      onSubmit={(e) => {
        e.preventDefault();
        void save();
      }}
      onKeyDown={onKeyDown}
      noValidate
      aria-label={U.form.legend(name)}
      className="sheet mt-1 space-y-4"
      data-unclaimed-form=""
    >
      {restored && <p className="help">{E.kept}</p>}
      <div>
        <label htmlFor={id("title")} className="field-label">
          {E.title}
        </label>
        <input
          ref={refs.title}
          id={id("title")}
          className="field"
          value={v.title}
          onChange={(e) => update({ title: e.target.value })}
          onBlur={() => blurCheck("title")}
          aria-invalid={errors.title ? true : undefined}
          aria-describedby={described("title", id("title-help"))}
        />
        <p id={id("title-help")} className="help mt-1">
          {U.form.titleHelp}
        </p>
        {errors.title && (
          <p id={id("title-err")} className="mt-1 font-semibold text-danger">
            {errors.title}
          </p>
        )}
      </div>
      <div>
        <label htmlFor={id("kind")} className="field-label">
          {E.type}
        </label>
        <select id={id("kind")} className="field max-w-xs" value={v.kind} onChange={(e) => update({ kind: e.target.value as EditableKind })}>
          {(Object.keys(E.kinds) as EditableKind[]).map((k) => (
            <option key={k} value={k}>
              {E.kinds[k]}
            </option>
          ))}
        </select>
      </div>
      <div>
        <label htmlFor={id("date")} className="field-label">
          {E.date}
        </label>
        <p id={id("date-hint")} className="help mt-1">
          {E.dateHint}
        </p>
        <p id={id("date-help")} className="help">
          {U.form.dateHelp}
        </p>
        <input
          ref={refs.date}
          id={id("date")}
          type="date"
          min="2000-01-01"
          max="2100-12-31"
          className="field max-w-xs"
          value={v.date}
          onChange={(e) => update({ date: e.target.value })}
          onBlur={() => blurCheck("date")}
          aria-invalid={errors.date ? true : undefined}
          aria-describedby={described("date", id("date-hint"), id("date-help"))}
        />
        {errors.date && (
          <p id={id("date-err")} className="mt-1 font-semibold text-danger">
            {errors.date}
          </p>
        )}
      </div>
      <div className="flex flex-wrap items-end gap-4">
        <div>
          <label htmlFor={id("time")} className="field-label">
            {E.time}
          </label>
          <input
            ref={refs.time}
            id={id("time")}
            type="time"
            className="field w-40"
            value={v.allDay ? "" : v.time}
            disabled={v.allDay}
            onChange={(e) => update({ time: e.target.value })}
            onBlur={() => blurCheck("time")}
            aria-invalid={errors.time ? true : undefined}
            aria-describedby={described("time")}
          />
        </div>
        <label className="flex min-h-[44px] cursor-pointer items-center gap-2">
          <input type="checkbox" className="h-5 w-5" checked={v.allDay} onChange={(e) => update({ allDay: e.target.checked })} />
          <span>{E.allDay}</span>
        </label>
      </div>
      {errors.time && (
        <p id={id("time-err")} className="font-semibold text-danger">
          {errors.time}
        </p>
      )}
      {problem &&
        ("session" in problem ? (
          <ActionProblem ref={setProblemEl} kind="session" />
        ) : (
          <p ref={setProblemEl} tabIndex={-1} className="font-semibold" data-form-problem="">
            {problem.text}
          </p>
        ))}
      <div className="flex flex-wrap gap-3">
        <button type="submit" className="btn-primary btn-sm" aria-disabled={busy ? true : undefined} aria-busy={busy || undefined}>
          {/* The width stays that of the longer label, so the button does not jump while busy. */}
          <span className="inline-grid">
            <span className="col-start-1 row-start-1">{busy ? U.form.busy : U.form.save}</span>
            <span aria-hidden="true" className="invisible col-start-1 row-start-1">
              {busy ? U.form.save : U.form.busy}
            </span>
          </span>
        </button>
        <button type="button" className="btn-secondary btn-sm" onClick={cancel}>
          {U.form.cancel}
        </button>
      </div>
    </form>
  );
}
