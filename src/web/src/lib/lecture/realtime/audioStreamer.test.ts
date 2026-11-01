import { ApiError } from "@/lib/api/errors";
import { AudioStreamer, type AudioLinkEvent, type AudioStreamerDeps, type SocketLike } from "./audioStreamer";

/**
 * Close-code handling of the upstream audio socket (L-HELLO). A ticket failure on `hello` always closes with 4401
 * (C-01), including the race where the course is archived or the session stopped between the ticket being issued and
 * the hello being redeemed. 4401 alone must never mean "signed out": the streamer asks for a fresh ticket, and only
 * that REST answer decides (401 = signed out, 403/404/409 = session not available).
 */

class FakeSocket implements SocketLike {
  readyState = 0;
  bufferedAmount = 0;
  binaryType = "";
  onopen: (() => void) | null = null;
  onmessage: ((e: { data: unknown }) => void) | null = null;
  onclose: ((e: { code: number }) => void) | null = null;
  onerror: (() => void) | null = null;
  sent: unknown[] = [];
  send(data: string | ArrayBufferView | ArrayBuffer): void {
    this.sent.push(data);
  }
  close(): void {
    this.readyState = 3;
  }
  serverOpen(): void {
    this.readyState = 1;
    this.onopen?.();
  }
  serverClose(code: number): void {
    this.readyState = 3;
    this.onclose?.({ code });
  }
}

type TicketAnswer = { ok: true } | { status: number; code: string };

function setup(answers: TicketAnswer[]) {
  const sockets: FakeSocket[] = [];
  const timers: { fn: () => void; ms: number; id: number }[] = [];
  let nextId = 1;
  let ticketCalls = 0;
  const deps: AudioStreamerDeps = {
    getTicket: () => {
      const a = answers[Math.min(ticketCalls, answers.length - 1)];
      ticketCalls++;
      return "ok" in a ? Promise.resolve({ ticket: `t${ticketCalls}` }) : Promise.reject(new ApiError(a.status, a.code, a.code));
    },
    createSocket: () => {
      const s = new FakeSocket();
      sockets.push(s);
      return s;
    },
    startCapture: () => Promise.resolve(() => undefined),
    setTimeout: (fn, ms) => {
      const id = nextId++;
      timers.push({ fn, ms, id });
      return id;
    },
    clearTimeout: (h) => {
      const i = timers.findIndex((t) => t.id === h);
      if (i >= 0) timers.splice(i, 1);
    },
    now: () => 0,
    random: () => 0.5,
  };
  const streamer = new AudioStreamer(deps);
  const events: AudioLinkEvent[] = [];
  streamer.subscribe((e) => events.push(e));
  /** Runs the pending reconnect: after a close the drain is cleared, so the only timer left besides the 60 s watchdog. */
  const fireRetry = () => {
    const t = timers.find((x) => x.ms < 60_000);
    if (!t) throw new Error("no retry scheduled");
    timers.splice(timers.indexOf(t), 1);
    t.fn();
  };
  const ended = () => events.filter((e): e is Extract<AudioLinkEvent, { kind: "ended" }> => e.kind === "ended").map((e) => e.reason);
  return { streamer, sockets, events, ended, fireRetry, tickets: () => ticketCalls, timers };
}

const flush = () => new Promise<void>((r) => setTimeout(r, 0));

/** Start, get the first ticket, open the socket and send hello (no ack yet: the server has not redeemed it). */
async function openWithHello(answers: TicketAnswer[]) {
  const h = setup(answers);
  h.streamer.start({});
  await flush();
  expect(h.sockets).toHaveLength(1);
  h.sockets[0].serverOpen();
  expect(JSON.parse(h.sockets[0].sent[0] as string)).toMatchObject({ type: "hello", ticket: "t1" });
  return h;
}

describe("AudioStreamer close codes (L-HELLO)", () => {
  it("4401 right after hello because the session was stopped or the course archived: session not available, not signed out", async () => {
    const h = await openWithHello([{ ok: true }, { status: 409, code: "invalid_state" }]);
    h.sockets[0].serverClose(4401);
    // Not ended yet, and never "auth" from the close code alone: it reconnects with a fresh ticket.
    expect(h.ended()).toEqual([]);
    expect(h.streamer.state()).toBe("reconnecting");
    h.fireRetry();
    await flush();
    expect(h.tickets()).toBe(2);
    expect(h.ended()).toEqual(["conflict"]);
    expect(h.streamer.state()).toBe("ended");
  });

  it("4401 after hello because the course was purged (ticket now 404): session not available", async () => {
    const h = await openWithHello([{ ok: true }, { status: 404, code: "not_found" }]);
    h.sockets[0].serverClose(4401);
    h.fireRetry();
    await flush();
    expect(h.ended()).toEqual(["conflict"]);
  });

  it("4401 because the login was revoked: the fresh ticket request is 401, which is the only path to signed out", async () => {
    const h = await openWithHello([{ ok: true }, { status: 401, code: "unauthenticated" }]);
    h.sockets[0].serverClose(4401);
    expect(h.ended()).toEqual([]);
    h.fireRetry();
    await flush();
    expect(h.ended()).toEqual(["auth"]);
  });

  it("4401 with the session still Active (expired or replayed ticket): reconnects with a new ticket and keeps streaming", async () => {
    const h = await openWithHello([{ ok: true }]);
    h.sockets[0].serverClose(4401);
    h.fireRetry();
    await flush();
    expect(h.sockets).toHaveLength(2);
    h.sockets[1].serverOpen();
    expect(JSON.parse(h.sockets[1].sent[0] as string)).toMatchObject({ type: "hello", ticket: "t2" });
    h.sockets[1].onmessage?.({ data: JSON.stringify({ type: "ack" }) });
    expect(h.streamer.state()).toBe("streaming");
    expect(h.ended()).toEqual([]);
  });

  it("4409 (cut off after the ticket was accepted) ends at once as session not available, without a new ticket", async () => {
    const h = await openWithHello([{ ok: true }]);
    h.sockets[0].onmessage?.({ data: JSON.stringify({ type: "ack" }) });
    h.sockets[0].serverClose(4409);
    expect(h.ended()).toEqual(["conflict"]);
    expect(h.tickets()).toBe(1);
  });

  it("1007/1009 end as rejected; 1006 and 1011 reconnect", async () => {
    for (const code of [1007, 1009]) {
      const h = await openWithHello([{ ok: true }]);
      h.sockets[0].serverClose(code);
      expect(h.ended()).toEqual(["rejected"]);
    }
    for (const code of [1006, 1011]) {
      const h = await openWithHello([{ ok: true }]);
      h.sockets[0].serverClose(code);
      expect(h.ended()).toEqual([]);
      expect(h.streamer.state()).toBe("reconnecting");
    }
  });
});
