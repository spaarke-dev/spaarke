/**
 * MessageChannel polyfill for jsdom — MUST be the FIRST import of any test file
 * that needs it (side-effect import order = evaluation order, and react-dom's
 * scheduler captures `MessageChannel` at module-evaluation time).
 *
 * WHY (spaarke-modal-system task 042 flake follow-up, 2026-08-02): this jsdom
 * environment has no `MessageChannel`/`setImmediate`, so React 19's concurrent
 * scheduler falls back to `setTimeout`, which was OBSERVED (instrumented runs —
 * see notes/task-042-completion.md) to strand deferred work under parallel-worker
 * contention: a state batch commits but its passive-effect cascade is never
 * scheduled, unrecoverably. Node's `worker_threads.MessageChannel` restores the
 * scheduler's preferred browser-like drain path, removing the starving fallback
 * at the root. Ports are `unref()`d so React's internally-held port cannot keep
 * the jest worker process alive after the file completes.
 */
import { MessageChannel as NodeMessageChannel, MessagePort as NodeMessagePort } from 'worker_threads';

/* eslint-disable @typescript-eslint/no-explicit-any */

class UnrefedMessageChannel extends NodeMessageChannel {
  constructor() {
    super();
    // unref'd ports still deliver while the event loop runs; they just don't
    // hold the process open — exactly what a test worker needs. CRITICAL:
    // Node auto-`ref()`s a port when a 'message' listener attaches (which
    // react-dom's scheduler does via `port1.onmessage = ...`), silently undoing
    // the unref and hanging the jest worker at file teardown — so `ref` is
    // pinned to a no-op on both ports.
    for (const port of [this.port1, this.port2] as unknown as Array<{
      unref?: () => void;
      ref?: () => void;
    }>) {
      port.unref?.();
      port.ref = () => {};
    }
  }
}

if (typeof (globalThis as any).MessageChannel === 'undefined') {
  (globalThis as any).MessageChannel = UnrefedMessageChannel;
  (globalThis as any).MessagePort = NodeMessagePort;
}

export {};
