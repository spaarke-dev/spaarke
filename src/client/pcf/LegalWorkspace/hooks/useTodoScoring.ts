/**
 * useTodoScoring — hook managing scoring data fetch for the To Do AI Summary dialog.
 *
 * Responsibilities:
 *   1. Track dialog open/closed state per to-do event (keyed on eventId).
 *   2. Call BFF scoring endpoint: GET /api/workspace/events/{id}/scores
 *   3. Fall back to deterministic mock data when the BFF is unavailable (NFR-06).
 *   4. Expose loading, result, error, and retry state to the dialog component.
 *
 * Architecture constraints:
 *   - Scoring calls MUST go through BFF API (ADR-013) — NEVER compute client-side.
 *   - BFF base URL is optional; when absent, mock fallback activates automatically.
 *   - Mock data is deterministic so tests and demos work reliably.
 *   - Uses AbortController so in-flight requests are cancelled on dialog close.
 *
 * Usage:
 *   const {
 *     isOpen, result, isLoading, error,
 *     openScoring, closeScoring, retry,
 *   } = useTodoScoring({ bffBaseUrl, accessToken });
 *
 *   // Open dialog for a to-do event
 *   openScoring(event.sprk_eventid, event.sprk_subject);
 */

import { useState, useCallback, useRef } from 'react';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** A single priority factor contributing to the priority score */
export interface ITodoScoringPriorityFactor {
  /** Display name of the factor */
  name: string;
  /** Formatted value string (e.g. "12 days", "87%", "2 grades") */
  value: string;
  /** Points contributed (0-max per factor) */
  points: number;
}

/** A complexity multiplier that may apply to the effort score */
export interface ITodoScoringMultiplier {
  /** Display name of the multiplier */
  name: string;
  /** Multiplier value (e.g. 1.3) */
  value: number;
  /** Whether this multiplier was applied to the current event */
  applied: boolean;
}

/** Priority scoring result */
export interface ITodoPriorityScore {
  /** Aggregate priority score (0-100, capped) */
  score: number;
  /** Derived level from score */
  level: 'Critical' | 'High' | 'Medium' | 'Low';
  /** Breakdown of contributing factors */
  factors: ITodoScoringPriorityFactor[];
}

/** Effort scoring result */
export interface ITodoEffortScore {
  /** Aggregate effort score (0-100, capped) */
  score: number;
  /** Derived level from score */
  level: 'High' | 'Med' | 'Low';
  /** Base effort before multipliers are applied */
  baseEffort: number;
  /** All possible multipliers with applied flag */
  multipliers: ITodoScoringMultiplier[];
}

/** Suggested action returned in the scoring result */
export interface ITodoScoringAction {
  /** Display label for the action button */
  label: string;
  /** Icon name key (resolved locally by the dialog) */
  icon: 'ArrowUpRegular' | 'PersonSwapRegular' | 'MoneyRegular' | 'TaskListSquareRegular' | 'FolderOpenRegular';
}

/** Full scoring result from BFF or mock */
export interface ITodoScoringResult {
  /** Priority score with factor breakdown */
  priority: ITodoPriorityScore;
  /** Effort score with multiplier list */
  effort: ITodoEffortScore;
  /** AI-generated analysis text */
  analysis: string;
  /** Suggested actions for the user */
  suggestedActions: ITodoScoringAction[];
  /** Whether this result came from mock data (true) or the live BFF (false) */
  isMockData: boolean;
}

/** Context passed to openScoring to identify the event */
export interface ITodoScoringEventContext {
  eventId: string;
  eventTitle: string;
}

/** Public interface of the hook */
export interface IUseTodoScoringResult {
  /** Whether the dialog is currently open */
  isOpen: boolean;
  /** The event context for the open dialog (null when closed) */
  eventContext: ITodoScoringEventContext | null;
  /** Scoring result when available */
  result: ITodoScoringResult | null;
  /** True while the BFF fetch or mock delay is in progress */
  isLoading: boolean;
  /** User-friendly error message when the request failed */
  error: string | null;
  /** Open the dialog and trigger the scoring fetch for an event */
  openScoring: (eventId: string, eventTitle: string) => void;
  /** Close the dialog and reset state */
  closeScoring: () => void;
  /** Retry after an error */
  retry: () => void;
}

export interface IUseTodoScoringOptions {
  /**
   * Base URL of the BFF API (e.g. "https://spe-api-dev-67e2xz.azurewebsites.net").
   * When omitted, mock data is used after a simulated 1.2s delay (NFR-06 fallback).
   */
  bffBaseUrl?: string;
  /**
   * Bearer token for authenticating against the BFF.
   * Typically obtained from the MSAL auth provider.
   */
  accessToken?: string;
}

// ---------------------------------------------------------------------------
// BFF endpoint path
// ---------------------------------------------------------------------------

const SCORES_ENDPOINT_TEMPLATE = '/api/workspace/events/{id}/scores';

/** Simulated delay for mock data — long enough to show the loading state */
const MOCK_DELAY_MS = 1200;

// ---------------------------------------------------------------------------
// Mock data (NFR-06: deterministic fallback)
// ---------------------------------------------------------------------------

/**
 * Static mock scoring result used when the BFF endpoint is unavailable.
 * Deterministic so tests and demos are reliable.
 */
