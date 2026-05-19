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
 *   5. User is authenticated
 *   6. Not already executing
 *
 * Spaarke Auth v2 (task 026): consumes `getAccessToken: () => Promise<string>`
 * instead of a snapshotted token. The SSE path inside executeAnalysis awaits
 * the getter once at stream-open and never persists the value.
 */

import { useCallback, useEffect, useRef, useState } from 'react';
import { executeAnalysis } from '../services/analysisApi';
import type { AnalysisStreamChunk } from '../services/analysisApi';
import type { AnalysisRecord, AnalysisError } from '../types';

const LOG_PREFIX = '[AnalysisWorkspace:useAnalysisExecution]';

/**
 * Maps backend `step` field values to frontend AiProgressStepper step IDs.
 */
const BACKEND_STEP_TO_FRONTEND: Record<string, string> = {
  document_loaded: 'document_loaded',
  extracting_text: 'extracting_text',
  text_extracted: 'context_ready',
  context_ready: 'context_ready',
  analyzing: 'analyzing',
};

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface UseAnalysisExecutionOptions {
  /** Loaded analysis record (null while loading) */
  analysis: AnalysisRecord | null;
  /** Document ID for the source document */
  documentId: string;
  /** Whether the user is authenticated (gate before executing). */
  isAuthenticated: boolean;
  /** Token getter — passed through to executeAnalysis's SSE setup. */
  getAccessToken: () => Promise<string>;
  /** Called when execution completes — triggers analysis reload */
  onComplete: () => void;
  /** Called with accumulated content during streaming for display */
  onStreamContent?: (content: string) => void;
}

