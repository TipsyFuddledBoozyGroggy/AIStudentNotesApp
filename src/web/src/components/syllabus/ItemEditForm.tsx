"use client";

import { useEffect, useId, useRef, useState, type KeyboardEvent } from "react";
import { ApiError } from "@/lib/api/errors";
import type { SyllabusApi } from "@/lib/syllabus/client";
import { isRealDate } from "@/lib/syllabus/format";
import { isLocalDate, isLocalTime } from "@/lib/syllabus/parse";
import type { EditableKind, ItemPatch, SyllabusItem } from "@/lib/syllabus/types";
import { EDIT_COPY as C, ITEM_COPY } from "./copy";
import { isRecord, readDraft, removeDraft, writeDraft } from "./storage";

export interface EditValues {
  title: string;
  kind: EditableKind;
  date: string;
  time: string;
  allDay: boolean;
}

const isValues = (v: unknown): v is EditValues =>
  isRecord(v) && typeof v.title === "string" && typeof v.kind === "string" && typeof v.date === "string" && typeof v.time === "string" && typeof v.allDay === "boolean";

export const draftKeyFor = (item: Pick<SyllabusItem, "syllabusId" | "id">) => `edit.${item.syllabusId}.${item.id}`;
export const hasDraft = (item: Pick<SyllabusItem, "syllabusId" | "id">) => readDraft(draftKeyFor(item), isValues) !== null;

export function valuesOf(item: SyllabusItem): EditValues {
  return {
    title: item.title,
    kind: item.kind === "policy" ? "other" : item.kind,
    date: item.when.date ?? "",
    time: item.when.time ?? "",
    // UX 5.6: the All day box is checked when there is no time.
    allDay: item.when.allDay || item.when.time === null,
  };
}

type Field = "title" | "date" | "time";
type Errors = Partial<Record<Field, string>>;

function validate(v: EditValues, needsDate: boolean): Errors {
  const e: Errors = {};
  const t = v.title.trim();
  if (!t) e.title = C.errors.titleEmpty;
  else if ([...t].length > 200) e.title = C.errors.titleLong;
  if (!v.date) {
    if (needsDate) e.date = C.errors.dateEmpty;
  } else if (!isLocalDate(v.date)) e.date = C.errors.dateFormat;
  else if (!isRealDate(v.date)) e.date = C.errors.dateReal;
  if (!v.allDay && v.time && !isLocalTime(v.time)) e.time = C.errors.time;
  return e;
}

/** Merge-patch of what changed only (absent = unchanged). Title whitespace is collapsed like the server would. */
export function buildPatch(item: SyllabusItem, v: EditValues): ItemPatch {
  const p: ItemPatch = {};
  const title = v.title.trim().replace(/\s+/g, " ");
  if (title !== item.title) p.title = title;
  if (item.kind !== "policy" && v.kind !== item.kind) p.kind = v.kind;
  if (v.date && v.date !== item.when.date) p.date = v.date;
  const time = v.allDay || !v.time ? null : v.time;
  const allDay = v.allDay || !v.time;
  if (allDay !== item.when.allDay) p.allDay = allDay;
  if (time !== item.when.time) {
    p.time = time;
    if (item.when.endTime && (time === null || time >= item.when.endTime)) p.endTime = null;
  }
  return p;
}

export interface ItemEditFormProps {
  api: SyllabusApi;
  item: SyllabusItem;
  /** "date" when opened from "Add date". */
  initialFocus: "title" | "date";
  onSaved(item: SyllabusItem): void;
  onCancel(): void;
  /** The item changed elsewhere (412): the row adopts the latest version; the form keeps the student's values. */
  onConflict(latest: SyllabusItem): void;
}

/**
 * Inline edit (UX 5.6): opens in place, not a modal. Enter saves, Escape cancels. Validation on blur and on save,
 * never per keystroke; errors are text beside the field with aria-describedby, and a failed save focuses the first
 * invalid field. The values are kept locally as a draft until saved or cancelled ("We kept your changes.").
 */
