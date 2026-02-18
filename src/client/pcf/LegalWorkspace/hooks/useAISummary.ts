/**
 * useAISummary — hook managing AI Summary dialog state and BFF API calls.
 *
 * Responsibilities:
 *   1. Track dialog open/closed state per event (keyed on eventId).
 *   2. Call BFF AI Summary endpoint (POST /api/workspace/ai-summary).
 *   3. Fall back to deterministic mock data when the BFF is unavailable (NFR-06).
 *   4. Expose loading, result, error, and retry state to the dialog component.
 *
 * Architecture constraints:
 *   - AI calls MUST go through BFF API (ADR-013) — NEVER call Azure AI directly from client.
 *   - BFF base URL is optional; when absent, mock fallback activates automatically.
 *   - Mock data is deterministic by event type so tests and demos work reliably.
 *   - Uses AbortController so in-flight requests are cancelled on dialog close.
 *
 * Usage:
 *   const {
 *     isOpen, result, isLoading, error,
 *     openSummary, closeSummary, retry,
 *   } = useAISummary({ bffBaseUrl, accessToken });
 *
 *   // Open dialog for an event
 *   openSummary(event.sprk_eventid, event.sprk_type, event.sprk_subject);
 */

import { useState, useCallback, useRef } from 'react';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** A single suggested action item with its icon identifier */
export interface IAISuggestedAction {
  /** Display label */
  label: string;
  /** Action type key used by the dialog to dispatch the action */
  type: 'reply' | 'create-task' | 'open-matter' | 'review' | 'approve';
  /** Icon name from @fluentui/react-icons (as string key for lazy resolution) */
  iconKey: 'ArrowReplyRegular' | 'TaskListSquareRegular' | 'FolderOpenRegular' | 'DocumentCheckmarkRegular' | 'CheckmarkCircleRegular';
}

/** AI Summary result — mirrors the BFF IAiSummaryResponse shape */
export interface IAISummaryResult {
  /** AI-generated summary text */
  summary: string;
  /** Suggested actions relevant to the event type */
  suggestedActions: IAISuggestedAction[];
  /** Confidence score (0-1) from the AI model */
  confidence: number;
  /** Whether this result came from mock data (true) or the live BFF (false) */
  isMockData: boolean;
}

/** Context passed to openSummary to identify the event and its type */
export interface IAISummaryEventContext {
  eventId: string;
  eventType: string | undefined;
  eventTitle: string;
}

/** Public interface of the hook */
export interface IUseAISummaryResult {
  /** Whether the dialog is currently open */
  isOpen: boolean;
  /** The event context for the open dialog (null when closed) */
  eventContext: IAISummaryEventContext | null;
  /** AI Summary result when available */
  result: IAISummaryResult | null;
  /** True while the BFF fetch or mock delay is in progress */
  isLoading: boolean;
  /** User-friendly error message when the request failed */
  error: string | null;
  /** Open the dialog and trigger the AI summary fetch for an event */
  openSummary: (eventId: string, eventType: string | undefined, eventTitle: string) => void;
  /** Close the dialog and reset state */
  closeSummary: () => void;
  /** Retry after an error */
  retry: () => void;
}