export interface UseAnalysisExecutionResult {
  isExecuting: boolean;
  executionError: AnalysisError | null;
  progressMessage: string;
  chunkCount: number;
  activeStepId: string | null;
  completedStepIds: string[];
  triggerExecute: () => void;
  cancelExecution: () => void;
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

export function useAnalysisExecution(options: UseAnalysisExecutionOptions): UseAnalysisExecutionResult {
  const { analysis, documentId, isAuthenticated, getAccessToken, onComplete, onStreamContent } = options;

  const [isExecuting, setIsExecuting] = useState(false);
  const [executionError, setExecutionError] = useState<AnalysisError | null>(null);
  const [progressMessage, setProgressMessage] = useState('');
  const [chunkCount, setChunkCount] = useState(0);
  const [activeStepId, setActiveStepId] = useState<string | null>(null);
  const [completedStepIds, setCompletedStepIds] = useState<string[]>([]);

  // Track whether we've already triggered execution for this analysis
  const executedRef = useRef<string | null>(null);
  const abortRef = useRef<AbortController | null>(null);
  const activeStepRef = useRef<string | null>(null);
  const firstContentChunkRef = useRef(false);

  /**
   * Check if the analysis should auto-execute.
   */
  const shouldAutoExecute = useCallback((): boolean => {
    if (!analysis) return false;
    if (!isAuthenticated) return false;
    if (isExecuting) return false;
    if (executedRef.current === analysis.id) return false;

    const isDraft = analysis.statusCode === 1 || analysis.status === 'draft';
    const isEmpty = !analysis.content || analysis.content.trim().length === 0;
    const hasAction = !!analysis.actionId || !!analysis.playbookId;

    if (!isDraft || !isEmpty || !hasAction) return false;

    console.log(
      `${LOG_PREFIX} Auto-execute conditions met: draft=${isDraft}, empty=${isEmpty}, hasAction=${hasAction}`
    );
    return true;
  }, [analysis, isAuthenticated, isExecuting]);

  /**
   * Execute the analysis via BFF SSE endpoint.
   */
  const doExecute = useCallback(async () => {
    if (!analysis || !isAuthenticated) return;

    console.log(`${LOG_PREFIX} Executing analysis: ${analysis.id}`);
    console.log(`${LOG_PREFIX}   actionId: ${analysis.actionId ?? 'none'}`);
    console.log(`${LOG_PREFIX}   playbookId: ${analysis.playbookId ?? 'none'}`);
    console.log(`${LOG_PREFIX}   documentId: ${documentId}`);

    executedRef.current = analysis.id;
    setIsExecuting(true);
    setExecutionError(null);
    setProgressMessage('Starting analysis...');
    setChunkCount(0);
    setActiveStepId(null);
    setCompletedStepIds([]);
    activeStepRef.current = null;
    firstContentChunkRef.current = false;

    const abortController = new AbortController();
    abortRef.current = abortController;

    let contentBuffer = '';
    let lastRenderTime = 0;
    const RENDER_INTERVAL = 150;

    try {
      await executeAnalysis({
        analysisId: analysis.id,
        documentIds: [documentId],
        actionId: analysis.actionId,
        playbookId: analysis.playbookId,
        getAccessToken,
        signal: abortController.signal,
        onChunk: (chunk: AnalysisStreamChunk) => {
          if (chunk.type === 'metadata') {
            setProgressMessage('Processing document...');
            activeStepRef.current = 'document_loaded';
            setActiveStepId('document_loaded');
          } else if (chunk.type === 'progress' && chunk.step) {
            const frontendStepId = BACKEND_STEP_TO_FRONTEND[chunk.step];
            if (frontendStepId && frontendStepId !== activeStepRef.current) {
              const prevStep = activeStepRef.current;
              if (prevStep) {
                setCompletedStepIds(prev => prev.includes(prevStep) ? prev : [...prev, prevStep]);
              }
              activeStepRef.current = frontendStepId;
              setActiveStepId(frontendStepId);
            }
          } else if (chunk.type === 'chunk' && chunk.content) {
            contentBuffer += chunk.content;
            setChunkCount(prev => prev + 1);
            setProgressMessage('Generating analysis...');

            if (!firstContentChunkRef.current) {
              firstContentChunkRef.current = true;
              const prevStep = activeStepRef.current;
              if (prevStep && prevStep !== 'delivering') {
                setCompletedStepIds(prev => prev.includes(prevStep) ? prev : [...prev, prevStep]);
              }
              activeStepRef.current = 'delivering';
              setActiveStepId('delivering');
            }

            const now = Date.now();
            if (now - lastRenderTime >= RENDER_INTERVAL) {
              onStreamContent?.(contentBuffer);
              lastRenderTime = now;
            }
          } else if (chunk.type === 'status' && chunk.content === 'done') {
            onStreamContent?.(contentBuffer);
            setProgressMessage('Analysis complete');
            setCompletedStepIds(['document_loaded', 'extracting_text', 'context_ready', 'analyzing', 'delivering']);
            setActiveStepId(null);
            activeStepRef.current = null;
          }
        },
      });

      console.log(`${LOG_PREFIX} Execution complete, reloading analysis from Dataverse`);
      setProgressMessage('Loading results...');

      // Small delay to allow Dataverse write to propagate
      await new Promise(resolve => setTimeout(resolve, 1000));

      onComplete();
    } catch (err) {
      if (abortController.signal.aborted) {
        console.log(`${LOG_PREFIX} Execution cancelled`);
        setProgressMessage('');
        return;
      }

      console.error(`${LOG_PREFIX} Execution failed:`, err);
      const analysisErr: AnalysisError =
        typeof err === 'object' && err !== null && 'errorCode' in err
          ? (err as AnalysisError)
          : {
              errorCode: 'EXECUTION_FAILED',
              message: err instanceof Error ? err.message : 'Analysis execution failed',
            };
      setExecutionError(analysisErr);
      setProgressMessage('Execution failed');
    } finally {
      setIsExecuting(false);
      abortRef.current = null;
    }
  }, [analysis, documentId, isAuthenticated, getAccessToken, onComplete, onStreamContent]);

  /**
   * Auto-execute when conditions are met.
   */
  useEffect(() => {
    if (shouldAutoExecute()) {
      doExecute();
    }
  }, [shouldAutoExecute, doExecute]);

  /**
   * Manually trigger execution — bypasses shouldAutoExecute guard.
   */
  const triggerExecute = useCallback(() => {
    if (!analysis || !isAuthenticated || isExecuting) return;
    doExecute();
  }, [analysis, isAuthenticated, isExecuting, doExecute]);

  /**
   * Abort in-progress execution.
   */
  const cancelExecution = useCallback(() => {
    abortRef.current?.abort();
  }, []);

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
    activeStepId,
    completedStepIds,
    triggerExecute,
    cancelExecution,
  };
}
