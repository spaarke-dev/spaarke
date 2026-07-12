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

/**
 * A Compose-direct / transient mount registration (spaarkeai-compose-r2 task 113 / UAT defect 4).
 *
 * When the user Browse-mounts a local file directly in Compose, its bytes are CLIENT-ONLY —
 * invisible to the chat session, so "summarize this document" cannot see it. This handler lets the
 * widget hand those bytes to the HOST (ConversationPane), which lands them as a `ChatSessionFile`
 * via the EXISTING chat upload endpoint and marks the session's active document (POST
 * `/api/compose/active-document`). Same non-bus, sibling-pane conduit rationale as the dispatcher
 * above — bytes travel by a DIRECT function call (never a PaneEventBus payload; ADR-030 §MUST NOT /
 * ADR-015 keep the bus content-free). Fire-and-forget from the widget's perspective.
 */
export type ComposeActiveDocumentRegistration = (info: {
  docxBytes: ArrayBuffer;
  fileName?: string;
}) => void | Promise<void>;

/**
 * ACCEPT a pending Compose redline (spaarkeai-compose-r2 DEF-12). The Accept control now lives on the
 * Assistant confirmation message (the AI↔user interaction surface), but the redline + its accept LOGIC
 * (`usePendingRedline.accept`) live in the WORKSPACE editor. This conduit lets the Assistant reach the
 * editor's accept WITHOUT a PaneEventBus discriminant (same non-bus, sibling-pane rationale as the
 * dispatcher + active-document conduits above) — the editor commits the tracked change addressed by
 * `ledgerRef`. Reject / Try-another do NOT use this conduit: they are durable ledger supersessions
 * owned by the Assistant's `useEditSupersession`, which re-materialize via the existing Flow-5 signal.
 * No-op (resolves) when no host handler is registered (standalone LegalWorkspace mount / isolated test).
 */
