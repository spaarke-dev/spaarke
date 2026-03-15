/**
 * useAnalysisExecution Hook
 *
 * Manages analysis execution state and the SSE streaming pipeline for the
 * AnalysisWorkspace control. Handles the initial AI analysis execution
 * via the BFF API /execute endpoint with Server-Sent Events streaming.
 *
 * Internally delegates SSE stream reading to a fetch+reader approach
 * consistent with the existing useSseStream hook pattern, eliminating the
 * ~160-line inline SSE reader that was previously in AnalysisWorkspaceApp.tsx.
 *
 * Follows UseXxxOptions/UseXxxResult convention from shared library hooks.
 * React 16/17 compatible (ADR-022).
 *
 * @version 1.0.0
 */

import * as React from 'react';
import { IAnalysis } from '../types';
import { logInfo, logError } from '../utils/logger';
import { markdownToHtml } from '../utils/markdownToHtml';

// ─────────────────────────────────────────────────────────────────────────────
// SSE Parsing Utility (shared pattern with useSseStream)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Parsed SSE chunk from the execute endpoint
 */
interface ExecuteSseChunk {
  type?: 'chunk' | 'metadata' | 'done' | 'error';
  content?: string;
  token?: string;
  error?: string;
  done?: boolean;
  documentName?: string;
}

/**
 * Parse a single SSE data line into a typed chunk.
 * Returns null for non-data lines, comments, and [DONE] markers.
 */
function parseSseDataLine(line: string): ExecuteSseChunk | null {
  if (!line.startsWith('data: ')) return null;

  const data = line.slice(6).trim();
  if (data === '[DONE]') return null;

  try {
    return JSON.parse(data) as ExecuteSseChunk;
  } catch {
    // Non-JSON data — treat as raw content
    if (data) {
      return { type: 'chunk', content: data };
    }
    return null;
  }
}

/**
 * Read an SSE stream from a Response and invoke callbacks for each chunk.
 * This is the shared SSE reader pattern used by useSseStream — extracted
 * here to avoid duplicating the fetch+reader+decoder logic.
 */
