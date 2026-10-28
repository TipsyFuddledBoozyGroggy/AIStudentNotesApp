// speech.v1 audio framing (API_CONTRACTS section 4.2, B2 notes section 15 item 5).
// Wire: 12-byte header then PCM. uint32 frameSeq, then uint64 captureOffsetMs, both LITTLE-ENDIAN
// (the contract text does not say; the B2 backend notes fix it). The server only echoes frameSeq and
// ignores captureOffsetMs, so a wrong byte order would be silent: tests pin the exact bytes.

export const SAMPLE_RATE = 16_000;
export const FRAME_MS = 200;
export const SAMPLES_PER_FRAME = (SAMPLE_RATE * FRAME_MS) / 1000; // 3200
export const HEADER_BYTES = 12;
export const FRAME_BYTES = HEADER_BYTES + SAMPLES_PER_FRAME * 2; // 6412
/** Server limit: 250 ms of PCM plus the header (contract section 10, C-04). */
export const MAX_FRAME_BYTES = HEADER_BYTES + ((SAMPLE_RATE * 250) / 1000) * 2;

const U32_MAX = 0xffff_ffff;

export function encodeFrame(frameSeq: number, captureOffsetMs: number, pcm: Int16Array): Uint8Array {
  if (!Number.isInteger(frameSeq) || frameSeq < 0 || frameSeq > U32_MAX) throw new RangeError("frameSeq out of range");
  if (!Number.isInteger(captureOffsetMs) || captureOffsetMs < 0 || !Number.isSafeInteger(captureOffsetMs)) throw new RangeError("captureOffsetMs out of range");
  const out = new Uint8Array(HEADER_BYTES + pcm.length * 2);
  const view = new DataView(out.buffer);
  view.setUint32(0, frameSeq, true);
  view.setBigUint64(4, BigInt(captureOffsetMs), true);
  for (let i = 0; i < pcm.length; i++) view.setInt16(HEADER_BYTES + i * 2, pcm[i], true); // pcm_s16le regardless of host order
  return out;
}

export function floatToInt16(x: number): number {
  const c = x > 1 ? 1 : x < -1 ? -1 : x;
  return c < 0 ? Math.round(c * 0x8000) : Math.round(c * 0x7fff);
}

/**
 * Streaming area-average downsampler (inRate >= outRate) that emits fixed-size Int16 frames.
 * Averaging over each output interval is a cheap anti-alias filter. Deterministic and allocation-light.
 */
export class FrameChunker {
  private readonly ratio: number;
  private acc = 0;
  private w = 0; // input-sample weight consumed by the output sample in progress
  private frame = new Int16Array(SAMPLES_PER_FRAME);
  private n = 0;

  constructor(
    inRate: number,
    private readonly onFrame: (pcm: Int16Array) => void,
  ) {
    if (!(inRate >= SAMPLE_RATE)) throw new RangeError("input sample rate below 16 kHz is not supported");
    this.ratio = inRate / SAMPLE_RATE;
  }

  push(samples: Float32Array): void {
    for (let i = 0; i < samples.length; i++) {
      const x = samples[i];
      const remaining = this.ratio - this.w;
      if (remaining > 1 + 1e-9) {
        this.acc += x;
        this.w += 1;
      } else {
        // This input sample finishes the output sample (possibly splitting it across two outputs).
        const take = Math.min(1, remaining);
        this.emit((this.acc + x * take) / this.ratio);
        this.acc = x * (1 - take);
        this.w = 1 - take;
      }
    }
  }

  private emit(v: number): void {
    this.frame[this.n++] = floatToInt16(v);
    if (this.n === SAMPLES_PER_FRAME) {
      const done = this.frame;
      this.frame = new Int16Array(SAMPLES_PER_FRAME);
      this.n = 0;
      this.onFrame(done);
    }
  }
}
