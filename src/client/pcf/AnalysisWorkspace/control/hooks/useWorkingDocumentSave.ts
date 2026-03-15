/**
 * useWorkingDocumentSave Hook
 *
 * Manages auto-save lifecycle for the working document and chat history.
 * Handles dirty tracking, 3-second debounce timer for document changes,
 * 2-second debounce timer for chat changes, and Dataverse persistence.
 *
 * Follows UseXxxOptions/UseXxxResult convention from shared library hooks.
 * React 16/17 compatible (ADR-022).
 *
 * @version 1.0.0
 */

import * as React from 'react';
import { IChatMessage } from '../types';
import { logInfo, logError } from '../utils/logger';

/**
 * Check if WebAPI is available (not in design-time/editor mode).
 * Custom Page editor doesn't implement WebAPI methods.
 */
function isWebApiAvailable(webApi: ComponentFramework.WebApi | undefined): boolean {
  if (!webApi) return false;

  try {
    // In design-time, retrieveRecord exists but throws "not implemented"
    // We check if the object has the expected shape
    return typeof webApi.updateRecord === 'function';
  } catch {
    return false;
  }
}

/**
 * Options for the useWorkingDocumentSave hook
 */
export interface UseWorkingDocumentSaveOptions {
  /** The analysis record ID in Dataverse */
  analysisId: string;

  /** Dataverse WebAPI for persistence */
  webApi: ComponentFramework.WebApi;

  /** Current working document content */
  workingDocument: string;

  /** Setter for working document content (called by handleDocumentChange) */
  setWorkingDocument: (content: string) => void;

  /** Current chat messages (read via ref to avoid stale closures) */
  chatMessages: IChatMessage[];

  /** Whether chat has unsaved changes (from useChatState hook) */
  isChatDirty: boolean;

  /** Mark chat as clean after save (from useChatState hook) */
  setIsChatDirty: (dirty: boolean) => void;

  /** Callback to notify parent of working document changes */
  onWorkingDocumentChange: (content: string) => void;

  /** Callback to notify parent of chat history changes */
  onChatHistoryChange: (history: string) => void;
}

/**
 * Result returned by the useWorkingDocumentSave hook
 */
export interface UseWorkingDocumentSaveResult {
  /** Whether the working document has unsaved changes */
  isDirty: boolean;

  /** Whether a save operation is in progress */
  isSaving: boolean;

  /** Timestamp of the last successful save */
  lastSaved: Date | null;

  /** Mark the document as dirty (call after content changes) */
  setIsDirty: (dirty: boolean) => void;

  /** Set the last saved timestamp (for external save operations like executeAnalysis) */
  setLastSaved: (date: Date | null) => void;

  /** Trigger a manual save */
  handleManualSave: () => void;

  /** Handle document content change — sets dirty flag */
  handleDocumentChange: (content: string) => void;

  /** Directly save analysis state (for external callers like executeAnalysis) */
  saveAnalysisState: () => Promise<void>;
}

/**
 * useWorkingDocumentSave Hook
 *
 * Encapsulates the auto-save debounce timer and dirty tracking for
 * both the working document and chat history.
 *
 * @example
 * ```tsx
 * const {
 *   isDirty, isSaving, lastSaved,
 *   setIsDirty, setLastSaved,
 *   handleManualSave, handleDocumentChange, saveAnalysisState,
 * } = useWorkingDocumentSave({
 *   analysisId,
 *   webApi,
 *   workingDocument,
 *   setWorkingDocument,
 *   chatMessages,
 *   isChatDirty,
 *   setIsChatDirty,
 *   onWorkingDocumentChange,
 *   onChatHistoryChange,
 * });
 * ```
 */
