/**
 * AnalysisWorkspace App Component
 *
 * Root layout for the AnalysisWorkspace Code Page. Renders a full-viewport
 * 3-panel layout:
 *   - Left panel: RichTextEditor (analysis output / streaming write target)
 *   - Center panel: Collapsible SourceDocumentViewer (original document reference)
 *   - Right panel: Embedded ChatPanel (SprkChat via AnalysisAiContext)
 *   - Draggable, keyboard-accessible splitters between panels (via usePanelLayout)
 *
 * Authentication (task 066):
 *   - Uses useAuth() hook to gate rendering until token is acquired
 *   - Shows Spinner while authenticating, error state with retry on failure
 *   - Token available to child components via useAuthContext()
 *   - Auth tokens acquired via Xrm.Utility.getGlobalContext() (never BroadcastChannel)
 *
 * PH-060-B: Theme detection uses webLightTheme only. Full theme support
 * (dark mode, high contrast) will be implemented in task 069.
 *
 * Task 062: Toolbar functionality (Save, Export, Copy, Undo/Redo) via
 * useAutoSave, useExportAnalysis, useDocumentHistory, and AnalysisToolbar.
 *
 * Task 002 / Task 011: SprkChat is now embedded directly as a ChatPanel in the right
 * panel via AnalysisAiContext. The previous Xrm.App.sidePanes launch code and
 * SprkChatLaunchContext interface have been removed.
 *
 * Task 010: DocumentStreamBridge and useSelectionBroadcast have been removed.
 * Streaming state now flows through AnalysisAiContext directly. Selection context
 * is communicated via React context rather than BroadcastChannel.
 *
 * Task 065: Analysis loading from BFF API via useAnalysisLoader. Document viewer
 * wired into SourceViewerPanel with real preview URLs.
 *
 * Task 081: Re-analysis progress indicator via useReAnalysisProgress hook and
 * ReAnalysisProgressOverlay component. Subscribes to "reanalysis_progress" and
 * "document_replaced" events from SprkChatBridge to show/hide a progress bar
 * overlay during re-analysis operations.
 *
 * Task 103: DiffReviewPanel integration via useDiffReview hook. When the AI
 * proposes revisions in diff mode (operationType="diff"), tokens are buffered
 * and a DiffReviewPanel overlay opens for Accept/Reject/Edit review. The panel
 * integrates with the undo stack via useDocumentHistory.pushVersion().
 *
 * @see ADR-006  - Code Pages for standalone dialogs (not PCF)
 * @see ADR-007  - Document access through BFF API (SpeFileStore facade)
 * @see ADR-008  - Endpoint filters for auth (Bearer token from Xrm SDK)
 * @see ADR-012  - Shared component library (RichTextEditor from @spaarke/ui-components)
 * @see ADR-021  - Fluent UI v9 design system (makeStyles + design tokens exclusively)
 * @see ADR-022  - React 19 APIs for Code Pages (hooks, concurrent features)
 */

import { useCallback, useEffect, useRef, useState } from 'react';
import {
  makeStyles,
  mergeClasses,
  shorthands,
  tokens,
  Spinner,
  Text,
  Button,
  ToggleButton,
  Toaster,
  Toast,
  ToastTitle,
  useToastController,
  useId,
} from '@fluentui/react-components';
import {
  ErrorCircle20Regular,
  LockClosed20Regular,
  ArrowClockwise20Regular,
  Play20Regular,
  PanelRight20Regular,
  Chat20Regular,
} from '@fluentui/react-icons';
import type { RichTextEditorRef } from '@spaarke/ui-components';
import { AiProgressStepper, DOCUMENT_ANALYSIS_STEPS } from '@spaarke/ui-components';
import { useDocumentHistory } from '@spaarke/ui-components/hooks/useDocumentHistory';
import { useAuth } from './hooks/useAuth';
import { useAnalysisLoader } from './hooks/useAnalysisLoader';
import { useAutoSave } from './hooks/useAutoSave';
import { useExportAnalysis } from './hooks/useExportAnalysis';
import { useReAnalysisProgress } from './hooks/useReAnalysisProgress';
import { useAnalysisExecution } from './hooks/useAnalysisExecution';
import { useDiffReview } from './hooks/useDiffReview';

