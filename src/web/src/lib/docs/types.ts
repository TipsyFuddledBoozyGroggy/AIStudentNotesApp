// docs.export.v1 (DRAFT, mock only: the "Demo Doc"). Types mirror src/shared/contracts/docs.export.v1 and
// docs/API_CONTRACTS_DOCS_EXPORT.md. Only the in_app docRef branch exists on the client: the `external` branch is reserved
// for a real provider that is not built or authorised, and the parser refuses it (contract section 7).

export type DocsProvider = "demo_docs";
export type DocsMode = "create" | "update" | "create_another";
export type BlockedReason = "too_large" | "nothing_to_export";
export type NotesSource = "mock" | "model";
export type RecordErrorCode = "provider_unavailable" | "rate_limited" | "provider_rejected" | "reconnect_required" | "doc_changed_outside" | "quota_exceeded";
export type DocsErrorCode =
  | "validation_failed"
  | "unauthenticated"
  | "csrf_failed"
  | "not_found"
  | "invalid_state"
  | "precondition_failed"
  | "rate_limited"
  | "provider_unavailable"
  | "too_large"
  | "unsupported_media_type"
  | "export_in_progress"
  | "retry_limit_reached";

export type DocBlock =
  | { kind: "heading"; level: 1 | 2; text: string }
  | { kind: "bullet"; text: string; citations: { startMs: number }[] }
  | { kind: "paragraph"; text: string };

export interface DocDocument {
  title: string;
  blocks: DocBlock[];
}

/** Where the Demo Doc is: always an app route in v1 (openKind in_app, openUrl null). */
export interface DocRef {
  docId: string;
  provider: DocsProvider;
  label: "Demo Doc";
  version: number;
  openKind: "in_app";
  openPath: string;
  openUrl: null;
}

export interface DocsExisting {
  exportId: string;
  docRef: DocRef;
  lastExportedAt: string;
  upToDate: boolean;
}

export interface DocsPreview {
  previewOnly: true;
  sessionId: string;
  notesId: string;
  notesTruncated: boolean;
  notesSource: NotesSource;
  destination: { provider: DocsProvider; label: "Demo Doc"; isDemo: true };
  document: DocDocument;
  counts: { sections: number; bullets: number; citations: number };
  contentDigest: string;
  existing: DocsExisting | null;
  actions: DocsMode[];
  canWrite: boolean;
  blockedReason: BlockedReason | null;
  generatedAt: string;
}

export interface DocsRecordError {
  code: RecordErrorCode;
  retryable: boolean;
  retryAfterSeconds: number | null;
}

export interface DocsExportRecord {
  exportId: string;
  sessionId: string;
  notesId: string;
  provider: DocsProvider;
  demoDoc: true;
  mode: DocsMode;
  status: "succeeded" | "failed";
  outcome: "created" | "updated" | "unchanged" | "failed";
  contentDigest: string;
  docRef: DocRef | null;
  attempt: number;
  createdAt: string;
  completedAt: string | null;
  error: DocsRecordError | null;
  retry: { allowed: boolean; attemptsLeft: number };
}

export interface DocsState {
  sessionId: string;
  target: DocRef | null;
  lastExport: DocsExportRecord | null;
}

export interface DemoDoc {
  docId: string;
  provider: DocsProvider;
  demoDoc: true;
  document: DocDocument;
  version: number;
  createdAt: string;
  updatedAt: string;
}

export interface DocsProblem {
  type: string;
  title: string;
  status: number;
  code: DocsErrorCode;
  traceId: string;
  detail?: string;
  instance?: string;
  errors?: Record<string, string[]>;
  retryAfterSeconds?: number;
}

/** The confirm body the client sends (docs-export-request). Never text, a title or a document. */
export interface DocsConfirmBody {
  notesId: string;
  contentDigest: string;
  mode: DocsMode;
  expectedExistingVersion: number | null;
  expectedExistingDocId: string | null;
  confirm: { provider: "demo_docs"; acknowledgedDemo: true };
}
