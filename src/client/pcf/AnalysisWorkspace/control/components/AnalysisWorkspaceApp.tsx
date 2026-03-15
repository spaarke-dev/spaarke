/**
 * Analysis Workspace App Component
 *
 * Main container component for the AI Document Analysis Workspace.
 * Design Reference: UI Screenshots/02-DOCUMENT-ANALYSIS-OUTPUT-FORM.jpg
 *
 * THREE-COLUMN LAYOUT:
 * 1. Left: Analysis Output (Working Document with Rich Text Editor)
 * 2. Center: Original Document Preview (Source file viewer)
 * 3. Right: Conversation Panel (AI Chat with SSE streaming)
 */

import * as React from 'react';
import {
  tokens,
  Spinner,
  Text,
  MessageBar,
  MessageBarBody,
  Button,
  Badge,
  Textarea,
} from '@fluentui/react-components';
import {
  ChatRegular,
  SaveRegular,
  Send24Regular,
  Copy24Regular,
  ArrowDownload24Regular,
  ChevronDoubleRight20Regular,
  ChevronDoubleLeft20Regular,
  ChevronRight20Regular,
  ChevronLeft20Regular,
  ArrowSync24Regular,
} from '@fluentui/react-icons';
import { IAnalysisWorkspaceAppProps, IChatMessage } from '../types';
import { logInfo, logError } from '../utils/logger';
import { RichTextEditor } from './RichTextEditor';
import { SourceDocumentViewer } from './SourceDocumentViewer';
import { ResumeSessionDialog } from './ResumeSessionDialog';
import { useSseStream } from '../hooks/useSseStream';
import { useAuth } from '../hooks/useAuth';
import { useDocumentResolution } from '../hooks/useDocumentResolution';
import { useWorkingDocumentSave } from '../hooks/useWorkingDocumentSave';
import { useChatState } from '../hooks/useChatState';
import { useAnalysisData, isWebApiAvailable } from '../hooks/useAnalysisData';
import { useAnalysisExecution } from '../hooks/useAnalysisExecution';
import { usePanelResize } from '../hooks/usePanelResize';
import { SprkChat } from '@spaarke/ui-components/dist/components/SprkChat';
import { useStyles } from './AnalysisWorkspaceApp.styles';

