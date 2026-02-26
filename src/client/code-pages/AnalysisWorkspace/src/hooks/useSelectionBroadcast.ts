/**
 * useSelectionBroadcast - Emit selection events from editor to SprkChat
 *
 * Monitors the RichTextEditor for text selection changes and broadcasts
 * selection_changed / selection_cleared events via SprkChatBridge. Debounces
 * at 300ms to prevent flooding during drag-select operations.
 *
 * Events are silently dropped when SprkChat pane is not open (the bridge
 * will send to BroadcastChannel but no receiver will pick them up).
 *
 * SECURITY: Auth tokens are NEVER included in selection events.
 *
 * @see ADR-012 - Shared component library (SprkChatBridge)
 * @see SelectionChangedPayload in SprkChatBridge.ts
 */

import { useEffect, useRef, useCallback } from "react";
import type { SprkChatBridge } from "@spaarke/ui-components/services/SprkChatBridge";
import type { RichTextEditorRef } from "@spaarke/ui-components";
import type { SelectionBoundingRect } from "../types";

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** Debounce delay for selection events to prevent flooding on drag-select */
const SELECTION_DEBOUNCE_MS = 300;

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface UseSelectionBroadcastOptions {
    /** Ref to the RichTextEditor instance */
    editorRef: React.RefObject<RichTextEditorRef | null>;
    /** SprkChatBridge instance for emitting events (null when bridge is not active) */
    bridge: SprkChatBridge | null;
    /** Whether selection broadcasting is enabled (default: true) */
    enabled?: boolean;
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

/**
 * Selection broadcast hook for emitting editor selections to SprkChat.
 *
 * Listens for `selectionchange` events on the document, reads the selection
 * from the DOM, and emits typed events via the SprkChatBridge.
 *
 * @example
 * ```tsx
 * useSelectionBroadcast({
 *     editorRef,
 *     bridge: documentStreaming.bridge,
 *     enabled: true,
 * });
 * ```
 */
export function useSelectionBroadcast(options: UseSelectionBroadcastOptions): void {
    const { editorRef, bridge, enabled = true } = options;

    const debounceTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
    const lastSelectionTextRef = useRef<string>("");

    /**
     * Read the current selection from the DOM and emit the appropriate
     * bridge event (selection_changed or selection_cleared).
     */
    const handleSelectionChange = useCallback(() => {
        if (!bridge || bridge.isDisconnected || !editorRef.current) {
            return;
        }

        const selection = document.getSelection();
        if (!selection || selection.rangeCount === 0) {
            // Emit clear if we had a previous selection
            if (lastSelectionTextRef.current) {
                lastSelectionTextRef.current = "";
                try {
                    bridge.emit("selection_changed", {
                        text: "",
                        startOffset: 0,
                        endOffset: 0,
                        context: "selection_cleared",
                    });
                } catch {
                    // Bridge disconnected or errored -- silently drop
                }
            }
            return;
        }

        const range = selection.getRangeAt(0);
        const selectedText = selection.toString().trim();

        // Check if the selection is within the editor container
        // by walking up from the range's common ancestor
        const editorContainer = findEditorContainer(range.commonAncestorContainer);
        if (!editorContainer) {
            // Selection is outside the editor -- ignore
            return;
        }

        if (!selectedText) {
            // Selection is empty (e.g., just a cursor placement)
            if (lastSelectionTextRef.current) {
                lastSelectionTextRef.current = "";
                try {
                    bridge.emit("selection_changed", {
                        text: "",
                        startOffset: 0,
                        endOffset: 0,
                        context: "selection_cleared",
                    });
                } catch {
                    // Silently drop
                }
            }
            return;
        }

        // Avoid emitting duplicate events for the same selection text
        if (selectedText === lastSelectionTextRef.current) {
            return;
        }

        lastSelectionTextRef.current = selectedText;

        // Get viewport-relative bounding rect
        const rect = range.getBoundingClientRect();
        const boundingRect: SelectionBoundingRect = {
            top: rect.top,
            left: rect.left,
            width: rect.width,
            height: rect.height,
        };

        // Get selected HTML content
        const fragment = range.cloneContents();
        const tempDiv = document.createElement("div");
        tempDiv.appendChild(fragment);
        const selectedHtml = tempDiv.innerHTML;

        try {
            bridge.emit("selection_changed", {
                text: selectedText,
                startOffset: range.startOffset,
                endOffset: range.endOffset,
                context: JSON.stringify({
                    selectedHtml,
                    boundingRect,
                    source: "analysis-editor",
                }),
            });
        } catch {
            // Bridge disconnected or errored -- silently drop
            // This is expected when SprkChat pane is not open
        }
    }, [bridge, editorRef]);

    /**
     * Debounced wrapper around handleSelectionChange.
     */
    const debouncedHandleSelection = useCallback(() => {
        if (debounceTimerRef.current) {
            clearTimeout(debounceTimerRef.current);
        }

        debounceTimerRef.current = setTimeout(() => {
            handleSelectionChange();
        }, SELECTION_DEBOUNCE_MS);
    }, [handleSelectionChange]);

    // -----------------------------------------------------------------------
    // Event listener setup and teardown
    // -----------------------------------------------------------------------

    useEffect(() => {
        if (!enabled || !bridge) {
            return;
        }

        document.addEventListener("selectionchange", debouncedHandleSelection);

        return () => {
            document.removeEventListener("selectionchange", debouncedHandleSelection);

            // Clear any pending debounce timer
            if (debounceTimerRef.current) {
                clearTimeout(debounceTimerRef.current);
                debounceTimerRef.current = null;
            }

            // Reset tracked selection
            lastSelectionTextRef.current = "";
        };
    }, [enabled, bridge, debouncedHandleSelection]);
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * Walk up the DOM tree from a node to find the Lexical editor container.
 * Returns the container element if found, null if the node is not inside
 * the editor.
 */
function findEditorContainer(node: Node | null): HTMLElement | null {
    let current: Node | null = node;
    while (current) {
        if (
            current instanceof HTMLElement &&
            (current.getAttribute("contenteditable") === "true" ||
                current.getAttribute("data-lexical-editor") === "true")
        ) {
            return current;
        }
        current = current.parentNode;
    }
    return null;
}
