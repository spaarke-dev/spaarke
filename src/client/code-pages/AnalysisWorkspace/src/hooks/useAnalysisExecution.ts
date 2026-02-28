/**
 * useAnalysisExecution - Auto-execute hook for draft analyses
 *
 * Detects when a draft analysis with empty content and an action/playbook
 * is loaded, then automatically triggers execution via the BFF SSE endpoint.
 * The BFF persists the working document to Dataverse as it streams.
 * On completion, reloads the analysis to display the final content.
 *
 * Auto-execute conditions (all must be true):
 *   1. Analysis is loaded (not null)
 *   2. Status is "draft" (statusCode === 1 or status === "draft")
 *   3. Content is empty (0 chars)
 *   4. Has an actionId OR playbookId
 *   5. Token is available (BFF auth)
 *   6. Not already executing
 *   7. Record was created within the last 60 seconds (newly created, not reopened)
 *
 * Condition 7 distinguishes new records (user just created → auto-execute)
 * from reopened existing records (user browsing → require explicit action).
 * This prevents overwriting user edits in sprk_workingdocument.
 */

import { useCallback, useEffect, useRef, useState } from "react";
import { executeAnalysis } from "../services/analysisApi";
import type { AnalysisStreamChunk } from "../services/analysisApi";
import type { AnalysisRecord, AnalysisError } from "../types";

const LOG_PREFIX = "[AnalysisWorkspace:useAnalysisExecution]";

/**
 * Max age (in ms) for a record to be considered "newly created".
 * If createdOn is within this window, the record was just created and
 * should auto-execute. Older records are "reopened" and require explicit trigger.
 */
const NEW_RECORD_AGE_MS = 60_000; // 60 seconds

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface UseAnalysisExecutionOptions {
    /** Loaded analysis record (null while loading) */
    analysis: AnalysisRecord | null;
    /** Document ID for the source document */
    documentId: string;
    /** Bearer auth token for BFF API */
    token: string | null;
    /** Called when execution completes — triggers analysis reload */
    onComplete: () => void;
    /** Called with accumulated content during streaming for display */
    onStreamContent?: (content: string) => void;
}