// Build info for version footer
const VERSION = '1.3.5';
const BUILD_DATE = '2026-02-24';

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export const AnalysisWorkspaceApp: React.FC<IAnalysisWorkspaceAppProps> = ({
  analysisId,
  documentId,
  containerId,
  fileId,
  apiBaseUrl,
  webApi,
  // isAuthReady is set by the parent (index.ts) once @spaarke/auth is initialized
  isAuthReady = false,
  useLegacyChat = false,
  onWorkingDocumentChange,
  onChatHistoryChange,
  onStatusChange,
}) => {
  const styles = useStyles();

  // State — only UI layout state remains inline; all domain state is in hooks
  const [workingDocument, setWorkingDocument] = React.useState('');

  // Panel resize hook — manages visibility, width state, refs, and drag handlers
  const {
    isConversationPanelVisible,
    isDocumentPanelVisible,
    leftPanelWidth,
    centerPanelWidth,
    containerRef,
    leftPanelRef,
    centerPanelRef,
    setIsConversationPanelVisible,
    setIsDocumentPanelVisible,
    handleResizeMouseDown,
  } = usePanelResize();

  // Auth hook — manages auth initialization, token acquisition, and SprkChat token refresh
  const {
    isAuthInitialized,
    accessToken: sprkChatAccessToken,
    sessionId: sprkChatSessionId,
    getAccessToken,
    setSessionId: setSprkChatSessionId,
  } = useAuth({ isAuthReady, useLegacyChat });

  // Document resolution hook — resolves document/container/file IDs from Dataverse
  const {
    documentId: resolvedDocumentId,
    containerId: resolvedContainerId,
    fileId: resolvedFileId,
    documentName: resolvedDocumentName,
    playbookId,
    resolveFromDocumentId,
    setPlaybookId,
    setDocumentId: setResolvedDocumentId,
  } = useDocumentResolution({ documentId, containerId, fileId, webApi });

  // Chat state refs for SSE callbacks (set after useChatState hook below)
  const chatSettersRef = React.useRef<{
    setChatMessages: React.Dispatch<React.SetStateAction<IChatMessage[]>>;
    setStreamingResponse: React.Dispatch<React.SetStateAction<string>>;
    setIsChatDirty: (dirty: boolean) => void;
  } | null>(null);

  // SSE Stream Hook for AI Chat (legacy)
  // Note: callbacks use chatSettersRef to avoid circular dependency with useChatState
  const [sseState, sseActions] = useSseStream({
    apiBaseUrl,
    analysisId,
    getAccessToken: isAuthInitialized ? getAccessToken : undefined,
    onToken: token => {
      chatSettersRef.current?.setStreamingResponse(prev => prev + token);
    },
    onComplete: fullResponse => {
      // Add assistant message to chat history
      const assistantMessage: IChatMessage = {
        id: `msg-${Date.now()}`,
        role: 'assistant',
        content: fullResponse,
        timestamp: new Date().toISOString(),
      };
      chatSettersRef.current?.setChatMessages(prev => [...prev, assistantMessage]);
      chatSettersRef.current?.setStreamingResponse('');
      chatSettersRef.current?.setIsChatDirty(true); // Mark chat as dirty to trigger auto-save
    },
    onError: err => {
      logError('AnalysisWorkspaceApp', 'SSE stream error', err);
      // Add error message to chat
      const errorMessage: IChatMessage = {
        id: `msg-${Date.now()}`,
        role: 'assistant',
        content: `Sorry, I encountered an error: ${err.message}. Please try again.`,
        timestamp: new Date().toISOString(),
      };
      chatSettersRef.current?.setChatMessages(prev => [...prev, errorMessage]);
      chatSettersRef.current?.setStreamingResponse('');
    },
  });

  // Chat state hook — manages chat messages, session resume, and chat input
  const {
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
    chatInput,
    setChatInput,
    handleResumeWithHistory,
    handleStartFresh,
    handleDismissResumeDialog,
    handleSendMessage,
    handleChatKeyDown,
    handleSprkChatSessionCreated,
  } = useChatState({
    analysisId,
    apiBaseUrl,
    resolvedDocumentId,
    resolvedDocumentName,
    workingDocument,
    useLegacyChat,
    getAccessToken: isAuthInitialized ? getAccessToken : undefined,
    sseState,
    sseActions,
    setSprkChatSessionId,
  });

  // Working document save hook — manages auto-save debounce, dirty tracking, persistence
  const { isDirty, isSaving, lastSaved, setIsDirty, setLastSaved, handleManualSave, handleDocumentChange, saveAnalysisState } = useWorkingDocumentSave({
    analysisId,
    webApi,
    workingDocument,
    setWorkingDocument,
    chatMessages,
    isChatDirty,
    setIsChatDirty,
    onWorkingDocumentChange,
    onChatHistoryChange,
  });

  // Wire up chat setters ref for SSE callbacks (avoids circular dependency)
  React.useEffect(() => {
    chatSettersRef.current = { setChatMessages, setStreamingResponse, setIsChatDirty };
  }, [setChatMessages, setStreamingResponse, setIsChatDirty]);

  // Analysis data hook — manages loading, error, analysis record, pending execution
  const {
    isLoading, setIsLoading,
    error, setError,
    analysis: _analysis,
    pendingExecution, clearPendingExecution,
  } = useAnalysisData({
    analysisId,
    documentId,
    webApi,
    useLegacyChat,
    isAuthInitialized,
    setPlaybookId,
    setDocumentId: setResolvedDocumentId,
    resolveFromDocumentId,
    setWorkingDocument,
    setIsSessionResumed,
    setPendingChatHistory,
    setShowResumeDialog,
    onStatusChange,
  });

  // Analysis execution hook — manages execution state and SSE streaming pipeline
  const { isExecuting, executionProgress, executeAnalysis } = useAnalysisExecution({
    apiBaseUrl,
    analysisId,
    webApi,
    playbookId,
    setWorkingDocument,
    setIsDirty,
    setLastSaved,
    setError,
    isWebApiAvailable,
  });

  // Execute pending analysis when auth becomes available
  React.useEffect(() => {
    if (isAuthInitialized && pendingExecution && !isExecuting) {
      logInfo('AnalysisWorkspaceApp', 'Auth now initialized, executing pending analysis');
      setIsLoading(false);
      executeAnalysis(pendingExecution.analysis, pendingExecution.docId);
      clearPendingExecution();
    }
  }, [isAuthInitialized, pendingExecution, isExecuting, executeAnalysis, setIsLoading, clearPendingExecution]);

  // ─────────────────────────────────────────────────────────────────────────
  // Event Handlers
  // ─────────────────────────────────────────────────────────────────────────

  /**
   * Handle manual re-execution of analysis.
   * Allows user to re-run the AI analysis on demand.
   */
  const handleReExecute = async () => {
    if (!_analysis || isExecuting) return;

    const docId = resolvedDocumentId;
    if (!docId) {
      setError('Cannot re-execute: no document associated with this analysis.');
      return;
    }

    logInfo('AnalysisWorkspaceApp', 'User triggered re-execution of analysis');
    await executeAnalysis(_analysis, docId);
  };

  const formatLastSaved = (): string => {
    if (!lastSaved) return '';
    return `Last saved: ${lastSaved.toLocaleTimeString()}`;
  };

  // ─────────────────────────────────────────────────────────────────────────
  // Render
  // ─────────────────────────────────────────────────────────────────────────

  if (isLoading) {
    return (
      <div className={styles.loadingContainer}>
        <Spinner size="large" label="Loading analysis..." />
      </div>
    );
  }

  // NOTE: We no longer show a full-screen spinner during execution.
  // Instead, we show the workspace layout with streaming content visible in the
  // Analysis Output panel - providing a ChatGPT-like streaming experience.

  if (error) {
    return (
      <div className={styles.errorContainer}>
        <MessageBar intent="error">
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      </div>
    );
  }

  return (
    <div className={styles.container}>
      {/* Resume Session Dialog is rendered at the end of this component via ResumeSessionDialog */}

      {/* Content - 3 Column Layout (no header - form already shows name/status) */}
      <div className={styles.content} ref={containerRef}>
        {/* LEFT PANEL - Analysis Output / Working Document */}
        <div
          ref={leftPanelRef}
          className={styles.leftPanel}
          style={leftPanelWidth ? { flex: `0 0 ${leftPanelWidth}px` } : undefined}
        >
          <div className={styles.panelHeader}>
            <div className={styles.panelHeaderLeft}>
              <Text weight="semibold">ANALYSIS OUTPUT</Text>
              {isExecuting ? (
                <span className={styles.streamingIndicator}>
                  <Spinner size="tiny" />
                  <Text size={200}>{executionProgress || 'Analyzing...'}</Text>
                </span>
              ) : (
                <span
                  className={`${styles.statusIndicator} ${isDirty ? styles.unsavedIndicator : styles.savedIndicator}`}
                >
                  {isDirty ? '• Unsaved' : formatLastSaved()}
                </span>
              )}
            </div>
            <div className={styles.panelHeaderActions}>
              {/* Using native title instead of Tooltip to avoid portal rendering issues in PCF */}
              <Button
                icon={<ArrowSync24Regular />}
                appearance="subtle"
                size="small"
                onClick={handleReExecute}
                disabled={isExecuting || !_analysis?._sprk_actionid_value}
                title="Re-execute analysis"
                aria-label="Re-execute analysis"
              />
              <Button
                icon={<SaveRegular />}
                appearance="subtle"
                size="small"
                onClick={handleManualSave}
                disabled={!isDirty || isSaving || isExecuting}
                title="Save"
                aria-label="Save"
              />
              <Button
                icon={<Copy24Regular />}
                appearance="subtle"
                size="small"
                onClick={() => navigator.clipboard.writeText(workingDocument)}
                title="Copy to clipboard"
                aria-label="Copy to clipboard"
              />
              <Button
                icon={<ArrowDownload24Regular />}
                appearance="subtle"
                size="small"
                title="Download"
                aria-label="Download"
              />
              <Button
                icon={isDocumentPanelVisible ? <ChevronRight20Regular /> : <ChevronLeft20Regular />}
                appearance="subtle"
                size="small"
                onClick={() => setIsDocumentPanelVisible(!isDocumentPanelVisible)}
                title={isDocumentPanelVisible ? 'Hide document' : 'Show document'}
                aria-label={isDocumentPanelVisible ? 'Hide document' : 'Show document'}
              />
            </div>
          </div>
          <div className={styles.editorContainer}>
            <RichTextEditor
              value={workingDocument}
              onChange={handleDocumentChange}
              readOnly={false}
              isDarkMode={false}
              placeholder="Analysis output will appear here..."
            />
          </div>
        </div>

        {/* Resize handle - left/center */}
        {isDocumentPanelVisible && (
          <div
            className={styles.resizeHandle}
            onMouseDown={handleResizeMouseDown('left-center', leftPanelRef.current)}
          />
        )}

        {/* CENTER PANEL - Original Document Preview (collapsible) */}
        <div
          ref={centerPanelRef}
          className={`${styles.centerPanel} ${!isDocumentPanelVisible ? styles.centerPanelCollapsed : ''}`}
          style={centerPanelWidth && isDocumentPanelVisible ? { flex: `0 0 ${centerPanelWidth}px` } : undefined}
        >
          <div className={styles.panelHeader}>
            <div className={styles.panelHeaderLeft}>
              <Text weight="semibold">
                {resolvedDocumentName ? resolvedDocumentName.toUpperCase() : 'ORIGINAL DOCUMENT'}
              </Text>
            </div>
            <div className={styles.panelHeaderActions}>
              <Button
                icon={isConversationPanelVisible ? <ChevronDoubleRight20Regular /> : <ChevronDoubleLeft20Regular />}
                appearance="subtle"
                size="small"
                onClick={() => setIsConversationPanelVisible(!isConversationPanelVisible)}
                title={isConversationPanelVisible ? 'Hide conversation' : 'Show conversation'}
                aria-label={isConversationPanelVisible ? 'Hide conversation' : 'Show conversation'}
              />
            </div>
          </div>
          <div className={styles.documentPreview}>
            <SourceDocumentViewer
              documentId={resolvedDocumentId}
              containerId={resolvedContainerId}
              fileId={resolvedFileId}
              apiBaseUrl={apiBaseUrl}
              getAccessToken={isAuthInitialized ? getAccessToken : undefined}
            />
          </div>
        </div>

        {/* Resize handle - center/right */}
        {isConversationPanelVisible && isDocumentPanelVisible && (
          <div
            className={styles.resizeHandle}
            onMouseDown={handleResizeMouseDown('center-right', centerPanelRef.current)}
          />
        )}

        {/* RIGHT PANEL - Conversation / AI Chat (collapsible) */}
        <div className={`${styles.rightPanel} ${!isConversationPanelVisible ? styles.rightPanelCollapsed : ''}`}>
          <div className={styles.panelHeader}>
            <div className={styles.panelHeaderLeft}>
              <Text weight="semibold">CONVERSATION</Text>
              {useLegacyChat && chatMessages.length > 0 && (
                <Badge appearance="filled" color="brand" size="small">
                  {chatMessages.length}
                </Badge>
              )}
            </div>
          </div>
          {useLegacyChat ? (
            /* Legacy chat panel — uses /api/ai/analysis/{id}/continue SSE */
            <div className={styles.chatContainer}>
              <div className={styles.chatMessages}>
                {chatMessages.length === 0 ? (
                  <div
                    style={{
                      textAlign: 'center',
                      padding: '24px',
                      color: tokens.colorNeutralForeground3,
                    }}
                  >
                    <ChatRegular style={{ fontSize: '32px', marginBottom: '12px' }} />
                    <Text block size={300}>
                      Start a conversation
                    </Text>
                    <Text block size={200} style={{ marginTop: '8px' }}>
                      Ask questions or request changes to refine your analysis
                    </Text>
                  </div>
                ) : (
                  chatMessages.map(msg => (
                    <div
                      key={msg.id}
                      className={`${styles.chatMessage} ${
                        msg.role === 'user' ? styles.chatMessageUser : styles.chatMessageAssistant
                      }`}
                    >
                      <div className={styles.chatMessageRole}>{msg.role === 'user' ? 'You' : 'AI Assistant'}</div>
                      <div className={styles.chatMessageContent}>{msg.content}</div>
                    </div>
                  ))
                )}
                {/* Streaming response - show in real-time */}
                {sseState.isStreaming && (
                  <div className={`${styles.chatMessage} ${styles.chatMessageAssistant}`}>
                    <div className={styles.chatMessageRole}>AI Assistant</div>
                    <div className={styles.chatMessageContent}>
                      {streamingResponse || (
                        <span className={styles.streamingIndicator}>
                          <Spinner size="tiny" />
                          <Text size={200}>Thinking...</Text>
                        </span>
                      )}
                    </div>
                  </div>
                )}
              </div>
              <div className={styles.chatInputContainer}>
                {isResumingSession ? (
                  <div
                    style={{
                      display: 'flex',
                      alignItems: 'center',
                      gap: '8px',
                      padding: '12px',
                      justifyContent: 'center',
                    }}
                  >
                    <Spinner size="tiny" />
                    <Text size={200}>Initializing session...</Text>
                  </div>
                ) : !isSessionResumed && showResumeDialog ? (
                  <div
                    style={{
                      padding: '12px',
                      textAlign: 'center',
                      color: tokens.colorNeutralForeground3,
                    }}
                  >
                    <Text size={200}>Choose how to continue...</Text>
                  </div>
                ) : (
                  <div className={styles.chatInputWrapper}>
                    <Textarea
                      className={styles.chatTextarea}
                      placeholder={isSessionResumed ? 'Type a message...' : 'Waiting for session...'}
                      value={chatInput}
                      onChange={(_e, data) => setChatInput(data.value)}
                      onKeyDown={handleChatKeyDown}
                      disabled={sseState.isStreaming || !isSessionResumed}
                      resize="vertical"
                      rows={2}
                    />
                    <Button
                      icon={<Send24Regular />}
                      appearance="primary"
                      onClick={handleSendMessage}
                      disabled={!chatInput.trim() || sseState.isStreaming || !isSessionResumed}
                    />
                  </div>
                )}
              </div>
            </div>
          ) : (
            /* New SprkChat component — uses /api/ai/chat/ endpoints */
            <div className={styles.sprkChatWrapper}>
              <SprkChat
                sessionId={sprkChatSessionId}
                documentId={resolvedDocumentId}
                playbookId={playbookId || ''}
                apiBaseUrl={apiBaseUrl.replace(/\/+$/, '').replace(/\/api\/?$/, '')}
                accessToken={sprkChatAccessToken}
                onSessionCreated={handleSprkChatSessionCreated}
              />
            </div>
          )}
        </div>
      </div>

      {/* Version Footer */}
      <div className={styles.versionFooter}>
        v{VERSION} • Built {BUILD_DATE}
      </div>

      {/* Resume Session Dialog (legacy chat only — SprkChat manages its own sessions) */}
      {useLegacyChat && (
        <ResumeSessionDialog
          open={showResumeDialog}
          chatMessageCount={pendingChatHistory ? pendingChatHistory.length : 0}
          onResumeWithHistory={handleResumeWithHistory}
          onStartFresh={handleStartFresh}
          onDismiss={handleDismissResumeDialog}
        />
      )}
    </div>
  );
};