export type ComposeRedlineAccept = (ledgerRef: string) => void;

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

  /**
   * Stable delegate to the currently-registered host active-document handler (task 113). No-op
   * (resolves) when no host handler is registered — the widget gates on
   * {@link hasActiveDocumentHandler} via {@link useComposeActiveDocumentRegistration}.
   */
  registerActiveDocument: ComposeActiveDocumentRegistration;
  /** Register (or clear, with `null`) the host's active-document handler. */
  setActiveDocumentHandler: (handler: ComposeActiveDocumentRegistration | null) => void;
  /** True when a host active-document handler is currently registered. */
  hasActiveDocumentHandler: boolean;

  /**
   * Stable delegate to the currently-registered editor redline-accept handler (DEF-12). No-op when
   * none is registered — the Assistant gates on {@link hasRedlineAcceptHandler} via
   * {@link useComposeRedlineAccept}.
   */
  acceptRedline: ComposeRedlineAccept;
  /** Register (or clear, with `null`) the editor's redline-accept handler (workspace-side). */
  setRedlineAcceptHandler: (handler: ComposeRedlineAccept | null) => void;
  /** True when an editor redline-accept handler is currently registered. */
  hasRedlineAcceptHandler: boolean;
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
export function ComposeActionBridgeProvider(props: ComposeActionBridgeProviderProps): React.JSX.Element {
  const dispatcherRef = React.useRef<ComposeActionEnqueue | null>(null);
  const [hasDispatcher, setHasDispatcher] = React.useState<boolean>(false);

  const activeDocHandlerRef = React.useRef<ComposeActiveDocumentRegistration | null>(null);
  const [hasActiveDocumentHandler, setHasActiveDocumentHandler] = React.useState<boolean>(false);

  const redlineAcceptRef = React.useRef<ComposeRedlineAccept | null>(null);
  const [hasRedlineAcceptHandler, setHasRedlineAcceptHandler] = React.useState<boolean>(false);

  const setDispatcher = React.useCallback((dispatcher: ComposeActionEnqueue | null): void => {
    dispatcherRef.current = dispatcher;
    setHasDispatcher(dispatcher !== null);
  }, []);

  const enqueue = React.useCallback<ComposeActionEnqueue>(request => {
    const dispatcher = dispatcherRef.current;
    if (!dispatcher) {
      return Promise.reject(
        new Error('[ComposeActionBridge] no host dispatcher registered — the Assistant queue is not mounted')
      );
    }
    return dispatcher(request);
  }, []);

  const setActiveDocumentHandler = React.useCallback((handler: ComposeActiveDocumentRegistration | null): void => {
    activeDocHandlerRef.current = handler;
    setHasActiveDocumentHandler(handler !== null);
  }, []);

  const registerActiveDocument = React.useCallback<ComposeActiveDocumentRegistration>(info => {
    const handler = activeDocHandlerRef.current;
    // No host handler (e.g. standalone LegalWorkspace mount) → inert no-op, never rejects: the
    // Compose Save path still works; only chat visibility of the direct upload is skipped.
    if (!handler) return Promise.resolve();
    return Promise.resolve(handler(info));
  }, []);

  const setRedlineAcceptHandler = React.useCallback((handler: ComposeRedlineAccept | null): void => {
    redlineAcceptRef.current = handler;
    setHasRedlineAcceptHandler(handler !== null);
  }, []);

  const acceptRedline = React.useCallback<ComposeRedlineAccept>(ledgerRef => {
    // No editor handler (e.g. standalone mount) → inert no-op; the Assistant gates on
    // hasRedlineAcceptHandler and only shows Accept when a live editor is registered.
    redlineAcceptRef.current?.(ledgerRef);
  }, []);

  const value = React.useMemo<ComposeActionBridgeValue>(
    () => ({
      enqueue,
      setDispatcher,
      hasDispatcher,
      registerActiveDocument,
      setActiveDocumentHandler,
      hasActiveDocumentHandler,
      acceptRedline,
      setRedlineAcceptHandler,
      hasRedlineAcceptHandler,
    }),
    [
      enqueue,
      setDispatcher,
      hasDispatcher,
      registerActiveDocument,
      setActiveDocumentHandler,
      hasActiveDocumentHandler,
      acceptRedline,
      setRedlineAcceptHandler,
      hasRedlineAcceptHandler,
    ]
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

/**
 * Host-side registration hook (task 113 / UAT defect 4). Call from the pane that owns the chat
 * session (`ConversationPane`) to publish its active-document handler — the one that lands a
 * Compose-direct upload's bytes as a `ChatSessionFile` and marks it active. Effect-scoped:
 * re-registers on identity change, clears on unmount. No-op outside a
 * {@link ComposeActionBridgeProvider}.
 */
export function useRegisterComposeActiveDocumentHandler(handler: ComposeActiveDocumentRegistration): void {
  const bridge = useComposeActionBridge();
  const setActiveDocumentHandler = bridge?.setActiveDocumentHandler;
  React.useEffect(() => {
    if (!setActiveDocumentHandler) return;
    setActiveDocumentHandler(handler);
    return () => setActiveDocumentHandler(null);
  }, [setActiveDocumentHandler, handler]);
}

/**
 * Widget-side consumer hook (task 113 / UAT defect 4). Returns the stable
 * {@link ComposeActiveDocumentRegistration} delegate when a host handler is registered, else
 * `null` (standalone mount / isolated test) — the widget gates on the null and skips registration.
 */
export function useComposeActiveDocumentRegistration(): ComposeActiveDocumentRegistration | null {
  const bridge = useComposeActionBridge();
  return bridge && bridge.hasActiveDocumentHandler ? bridge.registerActiveDocument : null;
}

/**
 * Workspace-side registration hook (DEF-12). Call from the pane that owns the editor (ComposeWorkspace)
 * to publish its redline-accept handler — the one that commits the pending redline addressed by
 * `ledgerRef` via `usePendingRedline.accept`. Effect-scoped: re-registers on identity change, clears on
 * unmount. No-op outside a {@link ComposeActionBridgeProvider}.
 */
export function useRegisterComposeRedlineAcceptHandler(handler: ComposeRedlineAccept): void {
  const bridge = useComposeActionBridge();
  const setRedlineAcceptHandler = bridge?.setRedlineAcceptHandler;
  React.useEffect(() => {
    if (!setRedlineAcceptHandler) return;
    setRedlineAcceptHandler(handler);
    return () => setRedlineAcceptHandler(null);
  }, [setRedlineAcceptHandler, handler]);
}

/**
 * Assistant-side consumer hook (DEF-12). Returns the stable {@link ComposeRedlineAccept} delegate when
 * an editor handler is registered, else `null` (standalone mount / isolated test) — the Assistant gates
 * on the null to decide whether the per-message Accept control can reach a live editor.
 */
export function useComposeRedlineAccept(): ComposeRedlineAccept | null {
  const bridge = useComposeActionBridge();
  return bridge && bridge.hasRedlineAcceptHandler ? bridge.acceptRedline : null;
}
