/**
 * composeActionBridge — cross-pane conduit for the Compose inline-toolbar
 * dispatch hand-off (spaarkeai-compose-r2 FR-13 / task 046).
 *
 * WHY THIS EXISTS (design.md §3 + §7.2; FR-18):
 *   The inline AI toolbar (`ComposeAiToolbar`, task 030) lives in the WORKSPACE
 *   pane (inside `ComposeEditor` → `ComposeWorkspace`, mounted by the workspace
 *   section factory). The FIFO serialization queue that all Compose AI actions
 *   MUST funnel through (`useSerialActionQueue`, FR-18) lives in the ASSISTANT
 *   pane (`ConversationPane.dispatchComposeAction`) — so its stream/ledger
 *   ordering coordinates with the assistant transcript + chip dispatches. The
 *   two panes are SIBLINGS under the three-pane shell; the toolbar therefore
 *   needs a NON-BUS conduit to reach the assistant's queue.
 *
 *   This is deliberately NOT a PaneEventBus event: per Spike 0 (2026-07-08) +
 *   design.md §7.2, the dispatch TRIGGER is a DIRECT `dispatchConsumer` call,
 *   never a bus discriminant (the retracted `compose_action_request` name).
 *   PaneEventBus carries pane *choreography* only. This bridge is a plain React
 *   context holding the host's bound dispatcher — no new discriminant, so the
 *   ADR-030 four-channel closed union is untouched.
 *
 * HOW IT WORKS:
 *   - `ThreePaneShell` (SpaarkeAi) renders `ComposeActionBridgeProvider` above
 *     both panes.
 *   - `ConversationPane` registers its `dispatchComposeAction` via
 *     `useRegisterComposeActionDispatcher(...)` (effect-scoped: cleared on
 *     unmount / re-registered when the bound dispatcher identity changes).
 *   - The workspace section factory reads the bridge and forwards
 *     `bridge.enqueue` to `ComposeWorkspace.enqueueComposeAction` ONLY when a
 *     host dispatcher is registered (`hasDispatcher`) — otherwise it omits it,
 *     so a standalone Path-A mount (e.g. LegalWorkspace with no Assistant
 *     queue, no bridge provider) falls back to the toolbar's own bound
 *     dispatcher (`useComposeActionBridge()` returns null there).
 *
 * The `enqueue` returned by the provider is STABLE across dispatcher swaps (it
 * reads a ref), so `ComposeWorkspace`'s `useMemo`-bound toolbar wiring does not
 * churn when the assistant re-binds its dispatcher after a session change.
 *
 * @see ./composeLaunchContext.ts — sibling cross-pane compose context (document ref)
 * @see ../widgets/ComposeAiToolbar.tsx — the `ComposeActionEnqueue` consumer
 * @see src/solutions/SpaarkeAi/src/components/conversation/useSerialActionQueue.ts
 *      — the host queue this bridge routes into (FR-18)
 * @see projects/spaarkeai-compose-r2/notes/spikes/spike-0-dispatch-path.md §4
 */

import * as React from 'react';
import type { ComposeActionEnqueue } from '../widgets/ComposeAiToolbar';

export interface ComposeActionBridgeValue {
  /**
   * Stable enqueue delegating to the currently-registered host dispatcher.
   * Rejects if invoked while no dispatcher is registered (defensive — consumers
   * should gate on {@link hasDispatcher} and omit the prop instead, so the
   * toolbar falls back to its own dispatcher).
   */
  enqueue: ComposeActionEnqueue;
  /** Register (or clear, with `null`) the host's serial dispatcher. */
  setDispatcher: (dispatcher: ComposeActionEnqueue | null) => void;
  /** True when a host dispatcher is currently registered. */
  hasDispatcher: boolean;
}

export const ComposeActionBridgeContext = React.createContext<ComposeActionBridgeValue | null>(null);
ComposeActionBridgeContext.displayName = 'ComposeActionBridgeContext';

/**
 * Consume the Compose action bridge. Returns `null` when rendered outside a
 * {@link ComposeActionBridgeProvider} (e.g. standalone LegalWorkspace mount or
 * an isolated unit test) — consumers treat `null` as "no host queue" and fall
 * through to the toolbar's own bound dispatcher.
 */
export function useComposeActionBridge(): ComposeActionBridgeValue | null {
  return React.useContext(ComposeActionBridgeContext);
}

export interface ComposeActionBridgeProviderProps {
  children?: React.ReactNode;
}

/**
 * Provides the {@link ComposeActionBridgeContext}. Holds the host dispatcher in
 * a ref (so `enqueue` is stable) plus a `hasDispatcher` flag (state, so the
 * workspace section re-renders and threads `enqueue` the moment the assistant
 * registers its queue).
 */
export function ComposeActionBridgeProvider(
  props: ComposeActionBridgeProviderProps
): React.JSX.Element {
  const dispatcherRef = React.useRef<ComposeActionEnqueue | null>(null);
  const [hasDispatcher, setHasDispatcher] = React.useState<boolean>(false);

  const setDispatcher = React.useCallback((dispatcher: ComposeActionEnqueue | null): void => {
    dispatcherRef.current = dispatcher;
    setHasDispatcher(dispatcher !== null);
  }, []);

  const enqueue = React.useCallback<ComposeActionEnqueue>((request) => {
    const dispatcher = dispatcherRef.current;
    if (!dispatcher) {
      return Promise.reject(
        new Error('[ComposeActionBridge] no host dispatcher registered — the Assistant queue is not mounted')
      );
    }
    return dispatcher(request);
  }, []);

  const value = React.useMemo<ComposeActionBridgeValue>(
    () => ({ enqueue, setDispatcher, hasDispatcher }),
    [enqueue, setDispatcher, hasDispatcher]
  );

  return React.createElement(ComposeActionBridgeContext.Provider, { value }, props.children);
}

/**
 * Host-side registration hook. Call from the pane that owns the serial dispatch
 * queue (`ConversationPane`) to publish its `dispatchComposeAction` into the
 * bridge. Registration is effect-scoped: it re-registers when the bound
 * dispatcher identity changes and clears on unmount. No-op when rendered
 * outside a {@link ComposeActionBridgeProvider}.
 */
export function useRegisterComposeActionDispatcher(dispatcher: ComposeActionEnqueue): void {
  const bridge = useComposeActionBridge();
  const setDispatcher = bridge?.setDispatcher;
  React.useEffect(() => {
    if (!setDispatcher) return;
    setDispatcher(dispatcher);
    return () => setDispatcher(null);
  }, [setDispatcher, dispatcher]);
}
