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
  /**
   * spaarkeai-compose-r2 Wave 3 (DEF-11 TEXT-path close) — the Compose TAB's document session id
   * (`ComposeWorkspace.state.sessionId`: the Wave-2 browse-minted id, or a stored/upload session id).
   * The host threads it into the `POST /api/compose/active-document` body as `documentSessionId` so the
   * server sets `ChatSession.ActiveDocument.DocumentSessionId`; `BindingCapabilityTool` then routes a
   * TEXT/typed revise-or-draft into THIS document session (redline in the open doc) instead of
   * fail-softing to the chat session. Omitted (undefined) on registrations that have no tab session yet.
   */
  documentSessionId?: string;
  /**
   * spaarkeai-compose-r2 R3 ("Visible to assistant" toggle) — whether this document should be
   * PRESENT in the Assistant's chat context. Omitted / `true` = register it as visible (the default
   * for every auto-register path: Browse, stored-doc DEF-10, upload). `false` = WITHDRAW it (the
   * toggle turned OFF); the host threads it into the `POST /api/compose/active-document` body so the
   * sibling server agent clears `ChatSession.ActiveDocument` for this session. Additive — existing
   * callers omit it and keep the visible-by-default behaviour.
   */
  visible?: boolean;
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

/**
 * CHANGE the active Compose document's "Visible to assistant" state (spaarkeai-compose-r2 R3).
 * The toggle lives on the WORKSPACE tab strip (WorkspacePane.handleToggleVisibility), but the
 * document BYTES (needed to feed the doc into chat context) live in the WORKSPACE editor
 * (ComposeWorkspace). This conduit lets WorkspacePane's toggle drive the editor's active-document
 * register/withdraw WITHOUT a PaneEventBus discriminant — same non-bus, sibling-pane rationale as the
 * dispatcher / active-document / redline-accept conduits above. `true` = ON (register the doc's
 * identity + extracted text into chat context so the Assistant can answer "what file is loaded");
 * `false` = OFF (withdraw it). No-op when no editor handler is registered (no Compose tab open /
 * standalone mount).
 */
export type ComposeVisibilityChange = (visible: boolean) => void;

/**
 * INSERT an Assistant suggestion's text into the Compose editor as a tracked change at the user's
 * current selection/cursor (spaarkeai-compose-r2 R4 — "Insert into document"). The suggestion lives
 * in the ASSISTANT pane (SprkChat message); the editor + redline engine live in the WORKSPACE pane
 * (ComposeWorkspace → usePendingRedline). This conduit routes the click to the editor WITHOUT a
 * PaneEventBus discriminant (same rationale as the conduits above). The editor materializes it via
 * the EXISTING `materializeComposeDraft` — a live selection → strike+replace, else insert at cursor —
 * so it renders as a pending redline with the shipped per-change Accept/Reject popover. No-op when no
 * editor handler is registered (no Compose tab open / standalone mount).
 */
export type ComposeInsertSuggestion = (content: string, messageId?: string) => void;

/**
 * TRIGGER the Compose editor's Save (spaarkeai-compose-r2 FIX #1b). The "Add the document to the DMS"
 * chip lives in the ASSISTANT pane (ConversationPane); the create-on-save / save-to-matter flow
 * (`ComposeWorkspace.triggerSave`) lives in the WORKSPACE pane. This conduit lets the chip drive the
 * editor's Save WITHOUT a PaneEventBus discriminant — same non-bus, sibling-pane rationale as the
 * conduits above. No-op (resolves) when no editor handler is registered (no Compose tab open /
 * standalone mount) — the Assistant gates on {@link hasComposeSaveHandler} via {@link useComposeSave}.
 */
export type ComposeSaveHandler = () => void | Promise<void>;

/**
 * REPORT a completed Compose Save back to the Assistant (spaarkeai-compose-r2 FIX #7a). When the
 * create-on-save / save-to-matter flow succeeds, ComposeWorkspace hands the HOST (ConversationPane)
 * the persisted document's identity so the Assistant can POST a PERSISTENT "Saved '{filename}' to the
 * DMS." chat message with an "Open preview" affordance — replacing the transient in-editor Saved ✓
 * banner's preview link. Flows WORKSPACE → ASSISTANT (the inverse direction of the save-trigger
 * conduit); same non-bus, sibling-pane rationale. `documentRecordId` is the server-minted
 * `sprk_documentid`. No-op when no host handler is registered (standalone mount).
 */
