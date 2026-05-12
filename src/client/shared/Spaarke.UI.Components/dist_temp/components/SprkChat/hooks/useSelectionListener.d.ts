/**
 * useSelectionListener - Subscribe to cross-pane selection events from SprkChatBridge
 *
 * Listens for `selection_changed` events emitted by the Analysis Workspace editor
 * (via useSelectionBroadcast) and stores the parsed selection in React state.
 * Handles selection clearing, bridge disconnect, and context/document changes.
 *
 * Events with `context: "selection_cleared"` (or empty `text`) are treated as
 * selection-clear signals, resetting the state to null.
 *
 * Large selections (>5000 chars) have their preview text truncated; the full
 * untruncated text is preserved in `fullText` for refinement payloads.
 *
 * SECURITY: Auth tokens are NEVER included in selection events (enforced by
 * useSelectionBroadcast on the emitter side).
 *
 * @see ADR-012 - Shared Component Library (SprkChatBridge lives in @spaarke/ui-components)
 * @see ADR-022 - React 16 APIs only (useState, useEffect, useCallback, useRef)
 * @see SelectionChangedPayload in SprkChatBridge.ts
 * @see useSelectionBroadcast in AnalysisWorkspace (emitter side)
 */
import type { SprkChatBridge } from '../../../services/SprkChatBridge';
import type { ICrossPaneSelection } from '../types';
export interface UseSelectionListenerOptions {
    /** SprkChatBridge instance to subscribe to (null when bridge is not active) */
    bridge: SprkChatBridge | null | undefined;
    /** Whether to listen for selection events (default: true) */
    enabled?: boolean;
}
export interface IUseSelectionListenerResult {
    /** The current cross-pane selection, or null when no selection is active */
    selection: ICrossPaneSelection | null;
    /** Programmatically clear the selection state */
    clearSelection: () => void;
}
/**
 * Subscribes to `selection_changed` events on the SprkChatBridge and exposes
 * the current cross-pane selection as React state.
 *
 * @param options - Bridge instance and enabled flag
 * @returns The current selection (or null) and a clearSelection function
 *
 * @example
 * ```tsx
 * const { selection, clearSelection } = useSelectionListener({
 *     bridge: props.bridge,
 *     enabled: true,
 * });
 *
 * // Pass to SprkChatHighlightRefine
 * <SprkChatHighlightRefine
 *     crossPaneSelection={selection}
 *     ...
 * />
 * ```
 */
export declare function useSelectionListener(options: UseSelectionListenerOptions): IUseSelectionListenerResult;
//# sourceMappingURL=useSelectionListener.d.ts.map