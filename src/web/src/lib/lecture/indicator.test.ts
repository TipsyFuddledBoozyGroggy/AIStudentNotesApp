import { describe, expect, it } from "vitest";
import { OFFLINE_LABEL, deriveRecordingIndicator, indicatorAnnouncement, type TrackLike } from "./indicator";

const live: TrackLike = { readyState: "live", enabled: true, muted: false };
const ended: TrackLike = { readyState: "ended", enabled: true, muted: false };
const d = deriveRecordingIndicator;

describe("recording indicator (C-16)", () => {
  it("Active + live track + connected = Recording", () => {
    expect(d({ serverState: "Active", connection: "connected", track: live })).toEqual({ kind: "Recording" });
  });
  it("Paused / Stopped with the track ended agree", () => {
    expect(d({ serverState: "Paused", connection: "connected", track: ended }).kind).toBe("Paused");
    expect(d({ serverState: "Paused", connection: "connected", track: null }).kind).toBe("Paused");
    expect(d({ serverState: "Stopped", connection: "connected", track: null })).toEqual({ kind: "Stopped", failed: false });
    expect(d({ serverState: "Failed", connection: "connected", track: null })).toEqual({ kind: "Stopped", failed: true });
  });
  it.each(["Paused", "Stopped", "Failed"] as const)("server %s but mic live = Mismatch with stopCapture", (s) => {
    expect(d({ serverState: s, connection: "connected", track: live })).toEqual({
      kind: "Mismatch", reason: "track-live-while-server-not-active", stopCapture: true,
    });
  });
  it("still demands stopCapture while disconnected if the mic is hot and last-known state is not Active", () => {
    expect(d({ serverState: "Paused", connection: "disconnected", track: live })).toMatchObject({ kind: "Mismatch", stopCapture: true });
  });
  it("Active but no live or flowing track = Mismatch without stopCapture", () => {
    expect(d({ serverState: "Active", connection: "connected", track: null })).toMatchObject({ kind: "Mismatch", stopCapture: false });
    expect(d({ serverState: "Active", connection: "connected", track: ended })).toMatchObject({ reason: "server-active-without-live-track" });
    expect(d({ serverState: "Active", connection: "connected", track: { ...live, muted: true } })).toMatchObject({ reason: "server-active-track-muted" });
    expect(d({ serverState: "Active", connection: "connected", track: { ...live, enabled: false } })).toMatchObject({ reason: "server-active-track-muted" });
  });
  it("disconnected (or unknown server state) = NotConnected with the exact label", () => {
    const r = d({ serverState: "Active", connection: "disconnected", track: live });
    expect(r).toEqual({ kind: "NotConnected", label: "not connected, audio not being saved", localTrackLive: true });
    expect(OFFLINE_LABEL).toBe("not connected, audio not being saved");
    expect(d({ serverState: null, connection: "connected", track: null }).kind).toBe("NotConnected");
  });
  it("announcement text is distinct per state", () => {
    const texts = [
      indicatorAnnouncement({ kind: "Recording" }),
      indicatorAnnouncement({ kind: "Paused" }),
      indicatorAnnouncement({ kind: "Stopped", failed: false }),
      indicatorAnnouncement({ kind: "Mismatch", reason: "track-live-while-server-not-active", stopCapture: true }),
      indicatorAnnouncement({ kind: "NotConnected", label: OFFLINE_LABEL, localTrackLive: false }),
    ];
    expect(new Set(texts).size).toBe(texts.length);
  });
});
