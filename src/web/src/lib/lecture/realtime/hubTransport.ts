import { HttpError, HttpTransportType, HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from "@microsoft/signalr";
import type { HubEvent, JoinRequest, JoinResponse, Seq, TranscriptPage } from "../types";
import { isHubEvent, isJoinResponse, isTranscriptPage } from "./eventGuard";

/** Error whose `code` is a contract ErrorCode (or "connection_failed"). Never carries server text. */
export class LectureHubError extends Error {
  constructor(readonly code: string) {
    super(code);
    this.name = "LectureHubError";
  }
}

const KNOWN_CODES = ["not_found", "validation_failed", "rate_limited", "unauthenticated", "internal_error", "csrf_failed", "consent_required"];

/** B2 notes item 8a: a hub error arrives as a message that ENDS with the code. */
export function hubErrorCode(err: unknown): string {
  if (err instanceof HttpError) {
    if (err.statusCode === 401) return "unauthenticated";
    if (err.statusCode === 403) return "csrf_failed";
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
    await this.teardown();
    const conn = new HubConnectionBuilder()
      .withUrl(this.o.hubUrl ?? "/hubs/lecture", { transport: HttpTransportType.WebSockets, withCredentials: true })
      .configureLogging(LogLevel.None)
      .build();
    conn.on("event", (e: unknown) => {
      if (!isHubEvent(e) || e.sessionId !== this.o.sessionId) return;
      for (const h of [...this.handlers]) h(e);
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

  /** Leaves the session (best effort) and closes the connection. */
  async dispose(): Promise<void> {
    const conn = this.conn;
    if (conn?.state === HubConnectionState.Connected) await conn.invoke("Leave", { sessionId: this.o.sessionId }).catch(() => undefined);
    await this.teardown();
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
