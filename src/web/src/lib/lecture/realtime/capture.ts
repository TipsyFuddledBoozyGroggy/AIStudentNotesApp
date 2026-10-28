import { FrameChunker } from "./audioFrame";

/**
 * Browser capture: MediaStream -> AudioWorklet -> 16 kHz PCM16 frames. The graph is source -> worklet only; it is
 * never connected to the destination, so nothing is played back. Must be called only after the student started
 * the lecture (the stream comes from a getUserMedia call made on that press).
 */
export async function startWorkletCapture(stream: MediaStream, onFrame: (pcm: Int16Array) => void): Promise<() => void> {
  const Ctor = window.AudioContext;
  let ctx: AudioContext;
  try {
    ctx = new Ctor({ sampleRate: 16000 }); // Chromium and Firefox resample for us
  } catch {
    ctx = new Ctor();
  }
  try {
    await ctx.audioWorklet.addModule("/worklets/pcm-capture.js");
    if (ctx.state === "suspended") await ctx.resume();
    const chunker = new FrameChunker(ctx.sampleRate, onFrame);
    const source = ctx.createMediaStreamSource(stream);
    const node = new AudioWorkletNode(ctx, "pcm-capture", { numberOfInputs: 1, numberOfOutputs: 0, channelCount: 1, channelCountMode: "explicit" });
    node.port.onmessage = (e: MessageEvent<Float32Array>) => chunker.push(e.data);
    source.connect(node);
    return () => {
      node.port.onmessage = null;
      try {
        source.disconnect();
      } catch {
        /* already disconnected */
      }
      void ctx.close().catch(() => undefined);
    };
  } catch (e) {
    void ctx.close().catch(() => undefined);
    throw e;
  }
}