export const useWorkingDocumentSave = (options: UseWorkingDocumentSaveOptions): UseWorkingDocumentSaveResult => {
  const {
    analysisId,
    webApi,
    workingDocument,
    setWorkingDocument,
    chatMessages,
    isChatDirty,
    setIsChatDirty,
    onWorkingDocumentChange,
    onChatHistoryChange,
  } = options;

  // Save tracking state
  const [isDirty, setIsDirty] = React.useState(false);
  const [isSaving, setIsSaving] = React.useState(false);
  const [lastSaved, setLastSaved] = React.useState<Date | null>(null);

  // Ref to track current chatMessages for save operations (avoids stale closure)
  const chatMessagesRef = React.useRef<IChatMessage[]>([]);

  // Keep chatMessagesRef in sync with state (MUST run before auto-save effect)
  React.useEffect(() => {
    chatMessagesRef.current = chatMessages;
  }, [chatMessages]);

  /**
   * Save analysis state (working document + chat history) to Dataverse.
   * Called by auto-save effects when either workingDocument or chatMessages change.
   */
  const saveAnalysisState = React.useCallback(async () => {
    if (!analysisId || isSaving) return;

    // Skip save in design-time mode
    if (!isWebApiAvailable(webApi)) {
      logInfo('useWorkingDocumentSave', 'Design-time mode - save skipped');
      setIsDirty(false);
      setIsChatDirty(false);
      setLastSaved(new Date());
      return;
    }

    try {
      setIsSaving(true);
      logInfo('useWorkingDocumentSave', 'Saving analysis state (working document + chat history)');

      await webApi.updateRecord('sprk_analysis', analysisId, {
        sprk_workingdocument: workingDocument,
        sprk_chathistory: JSON.stringify(chatMessagesRef.current),
      });

      setIsDirty(false);
      setIsChatDirty(false);
      setLastSaved(new Date());
      logInfo('useWorkingDocumentSave', 'Analysis state saved');
    } catch (err) {
      // Handle "not implemented" errors from design-time environment
      const errorMessage = err instanceof Error ? err.message : String(err);
      if (errorMessage.toLowerCase().includes('not implemented')) {
        logInfo('useWorkingDocumentSave', 'Design-time mode - save skipped');
        setIsDirty(false);
        setIsChatDirty(false);
        return;
      }
      logError('useWorkingDocumentSave', 'Failed to save analysis state', err);
    } finally {
      setIsSaving(false);
    }
  }, [analysisId, isSaving, webApi, workingDocument, setIsChatDirty]);

  // Auto-save effect for working document changes (3-second debounce)
  React.useEffect(() => {
    if (isDirty && !isSaving) {
      const timer = setTimeout(() => {
        saveAnalysisState();
      }, 3000); // Auto-save after 3 seconds of no changes
      return () => clearTimeout(timer);
    }
  }, [workingDocument, isDirty]);

  // Auto-save effect for chat message changes (2-second debounce)
  // Triggers save when isChatDirty is set (after user sends message or AI responds)
  React.useEffect(() => {
    if (!isChatDirty) return;

    const timer = setTimeout(() => {
      logInfo('useWorkingDocumentSave', `Auto-saving chat history (${chatMessages.length} messages)`);
      saveAnalysisState();
    }, 2000); // Save 2 seconds after chat changes
    return () => clearTimeout(timer);
  }, [isChatDirty, chatMessages.length]);

  // Notify parent of working document changes
  React.useEffect(() => {
    onWorkingDocumentChange(workingDocument);
  }, [workingDocument]);

  // Notify parent of chat history changes
  React.useEffect(() => {
    onChatHistoryChange(JSON.stringify(chatMessages));
  }, [chatMessages]);

  // Handle document content change — updates content and sets dirty flag
  const handleDocumentChange = React.useCallback((content: string) => {
    setWorkingDocument(content);
    setIsDirty(true);
  }, [setWorkingDocument]);

  // Trigger a manual save
  const handleManualSave = React.useCallback(() => {
    saveAnalysisState();
  }, [saveAnalysisState]);

  return {
    isDirty,
    isSaving,
    lastSaved,
    setIsDirty,
    setLastSaved,
    handleManualSave,
    handleDocumentChange,
    saveAnalysisState,
  };
};

export default useWorkingDocumentSave;
