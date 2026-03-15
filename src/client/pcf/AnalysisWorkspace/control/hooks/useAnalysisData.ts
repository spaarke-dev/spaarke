/**
 * useAnalysisData Hook
 *
 * Manages analysis data loading state for the AnalysisWorkspace control.
 * Handles fetching the analysis record from Dataverse, resolving related
 * document fields, detecting chat history for resume dialog, and detecting
 * draft analyses that need auto-execution.
 *
 * Follows UseXxxOptions/UseXxxResult convention from shared library hooks.
 * React 16/17 compatible (ADR-022).
 *
 * @version 1.0.0
 */

import * as React from 'react';
import { IAnalysis, IChatMessage } from '../types';
import { logInfo, logError } from '../utils/logger';
import { markdownToHtml, isMarkdown } from '../utils/markdownToHtml';

// ─────────────────────────────────────────────────────────────────────────────
// Utilities
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Check if WebAPI is available (not in design-time/editor mode).
 * Custom Page editor doesn't implement WebAPI methods.
 */
export function isWebApiAvailable(webApi: ComponentFramework.WebApi | undefined): boolean {
  if (!webApi) return false;

  try {
    if (typeof webApi.retrieveRecord !== 'function' || typeof webApi.updateRecord !== 'function') {
      return false;
    }
    return true;
  } catch {
    return false;
  }
}

/**
 * Extract error message from various error types (WebAPI, Error, string, object).
 */
function getErrorMessage(err: unknown): string {
  if (err instanceof Error) {
    return err.message;
  }
  if (typeof err === 'string') {
    return err;
  }
  if (err && typeof err === 'object') {
    const errObj = err as Record<string, unknown>;
    if (typeof errObj.message === 'string') {
      return errObj.message;
    }
    if (typeof errObj.errorCode === 'string' || typeof errObj.errorCode === 'number') {
      return `Error ${errObj.errorCode}: ${errObj.message || 'Unknown error'}`;
    }
    try {
      return JSON.stringify(err);
    } catch {
      return 'Unknown error occurred';
    }
  }
  return 'Unknown error occurred';
}

/**
 * Convert statuscode value to display string.
 * Uses standard Power Apps Status Reason (statuscode) field values.
 */
