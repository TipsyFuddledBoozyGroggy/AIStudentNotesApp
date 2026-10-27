/** mm:ss (or h:mm:ss) for transcript timestamps and elapsed time. */
export function formatMs(ms: number): string {
  const total = Math.max(0, Math.floor(ms / 1000));
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  const mm = String(m).padStart(2, "0");
  const ss = String(s).padStart(2, "0");
  return h > 0 ? `${h}:${mm}:${ss}` : `${mm}:${ss}`;
}

/** Spoken form for assistive tech, e.g. "12 minutes 3 seconds". */
export function spokenMs(ms: number): string {
  const total = Math.max(0, Math.floor(ms / 1000));
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  const part = (n: number, unit: string) => `${n} ${unit}${n === 1 ? "" : "s"}`;
  return [h > 0 ? part(h, "hour") : "", m > 0 || h > 0 ? part(m, "minute") : "", part(s, "second")].filter(Boolean).join(" ");
}