async function readSseStream(
  response: Response,
  callbacks: {
    onChunk: (chunk: ExecuteSseChunk) => void;
    onRawContent: (content: string) => void;
    onDone: () => void;
  }
): Promise<void> {
  const reader = response.body?.getReader();
  if (!reader) {
    throw new Error('Response body is not readable');
  }

  const decoder = new TextDecoder();
  let buffer = '';

  while (true) {
    const { done, value } = await reader.read();

    if (done) {
      callbacks.onDone();
      break;
    }

    // Decode chunk
    buffer += decoder.decode(value, { stream: true });

    // Process SSE events (format: "data: {json}\n")
    const lines = buffer.split('\n');
    buffer = lines.pop() || ''; // Keep incomplete line in buffer

    for (const line of lines) {
      const chunk = parseSseDataLine(line);
      if (chunk) {
        if (chunk.error || chunk.type === 'error') {
          throw new Error(chunk.error || 'Unknown error during analysis');
        }
        callbacks.onChunk(chunk);
      } else if (line.startsWith('data: ')) {
        // Non-parseable data line — treat as raw content
        const rawData = line.slice(6).trim();
        if (rawData && rawData !== '[DONE]') {
          callbacks.onRawContent(rawData);
        }
      }
    }
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Options for the useAnalysisExecution hook
 */
export interface UseAnalysisExecutionOptions {
  /** Base URL for the BFF API */
  apiBaseUrl: string;

  /** Analysis record ID from Dataverse */
  analysisId: string;

  /** Dataverse WebAPI reference for saving results */
  webApi: ComponentFramework.WebApi;

  /** Playbook ID to include in execution request (if set) */
  playbookId: string | null;

  /** Callback to update the working document content during streaming */
  setWorkingDocument: (content: string) => void;

  /** Callback to set dirty state after execution saves */
  setIsDirty: (dirty: boolean) => void;

  /** Callback to set last saved timestamp */
  setLastSaved: (date: Date | null) => void;

  /** Callback to set error state */
  setError: (error: string | null) => void;

  /** Callback to set loading state (used to clear loading when execution starts) */
  setIsLoading?: (loading: boolean) => void;

  /** Check if WebAPI is available (not in design-time mode) */
  isWebApiAvailable: (webApi: ComponentFramework.WebApi | undefined) => boolean;
}

/**
 * Result returned by the useAnalysisExecution hook
 */
export interface UseAnalysisExecutionResult {
  /** Whether an analysis execution is currently in progress */
  isExecuting: boolean;

  /** Progress message displayed during execution */
  executionProgress: string;

  /**
   * Execute the initial AI analysis via BFF API with SSE streaming.
   * Called when loading a Draft analysis with empty working document,
   * or when the user clicks the re-execute button.
   */
  executeAnalysis: (analysis: IAnalysis, docId: string) => Promise<void>;
}

// ─────────────────────────────────────────────────────────────────────────────
// Hook
// ─────────────────────────────────────────────────────────────────────────────

/**
 * useAnalysisExecution Hook
 *
 * Encapsulates analysis execution state and the SSE streaming pipeline.
 * Uses SSE stream reading consistent with the useSseStream hook pattern,
 * tailored for the /execute endpoint's request/response format.
 *
 * @example
 * ```tsx
 * const { isExecuting, executionProgress, executeAnalysis } = useAnalysisExecution({
 *   apiBaseUrl: props.apiBaseUrl,
 *   analysisId: props.analysisId,
 *   webApi: props.webApi,
 *   playbookId,
 *   setWorkingDocument,
 *   setIsDirty,
 *   setLastSaved,
 *   setError,
 *   isWebApiAvailable,
 * });
 *
 * // Auto-execute draft analysis
 * if (isDraft && hasEmptyWorkingDoc) {
 *   executeAnalysis(analysis, docId);
 * }
 * ```
 */
export const useAnalysisExecution = (options: UseAnalysisExecutionOptions): UseAnalysisExecutionResult => {
  const {
    apiBaseUrl,
    analysisId,
    webApi,
    playbookId,
    setWorkingDocument,
    setIsDirty,
    setLastSaved,
    setError,
    isWebApiAvailable: checkWebApiAvailable,
  } = options;

  const [isExecuting, setIsExecuting] = React.useState(false);
  const [executionProgress, setExecutionProgress] = React.useState('');

  /**
   * Execute the initial AI analysis via BFF API with SSE streaming.
   * Called when loading a Draft analysis with empty working document.
   */
  const executeAnalysis = React.useCallback(
    async (analysis: IAnalysis, docId: string): Promise<void> => {
      if (!analysis._sprk_actionid_value) {
        logError('useAnalysisExecution', 'Cannot execute: no action ID set');
        setError('Cannot execute analysis: no action selected. Please edit the analysis and select an action.');
        return;
      }

      setIsExecuting(true);
      setExecutionProgress('Starting analysis...');
      logInfo(
        'useAnalysisExecution',
        `Executing analysis for document ${docId} with action ${analysis._sprk_actionid_value}`
      );

      try {
        // Get access token via @spaarke/auth
        let authHeaders: Record<string, string> = {};
        try {
          // Import dynamically to avoid circular dependency
          const { getAuthProvider } = await import('@spaarke/auth');
          const provider = getAuthProvider();
          const token = await provider.getAccessToken();
          authHeaders = { Authorization: `Bearer ${token}` };
        } catch (authErr) {
          logError('useAnalysisExecution', 'Failed to acquire auth token for execute', authErr);
          throw new Error('Authentication failed. Please refresh and try again.');
        }

        // Build request body matching AnalysisExecuteRequest
        const requestBody: Record<string, unknown> = {
          documentIds: [docId],
          actionId: analysis._sprk_actionid_value,
          outputType: 0, // Document
        };

        // Add playbook ID if present (scopes will be resolved from playbook N:N relationships)
        if (playbookId) {
          requestBody.playbookId = playbookId;
          logInfo('useAnalysisExecution', `Including playbook ${playbookId} in execute request`);
        }

        logInfo('useAnalysisExecution', 'Execute request body', requestBody);

        // Normalize apiBaseUrl - remove trailing /api if present
        const normalizedBaseUrl = apiBaseUrl.replace(/\/api\/?$/, '');

        // Make fetch request to execute endpoint with SSE
        const response = await fetch(`${normalizedBaseUrl}/api/ai/analysis/execute`, {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            Accept: 'text/event-stream',
            ...authHeaders,
          },
          body: JSON.stringify(requestBody),
        });

        if (!response.ok) {
          const errorText = await response.text();
          throw new Error(`HTTP ${response.status}: ${errorText || response.statusText}`);
        }

        // Use shared SSE reader pattern to process the stream
        let accumulatedText = '';
        setExecutionProgress('Analyzing document...');

        await readSseStream(response, {
          onChunk: (chunk) => {
            if (chunk.type === 'metadata') {
              setExecutionProgress(`Analyzing: ${chunk.documentName || 'document'}...`);
            }

            const content = chunk.content || chunk.token;
            if (content) {
              accumulatedText += content;
              // Convert markdown to HTML for RichTextEditor display
              const htmlContent = markdownToHtml(accumulatedText);
              setWorkingDocument(htmlContent);
            }

            if (chunk.type === 'done') {
              logInfo('useAnalysisExecution', 'Execute completed', chunk);
            }
          },
          onRawContent: (rawData) => {
            accumulatedText += rawData;
            const htmlContent = markdownToHtml(accumulatedText);
            setWorkingDocument(htmlContent);
          },
          onDone: () => {
            logInfo('useAnalysisExecution', 'Execute stream completed');
          },
        });

        // Save the working document to Dataverse (as HTML for RichTextEditor compatibility)
        if (accumulatedText && checkWebApiAvailable(webApi)) {
          logInfo('useAnalysisExecution', 'Saving executed analysis to Dataverse');
          const finalHtml = markdownToHtml(accumulatedText);
          await webApi.updateRecord('sprk_analysis', analysisId, {
            sprk_workingdocument: finalHtml,
            statuscode: 100000001, // In Progress
          });
          setIsDirty(false);
          setLastSaved(new Date());
        }

        setExecutionProgress('');
        logInfo('useAnalysisExecution', `Analysis execution completed: ${accumulatedText.length} chars`);
      } catch (err) {
        const errorMessage = err instanceof Error ? err.message : String(err);
        logError('useAnalysisExecution', 'Analysis execution failed', err);
        setError(`Analysis failed: ${errorMessage}`);
        setExecutionProgress('');
      } finally {
        setIsExecuting(false);
      }
    },
    [apiBaseUrl, analysisId, webApi, playbookId, setWorkingDocument, setIsDirty, setLastSaved, setError, checkWebApiAvailable]
  );

  return {
    isExecuting,
    executionProgress,
    executeAnalysis,
  };
};

export default useAnalysisExecution;
