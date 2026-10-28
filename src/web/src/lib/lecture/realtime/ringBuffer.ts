/** Bounded FIFO. Pushing into a full buffer drops the OLDEST item and reports how many were dropped. */
export class RingBuffer<T> {
  private items: T[] = [];
  constructor(readonly capacity: number) {
    if (capacity < 1) throw new RangeError("capacity must be positive");
  }
  get length(): number {
    return this.items.length;
  }
  push(item: T): number {
    let dropped = 0;
    while (this.items.length >= this.capacity) {
      this.items.shift();
      dropped++;
    }
    this.items.push(item);
    return dropped;
  }
  peek(): T | undefined {
    return this.items[0];
  }
  shift(): T | undefined {
    return this.items.shift();
  }
  clear(): void {
    this.items = [];
  }
}
