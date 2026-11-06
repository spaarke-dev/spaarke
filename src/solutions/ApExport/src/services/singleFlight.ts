/**
 * A guard against a second call while one is in flight (a double click on Send). The React state that disables the
 * button updates after the next render, so a fast second click can still reach the handler; this flag is set
 * synchronously, so the second call is refused before it reaches the network.
 */

export interface SingleFlight {
  /** Run `fn` unless a run is in flight; resolves `{ ran: false }` without calling `fn` in that case. */
  run<T>(fn: () => Promise<T>): Promise<{ ran: true; value: T } | { ran: false }>;
  readonly busy: boolean;
}

export function createSingleFlight(): SingleFlight {
  let busy = false;
  return {
    get busy() {
      return busy;
    },
    async run<T>(fn: () => Promise<T>) {
      if (busy) return { ran: false as const };
      busy = true;
      try {
        return { ran: true as const, value: await fn() };
      } finally {
        busy = false;
      }
    },
  };
}