export interface UseAnalysisExecutionResult {
    /** Whether analysis execution is currently running */
    isExecuting: boolean;
    /** Error from execution (null on success) */
    executionError: AnalysisError | null;
    /** Current execution progress message */
    progressMessage: string;
    /** Number of content chunks received */
    chunkCount: number;
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

export function useAnalysisExecution(
    options: UseAnalysisExecutionOptions
): UseAnalysisExecutionResult {
    const { analysis, documentId, token, onComplete, onStreamContent } = options;

    const [isExecuting, setIsExecuting] = useState(false);
    const [executionError, setExecutionError] = useState<AnalysisError | null>(null);
    const [progressMessage, setProgressMessage] = useState("");
    const [chunkCount, setChunkCount] = useState(0);

    // Track whether we've already triggered execution for this analysis
    const executedRef = useRef<string | null>(null);
    const abortRef = useRef<AbortController | null>(null);

    /**
     * Check if the analysis should auto-execute.
     *
     * Auto-executes ONLY for newly created records (createdOn within 60s).
     * Reopened existing records require explicit user action to prevent
     * overwriting edits in sprk_workingdocument (no versioning yet).
     */
    const shouldAutoExecute = useCallback((): boolean => {
        if (!analysis) return false;
        if (!token) return false;
        if (isExecuting) return false;
        if (executedRef.current === analysis.id) return false;

        // Must be draft with empty content
        const isDraft = analysis.statusCode === 1 || analysis.status === "draft";
        const isEmpty = !analysis.content || analysis.content.trim().length === 0;
        if (!isDraft || !isEmpty) return false;

        // Must have an action or playbook to execute
        if (!analysis.actionId && !analysis.playbookId) return false;

        // Only auto-execute newly created records (within 60 seconds)
        if (!analysis.createdOn) return false;
        const ageMs = Date.now() - new Date(analysis.createdOn).getTime();
        if (ageMs > NEW_RECORD_AGE_MS) {
            console.log(
                `${LOG_PREFIX} Skipping auto-execute: record is ${Math.round(ageMs / 1000)}s old (max ${NEW_RECORD_AGE_MS / 1000}s)`
            );
            return false;
        }

        console.log(
            `${LOG_PREFIX} Auto-execute conditions met: draft=${isDraft}, empty=${isEmpty}, age=${Math.round(ageMs / 1000)}s`
        );
        return true;
    }, [analysis, token, isExecuting]);

    /**
     * Execute the analysis via BFF SSE endpoint.
     */
    const doExecute = useCallback(async () => {
        if (!analysis || !token) return;

        console.log(`${LOG_PREFIX} Auto-executing analysis: ${analysis.id}`);
        console.log(`${LOG_PREFIX}   actionId: ${analysis.actionId ?? "none"}`);
        console.log(`${LOG_PREFIX}   playbookId: ${analysis.playbookId ?? "none"}`);
        console.log(`${LOG_PREFIX}   documentId: ${documentId}`);

        // Mark as executed to prevent re-trigger
        executedRef.current = analysis.id;
        setIsExecuting(true);
        setExecutionError(null);
        setProgressMessage("Starting analysis...");
        setChunkCount(0);

        const abortController = new AbortController();
        abortRef.current = abortController;

        let contentBuffer = "";
        let lastRenderTime = 0;
        const RENDER_INTERVAL = 150; // ms — throttle to ~6-7 renders/sec to prevent Lexical DOM jank

        try {
            await executeAnalysis({
                analysisId: analysis.id,
                documentIds: [documentId],
                actionId: analysis.actionId,
                playbookId: analysis.playbookId,
                token,
                signal: abortController.signal,
                onChunk: (chunk: AnalysisStreamChunk) => {
                    if (chunk.type === "metadata") {
                        setProgressMessage("Processing document...");
                    } else if (chunk.type === "chunk" && chunk.content) {
                        contentBuffer += chunk.content;
                        setChunkCount((prev) => prev + 1);
                        setProgressMessage("Generating analysis...");

                        // Throttle render updates for per-token streaming
                        const now = Date.now();
                        if (now - lastRenderTime >= RENDER_INTERVAL) {
                            onStreamContent?.(contentBuffer);
                            lastRenderTime = now;
                        }
                    } else if (chunk.type === "status" && chunk.content === "done") {
                        // Final flush — always render complete content
                        onStreamContent?.(contentBuffer);
                        setProgressMessage("Analysis complete");
                    }
                },
            });

            console.log(`${LOG_PREFIX} Execution complete, reloading analysis from Dataverse`);
            setProgressMessage("Loading results...");

            // Small delay to allow Dataverse write to propagate
            await new Promise((resolve) => setTimeout(resolve, 1000));

            // Trigger reload from Dataverse
            onComplete();
        } catch (err) {
            if (abortController.signal.aborted) {
                console.log(`${LOG_PREFIX} Execution cancelled`);
                setProgressMessage("");
                return;
            }

            console.error(`${LOG_PREFIX} Execution failed:`, err);
            const analysisErr: AnalysisError =
                typeof err === "object" && err !== null && "errorCode" in err
                    ? (err as AnalysisError)
                    : {
                          errorCode: "EXECUTION_FAILED",
                          message:
                              err instanceof Error
                                  ? err.message
                                  : "Analysis execution failed",
                      };
            setExecutionError(analysisErr);
            setProgressMessage("Execution failed");

            // Allow retry by clearing the executed ref
            executedRef.current = null;
        } finally {
            setIsExecuting(false);
            abortRef.current = null;
        }
    }, [analysis, documentId, token, onComplete, onStreamContent]);

    /**
     * Auto-execute when conditions are met.
     */
    useEffect(() => {
        if (shouldAutoExecute()) {
            doExecute();
        }
    }, [shouldAutoExecute, doExecute]);

    /**
     * Cleanup: abort on unmount.
     */
    useEffect(() => {
        return () => {
            abortRef.current?.abort();
        };
    }, []);

    return {
        isExecuting,
        executionError,
        progressMessage,
        chunkCount,
    };
}