export interface IUseAISummaryOptions {
  /**
   * Base URL of the BFF API (e.g. "https://spe-api-dev-67e2xz.azurewebsites.net").
   * When omitted, mock data is used after a simulated 1.5s delay (NFR-06 fallback).
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

const AI_SUMMARY_ENDPOINT = '/api/workspace/ai-summary';

/** Simulated delay for mock data — long enough to show the loading state */
const MOCK_DELAY_MS = 1500;

// ---------------------------------------------------------------------------
// Mock data by event type (NFR-06: deterministic fallback)
// ---------------------------------------------------------------------------

/** Deterministic mock summary text keyed by normalised event type */
const MOCK_SUMMARIES: Record<string, string> = {
  email:
    'This email from opposing counsel discusses settlement terms for the Johnson v. Smith matter. ' +
    'The proposed settlement of $250,000 includes a confidentiality clause and mutual release. ' +
    'A response deadline of 5 business days has been indicated. Immediate review is recommended.',

  document:
    'This document is a draft motion for summary judgment containing 15 pages. ' +
    'Key arguments center on lack of disputed material facts and precedent from prior circuit decisions. ' +
    'Counsel should review the evidentiary support section before filing. No signature block is present yet.',

  documentreview:
    'This document review item covers the amended contract for external counsel engagement. ' +
    'Notable changes include revised hourly rates effective Q1 2026 and updated scope-of-work provisions. ' +
    'Rate increases average 8% over the prior agreement. Approval from the budget holder is required.',

  invoice:
    'This invoice from external counsel totals $12,500 for work performed in January 2026. ' +
    'The largest line item is deposition preparation at $4,200. Guideline compliance is 94%. ' +
    'Two line items may require justification under the approved billing guidelines.',

  task:
    'This task requires preparation of the discovery response package for the Wilson matter. ' +
    'The deadline is in 3 business days and 4 supporting documents still need to be attached. ' +
    'The assigned attorney has not acknowledged receipt. Escalation may be needed.',

  meeting:
    'This meeting summary covers the client status call for the Chen corporate transaction. ' +
    'Key outcomes: client approved the revised timeline, due diligence checklist was shared, ' +
    'and a follow-up call was scheduled for next Thursday at 2 PM EST.',

  analysis:
    'This AI analysis identified 3 billing anomalies and 2 guideline violations in the reviewed matter. ' +
    'Total risk exposure from the anomalies is estimated at $8,400. ' +
    'Recommended action: review flagged line items with external counsel before next billing cycle.',

  'financial-alert':
    'This financial alert was triggered because the matter has reached 92% of its approved budget. ' +
    'At the current spend rate, the budget will be exhausted within 18 days. ' +
    'Authorization for a budget increase or scope reduction is required promptly.',

  'status-change':
    'This status change notification indicates the matter was moved from Active to Under Review. ' +
    'The change was initiated by the lead attorney following receipt of a court filing. ' +
    'A status meeting has been automatically scheduled for review.',
};

/** Fallback summary for event types not covered in the map */
const DEFAULT_MOCK_SUMMARY =
  'This item requires your attention based on its priority score and recent activity. ' +
  'AI analysis indicates action may be needed within the next 2 business days. ' +
  'Please review the full item details and coordinate with your team as needed.';

/** Suggested actions by event type */
const MOCK_SUGGESTED_ACTIONS: Record<string, IAISuggestedAction[]> = {
  email: [
    { label: 'Reply', type: 'reply', iconKey: 'ArrowReplyRegular' },
    { label: 'Create task', type: 'create-task', iconKey: 'TaskListSquareRegular' },
    { label: 'Open matter', type: 'open-matter', iconKey: 'FolderOpenRegular' },
  ],
  document: [
    { label: 'Review', type: 'review', iconKey: 'DocumentCheckmarkRegular' },
    { label: 'Create task', type: 'create-task', iconKey: 'TaskListSquareRegular' },
    { label: 'Open matter', type: 'open-matter', iconKey: 'FolderOpenRegular' },
  ],
  documentreview: [
    { label: 'Review', type: 'review', iconKey: 'DocumentCheckmarkRegular' },
    { label: 'Create task', type: 'create-task', iconKey: 'TaskListSquareRegular' },
    { label: 'Open matter', type: 'open-matter', iconKey: 'FolderOpenRegular' },
  ],
  invoice: [
    { label: 'Approve', type: 'approve', iconKey: 'CheckmarkCircleRegular' },
    { label: 'Create task', type: 'create-task', iconKey: 'TaskListSquareRegular' },
    { label: 'Open matter', type: 'open-matter', iconKey: 'FolderOpenRegular' },
  ],
  task: [
    { label: 'Create task', type: 'create-task', iconKey: 'TaskListSquareRegular' },
    { label: 'Open matter', type: 'open-matter', iconKey: 'FolderOpenRegular' },
  ],
};

/** Default suggested actions for event types without a specific mapping */
const DEFAULT_SUGGESTED_ACTIONS: IAISuggestedAction[] = [
  { label: 'Create task', type: 'create-task', iconKey: 'TaskListSquareRegular' },
  { label: 'Open matter', type: 'open-matter', iconKey: 'FolderOpenRegular' },
];

/**
 * Build deterministic mock result for a given event type.
 * Confidence varies by event type to simulate realistic AI output.
 */
function buildMockResult(eventType: string | undefined): IAISummaryResult {
  const normalised = (eventType ?? '').toLowerCase();
  return {
    summary: MOCK_SUMMARIES[normalised] ?? DEFAULT_MOCK_SUMMARY,
    suggestedActions: MOCK_SUGGESTED_ACTIONS[normalised] ?? DEFAULT_SUGGESTED_ACTIONS,
    confidence: normalised === 'email' || normalised === 'invoice' ? 0.92 : 0.78,
    isMockData: true,
  };
}

// ---------------------------------------------------------------------------
// Hook implementation
// ---------------------------------------------------------------------------

export function useAISummary(options: IUseAISummaryOptions = {}): IUseAISummaryResult {
  const { bffBaseUrl, accessToken } = options;

  const [isOpen, setIsOpen] = useState<boolean>(false);
  const [eventContext, setEventContext] = useState<IAISummaryEventContext | null>(null);
  const [result, setResult] = useState<IAISummaryResult | null>(null);
  const [isLoading, setIsLoading] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);

