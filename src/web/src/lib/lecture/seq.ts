import type { Seq } from "./types";

const SEQ_RE = /^(0|[1-9][0-9]{0,18})$/;

export function isSeq(value: unknown): value is Seq {
  return typeof value === "string" && SEQ_RE.test(value);
}

/** Seq values are int64 decimal strings; never use Number() or string comparison. */
export function compareSeq(a: Seq, b: Seq): -1 | 0 | 1 {
  const x = BigInt(a);
  const y = BigInt(b);
  return x < y ? -1 : x > y ? 1 : 0;
}

export function maxSeq(a: Seq | null, b: Seq): Seq {
  return a === null || compareSeq(a, b) < 0 ? b : a;
}

export function nextSeq(a: Seq): Seq {
  return (BigInt(a) + 1n).toString();
}
