/**
 * useSerialActionQueue — FIFO serialization of Compose AI-action dispatches
 * (FR-18 / task 032, design.md §15 scope-lock decision #5: "multiple-action
 * concurrency → QUEUE SERIALLY in ConversationPane").
 *
 * Firing rapid, distinct AI actions (e.g. the FR-14 inline toolbar's Compare
 * then Draft in quick succession) MUST NOT interleave their SSE streams or
 * their ledger writes. This hook enqueues `ComposeActionRequest`s in arrival
 * order and drives them through the shipped `dispatchConsumer(bindingId,
 * args)` seam (ADR-039 Click path) ONE AT A TIME: request N+1 does not begin
 * until request N's dispatch Promise settles.
 *
 * ── Why awaiting `dispatchConsumer`'s Promise is sufficient ordering proof ──
 *
 * `dispatchConsumer` (task 023 / `@spaarke/ui-components`) awaits the FULL
 * SSE stream (`readSseStream`) before its returned Promise resolves or
 * rejects — the terminal `streaming_complete` PaneEventBus event has already
 * been published and the server has already written the ledger entry BEFORE
 * render (ADR-040 storage-precedes-rendering) by the time that Promise
 * settles. Serializing on that Promise therefore serializes both the SSE
 * stream lifecycle (no interleaving) AND the ledger write order (no
 * out-of-order `SessionOutput` rows) — the queue needs no separate
 * ledger-ordering mechanism.
 *
 * ── §11 justification (mirrors CLAUDE.md §11 three-question template) ──────
 *
 * (1) Existing — `useInjectionQueue` (this directory) serializes ASSISTANT
 *     MESSAGE INJECTION into SprkChat's single-slot prop; it has no concept
 *     of a stream-completion gate. `useEventBatch` (this directory) batches
 *     Event-path SSE arrivals by chip-settlement count; it does not gate
 *     unrelated dispatch invocations on each other. Neither hook serializes
 *     DISPATCH INVOCATIONS.
 * (2) Extension — folding stream-completion gating into either existing hook
 *     would conflate two distinct concerns (message-slot bookkeeping /
 *     upload-gesture batching vs. dispatch-execution ordering). A focused
 *     hook keeps each concern's invariants independently reviewable.
 * (3) Cost-of-doing-nothing — without this hook, firing Compare then Draft
 *     races two concurrent `dispatchConsumer` calls: their SSE streams
 *     interleave in the transcript and their ledger writes can land
 *     out-of-order, breaking FR-18's ordering guarantee.
 *
 * ── Contract-naming note (Spike-0 correction, design.md revision log
 *    2026-07-08) ──────────────────────────────────────────────────────────
 *
 * Earlier drafts of this project named a PaneEventBus discriminant
 * `compose_action_request` as the inbound signal this queue would consume.
 * design.md's Spike-0 finding retracted that name: it never existed in code,
 * and the corrected §7.2 dispatch sequence has the inline toolbar call
 * `dispatchConsumer(bindingId, {slots})` DIRECTLY rather than emitting a bus
 * event — PaneEventBus carries pane *choreography*, not the dispatch trigger.
 * This hook therefore accepts a locally-typed `ComposeActionRequest` (an
 * opaque `id` + the `dispatchConsumer` call args) rather than subscribing to
 * a PaneEventBus channel; no PaneEventBus discriminant is introduced or
 * required (ADR-030's five-channel closed union is untouched). Whatever
 * cross-pane hand-off the FR-14 toolbar (task 030) ultimately uses to reach
 * this queue's `enqueue` is a follow-on wiring decision — this hook's
 * serialization guarantee holds regardless of the caller.
 *
 * @see ADR-030 — typed PaneEventBus channels (unaffected; no new discriminant)
 * @see ADR-039 — one dispatch protocol; the queue only orders calls into the
 *      shipped `dispatchConsumer` seam, it does not add a path
 * @see ADR-040 — ledger storage precedes rendering; ordering proof above
 * @see dispatchConsumer.ts — the seam this queue serializes calls into
 */