import { EditorPanel } from './components/EditorPanel';
import { SourceViewerPanel } from './components/SourceViewerPanel';
import { ChatPanel } from './components/ChatPanel';
import { PanelSplitter } from './components/PanelSplitter';
import { ReAnalysisProgressOverlay } from './components/ReAnalysisProgressOverlay';
import { DiffReviewPanel } from './components/DiffReviewPanel';
import { AnalysisAiProvider } from './context/AnalysisAiContext';
import { usePanelLayout } from './hooks/usePanelLayout';
import { getRuntimeConfig } from './services/authInit';
import { markdownToHtml } from './utils/markdownToHtml';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface AppProps {
  /** Analysis session ID to load or resume */
  analysisId: string;
  /** Document ID for contextual analysis */
  documentId: string;
  /** SharePoint Embedded tenant/container ID */
  tenantId: string;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    width: '100vw',
    height: '100vh',
    overflow: 'hidden',
    backgroundColor: tokens.colorNeutralBackground1,
  },
  toolbar: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalS),
    ...shorthands.padding(tokens.spacingVerticalXS, tokens.spacingHorizontalM),
    backgroundColor: tokens.colorNeutralBackground2,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
    height: '40px',
    flexShrink: 0,
  },
  content: {
    display: 'flex',
    flexDirection: 'row',
    flex: 1,
    overflow: 'hidden',
  },
  editorPanel: {
    overflow: 'hidden',
    flexShrink: 0,
    position: 'relative',
  },
  sourcePanel: {
    overflow: 'hidden',
    flexShrink: 0,
  },
  chatPanel: {
    overflow: 'hidden',
    flexShrink: 0,
  },
  // Task 036: Smooth collapse/expand animation for panel toggle transitions.
  // Applied only when NOT dragging to avoid janky animation during mouse resize.
  panelAnimated: {
    transitionProperty: 'width, opacity',
    transitionDuration: tokens.durationNormal,
    transitionTimingFunction: tokens.curveEasyEase,
    '@media (prefers-reduced-motion: reduce)': {
      transitionDuration: '0ms',
    },
  },
  // ---- Auth states (task 066) ----
  authContainer: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    width: '100vw',
    height: '100vh',
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
    ...shorthands.gap(tokens.spacingVerticalM),
    ...shorthands.padding(tokens.spacingVerticalXXL, tokens.spacingHorizontalL),
    textAlign: 'center',
  },
  errorIcon: {
    color: tokens.colorPaletteRedForeground1,
    fontSize: '48px',
    marginBottom: tokens.spacingVerticalS,
  },
  lockIcon: {
    color: tokens.colorNeutralForeground3,
    fontSize: '48px',
    marginBottom: tokens.spacingVerticalS,
  },
  errorTitle: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  errorDetail: {
    color: tokens.colorNeutralForeground3,
    maxWidth: '400px',
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export function App({ analysisId, documentId, tenantId }: AppProps): JSX.Element {
  const styles = useStyles();

  // ---- Auth (task 066) ----
  // Hook must be called unconditionally (React rules of hooks)
  const { token, isAuthenticated, isAuthenticating, authError, isXrmUnavailable, retryAuth } = useAuth();

  // ---- Task 061: Toast notification ----
  const toasterId = useId('workspace-toast');
  const { dispatchToast } = useToastController(toasterId);

  // ---- State (all hooks must be called before any early return) ----
  const [isSourceCollapsed, setIsSourceCollapsed] = useState(false);
  const [editorContent, setEditorContent] = useState('');

  // ---- Resolved documentId: URL prop → Dataverse lookup fallback ----
  // When embedded on the sprk_analysis form, the URL may not include a documentId
  // parameter. Once the analysis record loads, we resolve it from the Dataverse
  // lookup field (_sprk_documentid_value → sourceDocumentId).
  const [resolvedDocumentId, setResolvedDocumentId] = useState(documentId);

  // Ref for programmatic access to the RichTextEditor (streaming insert, etc.)
  const editorRef = useRef<RichTextEditorRef>(null);

  // ---- Three-panel layout (Editor / Source / Chat) ----
  const {
    panelSizes,
    isSourceVisible,
    isChatVisible,
    toggleSource,
    toggleChat,
    splitter1Handlers,
    splitter2Handlers,
    isDragging,
    activeSplitter,
    containerRef,
    currentRatios,
  } = usePanelLayout();

  // ---- Task 035: M365 Copilot handoff — ensure Chat panel is visible ----
  // When the page is opened with an analysisId (via Copilot handoff or navigateTo),
  // force the Chat panel visible on first render even if sessionStorage had it hidden.
  // Uses a ref to ensure this only runs once per mount (not on re-renders).
  const handoffAppliedRef = useRef(false);
  useEffect(() => {
    if (!handoffAppliedRef.current && analysisId && !isChatVisible) {
      handoffAppliedRef.current = true;
      toggleChat();
    } else {
      handoffAppliedRef.current = true;
    }
  }, []); // eslint-disable-line react-hooks/exhaustive-deps -- intentionally once on mount

  // ---- Task 033: Keyboard shortcuts for panel toggle (Ctrl+Shift+S / Ctrl+Shift+C) ----
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (!e.ctrlKey || !e.shiftKey) return;

      if (e.key === 'S' || e.key === 's') {
        e.preventDefault();
        toggleSource();
      } else if (e.key === 'C' || e.key === 'c') {
        e.preventDefault();
        toggleChat();
      }
    };

    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [toggleSource, toggleChat]);

  // ---- BFF base URL for AnalysisAiProvider ----
  // Resolved lazily from Dataverse Environment Variables via resolveRuntimeConfig()
  // (called during bootstrap in index.tsx). Safe to call after authentication.
  const bffBaseUrl = (() => {
    try {
      return getRuntimeConfig().bffBaseUrl;
    } catch {
      // Fallback: runtime config not yet resolved (pre-auth). Return empty string;
      // AnalysisAiProvider will use it once auth completes and re-renders.
      return '';
    }
  })();

  // ---- Task 065: Analysis loading (Dataverse) + document metadata (BFF) ----
  // Pass resolvedDocumentId so document metadata loads once the ID is available
  // (either from URL param initially, or from analysis lookup after first load).
  const {
    analysis,
    document: documentMetadata,
    isAnalysisLoading,
    isDocumentLoading,
    analysisError,
    documentError,
    retry: retryLoad,
    reloadAnalysis,
  } = useAnalysisLoader({
    analysisId,
    documentId: resolvedDocumentId,
    token,
  });

  // ---- Resolve documentId from Dataverse lookup after analysis loads ----
  // When the Code Page is embedded on the form, the URL often doesn't include
  // documentId. The analysis record's lookup (_sprk_documentid_value) provides it.
  useEffect(() => {
    if (!resolvedDocumentId && analysis?.sourceDocumentId) {
      console.log(`[AnalysisWorkspace] Resolved documentId from analysis lookup: ${analysis.sourceDocumentId}`);
      setResolvedDocumentId(analysis.sourceDocumentId);
    }
  }, [resolvedDocumentId, analysis?.sourceDocumentId]);

  // ---- Auto-execute: trigger BFF execution for draft analyses ----
  const {
    isExecuting,
    executionError,
    progressMessage: executionProgress,
    chunkCount,
    activeStepId,
    completedStepIds,
    triggerExecute,
    cancelExecution,
  } = useAnalysisExecution({
    analysis,
    documentId: resolvedDocumentId,
    token,
    onComplete: () => {
      reloadAnalysis();
      // Task 061: Show completion toast
      dispatchToast(
        <Toast>
          <ToastTitle>Analysis complete</ToastTitle>
        </Toast>,
        { intent: 'success', timeout: 5000 }
      );
    },
    onStreamContent: content => {
      // Convert markdown to HTML for the RichTextEditor during streaming
      if (editorRef.current && content) {
        const html = markdownToHtml(content);
        editorRef.current.setHtml(html);
        setEditorContent(html);
      }
    },
  });

  // ---- Task 062: Auto-save via Dataverse PATCH (same-origin) ----
  const { saveState, lastSavedAt, saveError, forceSave, notifyContentChanged } = useAutoSave({
    analysisId,
    enabled: !!analysisId,
  });

  // ---- Task 062: Export to Word via BFF API ----
  const { exportState, exportError, doExport } = useExportAnalysis({
    analysisId,
    token,
    analysisTitle: analysis?.title,
  });

  // ---- Task 062: Document history for Undo/Redo ----
  const { undo, redo, canUndo, canRedo, historyLength, pushVersion } = useDocumentHistory(editorRef);

  // ---- SprkChatBridge for re-analysis progress + diff review events ----
  // A lightweight bridge instance for subscribing to reanalysis_progress and
  // document_stream events used by useReAnalysisProgress and useDiffReview.
  // Selection broadcast (task 064) and DocumentStreamBridge (task 063) have been
  // removed — streaming state now flows through AnalysisAiContext directly.
  const [appBridge, setAppBridge] = useState<
    import('@spaarke/ui-components/services/SprkChatBridge').SprkChatBridge | null
  >(null);

  useEffect(() => {
    if (!analysisId || !isAuthenticated) {
      return;
    }

    let bridge: import('@spaarke/ui-components/services/SprkChatBridge').SprkChatBridge | null = null;
    let cancelled = false;

    // Dynamic import to avoid circular dependency
    import('@spaarke/ui-components/services/SprkChatBridge').then(({ SprkChatBridge }) => {
      if (cancelled) {
        return;
      }
      try {
        bridge = new SprkChatBridge({
          context: analysisId,
          transport: 'auto',
        });
        setAppBridge(bridge);
      } catch (err) {
        console.error('[App] Failed to create bridge:', err);
      }
    });

    return () => {
      cancelled = true;
      if (bridge && !bridge.isDisconnected) {
        bridge.disconnect();
      }
      setAppBridge(null);
    };
  }, [analysisId, isAuthenticated]);

  // ---- Task 081: Re-analysis progress tracking ----
  const {
    isAnalyzing: isReAnalyzing,
    percent: reAnalysisPercent,
    message: reAnalysisMessage,
  } = useReAnalysisProgress({
    bridge: appBridge,
    enabled: isAuthenticated && !!analysisId,
  });

  // ---- Task 103: Diff review for AI-proposed revisions ----
  const { diffState, acceptDiff, rejectDiff, isDiffStreaming } = useDiffReview({
    bridge: appBridge,
    editorRef,
    enabled: isAuthenticated && !!analysisId,
    pushUndoVersion: pushVersion,
  });

  // ---- Task 065: Populate editor with loaded analysis content ----
  // Content in sprk_workingdocument may be markdown (from BFF streaming)
  // or Lexical HTML (from auto-save). Detect format and handle accordingly.
  useEffect(() => {
    if (analysis?.content && editorRef.current) {
      const trimmed = analysis.content.trim();
      // If content starts with an HTML tag, it's already HTML (from auto-save);
      // pass directly to the editor. Otherwise treat as markdown.
      const isHtml = trimmed.startsWith('<');
      const html = isHtml ? trimmed : markdownToHtml(trimmed);
      editorRef.current.setHtml(html);
      setEditorContent(html);
    }
  }, [analysis]);

  // ---- Handlers ----
  const handleToggleCollapse = useCallback(() => {
    setIsSourceCollapsed(prev => !prev);
  }, []);

  const handleEditorChange = useCallback(
    (html: string) => {
      setEditorContent(html);
      // Task 062: Notify auto-save of content changes
      notifyContentChanged(html);
    },
    [notifyContentChanged]
  );

  // ---- Auth loading state (task 066) ----
  if (isAuthenticating) {
    return (
      <div className={styles.authContainer} role="main" aria-label="Analysis Workspace" data-testid="auth-loading">
        <Spinner size="large" label="Authenticating..." />
        <Text size={200} className={styles.errorDetail}>
          Connecting to Dataverse...
        </Text>
      </div>
    );
  }

  // ---- Auth error: Xrm unavailable (outside Dataverse) ----
  if (authError && isXrmUnavailable) {
    return (
      <div className={styles.authContainer} role="alert" aria-label="Analysis Workspace" data-testid="auth-error-xrm">
        <LockClosed20Regular className={styles.lockIcon} />
        <Text size={400} className={styles.errorTitle}>
          Dataverse Required
        </Text>
        <Text size={200} className={styles.errorDetail}>
          Analysis Workspace must be opened from within Dataverse. Please open this page from a model-driven app form.
        </Text>
      </div>
    );
  }

  // ---- Auth error: token acquisition failure (retryable) ----
  if (authError) {
    return (
      <div className={styles.authContainer} role="alert" aria-label="Analysis Workspace" data-testid="auth-error-token">
        <ErrorCircle20Regular className={styles.errorIcon} />
        <Text size={400} className={styles.errorTitle}>
          Authentication Failed
        </Text>
        <Text size={200} className={styles.errorDetail}>
          {authError.message || 'Unable to acquire an authentication token. Please try again.'}
        </Text>
        <Button
          appearance="primary"
          icon={<ArrowClockwise20Regular />}
          onClick={retryAuth}
          data-testid="auth-retry-button"
        >
          Retry
        </Button>
      </div>
    );
  }

  // ---- Not yet authenticated (guard for edge case) ----
  if (!isAuthenticated) {
    return (
      <div className={styles.authContainer} role="main" aria-label="Analysis Workspace">
        <Spinner size="large" label="Initializing..." />
      </div>
    );
  }

  // ---- Authenticated: Render workspace layout ----
  // Column: toolbar → content (3-panel row: Editor | Source | Chat)
  // Wrapped in AnalysisAiProvider for shared context between editor and chat.
  return (
    <AnalysisAiProvider bffBaseUrl={bffBaseUrl}>
      <div className={styles.root} ref={containerRef}>
        {/* Task 061: Toast provider */}
        <Toaster toasterId={toasterId} position="top-end" />

        {/* Task 062: Workspace toolbar — Run Analysis + Source toggle + Chat toggle */}
        <div className={styles.toolbar}>
          <Button
            appearance="primary"
            icon={isExecuting ? <Spinner size="tiny" /> : <Play20Regular />}
            onClick={triggerExecute}
            disabled={isExecuting || (!analysis?.playbookId && !analysis?.actionId)}
            data-testid="run-analysis-button"
          >
            {isExecuting ? 'Running...' : 'Run Analysis'}
          </Button>
          <ToggleButton
            icon={<PanelRight20Regular />}
            checked={isSourceVisible}
            onClick={toggleSource}
            data-testid="source-pane-toggle"
          >
            Source
          </ToggleButton>
          <ToggleButton
            icon={<Chat20Regular />}
            checked={isChatVisible}
            onClick={toggleChat}
            data-testid="chat-pane-toggle"
          >
            Chat
          </ToggleButton>
        </div>

        {/* Content area: 3-panel layout (Editor | Source | Chat) */}
        <div className={styles.content}>
          {/* Editor Panel (always visible) */}
          <div
            className={mergeClasses(styles.editorPanel, !isDragging && styles.panelAnimated)}
            style={{ width: panelSizes.editor }}
          >
            {/* Show error state if analysis load or execution failed */}
            {(analysisError || executionError) && !isAnalysisLoading && !isExecuting ? (
              <div
                style={{
                  display: 'flex',
                  flexDirection: 'column',
                  alignItems: 'center',
                  justifyContent: 'center',
                  height: '100%',
                  gap: '12px',
                  padding: '24px',
                  textAlign: 'center',
                }}
                role="alert"
                data-testid="analysis-load-error"
              >
                <ErrorCircle20Regular className={styles.errorIcon} />
                <Text size={400} className={styles.errorTitle}>
                  Failed to Load Analysis
                </Text>
                <Text size={200} className={styles.errorDetail}>
                  {executionError?.message || analysisError?.message || 'Unable to load the analysis record.'}
                </Text>
                <Button appearance="primary" icon={<ArrowClockwise20Regular />} onClick={retryLoad}>
                  Retry
                </Button>
              </div>
            ) : (
              <EditorPanel
                ref={editorRef}
                value={editorContent}
                onChange={handleEditorChange}
                placeholder={
                  isExecuting ? executionProgress || 'Running analysis...' : 'Analysis output will appear here...'
                }
                isLoading={isAnalysisLoading}
                isStreaming={isExecuting}
                streamingMessage={executionProgress}
                // Task 062: Toolbar props
                saveState={saveState}
                onForceSave={forceSave}
                saveError={saveError}
                exportState={exportState}
                onExport={doExport}
                onUndo={undo}
                onRedo={redo}
                canUndo={canUndo}
                canRedo={canRedo}
                historyLength={historyLength}
                // Task 031: Inline AI Toolbar props
                analysisId={analysisId}
                onDiffAction={(selectedText: string) => {
                  // Diff-type inline actions (Simplify, Expand) are handled by the
                  // existing bridge-based diff review flow in useDiffReview. SprkChat
                  // receives the [Label] {selectedText} message via BroadcastChannel,
                  // processes it, and emits document_stream_start(operationType="diff")
                  // back through the bridge. useDiffReview then buffers tokens and opens
                  // DiffReviewPanel when the stream completes (task 103 pattern).
                  //
                  // This callback exists so App.tsx can show an optimistic loading state
                  // or log the initiation of a diff operation for diagnostics.
                  console.debug(
                    '[AnalysisWorkspace] Diff inline action initiated for selection length:',
                    selectedText.length
                  );
                }}
              />
            )}
            {/* AI analysis progress stepper (new analysis only — hide once streaming begins) */}
            {isExecuting && chunkCount < 5 && (
              <AiProgressStepper
                variant="card"
                steps={DOCUMENT_ANALYSIS_STEPS}
                activeStepId={activeStepId}
                completedStepIds={completedStepIds}
                title="Analyzing Document"
                onCancel={cancelExecution}
                isStreaming={isExecuting}
              />
            )}
            {/* Task 081: Re-analysis progress overlay (positioned over editor) */}
            <ReAnalysisProgressOverlay
              isVisible={isReAnalyzing}
              percent={reAnalysisPercent}
              message={reAnalysisMessage}
            />
            {/* Task 103: Diff review panel for AI-proposed revisions */}
            <DiffReviewPanel
              isOpen={diffState.isOpen}
              originalText={diffState.originalText}
              proposedText={diffState.proposedText}
              onAccept={acceptDiff}
              onReject={rejectDiff}
            />
          </div>

          {/* Splitter 1 + Source Panel — visible when source is shown */}
          {isSourceVisible && (
            <>
              {!isSourceCollapsed && (
                <PanelSplitter
                  onMouseDown={splitter1Handlers.onMouseDown}
                  onKeyDown={splitter1Handlers.onKeyDown}
                  onDoubleClick={splitter1Handlers.onDoubleClick}
                  isDragging={isDragging && activeSplitter === 1}
                  currentRatio={currentRatios.editor}
                />
              )}
              <div
                className={mergeClasses(styles.sourcePanel, !isDragging && styles.panelAnimated)}
                style={isSourceCollapsed ? undefined : { width: panelSizes.source }}
              >
                <SourceViewerPanel
                  isCollapsed={isSourceCollapsed}
                  onToggleCollapse={handleToggleCollapse}
                  // Task 065: Document viewer props
                  documentMetadata={documentMetadata}
                  isLoading={isDocumentLoading}
                  documentError={documentError}
                  onRetry={retryLoad}
                />
              </div>
            </>
          )}

          {/* Splitter + Chat Panel — visible when chat is shown.
              When source is visible, splitter 2 separates Source from Chat.
              When source is hidden, splitter 1 separates Editor from Chat
              (usePanelLayout handles the Editor<->Chat transfer in splitter 1). */}
          {isChatVisible && (
            <>
              <PanelSplitter
                onMouseDown={isSourceVisible ? splitter2Handlers.onMouseDown : splitter1Handlers.onMouseDown}
                onKeyDown={isSourceVisible ? splitter2Handlers.onKeyDown : splitter1Handlers.onKeyDown}
                onDoubleClick={isSourceVisible ? splitter2Handlers.onDoubleClick : splitter1Handlers.onDoubleClick}
                isDragging={isDragging && (isSourceVisible ? activeSplitter === 2 : activeSplitter === 1)}
                currentRatio={isSourceVisible ? currentRatios.source : currentRatios.editor}
              />
              <div
                className={mergeClasses(styles.chatPanel, !isDragging && styles.panelAnimated)}
                style={{ width: panelSizes.chat }}
                data-testid="chat-panel-container"
              >
                <ChatPanel />
              </div>
            </>
          )}
        </div>
      </div>
    </AnalysisAiProvider>
  );
}