export type ComposeSaveCompleted = (info: { documentRecordId: string; fileName?: string }) => void;

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

  /**
   * Stable delegate to the currently-registered editor visibility handler (R3). No-op when none is
   * registered — WorkspacePane gates on {@link hasComposeVisibilityHandler} via
   * {@link useComposeVisibility}.
   */
  setComposeVisibility: ComposeVisibilityChange;
  /** Register (or clear, with `null`) the editor's visibility handler (workspace-side / ComposeWorkspace). */
  setComposeVisibilityHandler: (handler: ComposeVisibilityChange | null) => void;
  /** True when an editor visibility handler is currently registered. */
  hasComposeVisibilityHandler: boolean;

  /**
   * Stable delegate to the currently-registered editor insert-suggestion handler (R4). No-op when
   * none is registered — the Assistant gates on {@link hasComposeInsertSuggestionHandler} via
   * {@link useComposeInsertSuggestion}.
   */
  insertSuggestion: ComposeInsertSuggestion;
  /** Register (or clear, with `null`) the editor's insert-suggestion handler (workspace-side). */
  setComposeInsertSuggestionHandler: (handler: ComposeInsertSuggestion | null) => void;
  /** True when an editor insert-suggestion handler is currently registered. */
  hasComposeInsertSuggestionHandler: boolean;

  /**
   * Stable delegate to the currently-registered editor Save handler (FIX #1b). No-op when none is
   * registered — the Assistant gates on {@link hasComposeSaveHandler} via {@link useComposeSave}.
   */
  triggerComposeSave: ComposeSaveHandler;
  /** Register (or clear, with `null`) the editor's Save handler (workspace-side / ComposeWorkspace). */
  setComposeSaveHandler: (handler: ComposeSaveHandler | null) => void;
  /** True when an editor Save handler is currently registered. */
  hasComposeSaveHandler: boolean;

  /**
   * Stable delegate to the currently-registered host save-completed handler (FIX #7a). No-op when
   * none is registered — the editor gates on {@link hasComposeSaveCompletedHandler} via
   * {@link useComposeSaveCompleted}.
   */
  notifyComposeSaveCompleted: ComposeSaveCompleted;
  /** Register (or clear, with `null`) the host's save-completed handler (assistant-side / ConversationPane). */
  setComposeSaveCompletedHandler: (handler: ComposeSaveCompleted | null) => void;
  /** True when a host save-completed handler is currently registered. */
  hasComposeSaveCompletedHandler: boolean;
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

  const visibilityHandlerRef = React.useRef<ComposeVisibilityChange | null>(null);
  const [hasComposeVisibilityHandler, setHasComposeVisibilityHandler] = React.useState<boolean>(false);

  const insertSuggestionRef = React.useRef<ComposeInsertSuggestion | null>(null);
  const [hasComposeInsertSuggestionHandler, setHasComposeInsertSuggestionHandler] = React.useState<boolean>(false);

  const saveHandlerRef = React.useRef<ComposeSaveHandler | null>(null);
  const [hasComposeSaveHandler, setHasComposeSaveHandler] = React.useState<boolean>(false);

  const saveCompletedHandlerRef = React.useRef<ComposeSaveCompleted | null>(null);
  const [hasComposeSaveCompletedHandler, setHasComposeSaveCompletedHandler] = React.useState<boolean>(false);

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

  const setComposeVisibilityHandler = React.useCallback((handler: ComposeVisibilityChange | null): void => {
    visibilityHandlerRef.current = handler;
    setHasComposeVisibilityHandler(handler !== null);
  }, []);

  const setComposeVisibility = React.useCallback<ComposeVisibilityChange>(visible => {
    // No editor handler (no Compose tab open / standalone mount) → inert no-op; WorkspacePane gates
    // on hasComposeVisibilityHandler before driving the toggle cross-pane.
    visibilityHandlerRef.current?.(visible);
  }, []);

  const setComposeInsertSuggestionHandler = React.useCallback((handler: ComposeInsertSuggestion | null): void => {
    insertSuggestionRef.current = handler;
    setHasComposeInsertSuggestionHandler(handler !== null);
  }, []);

  const insertSuggestion = React.useCallback<ComposeInsertSuggestion>((content, messageId) => {
    // No editor handler → inert no-op; the Assistant gates on hasComposeInsertSuggestionHandler and
    // only offers "Insert into document" when a live editor is registered.
    insertSuggestionRef.current?.(content, messageId);
  }, []);

  const setComposeSaveHandler = React.useCallback((handler: ComposeSaveHandler | null): void => {
    saveHandlerRef.current = handler;
    setHasComposeSaveHandler(handler !== null);
  }, []);

  const triggerComposeSave = React.useCallback<ComposeSaveHandler>(() => {
    // No editor handler (no Compose tab open / standalone mount) → inert no-op; the Assistant gates
    // on hasComposeSaveHandler before routing the "Add to DMS" chip here.
    return saveHandlerRef.current?.();
  }, []);

  const setComposeSaveCompletedHandler = React.useCallback((handler: ComposeSaveCompleted | null): void => {
    saveCompletedHandlerRef.current = handler;
    setHasComposeSaveCompletedHandler(handler !== null);
  }, []);

  const notifyComposeSaveCompleted = React.useCallback<ComposeSaveCompleted>(info => {
    // No host handler (standalone LegalWorkspace mount) → inert no-op; the editor's own Saved ✓
    // banner remains the only confirmation surface there.
    saveCompletedHandlerRef.current?.(info);
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
      setComposeVisibility,
      setComposeVisibilityHandler,
      hasComposeVisibilityHandler,
      insertSuggestion,
      setComposeInsertSuggestionHandler,
      hasComposeInsertSuggestionHandler,
      triggerComposeSave,
      setComposeSaveHandler,
      hasComposeSaveHandler,
      notifyComposeSaveCompleted,
      setComposeSaveCompletedHandler,
      hasComposeSaveCompletedHandler,
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
      setComposeVisibility,
      setComposeVisibilityHandler,
      hasComposeVisibilityHandler,
      insertSuggestion,
      setComposeInsertSuggestionHandler,
      hasComposeInsertSuggestionHandler,
      triggerComposeSave,
      setComposeSaveHandler,
      hasComposeSaveHandler,
      notifyComposeSaveCompleted,
      setComposeSaveCompletedHandler,
      hasComposeSaveCompletedHandler,
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

/**
 * Workspace-side registration hook (R3 — "Visible to assistant"). Call from the pane that owns the
 * editor (ComposeWorkspace) to publish its visibility handler — the one that registers/withdraws the
 * loaded document's identity + extracted text in the chat context via the active-document conduit.
 * Effect-scoped: re-registers on identity change, clears on unmount. No-op outside a
 * {@link ComposeActionBridgeProvider}.
 */
export function useRegisterComposeVisibilityHandler(handler: ComposeVisibilityChange): void {
  const bridge = useComposeActionBridge();
  const setComposeVisibilityHandler = bridge?.setComposeVisibilityHandler;
  React.useEffect(() => {
    if (!setComposeVisibilityHandler) return;
    setComposeVisibilityHandler(handler);
    return () => setComposeVisibilityHandler(null);
  }, [setComposeVisibilityHandler, handler]);
}

/**
 * Toggle-side consumer hook (R3). Returns the stable {@link ComposeVisibilityChange} delegate when an
 * editor handler is registered (a Compose tab is open), else `null` — WorkspacePane's toggle handler
 * gates on the null so toggling a non-Compose tab (or with no Compose tab open) is a plain no-op.
 */
export function useComposeVisibility(): ComposeVisibilityChange | null {
  const bridge = useComposeActionBridge();
  return bridge && bridge.hasComposeVisibilityHandler ? bridge.setComposeVisibility : null;
}

/**
 * Workspace-side registration hook (R4 — "Insert into document"). Call from the pane that owns the
 * editor (ComposeWorkspace) to publish its insert-suggestion handler — the one that materializes the
 * Assistant suggestion's text as a pending redline at the current selection/cursor via the existing
 * `usePendingRedline`/`materializeComposeDraft`. Effect-scoped: re-registers on identity change,
 * clears on unmount. No-op outside a {@link ComposeActionBridgeProvider}.
 */
export function useRegisterComposeInsertSuggestionHandler(handler: ComposeInsertSuggestion): void {
  const bridge = useComposeActionBridge();
  const setComposeInsertSuggestionHandler = bridge?.setComposeInsertSuggestionHandler;
  React.useEffect(() => {
    if (!setComposeInsertSuggestionHandler) return;
    setComposeInsertSuggestionHandler(handler);
    return () => setComposeInsertSuggestionHandler(null);
  }, [setComposeInsertSuggestionHandler, handler]);
}

/**
 * Assistant-side consumer hook (R4). Returns the stable {@link ComposeInsertSuggestion} delegate when
 * an editor handler is registered (a Compose tab is open), else `null` — the Assistant gates on the
 * null so the "Insert into document" affordance renders only when a live editor can receive it.
 */
export function useComposeInsertSuggestion(): ComposeInsertSuggestion | null {
  const bridge = useComposeActionBridge();
  return bridge && bridge.hasComposeInsertSuggestionHandler ? bridge.insertSuggestion : null;
}

/**
 * Workspace-side registration hook (FIX #1b — "Add to DMS"). Call from the pane that owns the editor
 * (ComposeWorkspace) to publish its Save handler (`triggerSave` — the create-on-save / save-to-matter
 * flow). Effect-scoped: re-registers on identity change, clears on unmount. No-op outside a
 * {@link ComposeActionBridgeProvider}.
 */
export function useRegisterComposeSaveHandler(handler: ComposeSaveHandler): void {
  const bridge = useComposeActionBridge();
  const setComposeSaveHandler = bridge?.setComposeSaveHandler;
  React.useEffect(() => {
    if (!setComposeSaveHandler) return;
    setComposeSaveHandler(handler);
    return () => setComposeSaveHandler(null);
  }, [setComposeSaveHandler, handler]);
}

/**
 * Assistant-side consumer hook (FIX #1b). Returns the stable {@link ComposeSaveHandler} delegate when
 * an editor Save handler is registered (a Compose tab is open), else `null` — the Assistant's
 * "Add to DMS" chip gates on the null to decide whether it can drive the editor's Save.
 */
export function useComposeSave(): ComposeSaveHandler | null {
  const bridge = useComposeActionBridge();
  return bridge && bridge.hasComposeSaveHandler ? bridge.triggerComposeSave : null;
}

/**
 * Assistant-side registration hook (FIX #7a). Call from the pane that owns the chat session
 * (ConversationPane) to publish its save-completed handler — the one that POSTs the persistent
 * "Saved '{filename}' to the DMS." chat message + "Open preview" affordance. Effect-scoped:
 * re-registers on identity change, clears on unmount. No-op outside a {@link ComposeActionBridgeProvider}.
 */
export function useRegisterComposeSaveCompletedHandler(handler: ComposeSaveCompleted): void {
  const bridge = useComposeActionBridge();
  const setComposeSaveCompletedHandler = bridge?.setComposeSaveCompletedHandler;
  React.useEffect(() => {
    if (!setComposeSaveCompletedHandler) return;
    setComposeSaveCompletedHandler(handler);
    return () => setComposeSaveCompletedHandler(null);
  }, [setComposeSaveCompletedHandler, handler]);
}

/**
 * Workspace-side consumer hook (FIX #7a). Returns the stable {@link ComposeSaveCompleted} delegate
 * when a host handler is registered, else `null` (standalone mount / isolated test) — ComposeWorkspace
 * calls it on Save success so the Assistant posts the persistent confirmation, and skips it when null.
 */
export function useComposeSaveCompleted(): ComposeSaveCompleted | null {
  const bridge = useComposeActionBridge();
  return bridge && bridge.hasComposeSaveCompletedHandler ? bridge.notifyComposeSaveCompleted : null;
}
