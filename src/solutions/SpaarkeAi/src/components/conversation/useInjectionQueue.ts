/**
 * useInjectionQueue — ordered, loss-free Assistant-message injection through
 * SprkChat's single-slot `injectLocalMessage` prop.
 *
 * Extracted from ConversationPane.tsx by ai-architecture-redesign-r1 task 045
 * (FR-P3-06 thin-host decomposition). Semantics preserved verbatim from the
 * task-022b implementation:
 *
 *   - `inject(message)`  — occupies the slot immediately (replace semantics;
 *     the pre-existing direct `setPendingInjection` call sites: interjections,
 *     error lines, playbook-options message, hard-slash results). Does NOT
 *     disturb queued Event-path messages.
 *   - `enqueue(message)` — ordered injection. The Event-path SSE stream can
 *     deliver several renderable events in one network chunk (classification →
 *     output → notice); direct slot writes would clobber one another under
 *     React state batching, so Event-path messages queue and drain one-per-
 *     injection via SprkChat's `onLocalMessageInjected` acknowledgement.
 *   - `handleLocalMessageInjected()` — SprkChat ack; clears the slot back to
 *     null or promotes the next queued message.
 */

import * as React from "react";
import type { IChatMessage } from "@spaarke/ui-components";

export interface InjectionQueue {
  /** Current single-slot injection value for SprkChat's `injectLocalMessage`. */
  pendingInjection: IChatMessage | null;
  /** Replace the slot immediately (legacy direct-set semantics). */
  inject: (message: IChatMessage) => void;
  /** Ordered, loss-free injection (Event-path semantics, task 022b). */
  enqueue: (message: IChatMessage) => void;
  /** SprkChat `onLocalMessageInjected` acknowledgement handler. */
  handleLocalMessageInjected: () => void;
}

export function useInjectionQueue(): InjectionQueue {
  const [pendingInjection, setPendingInjection] = React.useState<IChatMessage | null>(null);
  const queueRef = React.useRef<IChatMessage[]>([]);

  const inject = React.useCallback((message: IChatMessage): void => {
    setPendingInjection(message);
  }, []);

  const enqueue = React.useCallback((message: IChatMessage): void => {
    setPendingInjection((prev) => {
      if (prev === null) return message;
      queueRef.current.push(message);
      return prev;
    });
  }, []);

  const handleLocalMessageInjected = React.useCallback((): void => {
    setPendingInjection(() => queueRef.current.shift() ?? null);
  }, []);

  // Identity changes only when the slot value changes (all methods stable).
  return React.useMemo(
    () => ({ pendingInjection, inject, enqueue, handleLocalMessageInjected }),
    [pendingInjection, inject, enqueue, handleLocalMessageInjected]
  );
}