const mockScoringResult: Omit<ITodoScoringResult, 'isMockData'> = {
  priority: {
    score: 85,
    level: 'High',
    factors: [
      { name: 'Overdue days',       value: '12 days',  points: 20 },
      { name: 'Budget utilization', value: '87%',      points: 15 },
      { name: 'Grades below C',     value: '2 grades', points: 10 },
      { name: 'Deadline proximity', value: '5 days',   points: 15 },
      { name: 'Matter value tier',  value: 'High',     points: 10 },
      { name: 'Pending invoices',   value: '3',        points: 15 },
    ],
  },
  effort: {
    score: 72,
    level: 'Med',
    baseEffort: 30,
    multipliers: [
      { name: 'Multiple parties',   value: 1.3, applied: true  },
      { name: 'Cross-jurisdiction', value: 1.2, applied: false },
      { name: 'Regulatory',         value: 1.1, applied: true  },
      { name: 'High value',         value: 1.2, applied: true  },
      { name: 'Time-sensitive',     value: 1.3, applied: false },
    ],
  },
  analysis:
    'This matter requires immediate attention due to approaching deadline and high budget ' +
    'utilization. Multiple overdue items and pending invoices suggest coordination gaps. ' +
    'Consider escalating priority and reassigning tasks to resolve bottlenecks before the ' +
    'deadline.',
  suggestedActions: [
    { label: 'Escalate priority', icon: 'ArrowUpRegular'     },
    { label: 'Reassign task',     icon: 'PersonSwapRegular'  },
    { label: 'Review budget',     icon: 'MoneyRegular'       },
  ],
};

// ---------------------------------------------------------------------------
// Hook implementation
// ---------------------------------------------------------------------------

export function useTodoScoring(options: IUseTodoScoringOptions = {}): IUseTodoScoringResult {
  const { bffBaseUrl, accessToken } = options;

  const [isOpen, setIsOpen]                 = useState<boolean>(false);
  const [eventContext, setEventContext]      = useState<ITodoScoringEventContext | null>(null);
  const [result, setResult]                 = useState<ITodoScoringResult | null>(null);
  const [isLoading, setIsLoading]           = useState<boolean>(false);
  const [error, setError]                   = useState<string | null>(null);

  // Hold AbortController so in-flight BFF requests are cancelled on dialog close
  const abortRef      = useRef<AbortController | null>(null);
  // Hold a timer reference for the mock delay so it can be cleared on close
  const mockTimerRef  = useRef<ReturnType<typeof setTimeout> | null>(null);

  // ---------------------------------------------------------------------------
  // Core fetch function
  // ---------------------------------------------------------------------------

  const fetchScoring = useCallback(
    (context: ITodoScoringEventContext) => {
      setIsLoading(true);
      setError(null);
      setResult(null);

      // --- No BFF configured: use mock fallback after simulated delay ---
      if (!bffBaseUrl) {
        mockTimerRef.current = setTimeout(() => {
          setResult({ ...mockScoringResult, isMockData: true });
          setIsLoading(false);
        }, MOCK_DELAY_MS);
        return;
      }

      // --- BFF path: GET scoring endpoint ---
      if (abortRef.current) {
        abortRef.current.abort();
      }
      const controller = new AbortController();
      abortRef.current = controller;

      const url = `${bffBaseUrl.replace(/\/$/, '')}${SCORES_ENDPOINT_TEMPLATE.replace('{id}', encodeURIComponent(context.eventId))}`;
      const headers: HeadersInit = { 'Content-Type': 'application/json' };
      if (accessToken) {
        headers['Authorization'] = `Bearer ${accessToken}`;
      }

      fetch(url, { method: 'GET', headers, signal: controller.signal })
        .then(async (response) => {
          if (!response.ok) {
            let message = `Scoring data unavailable (HTTP ${response.status})`;
            try {
              const problem = await response.json();
              if (problem?.title)  { message = problem.title; }
              else if (problem?.detail) { message = problem.detail; }
            } catch {
              // ignore parse errors — keep the generic message
            }
            throw new Error(message);
          }
          return response.json() as Promise<Omit<ITodoScoringResult, 'isMockData'>>;
        })
        .then((data) => {
          setResult({ ...data, isMockData: false });
          setIsLoading(false);
        })
        .catch((err: Error) => {
          if (err.name === 'AbortError') { return; } // dialog closed — ignore
          // BFF call failed: fall back to mock data so the UI remains functional (NFR-06)
          if (process.env.NODE_ENV !== 'production') {
            // eslint-disable-next-line no-console
            console.warn(
              '[useTodoScoring] BFF call failed, falling back to mock data:',
              err.message
            );
          }
          setResult({ ...mockScoringResult, isMockData: true });
          setIsLoading(false);
        });
    },
    [bffBaseUrl, accessToken]
  );

  // ---------------------------------------------------------------------------
  // Public API
  // ---------------------------------------------------------------------------

  const openScoring = useCallback(
    (eventId: string, eventTitle: string) => {
      const context: ITodoScoringEventContext = { eventId, eventTitle };
      setEventContext(context);
      setIsOpen(true);
      fetchScoring(context);
    },
    [fetchScoring]
  );

  const closeScoring = useCallback(() => {
    // Cancel any in-flight request
    if (abortRef.current) {
      abortRef.current.abort();
      abortRef.current = null;
    }
    // Cancel any pending mock timer
    if (mockTimerRef.current !== null) {
      clearTimeout(mockTimerRef.current);
      mockTimerRef.current = null;
    }
    setIsOpen(false);
    setEventContext(null);
    setResult(null);
    setIsLoading(false);
    setError(null);
  }, []);

  const retry = useCallback(() => {
    if (!eventContext) { return; }
    fetchScoring(eventContext);
  }, [eventContext, fetchScoring]);

  return {
    isOpen,
    eventContext,
    result,
    isLoading,
    error,
    openScoring,
    closeScoring,
    retry,
  };
}
