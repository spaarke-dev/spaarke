/**
 * useChatState Hook
 *
 * Manages chat session state for the AnalysisWorkspace control.
 * Handles chat messages, streaming response, session resume detection,
 * resume confirmation dialog, and chat input lifecycle.
 *
 * Follows UseXxxOptions/UseXxxResult convention from shared library hooks.
 * React 16/17 compatible (ADR-022).
 *
 * @version 1.0.0
 */

import * as React from 'react';
import { IChatMessage } from '../types';
import { logInfo, logError } from '../utils/logger';
import type { ISseStreamState, ISseStreamActions } from './useSseStream';
import type { IChatSession } from '@spaarke/ui-components/dist/components/SprkChat';

/**
 * Options for the useChatState hook
 */
export interface UseChatStateOptions {
  /** The analysis record ID in Dataverse */
  analysisId: string;

  /** BFF API base URL for the resume endpoint */
  apiBaseUrl: string;

  /** Resolved document ID for resume request */
  resolvedDocumentId: string;

  /** Resolved document name for resume request */
  resolvedDocumentName: string;

  /** Current working document content (sent in resume request) */
  workingDocument: string;

  /** Whether to use legacy chat (affects chat history parsing) */
  useLegacyChat: boolean;

  /** Callback to acquire an access token */
  getAccessToken: (() => Promise<string>) | undefined;

  /** SSE stream state (from useSseStream hook) */
  sseState: ISseStreamState;

  /** SSE stream actions (from useSseStream hook) */
  sseActions: ISseStreamActions;

  /** Callback from auth hook to set SprkChat session ID */
  setSprkChatSessionId: (sessionId: string | undefined) => void;
}

/**
 * Result returned by the useChatState hook
 */
export interface UseChatStateResult {
  /** Current chat messages */
  chatMessages: IChatMessage[];

  /** Set chat messages directly (used by loadAnalysis for history parsing) */
  setChatMessages: React.Dispatch<React.SetStateAction<IChatMessage[]>>;

  /** Current streaming response text (accumulates during AI response) */
  streamingResponse: string;

  /** Set streaming response (used by SSE callbacks) */
  setStreamingResponse: React.Dispatch<React.SetStateAction<string>>;

  /** Whether the session has been resumed (chat is usable) */
  isSessionResumed: boolean;

  /** Set session resumed state (used by loadAnalysis) */
  setIsSessionResumed: (resumed: boolean) => void;

  /** Whether a resume operation is in progress */
  isResumingSession: boolean;

  /** Whether the resume confirmation dialog is visible */
  showResumeDialog: boolean;

  /** Set whether to show the resume dialog (used by loadAnalysis) */
  setShowResumeDialog: (show: boolean) => void;

  /** Pending chat history loaded from Dataverse (shown in resume dialog) */
  pendingChatHistory: IChatMessage[] | null;

  /** Set pending chat history (used by loadAnalysis) */
  setPendingChatHistory: (history: IChatMessage[] | null) => void;

  /** Whether chat history has unsaved changes */
  isChatDirty: boolean;

  /** Mark chat as dirty (triggers auto-save via save hook) */
  setIsChatDirty: (dirty: boolean) => void;

  /** Current chat input text */
  chatInput: string;

  /** Set chat input text */
  setChatInput: (input: string) => void;

  /** Handle resume with existing chat history */
  handleResumeWithHistory: () => void;

  /** Handle starting a fresh session (discard history) */
  handleStartFresh: () => void;

  /** Handle dismissing the resume dialog (starts fresh by default) */
  handleDismissResumeDialog: () => void;

  /** Handle sending a chat message */
  handleSendMessage: () => Promise<void>;

  /** Handle keydown in chat input (Enter to send) */
  handleChatKeyDown: (e: React.KeyboardEvent) => void;

  /** Handle SprkChat session creation */
  handleSprkChatSessionCreated: (session: IChatSession) => void;
}

/**
 * useChatState Hook
 *
 * Encapsulates all chat-related state and session resume logic.
 *
 * @example
 * ```tsx
 * const {
 *   chatMessages, streamingResponse,
 *   isSessionResumed, isResumingSession, showResumeDialog, pendingChatHistory,
 *   chatInput, setChatInput,
 *   handleResumeWithHistory, handleStartFresh, handleSendMessage,
 *   handleChatKeyDown, handleSprkChatSessionCreated,
 * } = useChatState({
 *   analysisId, apiBaseUrl, resolvedDocumentId, resolvedDocumentName,
 *   workingDocument, useLegacyChat, getAccessToken,
 *   sseState, sseActions, setSprkChatSessionId,
 * });
 * ```
 */
