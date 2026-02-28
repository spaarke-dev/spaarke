/**
 * useAutoSave - Debounced auto-save hook for AnalysisWorkspace
 *
 * Automatically persists editor content to Dataverse (same-origin Web API)
 * after a configurable debounce period (default 3 seconds). Tracks save state
 * for UI feedback (idle/saving/saved/error) and provides a forceSave function
 * for Ctrl+S.
 *
 * Writes directly to sprk_workingdocument via Dataverse PATCH — no BFF
 * round-trip or Bearer token needed.
 */

import { useCallback, useEffect, useRef, useState } from "react";
import { saveAnalysisContent } from "../services/analysisApi";
import type { SaveState } from "../types";

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** Default debounce delay for auto-save (milliseconds) */
const DEFAULT_DEBOUNCE_MS = 3000;

/** Duration to show "saved" indicator before returning to "idle" */
const SAVED_INDICATOR_MS = 2000;

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface UseAutoSaveOptions {
    /** GUID of the analysis record to save */
    analysisId: string;
    /** Whether auto-save is enabled */
    enabled?: boolean;
    /** Debounce delay in milliseconds (default: 3000) */
    debounceMs?: number;
}

export interface UseAutoSaveResult {
    /** Current save state: idle | saving | saved | error */
    saveState: SaveState;
    /** ISO timestamp of the last successful save */
    lastSavedAt: string | null;
    /** Error message from the last failed save attempt */
    saveError: string | null;
    /** Trigger an immediate save (for Ctrl+S) */
    forceSave: () => void;
    /** Notify the hook that content has changed (triggers debounced save) */
    notifyContentChanged: (content: string) => void;
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

/**
 * Auto-save hook with 3-second debounce. Writes to Dataverse via same-origin PATCH.
 *
 * @example
 * ```tsx
 * const { saveState, lastSavedAt, forceSave, notifyContentChanged } = useAutoSave({
 *     analysisId: "abc-123",
 *     enabled: true,
 * });
 *
 * // In editor onChange:
 * const handleChange = (html: string) => {
 *     setEditorContent(html);
 *     notifyContentChanged(html);
 * };
 * ```
 */
export function useAutoSave(options: UseAutoSaveOptions): UseAutoSaveResult {
    const {
        analysisId,
        enabled = true,
        debounceMs = DEFAULT_DEBOUNCE_MS,
    } = options;

    const [saveState, setSaveState] = useState<SaveState>("idle");
    const [lastSavedAt, setLastSavedAt] = useState<string | null>(null);
    const [saveError, setSaveError] = useState<string | null>(null);

    // Refs for debounce management
    const debounceTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
    const savedIndicatorTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
    const pendingContentRef = useRef<string | null>(null);
    const isSavingRef = useRef(false);

    // Cleanup timers on unmount
    useEffect(() => {
        return () => {
            if (debounceTimerRef.current) {
                clearTimeout(debounceTimerRef.current);
            }
            if (savedIndicatorTimerRef.current) {
                clearTimeout(savedIndicatorTimerRef.current);
            }
        };
    }, []);

    /**
     * Perform the actual save operation.
     */
    const doSave = useCallback(
        async (content: string) => {
            if (!analysisId || !enabled) {
                return;
            }

            // Prevent concurrent saves
            if (isSavingRef.current) {
                pendingContentRef.current = content;
                return;
            }

            isSavingRef.current = true;
            setSaveState("saving");
            setSaveError(null);

            try {
                await saveAnalysisContent(analysisId, content);

                const now = new Date().toISOString();
                setLastSavedAt(now);
                setSaveState("saved");

                // Reset to idle after a brief "saved" indicator
                savedIndicatorTimerRef.current = setTimeout(() => {
                    setSaveState("idle");
                }, SAVED_INDICATOR_MS);
            } catch (err: unknown) {
                const message =
                    err && typeof err === "object" && "message" in err
                        ? (err as { message: string }).message
                        : "Failed to save analysis content";
                setSaveError(message);
                setSaveState("error");
                console.error("[useAutoSave] Save failed:", err);
            } finally {
                isSavingRef.current = false;

                // If content changed while saving, save the latest version
                if (pendingContentRef.current !== null) {
                    const latestContent = pendingContentRef.current;
                    pendingContentRef.current = null;
                    doSave(latestContent);
                }
            }
        },
        [analysisId, enabled]
    );

    /**
     * Notify that content has changed. Triggers a debounced save.
     */
    const notifyContentChanged = useCallback(
        (content: string) => {
            if (!enabled || !analysisId) {
                return;
            }

            // Clear existing debounce timer
            if (debounceTimerRef.current) {
                clearTimeout(debounceTimerRef.current);
            }

            // Set new debounce timer
            debounceTimerRef.current = setTimeout(() => {
                doSave(content);
            }, debounceMs);
        },
        [enabled, analysisId, debounceMs, doSave]
    );

    /**
     * Force an immediate save (bypasses debounce). Used by Ctrl+S.
     */
    const forceSave = useCallback(() => {
        // Clear any pending debounce
        if (debounceTimerRef.current) {
            clearTimeout(debounceTimerRef.current);
            debounceTimerRef.current = null;
        }

        // If there is pending content, save it immediately
        if (pendingContentRef.current !== null) {
            doSave(pendingContentRef.current);
            pendingContentRef.current = null;
        }
    }, [doSave]);

    return {
        saveState,
        lastSavedAt,
        saveError,
        forceSave,
        notifyContentChanged,
    };
}
