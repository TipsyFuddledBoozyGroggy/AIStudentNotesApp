import { act, render } from "@testing-library/react";
import { StrictMode } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError, type ApiClient } from "@/lib/api";
import { AnnouncerProvider } from "./Announcer";
import { FakeMicSource } from "./micSource";
import { createRealRuntime } from "./realRuntime";
import { DEFAULT_SETTINGS } from "./settings";
import { useLectureSession, type LectureSession } from "./useLectureSession";

// Slice 8x (1): the reopen route's hub race. The real HubTransport, createRealRuntime and useLectureSession run here over a
// fake SignalR layer that records every connect / start / invoke / stop in order and models the server's C-26 cap (at most
// MAX connections per user; the newest is refused by closing it right after the handshake, as LectureHub.OnConnectedAsync
// does). Root cause confirmed in a real browser before this test was written (hub frames logged per socket on
// /lectures/{id} under `next dev`): React StrictMode's run / cleanup / run called connect() twice on the shared transport
// before either had a connection, so the first run's dispose() found nothing to close and the page kept TWO open sockets,
// one joined and one orphaned (never joined, never closed). With another tab (or the previous load's sockets not yet
// released) the joining one was the 4th connection and was refused on every retry: the bar stayed "Not recording".

const h = vi.hoisted(() => {
  type Deferred<T> = { promise: Promise<T>; resolve: (v: T) => void; reject: (e: unknown) => void };
  const defer = <T,>(): Deferred<T> => {
    let resolve!: (v: T) => void;
    let reject!: (e: unknown) => void;
    const promise = new Promise<T>((a, b) => ((resolve = a), (reject = b)));
    return { promise, resolve, reject };
  };
  const server = { max: 3, occupied: 0, log: [] as string[], conns: [] as FakeConn[], autoJoin: true, holdLeave: false };
  const live = () => server.conns.filter((c) => c.state === "Connected").length;
  class FakeConn {
    readonly n = server.conns.length + 1;
    state: "Disconnected" | "Connecting" | "Connected" = "Disconnected";
    joins: Deferred<unknown>[] = [];
    leave: Deferred<unknown> | null = null;
    private close: (() => void) | null = null;
    private ev: ((e: unknown) => void) | null = null;
    constructor() {
      server.conns.push(this);
    }
    on(name: string, cb: (e: unknown) => void) {
      if (name === "event") this.ev = cb;
    }
    onclose(cb: () => void) {
      this.close = cb;
    }
    async start() {
      server.log.push(`start#${this.n}`);
      this.state = "Connecting";
      await Promise.resolve();
      if (this.state !== "Connecting") throw new Error("stopped during start");
      this.state = "Connected";
      if (live() + server.occupied > server.max) {
        // C-26: the server accepts the handshake, then aborts the newest connection.
        server.log.push(`refused#${this.n}`);
        queueMicrotask(() => this.drop());
      }
    }
    async stop() {
      if (this.state === "Disconnected") return;
      server.log.push(`stop#${this.n}`);
      this.drop();
    }
    drop() {
      if (this.state === "Disconnected") return;
      this.state = "Disconnected";
      for (const j of this.joins) j.reject(new Error("Invocation canceled due to the underlying connection being closed."));
      this.leave?.reject(new Error("closed"));
      this.close?.();
    }
    invoke(method: string, arg: unknown) {
      if (method === "Ping") return Promise.resolve();
      server.log.push(`${method}#${this.n}`);
      if (this.state !== "Connected") return Promise.reject(new Error("not connected"));
      if (method === "Leave") {
        this.leave = defer();
        if (!server.holdLeave) this.leave.resolve(null);
        return this.leave.promise;
      }
      const d = defer<unknown>();
      this.joins.push(d);
      if (server.autoJoin) queueMicrotask(() => this.answerJoin());
      void arg;
      return d.promise;
    }
    /** The server's Join result on THIS connection: replay one session.state event, then the response. */
    answerJoin() {
      const d = this.joins.at(-1);
      if (!d || this.state !== "Connected") return;
      this.ev?.({ v: 1, type: "session.state", sessionId: "ses_hub", seq: "1", ts: "2026-10-31T10:00:00Z", payload: { state: "Stopped", reason: "user" } });
      d.resolve({ state: "Stopped", currentSeq: "1", currentSummaryRevision: null, replay: { truncated: false, fromSeq: "1", toSeq: "1" } });
    }
  }
  return { server, FakeConn };
});

vi.mock("@microsoft/signalr", () => {
  class HubConnectionBuilder {
    withUrl() {
      return this;
    }
    configureLogging() {
      return this;
    }
    build() {
      h.server.log.push(`connect#${h.server.conns.length + 1}`);
      return new h.FakeConn();
    }
  }
  class HttpError extends Error {
    constructor(
      message: string,
      readonly statusCode: number,
    ) {
      super(message);
    }
  }
  return {
    HubConnectionBuilder,
    HttpError,
    HttpTransportType: { WebSockets: 1 },
    HubConnectionState: { Connected: "Connected", Disconnected: "Disconnected", Connecting: "Connecting" },
    LogLevel: { None: 6 },
  };
});