export const useChatState = (options: UseChatStateOptions): UseChatStateResult => {
  const {
    analysisId,
    apiBaseUrl,
    resolvedDocumentId,
    resolvedDocumentName,
    workingDocument,
    useLegacyChat: _useLegacyChat,
    getAccessToken,
    sseState,
    sseActions,
    setSprkChatSessionId,
  } = options;

  // Chat message state
  const [chatMessages, setChatMessages] = React.useState<IChatMessage[]>([]);
  const [isChatDirty, setIsChatDirty] = React.useState(false);
  const [streamingResponse, setStreamingResponse] = React.useState('');

  // Session management state
  const [isSessionResumed, setIsSessionResumed] = React.useState(false);
  const [isResumingSession, setIsResumingSession] = React.useState(false);
  const [showResumeDialog, setShowResumeDialog] = React.useState(false);
  const [pendingChatHistory, setPendingChatHistory] = React.useState<IChatMessage[] | null>(null);

  // Chat input state
  const [chatInput, setChatInput] = React.useState('');

  /**
   * Resume session by calling the BFF API /resume endpoint.
   * Creates an in-memory session on the server so chat can work.
   */
  const resumeSession = React.useCallback(async (includeChatHistory: boolean) => {
    if (!analysisId || !resolvedDocumentId) {
      logInfo('useChatState', 'Cannot resume - missing analysisId or documentId');
      setIsSessionResumed(true); // Allow chat anyway for new records
      return;
    }

    // IMPORTANT: Capture pending history in local variable BEFORE async operations
    // React state closures can become stale during async operations
    const historyToLoad = includeChatHistory ? pendingChatHistory : null;
    logInfo(
      'useChatState',
      `Resume session: includeChatHistory=${includeChatHistory}, historyToLoad has ${historyToLoad?.length ?? 0} messages`
    );

    setIsResumingSession(true);
    setShowResumeDialog(false);

    try {
      // Build request headers with authentication
      const headers: Record<string, string> = {
        'Content-Type': 'application/json',
        Accept: 'application/json',
      };

      if (getAccessToken) {
        try {
          const accessToken = await getAccessToken();
          headers['Authorization'] = `Bearer ${accessToken}`;
        } catch (tokenError) {
          logError('useChatState', 'Failed to acquire access token for resume', tokenError);
          throw new Error('Authentication failed');
        }
      }

      // Build request body
      const requestBody = {
        documentId: resolvedDocumentId,
        documentName: resolvedDocumentName || 'Unknown',
        workingDocument: workingDocument,
        chatHistory: historyToLoad,
        includeChatHistory,
      };

      // Call the resume endpoint
      const baseUrl = apiBaseUrl.replace(/\/+$/, '');
      const apiPath = baseUrl.endsWith('/api') ? '' : '/api';
      const url = `${baseUrl}${apiPath}/ai/analysis/${analysisId}/resume`;

      logInfo('useChatState', `Calling resume API: ${url}, includeChatHistory=${includeChatHistory}`);

      const response = await fetch(url, {
        method: 'POST',
        headers,
        body: JSON.stringify(requestBody),
      });

      if (!response.ok) {
        const errorData = await response.json().catch(() => ({}));
        throw new Error(errorData.error || `HTTP ${response.status}`);
      }

      const result = await response.json();
      logInfo('useChatState', `Session resumed: ${result.chatMessagesRestored} messages restored`);

      // Load chat history into UI if resuming with history
      if (historyToLoad && historyToLoad.length > 0) {
        logInfo('useChatState', `Loading ${historyToLoad.length} chat messages into UI`);
        setChatMessages(historyToLoad);
      } else {
        // Starting fresh - clear chat messages
        logInfo('useChatState', 'Starting fresh - clearing chat messages');
        setChatMessages([]);
      }

      setIsSessionResumed(true);
      setPendingChatHistory(null);
    } catch (err) {
      logError('useChatState', 'Failed to resume session', err);
      // Still allow chat even if resume fails - the error will show on first message
      // Also load chat history into UI if user wanted to resume with history
      if (historyToLoad && historyToLoad.length > 0) {
        logInfo('useChatState', `Loading ${historyToLoad.length} chat messages despite API error`);
        setChatMessages(historyToLoad);
      }
      setIsSessionResumed(true);
      setPendingChatHistory(null);
    } finally {
      setIsResumingSession(false);
    }
  }, [analysisId, resolvedDocumentId, resolvedDocumentName, workingDocument, pendingChatHistory, getAccessToken, apiBaseUrl]);

  // Dialog handlers
  const handleResumeWithHistory = React.useCallback(() => {
    resumeSession(true);
  }, [resumeSession]);

  const handleStartFresh = React.useCallback(() => {
    resumeSession(false);
  }, [resumeSession]);

  const handleDismissResumeDialog = React.useCallback(() => {
    // If user dismisses, start fresh by default
    resumeSession(false);
  }, [resumeSession]);

  // Chat send handler
  const handleSendMessage = React.useCallback(async () => {
    if (!chatInput.trim() || sseState.isStreaming) return;

    const userMessage: IChatMessage = {
      id: `msg-${Date.now()}`,
      role: 'user',
      content: chatInput.trim(),
      timestamp: new Date().toISOString(),
    };

    setChatMessages(prev => [...prev, userMessage]);
    const messageText = chatInput.trim();
    setChatInput('');
    setIsChatDirty(true); // Mark chat as dirty to trigger auto-save

    logInfo('useChatState', 'Chat message sent', userMessage);

    // Build chat history for context
    const history = chatMessages.map(msg => ({
      role: msg.role,
      content: msg.content,
    }));

    // Start SSE stream for AI response
    await sseActions.sendMessage(messageText, history);
  }, [chatInput, sseState.isStreaming, chatMessages, sseActions, setIsChatDirty]);

  // Chat keyboard handler
  const handleChatKeyDown = React.useCallback((e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      handleSendMessage();
    }
  }, [handleSendMessage]);

  // SprkChat session created handler
  const handleSprkChatSessionCreated = React.useCallback((session: IChatSession) => {
    logInfo('useChatState', `SprkChat session created: ${session.sessionId}`);
    setSprkChatSessionId(session.sessionId);
  }, [setSprkChatSessionId]);

  return {
    chatMessages,
    setChatMessages,
    streamingResponse,
    setStreamingResponse,
    isSessionResumed,
    setIsSessionResumed,
    isResumingSession,
    showResumeDialog,
    setShowResumeDialog,
    pendingChatHistory,
    setPendingChatHistory,
    isChatDirty,
    setIsChatDirty,
    chatInput,
    setChatInput,
    handleResumeWithHistory,
    handleStartFresh,
    handleDismissResumeDialog,
    handleSendMessage,
    handleChatKeyDown,
    handleSprkChatSessionCreated,
  };
};

export default useChatState;
