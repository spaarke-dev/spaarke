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

import { useState, useEffect, useCallback, useRef } from "react";
import type { SprkChatBridge, SelectionChangedPayload } from "../../../services/SprkChatBridge";
import type { ICrossPaneSelection } from "../types";
import { CROSS_PANE_SELECTION_MAX_PREVIEW } from "../types";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

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

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * Parse the JSON-encoded `context` field from a SelectionChangedPayload.
 *
 * The emitter (useSelectionBroadcast) encodes selectedHtml, boundingRect, and
 * source into the context string. If context is missing or unparseable, we
 * return safe defaults.
 */
function parseSelectionContext(
    contextStr: string | undefined
): { selectedHtml: string; source: string } {
    if (!contextStr || contextStr === "selection_cleared") {
        return { selectedHtml: "", source: "" };
    }

    try {
        const parsed = JSON.parse(contextStr) as {
            selectedHtml?: string;
            source?: string;
        };
        return {
            selectedHtml: typeof parsed.selectedHtml === "string" ? parsed.selectedHtml : "",
            source: typeof parsed.source === "string" ? parsed.source : "",
        };
    } catch {
        // Malformed context — treat as no context
        return { selectedHtml: "", source: "" };
    }
}

/**
 * Truncate text to the maximum preview length, appending an ellipsis if truncated.
 */
function truncatePreview(text: string, maxLength: number): string {
    if (text.length <= maxLength) {
        return text;
    }
    return text.substring(0, maxLength) + "\u2026"; // Unicode ellipsis
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

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
export function useSelectionListener(
    options: UseSelectionListenerOptions
): IUseSelectionListenerResult {
    const { bridge, enabled = true } = options;

    const [selection, setSelection] = useState<ICrossPaneSelection | null>(null);

    // Keep a ref to the latest selection for use in callbacks that shouldn't
    // re-subscribe on every state change.
    const selectionRef = useRef<ICrossPaneSelection | null>(null);
    selectionRef.current = selection;

    /**
     * Programmatically clear the selection state.
     * Useful when the user dismisses the refine toolbar or when
     * the document/record context changes.
     */
    const clearSelection = useCallback(() => {
        setSelection(null);
    }, []);

    // -----------------------------------------------------------------------
    // Subscribe to bridge selection_changed events
    // -----------------------------------------------------------------------

    useEffect(() => {
        if (!enabled || !bridge || bridge.isDisconnected) {
            // No bridge or disabled — clear any lingering selection and bail
            if (selectionRef.current !== null) {
                setSelection(null);
            }
            return;
        }

        const handleSelectionChanged = (payload: SelectionChangedPayload): void => {
            // Determine if this is a selection-clear event
            const isClear =
                !payload.text ||
                payload.text.length === 0 ||
                payload.context === "selection_cleared";

            if (isClear) {
                setSelection(null);
                return;
            }

            // Parse the JSON context for selectedHtml, source, etc.
            const { selectedHtml, source } = parseSelectionContext(payload.context);

            // Build the cross-pane selection state
            const newSelection: ICrossPaneSelection = {
                text: truncatePreview(payload.text, CROSS_PANE_SELECTION_MAX_PREVIEW),
                fullText: payload.text,
                selectedHtml,
                startOffset: payload.startOffset,
                endOffset: payload.endOffset,
                source,
            };

            setSelection(newSelection);
        };

        // Subscribe — returns an unsubscribe function
        const unsubscribe = bridge.subscribe("selection_changed", handleSelectionChanged);

        return () => {
            unsubscribe();
            // Clear selection state on unsubscribe (bridge disconnect / component unmount)
            setSelection(null);
        };
    }, [bridge, enabled]);

    // -----------------------------------------------------------------------
    // Clear selection when bridge disconnects mid-lifecycle
    // -----------------------------------------------------------------------

    useEffect(() => {
        if (bridge && bridge.isDisconnected && selectionRef.current !== null) {
            setSelection(null);
        }
    }, [bridge]);

    return {
        selection,
        clearSelection,
    };
}