export function ItemEditForm({ api, item, initialFocus, onSaved, onCancel, onConflict }: ItemEditFormProps) {
  const uid = useId();
  const id = (f: string) => `${uid}-${f}`;
  const draftKey = draftKeyFor(item);
  const [restored] = useState(() => readDraft(draftKey, isValues));
  const [v, setV] = useState<EditValues>(() => restored ?? valuesOf(item));
  const [errors, setErrors] = useState<Errors>({});
  const [serverErr, setServerErr] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const refs = { title: useRef<HTMLInputElement>(null), date: useRef<HTMLInputElement>(null), time: useRef<HTMLInputElement>(null) };
  const needsDate = item.when.status === "needs_date";

  useEffect(() => {
    (initialFocus === "date" ? refs.date : refs.title).current?.focus();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- focus once, on open
  }, []);

  const update = (patch: Partial<EditValues>) => {
    setV((prev) => {
      const next = { ...prev, ...patch };
      writeDraft(draftKey, next);
      return next;
    });
  };

  const blurCheck = (f: Field) => {
    const e = validate(v, needsDate);
    setErrors((prev) => ({ ...prev, [f]: e[f] }));
  };

  async function save() {
    if (busy) return;
    const e = validate(v, needsDate);
    setErrors(e);
    const first = (["title", "date", "time"] as const).find((f) => e[f]);
    if (first) {
      refs[first].current?.focus();
      return;
    }
    const patch = buildPatch(item, v);
    if (Object.keys(patch).length === 0) {
      removeDraft(draftKey);
      onCancel();
      return;
    }
    setBusy(true);
    setServerErr(null);
    try {
      const saved = await api.updateItem(item.syllabusId, item.id, patch, item.etag);
      removeDraft(draftKey);
      onSaved(saved);
    } catch (err) {
      if (err instanceof ApiError && err.status === 412) {
        try {
          onConflict(await api.getItem(item.syllabusId, item.id));
        } catch {
          /* keep the form; the message below still applies */
        }
        setServerErr(ITEM_COPY.changedElsewhere);
      } else setServerErr(err instanceof ApiError && err.status === 400 ? C.errors.server : ITEM_COPY.saveFailed);
      refs.title.current?.focus();
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

  const described = (f: Field, hint?: string) => [hint, errors[f] ? id(`${f}-err`) : null].filter(Boolean).join(" ") || undefined;

  return (
    <form
      onSubmit={(e) => {
        e.preventDefault();
        void save();
      }}
      onKeyDown={onKeyDown}
      noValidate
      aria-label={C.legend(item.title)}
      className="sheet mt-1 space-y-4"
    >
      {restored && <p className="help">{C.kept}</p>}
      {item.decision === "approved" && item.syncState === "not_synced" && <p className="help">{C.approvedNote}</p>}
      <div>
        <label htmlFor={id("title")} className="field-label">
          {C.title}
        </label>
        <input
          ref={refs.title}
          id={id("title")}
          className="field"
          value={v.title}
          onChange={(e) => update({ title: e.target.value })}
          onBlur={() => blurCheck("title")}
          aria-invalid={errors.title ? true : undefined}
          aria-describedby={described("title")}
        />
        {errors.title && (
          <p id={id("title-err")} className="mt-1 font-semibold text-danger">
            {errors.title}
          </p>
        )}
      </div>
      <div>
        <label htmlFor={id("kind")} className="field-label">
          {C.type}
        </label>
        <select id={id("kind")} className="field max-w-xs" value={v.kind} onChange={(e) => update({ kind: e.target.value as EditableKind })}>
          {(Object.keys(C.kinds) as EditableKind[]).map((k) => (
            <option key={k} value={k}>
              {C.kinds[k]}
            </option>
          ))}
        </select>
      </div>
      <div>
        <label htmlFor={id("date")} className="field-label">
          {C.date}
        </label>
        <p id={id("date-hint")} className="help mt-1">
          {C.dateHint}
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
          aria-describedby={described("date", id("date-hint"))}
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
            {C.time}
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
          <span>{C.allDay}</span>
        </label>
      </div>
      {errors.time && (
        <p id={id("time-err")} className="font-semibold text-danger">
          {errors.time}
        </p>
      )}
      {serverErr && <p className="font-semibold">{serverErr}</p>}
      <div className="flex flex-wrap gap-3">
        <button type="submit" className="btn-primary btn-sm" aria-disabled={busy ? true : undefined}>
          {C.save}
        </button>
        <button type="button" className="btn-secondary btn-sm" onClick={cancel}>
          {C.cancel}
        </button>
      </div>
    </form>
  );
}
