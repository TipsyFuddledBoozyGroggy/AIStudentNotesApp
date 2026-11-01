import { ApiError } from "@/lib/api/errors";
import { AudioStreamer, type AudioLinkEvent, type AudioStreamerDeps, type SocketLike } from "./audioStreamer";

/**
 * Close-code handling of the upstream audio socket (L-HELLO). A ticket failure on `hello` always closes with 4401
 * (C-01), including the race where the course is archived or the session stopped between the ticket being issued and
 * the hello being redeemed. 4401 alone must never mean "signed out": the streamer asks for a fresh ticket, and only
 * that REST answer decides (401 or 403 csrf_failed = signed out, any other 403, 404, 409 = session not available).
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

  it("4401 because the login was revoked: the fresh ticket request is 401, which ends as signed out (a 403 with problem code csrf_failed is the other path)", async () => {
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

  it("PO-20af: 3 consecutive 4401 closes that never reached an ack end the stream as rejected (well before the 60 s watchdog)", async () => {
    // REST keeps issuing tickets (session Active), but every hello is refused with 4401: without a cap this loops forever.
    const h = await openWithHello([{ ok: true }]);
    h.sockets[0].serverClose(4401);
    expect(h.ended()).toEqual([]);
    h.fireRetry();
    await flush();
    h.sockets[1].serverOpen();
    h.sockets[1].serverClose(4401);
    expect(h.ended()).toEqual([]);
    expect(h.streamer.state()).toBe("reconnecting");
    h.fireRetry();
    await flush();
    h.sockets[2].serverOpen();
    h.sockets[2].serverClose(4401);
    expect(h.ended()).toEqual(["rejected"]);
    expect(h.streamer.state()).toBe("ended");
    expect(h.tickets()).toBe(3);
    expect(h.timers).toHaveLength(0); // no retry and no watchdog left behind
    expect(h.events.some((e) => e.kind === "offline_timeout")).toBe(false);
  });

  it("PO-20af: an ack resets the 4401 count, so 4401s separated by working connections keep reconnecting", async () => {
    const h = await openWithHello([{ ok: true }]);
    h.sockets[0].serverClose(4401);
    h.fireRetry();
    await flush();
    h.sockets[1].serverOpen();
    h.sockets[1].serverClose(4401); // two without an ack
    h.fireRetry();
    await flush();
    h.sockets[2].serverOpen();
    h.sockets[2].onmessage?.({ data: JSON.stringify({ type: "ack" }) }); // reached an ack: count back to zero
    expect(h.streamer.state()).toBe("streaming");
    h.sockets[2].serverClose(4401);
    h.fireRetry();
    await flush();
    h.sockets[3].serverOpen();
    h.sockets[3].serverClose(4401); // two again since the ack: still under the cap
    expect(h.ended()).toEqual([]);
    expect(h.streamer.state()).toBe("reconnecting");
    h.fireRetry();
    await flush();
    h.sockets[4].serverOpen();
    h.sockets[4].serverClose(4401); // third since the ack
    expect(h.ended()).toEqual(["rejected"]);
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

/**
 * PO-20ag (T-CSRF-403): a ticket refusal is read by its problem code, not only its status. Only 403 `csrf_failed`
 * means signed out (the same outcome the live-session path gives `csrf_failed`); a 403 `consent_required`
 * (API_CONTRACTS section 2) and any other 403 keep the existing "session not available" outcome.
 */
describe("AudioStreamer ticket 403 by problem code (PO-20ag)", () => {
  async function firstTicketRefused(answer: TicketAnswer) {
    const h = setup([answer]);
    h.streamer.start({});
    await flush();
    return h;
  }

  it("ticket 403 csrf_failed ends as auth (signed out), with no socket and no retry", async () => {
    const h = await firstTicketRefused({ status: 403, code: "csrf_failed" });
    expect(h.ended()).toEqual(["auth"]);
    expect(h.sockets).toHaveLength(0);
    expect(h.tickets()).toBe(1);
    expect(h.timers).toHaveLength(0);
  });

  it("ticket 403 csrf_failed on the reconnect after a 4401 also ends as auth", async () => {
    const h = await openWithHello([{ ok: true }, { status: 403, code: "csrf_failed" }]);
    h.sockets[0].serverClose(4401);
    expect(h.ended()).toEqual([]);
    h.fireRetry();
    await flush();
    expect(h.ended()).toEqual(["auth"]);
  });

  it("ticket 403 consent_required keeps its current handling: session not available (conflict), not signed out", async () => {
    const h = await firstTicketRefused({ status: 403, code: "consent_required" });
    expect(h.ended()).toEqual(["conflict"]);
    expect(h.timers).toHaveLength(0);
  });

  it.each(["http_error", "", "forbidden", "something_new"])("ticket 403 with no or an unknown code (%j) is unchanged: conflict", async (code) => {
    const h = await firstTicketRefused({ status: 403, code });
    expect(h.ended()).toEqual(["conflict"]);
  });

  it("a refusal object with a 403 status and no code property at all is unchanged: conflict", async () => {
    const s = new AudioStreamer({
      getTicket: () => Promise.reject({ status: 403 }),
      createSocket: () => new FakeSocket(),
      startCapture: () => Promise.resolve(() => undefined),
      setTimeout: () => 0,
      clearTimeout: () => undefined,
      now: () => 0,
      random: () => 0.5,
    });
    const ended: string[] = [];
    s.subscribe((e) => e.kind === "ended" && ended.push(e.reason));
    s.start({});
    await flush();
    expect(ended).toEqual(["conflict"]);
  });
});
