// AudioWorklet processor: forwards mono Float32 blocks to the main thread (batched ~43 ms at 48 kHz).
// It produces no output and is never connected to the destination, so nothing is played.
class PcmCapture extends AudioWorkletProcessor {
  constructor() {
    super();
    this.buf = new Float32Array(2048);
    this.n = 0;
  }
  process(inputs) {
    const ch = inputs[0] && inputs[0][0];
    if (!ch) return true;
    let i = 0;
    while (i < ch.length) {
      const take = Math.min(ch.length - i, this.buf.length - this.n);
      this.buf.set(ch.subarray(i, i + take), this.n);
      this.n += take;
      i += take;
      if (this.n === this.buf.length) {
        const out = this.buf;
        this.buf = new Float32Array(2048);
        this.n = 0;
        this.port.postMessage(out, [out.buffer]);
      }
    }
    return true;
  }
}
registerProcessor("pcm-capture", PcmCapture);