  // Hold AbortController so in-flight BFF requests are cancelled on dialog close
  const abortRef = useRef<AbortController | null>(null);
  // Hold a timer reference for the mock delay so it can be cleared on close
  const mockTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // ---------------------------------------------------------------------------
  // Core fetch function — called by openSummary and retry
  // ---------------------------------------------------------------------------

  const fetchSummary = useCallback(
    (context: IAISummaryEventContext) => {
      setIsLoading(true);
      setError(null);
      setResult(null);

      // --- No BFF configured: use mock fallback after simulated delay ---
      if (!bffBaseUrl) {
        mockTimerRef.current = setTimeout(() => {
          setResult(buildMockResult(context.eventType));
          setIsLoading(false);
        }, MOCK_DELAY_MS);
        return;
      }

      // --- BFF path: POST to AI Summary endpoint ---
      if (abortRef.current) {
        abortRef.current.abort();
      }
      const controller = new AbortController();
      abortRef.current = controller;

      const url = `${bffBaseUrl.replace(/\/$/, '')}${AI_SUMMARY_ENDPOINT}`;
      const headers: HeadersInit = { 'Content-Type': 'application/json' };
      if (accessToken) {
        headers['Authorization'] = `Bearer ${accessToken}`;
      }

      const body = JSON.stringify({
        entityType: context.eventType ?? 'unknown',
        entityId: context.eventId,
        context: { title: context.eventTitle },
      });

      fetch(url, { method: 'POST', headers, body, signal: controller.signal })
        .then(async (response) => {
          if (!response.ok) {
            let message = `AI Summary unavailable (HTTP ${response.status})`;
            try {
              const problem = await response.json();
              if (problem?.title) message = problem.title;
              else if (problem?.detail) message = problem.detail;
            } catch {
              // ignore parse errors — keep the generic message
            }
            throw new Error(message);
          }
          return response.json() as Promise<{ summary: string; suggestedActions?: string[]; confidence?: number }>;
        })
        .then((data) => {
          // Map the BFF response shape to our richer local type.
          // The BFF returns generic string[] for suggested actions; we map them
          // to typed objects here, or fall back to event-type defaults.
          const normalised = (context.eventType ?? '').toLowerCase();
          const actions =
            MOCK_SUGGESTED_ACTIONS[normalised] ?? DEFAULT_SUGGESTED_ACTIONS;

          setResult({
            summary: data.summary,
            suggestedActions: actions,
            confidence: data.confidence ?? 0.8,
            isMockData: false,
          });
          setIsLoading(false);
        })
        .catch((err: Error) => {
          if (err.name === 'AbortError') return; // dialog closed — ignore
          // BFF call failed: fall back to mock data so the UI remains functional (NFR-06)
          console.warn(
            '[useAISummary] BFF call failed, falling back to mock data:',
            err.message
          );
          setResult(buildMockResult(context.eventType));
          setIsLoading(false);
        });
    },
    [bffBaseUrl, accessToken]
  );

  // ---------------------------------------------------------------------------
  // Public API
  // ---------------------------------------------------------------------------

  const openSummary = useCallback(
    (eventId: string, eventType: string | undefined, eventTitle: string) => {
      const context: IAISummaryEventContext = { eventId, eventType, eventTitle };
      setEventContext(context);
      setIsOpen(true);
      fetchSummary(context);
    },
    [fetchSummary]
  );

  const closeSummary = useCallback(() => {
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
    if (!eventContext) return;
    fetchSummary(eventContext);
  }, [eventContext, fetchSummary]);

  return {
    isOpen,
    eventContext,
    result,
    isLoading,
    error,
    openSummary,
    closeSummary,
    retry,
  };
}
