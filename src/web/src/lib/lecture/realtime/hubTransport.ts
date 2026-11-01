import { HttpError, HttpTransportType, HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from "@microsoft/signalr";
import type { HubEvent, JoinRequest, JoinResponse, Seq, TranscriptPage } from "../types";
import { parseHubEvent, isJoinResponse, isTranscriptPage } from "./eventGuard";

/** Error whose `code` is a contract ErrorCode (or "connection_failed"). Never carries server text. */
export class LectureHubError extends Error {
  constructor(readonly code: string) {
    super(code);
    this.name = "LectureHubError";
  }
}

const KNOWN_CODES = ["not_found", "validation_failed", "rate_limited", "unauthenticated", "internal_error", "csrf_failed", "consent_required"];

/**
 * PO-20ag: a refused negotiate / upgrade is a SignalR HttpError whose message is the response body text, here the
 * ProblemDetails JSON, followed by `: Status code '403'` (HttpError's constructor appends it). Returns the body's
 * `code` when it is a contract code, else null. Never surfaces server text.
 */
function problemCodeOf(message: string): string | null {
  const body = message.replace(/: Status code '\d+'$/, "");
  try {
    const p: unknown = JSON.parse(body);
    const code = typeof p === "object" && p !== null ? (p as { code?: unknown }).code : undefined;
    return typeof code === "string" && KNOWN_CODES.includes(code) ? code : null;
  } catch {
    return null;
  }
}

/** B2 notes item 8a: a hub error arrives as a message that ENDS with the code. */
export function hubErrorCode(err: unknown): string {
  if (err instanceof HttpError) {
    if (err.statusCode === 401) return "unauthenticated";
    // PO-20ag: read the problem code (the server's only 403 here is csrf_failed); no readable contract code keeps the
    // previous status mapping.
    if (err.statusCode === 403) return problemCodeOf(err.message) ?? "csrf_failed";
    if (err.statusCode === 404) return "not_found";
    return "connection_failed";
  }
  const msg = err instanceof Error ? err.message : "";
  for (const c of KNOWN_CODES) if (msg.endsWith(c)) return c;
  return "connection_failed";
}

export interface HubTransportOptions {
  sessionId: string;
  /** Same-origin path; proxied to the API. */
  hubUrl?: string;
  fetchTranscript(afterSeq: Seq | null, limit: number): Promise<unknown>;
}

const PING_MS = 30_000;

/**
 * lecture.v1 over SignalR (WebSockets only, cookie auth, same-origin so Origin matches the allow-list).
 * Reconnect policy lives in ResumeController, not here: automatic reconnect is off. Every connect builds a fresh
 * connection. Incoming events are structurally checked; anything malformed is dropped.
 */
export class HubTransport {
  private conn: HubConnection | null = null;
  private handlers = new Set<(e: HubEvent) => void>();
  private dropHandlers = new Set<() => void>();
  private pingTimer: ReturnType<typeof setInterval> | null = null;
  private epoch = 0;
  private readonly onOffline = () => void this.conn?.stop();

  constructor(private readonly o: HubTransportOptions) {}

  subscribe = (h: (e: HubEvent) => void): (() => void) => {
    this.handlers.add(h);
    return () => this.handlers.delete(h);
  };

  onDisconnect = (cb: () => void): (() => void) => {
    this.dropHandlers.add(cb);
    return () => this.dropHandlers.delete(cb);
  };

  connect = async (): Promise<void> => {
    // Each connect and each dispose starts a new epoch. A connect whose epoch is no longer current (a later connect or
    // a dispose ran while it awaited) closes its own connection and rejects: it never becomes `this.conn`, so it can
    // neither orphan a live socket (React StrictMode's run / cleanup / run used to leave two open, one never joined,
    // counted against the per-user hub cap) nor be joined by a newer caller.
    const epoch = ++this.epoch;
    await this.teardown();
    if (epoch !== this.epoch) throw new LectureHubError("connection_failed");
    const conn = new HubConnectionBuilder()
      .withUrl(this.o.hubUrl ?? "/hubs/lecture", { transport: HttpTransportType.WebSockets, withCredentials: true })
      .configureLogging(LogLevel.None)
      .build();
    conn.on("event", (e: unknown) => {
      if (this.conn !== conn) return; // a superseded or disposed connection's late frames belong to no live generation
      const ev = parseHubEvent(e);
      if (!ev || ev.sessionId !== this.o.sessionId) return;
      for (const h of [...this.handlers]) h(ev);
    });
    conn.onclose(() => {
      if (this.conn !== conn) return;
      this.stopPing();
      for (const d of [...this.dropHandlers]) d();
    });
    this.conn = conn;
    try {
      await conn.start();
    } catch (e) {
      if (this.conn === conn) this.conn = null;
      throw new LectureHubError(hubErrorCode(e));
    }
    if (epoch !== this.epoch || this.conn !== conn) {
      if (this.conn === conn) this.conn = null;
      await conn.stop().catch(() => undefined);
      throw new LectureHubError("connection_failed");
    }
    this.pingTimer = setInterval(() => void conn.invoke("Ping").catch(() => undefined), PING_MS);
    window.addEventListener("offline", this.onOffline); // browsers can be slow to notice a dead socket
  };

  join = async (req: JoinRequest): Promise<JoinResponse> => {
    const conn = this.conn;
    if (!conn || conn.state !== HubConnectionState.Connected) throw new LectureHubError("connection_failed");
    try {
      const res: unknown = await conn.invoke("Join", req);
      if (!isJoinResponse(res)) throw new LectureHubError("connection_failed");
      return res;
    } catch (e) {
      throw e instanceof LectureHubError ? e : new LectureHubError(hubErrorCode(e));
    }
  };

  fetchTranscript = async (afterSeq: Seq | null, limit: number): Promise<TranscriptPage> => {
    const page = await this.o.fetchTranscript(afterSeq, limit);
    if (!isTranscriptPage(page)) throw new LectureHubError("connection_failed");
    return page;
  };

  /**
   * Leaves the session (best effort) and closes the connection that is current NOW. Synchronously detaches it and
   * supersedes any connect still in flight, so the asynchronous tail (Leave, stop) can only touch that one connection:
   * a connect made after this call (a StrictMode remount, or any remount reusing this transport) is never closed by it.
   */
  async dispose(): Promise<void> {
    this.epoch++;
    this.stopPing();
    const conn = this.conn;
    this.conn = null;
    if (!conn) return;
    if (conn.state === HubConnectionState.Connected) await conn.invoke("Leave", { sessionId: this.o.sessionId }).catch(() => undefined);
    await conn.stop().catch(() => undefined);
  }

  private stopPing(): void {
    if (this.pingTimer !== null) clearInterval(this.pingTimer);
    this.pingTimer = null;
    window.removeEventListener("offline", this.onOffline);
  }

  private async teardown(): Promise<void> {
    this.stopPing();
    const c = this.conn;
    this.conn = null;
    if (c) await c.stop().catch(() => undefined);
  }
}
