// TypeScript mirror of src/shared/contracts/syllabus.v1 (FROZEN v1, docs/API_CONTRACTS_P2.md). The schemas are the
// authority; parse.ts checks every field the UI reads and copies only known fields, so unknown members never reach
// a component. Ids are opaque: never parse them.

export type ExtractionStatus = "queued" | "extracting" | "proposing" | "ready" | "needs_text" | "partial" | "failed";
export type StatusReason =
  | "no_text_layer"
  | "unreadable_file"
  | "password_protected"
  | "too_long"
  | "budget_limited"
  | "provider_unavailable"
  | "timeout"
  | "invalid_output"
  | "disabled";
export type ItemKind = "assignment" | "exam" | "reading" | "office_hours" | "policy" | "other";
export type EditableKind = Exclude<ItemKind, "policy">;
export type Confidence = "looks_right" | "probably_right" | "please_check";
export type Decision = "undecided" | "approved" | "skipped";
export type SyncState = "not_synced" | "synced" | "changed_since_sync" | "sync_failed";
export type Origin = "ai" | "student";
export type WhenStatus = "resolved" | "needs_date" | "not_applicable";
export type Derivation = "explicit_full" | "year_from_term" | "relative_to_anchor" | "student_entered";
export type UnknownReason =
  | "not_stated"
  | "tba"
  | "relative_unresolvable"
  | "conflicting"
  | "ambiguous_format"
  | "table_unreadable"
  | "date_not_verifiable";
export type TimezoneSource = "stated_in_source" | "course_default" | "student";
export type Weekday = "MO" | "TU" | "WE" | "TH" | "FR" | "SA" | "SU";
export type Flag =
  | "year_inferred"
  | "relative_date"
  | "timezone_assumed"
  | "time_missing"
  | "conflicting_sources"
  | "tentative_language"
  | "table_layout_risk"
  | "section_ambiguous"
  | "range_end_inferred"
  | "weekday_mismatch"
  | "date_not_verifiable"
  | "until_from_term"
  | "low_text_quality"
  | "duplicate_candidate"
  | "outside_term"
  | "dst_ambiguous"
  | "injection_suspect"
  | "supersession_cue";

export const IN_PROGRESS: ReadonlySet<ExtractionStatus> = new Set(["queued", "extracting", "proposing"]);

export interface Syllabus {
  id: string;
  courseId: string;
  title: string | null;
  inputKind: "upload" | "paste";
  fileType: "pdf" | "docx" | "txt" | null;
  status: ExtractionStatus;
  statusReason: StatusReason | null;
  pollAfterSeconds: number | null;
  aiSource: "mock" | "model";
  demoSource: boolean;
  demoCalendar: true;
  originalRetained: false;
  document: {
    pageCount: number | null;
    charCount: number;
    unreadablePages: number[];
    hiddenContentRemoved: number;
    injectionSuspect: boolean;
    hiddenContentDropped: boolean;
  };
  unprocessedRanges: { fromPage: number; toPage: number; reason: StatusReason }[];
  context: { termStart: string | null; termEnd: string | null; defaultTimezone: string };
  counts: {
    total: number;
    undecided: number;
    approved: number;
    skipped: number;
    needsDate: number;
    synced: number;
    droppedInvalid: number;
    unclaimedDates: number;
  };
  etag: string;
  createdAt: string;
  updatedAt: string;
  batchApproveAllowed: boolean;
  retention: { canonicalTextPurged: boolean; canonicalTextPurgeAt: string | null };
}

export interface Snippet {
  /** null only after the retention purge, together with context, start and end. */
  text: string | null;
  start: number | null;
  end: number | null;
  page: number | null;
  precision: "exact" | "line";
  context: { text: string; start: number; end: number } | null;
}

export interface ItemWhen {
  status: WhenStatus;
  written: { dateText: string | null; timeText: string | null } | null;
  date: string | null;
  endDate: string | null;
  time: string | null;
  endTime: string | null;
  allDay: boolean;
  timezone: string | null;
  timezoneSource: TimezoneSource | null;
  derivation: Derivation | null;
  unknownReason: UnknownReason | null;
  alternatives: { date: string; time: string | null }[];
}

