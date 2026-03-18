/**
 * usePlaybookExecution — React hook for invoking AI playbooks via the BFF API.
 *
 * Calls POST /api/v1/external/ai/playbook with a playbook ID and execution context.
 * The BFF orchestrates the entire AI pipeline server-side — no playbook definitions,
 * prompt templates, or model details are ever sent to or stored in the client.
 *
 * Security: The client sends only stable, opaque playbook IDs (not content).
 * The BFF resolves playbook definitions from secure server-side storage.
 *
 * ADR-013: AI features via BFF API — no separate AI service, no client-side AI calls.
 */

import { useState, useCallback } from "react";
import { bffApiCall } from "../auth/bff-client";
import { ApiError } from "../types";

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

/**
 * A single structured section of a playbook result.
 * Results are pre-structured by the BFF — not raw chat text.
 */
export interface PlaybookResultSection {
  /** Section heading (e.g. "Executive Summary", "Key Issues", "Risk Factors") */
  title: string;
  /** Section body content (may contain newlines for multi-line paragraphs) */
  content: string;
}

/**
 * Structured result returned by the BFF after playbook execution.
 * The BFF formats AI output into sections before returning to the client.
 */
export interface PlaybookResult {
  /** Stable opaque playbook identifier that was executed */
  playbookId: string;
  /** Human-readable label for the playbook (for display only) */
  playbookLabel: string;
  /** Structured result sections — never raw chat or prompt output */
  sections: PlaybookResultSection[];
  /** ISO date string when the BFF completed execution */
  executedAt: string;
}

/**
 * Request context sent to the BFF for playbook execution.
 * Only stable IDs are sent — no user-constructed prompts or playbook content.
 */
export interface PlaybookExecutionRequest {
  /** Stable opaque playbook identifier (e.g. "summarize-document", "summarize-project") */
  playbookId: string;
  /** Secure Project record ID — scope for project-level playbooks */
  projectId: string;
  /** Optional Document record ID — required for document-scoped playbooks */
  documentId?: string;
}

// ---------------------------------------------------------------------------
// BFF API response envelope
// ---------------------------------------------------------------------------

interface PlaybookExecutionResponse {
  result: PlaybookResult;
}

// ---------------------------------------------------------------------------
// Hook state
// ---------------------------------------------------------------------------

export interface UsePlaybookExecutionState {
  /** True while a playbook execution is in progress */
  isExecuting: boolean;
  /** The most recently completed result, or null if not yet executed */
  result: PlaybookResult | null;
  /** Error message if the last execution failed, null otherwise */
  error: string | null;
  /**
   * Invoke a playbook by sending only stable IDs to the BFF.
   * The BFF resolves all playbook definitions server-side.
   */
  execute: (request: PlaybookExecutionRequest) => Promise<void>;
  /** Reset result and error state (e.g. when closing the result dialog) */
  reset: () => void;
}

// ---------------------------------------------------------------------------
// Hook implementation
// ---------------------------------------------------------------------------

/**
 * usePlaybookExecution — invoke AI playbooks via the BFF API.
 *
 * The hook exposes a typed `execute` function and tracks execution state
 * (isExecuting, result, error). Results are structured sections, not raw text.
 *
 * Playbook internals are never exposed to the client — only stable IDs are
 * transmitted. The BFF resolves definitions, builds prompts, and formats output
 * before returning `PlaybookResult.sections` to the caller.
 *
 * @example
 * ```tsx
 * const { isExecuting, result, error, execute, reset } = usePlaybookExecution();
 *
 * const handleSummarize = () => {
 *   void execute({ playbookId: "summarize-document", projectId, documentId });
 * };
 * ```
 */
export function usePlaybookExecution(): UsePlaybookExecutionState {
  const [isExecuting, setIsExecuting] = useState<boolean>(false);
  const [result, setResult] = useState<PlaybookResult | null>(null);
  const [error, setError] = useState<string | null>(null);

  const execute = useCallback(async (request: PlaybookExecutionRequest): Promise<void> => {
    setIsExecuting(true);
    setError(null);
    setResult(null);

    try {
      const response = await bffApiCall<PlaybookExecutionResponse>(
        "/api/v1/external/ai/playbook",
        {
          method: "POST",
          body: JSON.stringify(request),
        }
      );

      setResult(response.result);
    } catch (err) {
      if (err instanceof ApiError) {
        if (err.statusCode === 403) {
          setError(
            "You do not have permission to run AI analysis. Collaborate or Full Access is required."
          );
        } else if (err.statusCode === 404) {
          setError(
            "The document or project could not be found. Please refresh and try again."
          );
        } else if (err.statusCode >= 500) {
          setError(
            "The AI analysis service is temporarily unavailable. Please try again in a moment."
          );
        } else {
          setError(
            `AI analysis failed (${err.statusCode}). Please try again.`
          );
        }
      } else {
        setError(
          "An unexpected error occurred while running the AI analysis. Please try again."
        );
      }
      console.error("[usePlaybookExecution] Playbook execution failed:", err);
    } finally {
      setIsExecuting(false);
    }
  }, []);

  const reset = useCallback(() => {
    setResult(null);
    setError(null);
  }, []);

  return { isExecuting, result, error, execute, reset };
}
