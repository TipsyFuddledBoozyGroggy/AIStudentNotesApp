// Pure announcement logic for the three live regions (UX section 0). No React, injectable clock.

export const MIN_SPACING_MS = 2000;
export const DEDUPE_WINDOW_MS = 10_000;
export const FINALS_INTERVAL_MS = 30_000;
export const FINALS_MAX_LINES = 5;
export const TLDR_INTERVAL_MS = 60_000;

export interface Emitted {
  id: number;
  text: string;
}

export interface QueueDeps {
  now(): number;
  setTimeout(fn: () => void, ms: number): unknown;
  clearTimeout(h: unknown): void;
  onStatus(e: Emitted): void;
  onAlert(e: Emitted): void;
}

/**
 * Message queue: at least 2 s between announcements, alert preempts status, identical text within
 * 10 s is dropped, and an alert keyed to an episode fires once until clearEpisode(key).
 */
export class AnnouncementQueue {
  private queue: { channel: "status" | "alert"; text: string }[] = [];
  private lastEmitAt = -Infinity;
  private recent = new Map<string, number>();
  private episodes = new Set<string>();
  private timer: unknown = null;
  private id = 0;

  constructor(private readonly d: QueueDeps) {}

  status(text: string): void {
    this.enqueue("status", text);
  }

  /** Fires at most once per episode `key`; repeats are suppressed until clearEpisode(key). */
  alert(key: string, text: string): void {
    if (this.episodes.has(key)) return;
    this.episodes.add(key);
    this.enqueue("alert", text);
  }

  clearEpisode(key: string): void {
    this.episodes.delete(key);
  }

  dispose(): void {
    if (this.timer !== null) this.d.clearTimeout(this.timer);
    this.timer = null;
    this.queue = [];
  }

  private enqueue(channel: "status" | "alert", text: string): void {
    const t = this.d.now();
    const last = this.recent.get(text);
    if (last !== undefined && t - last < DEDUPE_WINDOW_MS) return;
    if (this.queue.some((q) => q.text === text)) return;
    this.queue.push({ channel, text });
    this.pump();
  }

  private pump(): void {
    if (this.timer !== null || this.queue.length === 0) return;
    const wait = this.lastEmitAt + MIN_SPACING_MS - this.d.now();
    if (wait > 0) {
      this.timer = this.d.setTimeout(() => {
        this.timer = null;
        this.pump();
      }, wait);
      return;
    }
    const idx = this.queue.findIndex((q) => q.channel === "alert");
    const [m] = this.queue.splice(idx >= 0 ? idx : 0, 1);
    const t = this.d.now();
    this.lastEmitAt = t;
    this.recent.set(m.text, t);
    const e = { id: ++this.id, text: m.text };
    if (m.channel === "alert") this.d.onAlert(e);
    else this.d.onStatus(e);
    this.pump();
  }
}

/** Batches new finals for the opt-in finals region: at most one batch per 30 s, max 5 lines. */
export class FinalsBatcher {
  private pending: string[] = [];
  private lastAt = -Infinity;

  constructor(
    private readonly intervalMs = FINALS_INTERVAL_MS,
    private readonly maxLines = FINALS_MAX_LINES,
  ) {}

  add(lines: string[]): void {
    this.pending.push(...lines);
  }

  /** Milliseconds until a batch may be emitted, or null when nothing is pending. */
  dueIn(nowMs: number): number | null {
    if (this.pending.length === 0) return null;
    return Math.max(0, this.lastAt + this.intervalMs - nowMs);
  }

  take(nowMs: number): string | null {
    if (this.pending.length === 0 || this.dueIn(nowMs) !== 0) return null;
    const n = this.pending.length;
    const shown = this.pending.slice(0, this.maxLines);
    this.pending = [];
    this.lastAt = nowMs;
    return [`${n} new ${n === 1 ? "line" : "lines"}`, ...shown].join(". ");
  }
}