const fakeApi = {
  getSummary: () => Promise.reject(new ApiError(404, "not_found", "none")),
  getNotes: () => Promise.reject(new ApiError(409, "invalid_state", "none")),
  getTranscriptPage: () => Promise.resolve({ events: [], hasMore: false }),
} as unknown as ApiClient;

const settle = async (ms = 30) => {
  for (let i = 0; i < 3; i++) await act(async () => void (await new Promise((r) => setTimeout(r, ms / 3))));
};
const openConns = () => h.server.conns.filter((c) => c.state === "Connected").map((c) => c.n);

function mount(strict: boolean, runtime = createRealRuntime({ api: fakeApi }, { id: "ses_hub", demoSource: false })) {
  const mic = new FakeMicSource("ok");
  const result = { current: null as unknown as LectureSession };
  function Probe() {
    result.current = useLectureSession({ runtime, track: null, mic, settings: DEFAULT_SETTINGS });
    return null;
  }
  // Rendered with `render`, not renderHook: a StrictMode element passed as renderHook's wrapper does not double-run
  // effects (checked in this vitest setup), a StrictMode root element does (run, cleanup, run, as `next dev` does).
  const tree = (
    <AnnouncerProvider>
      <Probe />
    </AnnouncerProvider>
  );
  const r = render(strict ? <StrictMode>{tree}</StrictMode> : tree);
  return { result, unmount: r.unmount, runtime };
}

beforeEach(() => {
  Object.assign(h.server, { max: 3, occupied: 0, log: [], conns: [], autoJoin: true, holdLeave: false });
});
afterEach(() => vi.useRealTimers());

describe("reopen route hub lifecycle (StrictMode run / cleanup / run on one shared HubTransport)", () => {
  it("StrictMode: one connection is opened and kept, it is the one that joins, and the bar shows Stopped", async () => {
    const { result } = mount(true);
    await settle();
    // Order: both effect runs connect; the superseded run's connection never starts (or is stopped); one Join, on the kept one.
    const joins = h.server.log.filter((l) => l.startsWith("Join#"));
    expect(joins).toHaveLength(1);
    expect(openConns()).toEqual([Number(joins[0].slice(5))]);
    expect(result.current.connection).toBe("connected");
    expect(result.current.display.kind).toBe("Stopped");
  });

  it("StrictMode with two hub connections already held by this user (another tab): still joins and shows Stopped", async () => {
    h.server.occupied = 2; // the cap is 3: there is room for exactly one connection from this page
    const { result } = mount(true);
    await settle();
    expect(h.server.log.filter((l) => l.startsWith("refused#"))).toEqual([]);
    expect(openConns()).toHaveLength(1);
    expect(result.current.display.kind).toBe("Stopped");
  });

  // A guard for the new dispose semantics (it also passed on the old transport, whose next connect happened to stop the
  // old connection first): the first three tests are the ones that failed before the fix.
  it("a remount while the first connection's Join is in flight: the late Join result there is ignored, the late dispose (Leave) never closes the new connection", async () => {
    h.server.autoJoin = false;
    h.server.holdLeave = true;
    const first = mount(false);
    await settle();
    expect(h.server.log).toEqual(["connect#1", "start#1", "Join#1"]);
    // Unmount (cleanup: rc.stop + runtime.dispose -> Leave on #1 is held open) and mount again with the SAME runtime.
    first.unmount();
    const second = mount(false, first.runtime);
    await settle();
    // The first connection's Join result arrives after its generation was disposed: nothing may come of it.
    h.server.conns[0].answerJoin();
    await settle();
    expect(second.result.current.display.kind).not.toBe("Stopped"); // only a Join on the live generation may set state
    const c2 = h.server.conns.find((c) => c.n !== 1 && c.state === "Connected")!;
    expect(c2).toBeDefined();
    c2.answerJoin();
    await settle();
    expect(second.result.current.display.kind).toBe("Stopped");
    // Now the old dispose's Leave completes: it may close #1 only.
    h.server.conns[0].leave?.resolve(null);
    await settle();
    expect(openConns()).toEqual([c2.n]);
    expect(h.server.log.filter((l) => l === `stop#${c2.n}`)).toEqual([]);
    expect(second.result.current.connection).toBe("connected");
    expect(second.result.current.display.kind).toBe("Stopped");
  });

  it("unmounting for good closes the only connection (Leave, then stop) and leaves nothing open", async () => {
    const { result, unmount } = mount(true);
    await settle();
    expect(result.current.display.kind).toBe("Stopped");
    const kept = openConns()[0];
    unmount();
    await settle();
    expect(openConns()).toEqual([]);
    expect(h.server.log.slice(-2)).toEqual([`Leave#${kept}`, `stop#${kept}`]);
  });
});
