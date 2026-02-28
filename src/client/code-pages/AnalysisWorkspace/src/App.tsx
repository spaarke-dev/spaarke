/**
 * AnalysisWorkspace App Component
 *
 * Root layout for the AnalysisWorkspace Code Page. Renders a full-viewport
 * 2-panel layout:
 *   - Left panel: RichTextEditor (analysis output / streaming write target)
 *   - Right panel: Collapsible SourceDocumentViewer (original document reference)
 *   - Draggable, keyboard-accessible splitter between panels
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
 * Task 063: SprkChatBridge document streaming integration via DocumentStreamBridge.
 * The DocumentStreamBridge component wires SprkChatBridge events (document_stream_start,
 * document_stream_token, document_stream_end, document_replaced) to the RichTextEditor's
 * streaming API via useDocumentStreamConsumer. StreamingIndicator renders above the editor.
 *
 * Task 064: Selection broadcast via useSelectionBroadcast (editor -> SprkChat).
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

import { useCallback, useEffect, useRef, useState } from "react";
import {
    makeStyles,
    shorthands,
    tokens,
    Spinner,
    Text,
    Button,
} from "@fluentui/react-components";
import {
    ErrorCircle20Regular,
    LockClosed20Regular,
    ArrowClockwise20Regular,
} from "@fluentui/react-icons";
import type { RichTextEditorRef } from "@spaarke/ui-components";
import { useDocumentHistory } from "@spaarke/ui-components/hooks/useDocumentHistory";
import { useAuth } from "./hooks/useAuth";
import { useAnalysisLoader } from "./hooks/useAnalysisLoader";
import { useAutoSave } from "./hooks/useAutoSave";
import { useExportAnalysis } from "./hooks/useExportAnalysis";
import { useSelectionBroadcast } from "./hooks/useSelectionBroadcast";
import { useReAnalysisProgress } from "./hooks/useReAnalysisProgress";
import { useAnalysisExecution } from "./hooks/useAnalysisExecution";
import { useDiffReview } from "./hooks/useDiffReview";

import { EditorPanel } from "./components/EditorPanel";
import { SourceViewerPanel } from "./components/SourceViewerPanel";
import { PanelSplitter } from "./components/PanelSplitter";
import { DocumentStreamBridge } from "./components/DocumentStreamBridge";
import { ReAnalysisProgressOverlay } from "./components/ReAnalysisProgressOverlay";
import { DiffReviewPanel } from "./components/DiffReviewPanel";
import { usePanelResize } from "./hooks/usePanelResize";
import { markdownToHtml } from "./utils/markdownToHtml";

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
        display: "flex",
        flexDirection: "row",
        width: "100vw",
        height: "100vh",
        overflow: "hidden",
        backgroundColor: tokens.colorNeutralBackground1,
    },
    leftPanel: {
        overflow: "hidden",
        flexShrink: 0,
        position: "relative",
    },
    rightPanel: {
        overflow: "hidden",
        flexShrink: 0,
    },
    rightPanelExpanded: {
        // When expanded, the right panel uses the width from usePanelResize
        flexGrow: 0,
    },
    rightPanelCollapsed: {
        // When collapsed, the strip is handled by SourceViewerPanel internally
        width: "auto",
    },
    // ---- Auth states (task 066) ----
    authContainer: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        width: "100vw",
        height: "100vh",
        backgroundColor: tokens.colorNeutralBackground1,
        color: tokens.colorNeutralForeground1,
        ...shorthands.gap(tokens.spacingVerticalM),
        ...shorthands.padding(tokens.spacingVerticalXXL, tokens.spacingHorizontalL),
        textAlign: "center",
    },
    errorIcon: {
        color: tokens.colorPaletteRedForeground1,
        fontSize: "48px",
        marginBottom: tokens.spacingVerticalS,
    },
    lockIcon: {
        color: tokens.colorNeutralForeground3,
        fontSize: "48px",
        marginBottom: tokens.spacingVerticalS,
    },
    errorTitle: {
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground1,
    },
    errorDetail: {
        color: tokens.colorNeutralForeground3,
        maxWidth: "400px",
    },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export function App({ analysisId, documentId, tenantId }: AppProps): JSX.Element {
    const styles = useStyles();

    // ---- Auth (task 066) ----
    // Hook must be called unconditionally (React rules of hooks)
    const {
        token,
        isAuthenticated,
        isAuthenticating,
        authError,
        isXrmUnavailable,
        retryAuth,
    } = useAuth();

    // ---- State (all hooks must be called before any early return) ----
    const [isSourceCollapsed, setIsSourceCollapsed] = useState(false);
    const [editorContent, setEditorContent] = useState("");

    // ---- Resolved documentId: URL prop → Dataverse lookup fallback ----
    // When embedded on the sprk_analysis form, the URL may not include a documentId
    // parameter. Once the analysis record loads, we resolve it from the Dataverse
    // lookup field (_sprk_documentid_value → sourceDocumentId).
    const [resolvedDocumentId, setResolvedDocumentId] = useState(documentId);

    // Ref for programmatic access to the RichTextEditor (streaming insert, etc.)
    const editorRef = useRef<RichTextEditorRef>(null);

    // ---- Panel resize ----
    const {
        leftPanelWidth,
        rightPanelWidth,
        isDragging,
        containerRef,
        onSplitterMouseDown,
        onSplitterKeyDown,
        resetToDefault,
        currentRatio,
    } = usePanelResize({
        defaultRatio: 0.6,
        minLeftWidth: 300,
        minRightWidth: 200,
        isRightCollapsed: isSourceCollapsed,
    });

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
            console.log(
                `[AnalysisWorkspace] Resolved documentId from analysis lookup: ${analysis.sourceDocumentId}`
            );
            setResolvedDocumentId(analysis.sourceDocumentId);
        }
    }, [resolvedDocumentId, analysis?.sourceDocumentId]);

    // ---- Auto-execute: trigger BFF execution for draft analyses ----
    const {
        isExecuting,
        executionError,
        progressMessage: executionProgress,
        chunkCount,
    } = useAnalysisExecution({
        analysis,
        documentId: resolvedDocumentId,
        token,
        onComplete: reloadAnalysis, // Reload analysis only (not source document)
        onStreamContent: (content) => {
            // Convert markdown to HTML for the RichTextEditor during streaming
            if (editorRef.current && content) {
                const html = markdownToHtml(content);
                editorRef.current.setHtml(html);
                setEditorContent(html);
            }
        },
    });

    // ---- Task 062: Auto-save via Dataverse PATCH (same-origin) ----
    const {
        saveState,
        lastSavedAt,
        saveError,
        forceSave,
        notifyContentChanged,
    } = useAutoSave({
        analysisId,
        enabled: !!analysisId,
    });

    // ---- Task 062: Export to Word via BFF API ----
    const {
        exportState,
        exportError,
        doExport,
    } = useExportAnalysis({
        analysisId,
        token,
        analysisTitle: analysis?.title,
    });

    // ---- Task 062: Document history for Undo/Redo ----
    const {
        undo,
        redo,
        canUndo,
        canRedo,
        historyLength,
        pushVersion,
    } = useDocumentHistory(editorRef);

    // ---- Task 064: Selection broadcast to SprkChat ----
    // The bridge is created by DocumentStreamBridge. We need to access it.
    // For simplicity, we create a separate bridge ref for selection broadcast.
    // The useSelectionBroadcast hook uses the same bridge pattern.
    // Since DocumentStreamBridge creates the bridge internally, we use a
    // streaming state callback to get a reference. For selection, we use
    // a lightweight approach: listen on document selectionchange directly.
    // The bridge for selection is obtained from the DocumentStreamBridge's
    // onStreamingStateChange callback. However, to keep things clean,
    // we import SprkChatBridge directly for selection and re-analysis progress events.
    const [appBridge, setAppBridge] = useState<import("@spaarke/ui-components/services/SprkChatBridge").SprkChatBridge | null>(null);

    // Create bridge for selection + re-analysis progress events (uses same channel as streaming)
    useEffect(() => {
        if (!analysisId || !isAuthenticated) {
            return;
        }

        let bridge: import("@spaarke/ui-components/services/SprkChatBridge").SprkChatBridge | null = null;
        let cancelled = false;

        // Dynamic import to avoid circular dependency
        import("@spaarke/ui-components/services/SprkChatBridge").then(({ SprkChatBridge }) => {
            if (cancelled) {
                return;
            }
            try {
                bridge = new SprkChatBridge({ context: analysisId, transport: "auto" });
                setAppBridge(bridge);
            } catch (err) {
                console.error("[App] Failed to create bridge:", err);
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

    useSelectionBroadcast({
        editorRef,
        bridge: appBridge,
        enabled: isAuthenticated && !!analysisId,
    });

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
    const {
        diffState,
        acceptDiff,
        rejectDiff,
        isDiffStreaming,
    } = useDiffReview({
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
            const isHtml = trimmed.startsWith("<");
            const html = isHtml ? trimmed : markdownToHtml(trimmed);
            editorRef.current.setHtml(html);
            setEditorContent(html);
        }
    }, [analysis]);

    // ---- Handlers ----
    const handleToggleCollapse = useCallback(() => {
        setIsSourceCollapsed((prev) => !prev);
    }, []);

    const handleEditorChange = useCallback((html: string) => {
        setEditorContent(html);
        // Task 062: Notify auto-save of content changes
        notifyContentChanged(html);
    }, [notifyContentChanged]);

    // ---- Auth loading state (task 066) ----
    if (isAuthenticating) {
        return (
            <div
                className={styles.authContainer}
                role="main"
                aria-label="Analysis Workspace"
                data-testid="auth-loading"
            >
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
            <div
                className={styles.authContainer}
                role="alert"
                aria-label="Analysis Workspace"
                data-testid="auth-error-xrm"
            >
                <LockClosed20Regular className={styles.lockIcon} />
                <Text size={400} className={styles.errorTitle}>
                    Dataverse Required
                </Text>
                <Text size={200} className={styles.errorDetail}>
                    Analysis Workspace must be opened from within Dataverse.
                    Please open this page from a model-driven app form.
                </Text>
            </div>
        );
    }

    // ---- Auth error: token acquisition failure (retryable) ----
    if (authError) {
        return (
            <div
                className={styles.authContainer}
                role="alert"
                aria-label="Analysis Workspace"
                data-testid="auth-error-token"
            >
                <ErrorCircle20Regular className={styles.errorIcon} />
                <Text size={400} className={styles.errorTitle}>
                    Authentication Failed
                </Text>
                <Text size={200} className={styles.errorDetail}>
                    {authError.message || "Unable to acquire an authentication token. Please try again."}
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
            <div
                className={styles.authContainer}
                role="main"
                aria-label="Analysis Workspace"
            >
                <Spinner size="large" label="Initializing..." />
            </div>
        );
    }

    // ---- Authenticated: Render 2-panel workspace layout ----
    return (
        <div className={styles.root} ref={containerRef}>
            {/* Left Panel -- Editor + Streaming Bridge + Re-Analysis Overlay */}
            <div
                className={styles.leftPanel}
                style={{ width: leftPanelWidth }}
            >
                {/* Task 063: SprkChatBridge document streaming wiring */}
                <DocumentStreamBridge
                    context={analysisId}
                    editorRef={editorRef}
                    enabled={!!analysisId}
                />
                {/* Show error state if analysis load or execution failed */}
                {(analysisError || executionError) && !isAnalysisLoading && !isExecuting ? (
                    <div
                        style={{
                            display: "flex",
                            flexDirection: "column",
                            alignItems: "center",
                            justifyContent: "center",
                            height: "100%",
                            gap: "12px",
                            padding: "24px",
                            textAlign: "center",
                        }}
                        role="alert"
                        data-testid="analysis-load-error"
                    >
                        <ErrorCircle20Regular className={styles.errorIcon} />
                        <Text size={400} className={styles.errorTitle}>
                            Failed to Load Analysis
                        </Text>
                        <Text size={200} className={styles.errorDetail}>
                            {executionError?.message || analysisError?.message || "Unable to load the analysis record."}
                        </Text>
                        <Button
                            appearance="primary"
                            icon={<ArrowClockwise20Regular />}
                            onClick={retryLoad}
                        >
                            Retry
                        </Button>
                    </div>
                ) : (
                    <EditorPanel
                        ref={editorRef}
                        value={editorContent}
                        onChange={handleEditorChange}
                        placeholder={isExecuting ? (executionProgress || "Running analysis...") : "Analysis output will appear here..."}
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

            {/* Splitter -- visible only when right panel is expanded */}
            {!isSourceCollapsed && (
                <PanelSplitter
                    onMouseDown={onSplitterMouseDown}
                    onKeyDown={onSplitterKeyDown}
                    onDoubleClick={resetToDefault}
                    isDragging={isDragging}
                    currentRatio={currentRatio}
                />
            )}

            {/* Right Panel -- Source Document Viewer */}
            <div
                className={`${styles.rightPanel} ${
                    isSourceCollapsed
                        ? styles.rightPanelCollapsed
                        : styles.rightPanelExpanded
                }`}
                style={isSourceCollapsed ? undefined : { width: rightPanelWidth }}
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
        </div>
    );
}