import * as React from "react";
import type {
  DispatchConsumer,
  DispatchConsumerArgs,
  DispatchConsumerResult,
} from "@spaarke/ui-components";

/**
 * One queued Compose AI-action dispatch. `id` is caller-assigned (e.g. a
 * per-click correlation id) and surfaced via `inFlightId` for UI affordance
 * (e.g. "running: Compare (2 queued)").
 */
export interface ComposeActionRequest {
  /** Caller-assigned correlation id (opaque; not interpreted by the queue). */
  readonly id: string;
  /** Binding row GUID — the ONLY routing datum (ADR-039); forwarded verbatim. */
  readonly bindingId: string;
  /** Forwarded verbatim to `dispatchConsumer` as its second argument. */
  readonly args?: DispatchConsumerArgs;
}

interface QueueEntry {
  readonly request: ComposeActionRequest;
  readonly resolve: (result: DispatchConsumerResult) => void;
  readonly reject: (error: unknown) => void;
}

export interface SerialActionQueue {
  /**
   * Enqueue a Compose AI-action dispatch. Resolves/rejects with the same
   * outcome `dispatchConsumer` itself would produce, once this request's turn
   * arrives and its stream reaches a terminal state. Never throws
   * synchronously — ordering/backpressure is entirely Promise-based.
   */
  enqueue: (request: ComposeActionRequest) => Promise<DispatchConsumerResult>;
  /** Count of requests waiting behind the in-flight one (UI affordance). */
  pendingCount: number;
  /** `id` of the currently-streaming request, or null when idle. */
  inFlightId: string | null;
}

/**
 * Serializes calls into the supplied `dispatchConsumer` seam: at most one
 * dispatch is ever in flight, requests run in enqueue (arrival) order, and an
 * errored/aborted request releases the queue immediately (no deadlock) —
 * `entry.reject` still fires, allowing the next queued request to start.
 *
 * `dispatchConsumer` is read via a ref on every drain iteration (not closed
 * over at queue-construction time) so a host may swap in a fresh bound
 * dispatcher (e.g. after a session change) without losing in-flight queue
 * state.
 */
export function useSerialActionQueue(dispatchConsumer: DispatchConsumer): SerialActionQueue {
  const dispatchRef = React.useRef(dispatchConsumer);
  dispatchRef.current = dispatchConsumer;

  const queueRef = React.useRef<QueueEntry[]>([]);
  const runningRef = React.useRef(false);

  const [pendingCount, setPendingCount] = React.useState(0);
  const [inFlightId, setInFlightId] = React.useState<string | null>(null);

  const drain = React.useCallback((): void => {
    if (runningRef.current) return;
    runningRef.current = true;

    void (async (): Promise<void> => {
      while (queueRef.current.length > 0) {
        const entry = queueRef.current.shift() as QueueEntry;
        setPendingCount(queueRef.current.length);
        setInFlightId(entry.request.id);
        try {
          // Awaiting this Promise IS the serialization gate — see module
          // JSDoc for why that is sufficient for both stream + ledger order.
          const result = await dispatchRef.current(entry.request.bindingId, entry.request.args);
          entry.resolve(result);
        } catch (err) {
          // An errored/aborted in-flight action still releases the queue —
          // the loop continues to the next entry (NEGATIVE: no deadlock).
          entry.reject(err);
        }
      }
      setInFlightId(null);
      runningRef.current = false;
    })();
  }, []);

  const enqueue = React.useCallback(
    (request: ComposeActionRequest): Promise<DispatchConsumerResult> => {
      return new Promise<DispatchConsumerResult>((resolve, reject) => {
        queueRef.current.push({ request, resolve, reject });
        setPendingCount(queueRef.current.length);
        drain();
      });
    },
    [drain]
  );

  return React.useMemo(
    () => ({ enqueue, pendingCount, inFlightId }),
    [enqueue, pendingCount, inFlightId]
  );
}
