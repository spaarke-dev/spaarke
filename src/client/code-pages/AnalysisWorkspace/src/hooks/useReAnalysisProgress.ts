/**
 * useReAnalysisProgress - Hook for tracking re-analysis progress via SprkChatBridge
 *
 * Subscribes to "reanalysis_progress" and "document_replaced" events from SprkChatBridge
 * to drive the ReAnalysisProgressOverlay component. The overlay shows a progress bar
 * during re-analysis operations (percent + status message) and auto-dismisses when
 * the document is replaced or an error/cancellation occurs.
 *
 * NOTE: The actual document content replacement (undo snapshot + setHtml) is handled
 * by the existing DocumentStreamBridge / useDocumentStreaming pipeline. This hook
 * only manages overlay visibility and progress state.
 *
 * Data flow:
 *   Backend SSE "progress" → SprkChat → BroadcastChannel → SprkChatBridge
 *   → reanalysis_progress event → this hook → ReAnalysisProgressOverlay
 *
 *   Backend SSE "document_replace" → SprkChat → BroadcastChannel → SprkChatBridge
 *   → document_replaced event → this hook (dismisses overlay)
 *                              → useDocumentStreaming (replaces content + undo stack)
 *
 * Edge cases handled:
 *   - Progress event with no prior start: shows overlay immediately
 *   - Rapid progress updates: state updates immediately (React batches renders)
 *   - Stale progress (no events for 30s): auto-dismiss with timeout
 *   - Component unmount: all subscriptions cleaned up
 *
 * @see SprkChatBridge - Cross-pane communication bridge
 * @see ReAnalysisProgressOverlay - Visual progress overlay
 * @see useDocumentStreaming - Handles the actual content replacement
 * @see ADR-012 - Shared Component Library
 */

import { useEffect, useRef, useState, useCallback } from "react";
import type { SprkChatBridge } from "@spaarke/ui-components/services/SprkChatBridge";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface UseReAnalysisProgressOptions {
    /**
     * SprkChatBridge instance to subscribe to events on.
     * Typically obtained from the bridge created in App.tsx or DocumentStreamBridge.
     * When null, subscriptions are skipped.
     */
    bridge: SprkChatBridge | null;

    /**
     * Whether the hook should be active.
     * Set to false to disable all subscriptions (e.g., when not authenticated).
     */
    enabled?: boolean;
}

export interface UseReAnalysisProgressResult {
    /** Whether the re-analysis progress overlay should be visible */
    isAnalyzing: boolean;
    /** Progress percentage (0-100) */
    percent: number;
    /** Human-readable status message */
    message: string;
    /** Manually dismiss the overlay (e.g., on error or user cancellation) */
    dismiss: () => void;
}

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/**
 * Auto-dismiss timeout in milliseconds.
 * If no progress or replacement event is received within this window,
 * the overlay is automatically dismissed to prevent stale UI.
 */
const STALE_TIMEOUT_MS = 30_000;

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

export function useReAnalysisProgress(
    options: UseReAnalysisProgressOptions,
): UseReAnalysisProgressResult {
    const { bridge, enabled = true } = options;

    const [isAnalyzing, setIsAnalyzing] = useState(false);
    const [percent, setPercent] = useState(0);
    const [message, setMessage] = useState("");

    // Ref to track the stale-timeout timer so we can clear it on updates
    const staleTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    const clearStaleTimer = useCallback(() => {
        if (staleTimerRef.current !== null) {
            clearTimeout(staleTimerRef.current);
            staleTimerRef.current = null;
        }
    }, []);

    const resetStaleTimer = useCallback(() => {
        clearStaleTimer();
        staleTimerRef.current = setTimeout(() => {
            // Auto-dismiss if no events received for STALE_TIMEOUT_MS
            setIsAnalyzing(false);
            setPercent(0);
            setMessage("");
        }, STALE_TIMEOUT_MS);
    }, [clearStaleTimer]);

    const dismiss = useCallback(() => {
        clearStaleTimer();
        setIsAnalyzing(false);
        setPercent(0);
        setMessage("");
    }, [clearStaleTimer]);

    // -----------------------------------------------------------------------
    // Subscribe to bridge events
    // -----------------------------------------------------------------------

    useEffect(() => {
        if (!bridge || !enabled) {
            return;
        }

        // Subscribe to reanalysis_progress events
        const unsubProgress = bridge.subscribe(
            "reanalysis_progress",
            (payload) => {
                // Show overlay on first progress event (handles "progress without start")
                setIsAnalyzing(true);
                setPercent(payload.percent);
                setMessage(payload.message);

                // Reset stale timer on each progress update
                resetStaleTimer();
            },
        );

        // Subscribe to document_replaced events to dismiss overlay
        // The actual content replacement is handled by useDocumentStreaming.
        // This subscription only manages the overlay visibility.
        const unsubReplaced = bridge.subscribe(
            "document_replaced",
            () => {
                // Document has been replaced — dismiss the progress overlay
                clearStaleTimer();
                setIsAnalyzing(false);
                setPercent(100);
                setMessage("");
            },
        );

        return () => {
            unsubProgress();
            unsubReplaced();
            clearStaleTimer();
        };
    }, [bridge, enabled, resetStaleTimer, clearStaleTimer]);

    // Clean up stale timer on unmount
    useEffect(() => {
        return () => {
            clearStaleTimer();
        };
    }, [clearStaleTimer]);

    return {
        isAnalyzing,
        percent,
        message,
        dismiss,
    };
}