function getStatusString(status: number): string {
  switch (status) {
    case 1:
      return 'Draft';
    case 100000001:
      return 'In Progress';
    case 100000002:
      return 'In Review';
    case 2:
      return 'Closed';
    case 100000003:
      return 'Completed';
    default:
      return 'Unknown';
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Pending execution state -- stores analysis data when auth wasn't ready at load time.
 */
export interface PendingExecution {
  analysis: IAnalysis;
  docId: string;
}

/**
 * Options for the useAnalysisData hook
 */
export interface UseAnalysisDataOptions {
  /** Analysis record ID from Dataverse */
  analysisId: string;

  /** Initial document ID from PCF props */
  documentId: string;

  /** Dataverse WebAPI reference for querying records */
  webApi: ComponentFramework.WebApi;

  /** Whether to use legacy chat (affects chat history parsing) */
  useLegacyChat: boolean;

  /** Whether @spaarke/auth is initialized (needed for pending execution check) */
  isAuthInitialized: boolean;

  /** Callback to set the playbook ID (from analysis record's _sprk_playbook_value) */
  setPlaybookId: (id: string | null) => void;

  /** Callback to set the resolved document ID */
  setDocumentId: (id: string) => void;

  /** Callback to resolve document details from a document ID */
  resolveFromDocumentId: (docId: string) => Promise<void>;

  /** Callback to set the working document content */
  setWorkingDocument: (content: string) => void;

  /** Callback to set session resumed state (from useChatState) */
  setIsSessionResumed: (resumed: boolean) => void;

  /** Callback to set pending chat history (from useChatState) */
  setPendingChatHistory: (history: IChatMessage[] | null) => void;

  /** Callback to show/hide resume dialog (from useChatState) */
  setShowResumeDialog: (show: boolean) => void;

  /** Callback to notify parent of status changes */
  onStatusChange: (status: string) => void;
}

/**
 * Result returned by the useAnalysisData hook
 */
export interface UseAnalysisDataResult {
  /** Whether the analysis is currently loading */
  isLoading: boolean;

  /** Set loading state */
  setIsLoading: (loading: boolean) => void;

  /** Error message from loading, if any */
  error: string | null;

  /** Set the error message */
  setError: (error: string | null) => void;

  /** The loaded analysis record (null until loaded) */
  analysis: IAnalysis | null;

  /** Set the analysis record */
  setAnalysis: (analysis: IAnalysis | null) => void;

  /** Pending execution data -- set when a draft analysis needs execution but auth wasn't ready */
  pendingExecution: PendingExecution | null;

  /** Clear pending execution (after execution begins) */
  clearPendingExecution: () => void;

  /** Reload the analysis from Dataverse */
  loadAnalysis: () => Promise<void>;
}

// ─────────────────────────────────────────────────────────────────────────────
// Hook
// ─────────────────────────────────────────────────────────────────────────────

/**
 * useAnalysisData Hook
 *
 * Encapsulates analysis data loading state: isLoading, error, analysis record,
 * and pending execution detection. Delegates working document, chat, and save
 * state to their respective hooks.
 *
 * @example
 * ```tsx
 * const {
 *   isLoading, error, analysis, pendingExecution,
 *   setError, setIsLoading, clearPendingExecution, loadAnalysis,
 * } = useAnalysisData({
 *   analysisId, documentId, webApi, useLegacyChat, isAuthInitialized,
 *   setPlaybookId, setDocumentId, resolveFromDocumentId,
 *   setWorkingDocument, setIsSessionResumed, setPendingChatHistory,
 *   setShowResumeDialog, onStatusChange,
 * });
 * ```
 */
export const useAnalysisData = (options: UseAnalysisDataOptions): UseAnalysisDataResult => {
  const {
    analysisId,
    documentId,
    webApi,
    useLegacyChat,
    isAuthInitialized,
    setPlaybookId,
    setDocumentId,
    resolveFromDocumentId,
    setWorkingDocument,
    setIsSessionResumed,
    setPendingChatHistory,
    setShowResumeDialog,
    onStatusChange,
  } = options;

  // Core loading state
  const [isLoading, setIsLoading] = React.useState(true);
  const [error, setError] = React.useState<string | null>(null);
  const [analysis, setAnalysis] = React.useState<IAnalysis | null>(null);

  // Pending execution -- stores analysis data when auth wasn't ready at load time
  const [pendingExecution, setPendingExecution] = React.useState<PendingExecution | null>(null);

  /**
   * Load analysis record from Dataverse.
   * Resolves document details, parses chat history, and detects
   * draft analyses that need auto-execution.
   */
  const loadAnalysis = React.useCallback(async () => {
    try {
      setIsLoading(true);
      setError(null);

      logInfo('useAnalysisData', `Loading analysis: ${analysisId}`);

      if (!analysisId) {
        // New record - show helpful message instead of error
        logInfo('useAnalysisData', 'New record - no analysis ID yet');
        setAnalysis({
          sprk_analysisid: '',
          sprk_name: 'New Analysis',
          sprk_documentid: documentId,
          statuscode: 1, // Draft
          sprk_workingdocument: '',
          createdon: new Date().toISOString(),
          modifiedon: new Date().toISOString(),
        } as IAnalysis);
        setWorkingDocument(
          '# New Analysis\n\n**Save the record first** to begin your analysis.\n\n1. Fill in the required fields (Name, Document, Action)\n2. Click **Save**\n3. Then click **Execute Analysis** to start'
        );
        setIsLoading(false);
        return;
      }

      // Check if WebAPI is available (not in design-time/editor mode)
      if (!isWebApiAvailable(webApi)) {
        logInfo('useAnalysisData', 'Design-time mode - showing placeholder');
        setAnalysis({
          sprk_analysisid: 'design-time-placeholder',
          sprk_name: 'Analysis Preview (Design Mode)',
          sprk_documentid: documentId,
          statuscode: 1,
          sprk_workingdocument:
            '# Design Mode Preview\n\nThis is a preview in the Custom Page editor.\n\nThe actual analysis content will load at runtime.',
          createdon: new Date().toISOString(),
          modifiedon: new Date().toISOString(),
        } as IAnalysis);
        setWorkingDocument('# Design Mode Preview\n\nThis is a preview in the Custom Page editor.');
        return;
      }

      // Fetch analysis record from Dataverse
      const result = await webApi.retrieveRecord(
        'sprk_analysis',
        analysisId,
        '?$select=sprk_name,statuscode,sprk_workingdocument,sprk_chathistory,_sprk_actionid_value,_sprk_playbook_value,createdon,modifiedon,_sprk_documentid_value'
      );

      logInfo('useAnalysisData', 'Analysis loaded', result);
      logInfo(
        'useAnalysisData',
        `Chat history field: ${result.sprk_chathistory ? `exists (${result.sprk_chathistory.length} chars)` : 'null/undefined'}`
      );

      // Store playbook ID if present (for execute request)
      if (result._sprk_playbook_value) {
        setPlaybookId(result._sprk_playbook_value);
        logInfo('useAnalysisData', `Playbook loaded: ${result._sprk_playbook_value}`);
      }

      setAnalysis(result as unknown as IAnalysis);

      // Load working document - convert markdown to HTML if needed for RichTextEditor
      const savedContent = result.sprk_workingdocument || '';
      const displayContent = savedContent && isMarkdown(savedContent) ? markdownToHtml(savedContent) : savedContent;
      setWorkingDocument(displayContent);

      // Fetch document details separately if we have a document ID
      const docId = result._sprk_documentid_value;
      if (docId) {
        // Store the document ID from analysis record
        setDocumentId(docId);

        // Resolve document details (container/file IDs, name) via hook
        await resolveFromDocumentId(docId);

        // Parse chat history FIRST -- if exists, show choice dialog (ADR-023)
        let hasChatHistory = false;
        if (useLegacyChat && result.sprk_chathistory) {
          try {
            const parsed = JSON.parse(result.sprk_chathistory);
            if (Array.isArray(parsed) && parsed.length > 0) {
              setPendingChatHistory(parsed);
              setShowResumeDialog(true);
              hasChatHistory = true;
              logInfo('useAnalysisData', `Found ${parsed.length} chat messages, showing resume dialog`);
            } else {
              logInfo(
                'useAnalysisData',
                `Chat history parsed but empty or not array: ${JSON.stringify(parsed)}, enabling chat`
              );
              setIsSessionResumed(true);
            }
          } catch (e) {
            logError('useAnalysisData', 'Failed to parse chat history, enabling chat anyway', e);
            setIsSessionResumed(true);
          }
        } else {
          logInfo('useAnalysisData', 'No chat history in analysis record, auto-resuming session');
          setIsSessionResumed(true);
        }

        // Check if we need to auto-execute the analysis (Draft with empty working document)
        if (!hasChatHistory) {
          const isDraft = result.statuscode === 1;
          const hasEmptyWorkingDoc = !result.sprk_workingdocument || result.sprk_workingdocument.trim() === '';
          const actionId = result._sprk_actionid_value;
          const hasAction = !!actionId;

          logInfo(
            'useAnalysisData',
            `Execute check: statuscode=${result.statuscode} (isDraft=${isDraft}), hasEmptyWorkingDoc=${hasEmptyWorkingDoc}, actionId=${actionId} (hasAction=${hasAction})`
          );

          if (isDraft && hasEmptyWorkingDoc && hasAction) {
            logInfo(
              'useAnalysisData',
              `Draft analysis with empty working document - auth ready: ${isAuthInitialized}`
            );

            // Store for execution (the parent component will trigger executeAnalysis)
            setPendingExecution({
              analysis: result as unknown as IAnalysis,
              docId,
            });

            if (isAuthInitialized) {
              // Auth ready -- parent should execute immediately
              setIsLoading(false);
              return;
            }
            // Otherwise pendingExecution will be picked up when auth initializes
          }
        }
      }

      onStatusChange(getStatusString(result.statuscode));
    } catch (err) {
      // Handle "not implemented" errors from design-time environment
      const errorMessage = getErrorMessage(err);
      if (errorMessage.toLowerCase().includes('not implemented')) {
        logInfo('useAnalysisData', 'Design-time mode detected via error');
        setAnalysis({
          sprk_analysisid: 'design-time-placeholder',
          sprk_name: 'Analysis Preview (Design Mode)',
          sprk_documentid: documentId,
          statuscode: 1,
          sprk_workingdocument: '# Design Mode Preview\n\nThis is a preview in the Custom Page editor.',
          createdon: new Date().toISOString(),
          modifiedon: new Date().toISOString(),
        } as IAnalysis);
        setWorkingDocument('# Design Mode Preview\n\nThis is a preview in the Custom Page editor.');
        return;
      }
      logError('useAnalysisData', 'Failed to load analysis', err);
      setError(`Failed to load analysis: ${errorMessage}`);
    } finally {
      setIsLoading(false);
    }
  }, [analysisId, documentId, webApi, useLegacyChat, isAuthInitialized, setPlaybookId, setDocumentId, resolveFromDocumentId, setWorkingDocument, setIsSessionResumed, setPendingChatHistory, setShowResumeDialog, onStatusChange]);

  // Load analysis data on mount
  React.useEffect(() => {
    loadAnalysis();
  }, [analysisId]);

  return {
    isLoading,
    setIsLoading,
    error,
    setError,
    analysis,
    setAnalysis,
    pendingExecution,
    clearPendingExecution: React.useCallback(() => setPendingExecution(null), []),
    loadAnalysis,
  };
};

export default useAnalysisData;