export interface Recurrence {
  pattern: "weekly";
  byDay: Weekday[];
  startTime: string;
  endTime: string | null;
  from: string;
  until: string;
  exceptions: string[];
}

export interface SyllabusItem {
  id: string;
  syllabusId: string;
  courseId: string;
  itemKey: string;
  origin: Origin;
  kind: ItemKind;
  title: string;
  notes: string | null;
  when: ItemWhen;
  recurrence: Recurrence | null;
  confidence: Confidence;
  confidenceReason: string;
  flags: Flag[];
  snippet: Snippet | null;
  edited: boolean;
  decision: Decision;
  syncState: SyncState;
  syncedRevision: number | null;
  revision: number;
  etag: string;
  createdAt: string;
  updatedAt: string;
}

export interface Page<T> {
  items: T[];
  nextCursor: string | null;
}

/** Members shared by the JSON (paste) body and the multipart `metadata` part (syllabus-create-request). */
export interface CreateMeta {
  /**
   * Term dates are sent both or neither: the contract allows either alone, but the real server answers 400
   * `termStart: required` unless both are present (recorded as a contract/server mismatch in the F2 hand-off).
   */
  termStart?: string | null;
  termEnd?: string | null;
  defaultTimezone: string;
  noticeVersion: string;
  /** Advisory only: the server records its own receipt time as notice.attestedAt (R3-11). */
  acceptedAt: string;
  title?: string | null;
}

/** POST /courses/{courseId}/syllabi, JSON (paste) form. */
export interface CreateFromTextInput extends CreateMeta {
  text: string;
}

/** POST /courses/{courseId}/syllabi, multipart form: exactly one `file` part plus one `metadata` part (no `text`). */
export interface CreateFromFileInput extends CreateMeta {
  file: Blob;
  /** Sent as the part filename only so the server can compare the extension; the server discards it (5.1 item 1). */
  fileName: string;
}

/** JSON merge-patch: absent = unchanged; null only where the schema allows it. */
export interface ItemPatch {
  kind?: EditableKind;
  title?: string;
  notes?: string | null;
  date?: string;
  endDate?: string | null;
  time?: string | null;
  endTime?: string | null;
  allDay?: boolean;
  timezone?: string;
}

export type BatchAction = "approve" | "skip" | "undecide";
export type ItemErrorCode =
  | "item_needs_date"
  | "item_not_syncable"
  | "item_not_approved"
  | "batch_not_eligible"
  | "acknowledgement_required"
  | "precondition_failed"
  | "not_found"
  | "provider_unavailable"
  | "invalid_state"
  | "event_limit_reached";

export interface BatchResult {
  results: { itemId: string; outcome: "applied" | "unchanged" | "rejected"; errorCode: ItemErrorCode | null; item: SyllabusItem | null }[];
  counts: { applied: number; unchanged: number; rejected: number };
}

export type RunStatus = "all_succeeded" | "partial" | "failed" | "nothing_to_do";
export type SyncOutcome = "created" | "updated" | "unchanged" | "failed" | "not_eligible";

/** syllabus-calendar-sync-result. A replay (Idempotency-Replayed) is history: callers re-read items for state. */
export interface SyncResult {
  syncId: string;
  syllabusId: string;
  status: RunStatus;
  completedAt: string;
  results: { itemId: string; itemKey: string; outcome: SyncOutcome; errorCode: ItemErrorCode | null; eventCount: number; syncState: SyncState }[];
  counts: { created: number; updated: number; unchanged: number; failed: number; notEligible: number };
}

export interface RemoveResult {
  syllabusId: string;
  status: RunStatus;
  results: { itemId: string; outcome: "removed" | "not_present" | "failed"; errorCode: ItemErrorCode | null; syncState: SyncState }[];
  counts: { removed: number; notPresent: number; failed: number };
}
