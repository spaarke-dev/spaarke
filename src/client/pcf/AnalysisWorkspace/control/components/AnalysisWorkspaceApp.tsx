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

import * as React from "react";
import {
    makeStyles,
    tokens,
    Spinner,
    Text,
    MessageBar,
    MessageBarBody,
    Button,
    Badge,
    Textarea
} from "@fluentui/react-components";
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
    ArrowSync24Regular
} from "@fluentui/react-icons";
import { IAnalysisWorkspaceAppProps, IChatMessage, IAnalysis } from "../types";
import { logInfo, logError } from "../utils/logger";
import { markdownToHtml, isMarkdown } from "../utils/markdownToHtml";
import { RichTextEditor } from "./RichTextEditor";
import { SourceDocumentViewer } from "./SourceDocumentViewer";
import { ResumeSessionDialog } from "./ResumeSessionDialog";
import { useSseStream } from "../hooks/useSseStream";
import { MsalAuthProvider, loginRequest } from "../services/auth";
import { SprkChat } from "@spaarke/ui-components/dist/components/SprkChat";
import type { IChatSession } from "@spaarke/ui-components/dist/components/SprkChat";

// Build info for version footer
const VERSION = "1.3.4";
const BUILD_DATE = "2026-02-24";

// ─────────────────────────────────────────────────────────────────────────────
// Styles - 3-Column Layout
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
    container: {
        display: "flex",
        flexDirection: "column",
        // Use viewport height minus header space (~180px for Dataverse form header/tabs)
        // This ensures the control fills available vertical space
        height: "calc(100vh - 180px)",
        minHeight: "500px", // Ensure minimum height for usability
        width: "100%",
        backgroundColor: tokens.colorNeutralBackground1,
        overflow: "hidden"
    },
    content: {
        display: "flex",
        flex: 1,
        overflow: "hidden"
    },
    // Left Panel - Analysis Output (resizable)
    leftPanel: {
        flex: "1 1 40%",
        minWidth: "250px",
        display: "flex",
        flexDirection: "column",
        overflow: "hidden",
        position: "relative" as const
    },
    // Center Panel - Source Document (resizable, collapsible)
    centerPanel: {
        flex: "1 1 35%",
        minWidth: "200px",
        display: "flex",
        flexDirection: "column",
        overflow: "hidden",
        position: "relative" as const,
        transition: "flex-basis 0.2s ease, min-width 0.2s ease, opacity 0.2s ease"
    },
    // Right Panel - Conversation (collapsible)
    rightPanel: {
        flex: "0 0 350px",
        minWidth: "300px",
        maxWidth: "450px",
        display: "flex",
        flexDirection: "column",
        overflow: "hidden",
        transition: "flex-basis 0.2s ease, min-width 0.2s ease, opacity 0.2s ease"
    },
    rightPanelCollapsed: {
        flex: "0 0 0px",
        minWidth: "0px",
        maxWidth: "0px",
        opacity: 0,
        overflow: "hidden"
    },
    centerPanelCollapsed: {
        flex: "0 0 0px",
        minWidth: "0px",
        opacity: 0,
        overflow: "hidden"
    },
    // Resize handle between panels
    resizeHandle: {
        width: "4px",
        cursor: "col-resize",
        backgroundColor: tokens.colorNeutralStroke1,
        transition: "background-color 0.15s ease",
        "&:hover": {
            backgroundColor: tokens.colorBrandBackground
        },
        "&:active": {
            backgroundColor: tokens.colorBrandBackgroundPressed
        }
    },
    panelHeader: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        gap: tokens.spacingHorizontalS,
        padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
        borderBottom: `1px solid ${tokens.colorNeutralStroke1}`,
        backgroundColor: tokens.colorNeutralBackground3,
        minHeight: "40px"
    },
    panelHeaderLeft: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalS
    },
    panelHeaderActions: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalXS,
        flexShrink: 0,
        // Prevent layout shifts on hover by ensuring consistent dimensions
        "& button": {
            minWidth: "32px",
            minHeight: "32px"
        }
    },
    editorContainer: {
        flex: 1,
        overflow: "auto",
        padding: tokens.spacingHorizontalM
    },
    documentPreview: {
        flex: 1,
        overflow: "auto",
        padding: tokens.spacingHorizontalM,
        backgroundColor: tokens.colorNeutralBackground1
    },
    documentPreviewEmpty: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        height: "100%",
        color: tokens.colorNeutralForeground3,
        gap: tokens.spacingVerticalM
    },
    chatContainer: {
        flex: 1,
        display: "flex",
        flexDirection: "column",
        overflow: "hidden"
    },
    chatMessages: {
        flex: 1,
        overflow: "auto",
        padding: tokens.spacingHorizontalM
    },
    chatMessage: {
        marginBottom: tokens.spacingVerticalM,
        padding: tokens.spacingHorizontalS,
        borderRadius: tokens.borderRadiusMedium
    },
    chatMessageUser: {
        backgroundColor: tokens.colorBrandBackground2,
        marginLeft: "20%"
    },
    chatMessageAssistant: {
        backgroundColor: tokens.colorNeutralBackground3,
        marginRight: "10%"
    },
    chatMessageRole: {
        fontSize: tokens.fontSizeBase200,
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground2,
        marginBottom: tokens.spacingVerticalXS
    },
    chatMessageContent: {
        fontSize: tokens.fontSizeBase300,
        lineHeight: "1.5",
        whiteSpace: "pre-wrap" as const
    },
    chatInputContainer: {
        padding: tokens.spacingHorizontalM,
        borderTop: `1px solid ${tokens.colorNeutralStroke1}`,
        backgroundColor: tokens.colorNeutralBackground2
    },
    chatInputWrapper: {
        display: "flex",
        gap: tokens.spacingHorizontalS,
        alignItems: "flex-end"
    },
    chatTextarea: {
        flex: 1
    },
    loadingContainer: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        height: "100%",
        gap: tokens.spacingVerticalL
    },
    errorContainer: {
        padding: tokens.spacingHorizontalL
    },
    statusIndicator: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalXS,
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground3,
        padding: tokens.spacingHorizontalS,
        minHeight: "24px"
    },
    savedIndicator: {
        color: tokens.colorStatusSuccessForeground1
    },
    unsavedIndicator: {
        color: tokens.colorStatusWarningForeground1
    },
    versionFooter: {
        fontSize: tokens.fontSizeBase100,
        color: tokens.colorNeutralForeground3,
        textAlign: "center" as const,
        padding: tokens.spacingVerticalXS,
        borderTop: `1px solid ${tokens.colorNeutralStroke1}`,
        backgroundColor: tokens.colorNeutralBackground2
    },
    streamingIndicator: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalXS,
        padding: tokens.spacingHorizontalS,
        color: tokens.colorBrandForeground1,
        minHeight: "24px"
    },
    sprkChatWrapper: {
        flex: 1,
        display: "flex",
        flexDirection: "column",
        overflow: "hidden"
    },
    // Choice Dialog Styles (ADR-023)
    choiceDialogContent: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalM
    },
    choiceOptionsContainer: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalS,
        marginTop: tokens.spacingVerticalM
    },
    choiceOptionButton: {
        display: "flex",
        alignItems: "center",
        justifyContent: "flex-start",
        gap: tokens.spacingHorizontalM,
        padding: tokens.spacingVerticalM,
        width: "100%",
        textAlign: "left" as const,
        minHeight: "64px"
    },
    choiceOptionIcon: {
        fontSize: "24px",
        color: tokens.colorBrandForeground1,
        flexShrink: 0
    },
    choiceOptionText: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXXS,
        overflow: "hidden"
    },
    choiceOptionTitle: {
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground1
    },
    choiceOptionDescription: {
        color: tokens.colorNeutralForeground2,
        fontSize: tokens.fontSizeBase200,
        lineHeight: tokens.lineHeightBase200
    }
});

// ─────────────────────────────────────────────────────────────────────────────
// Utilities
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Check if WebAPI is available (not in design-time/editor mode)
 * Custom Page editor doesn't implement WebAPI methods
 */
function isWebApiAvailable(webApi: ComponentFramework.WebApi | undefined): boolean {
    if (!webApi) return false;

    try {
        // Check if methods are real implementations
        if (typeof webApi.retrieveRecord !== "function" ||
            typeof webApi.updateRecord !== "function") {
            return false;
        }
        return true;
    } catch {
        return false;
    }
}

/**
 * Extract error message from various error types (WebAPI, Error, string, object)
 */
function getErrorMessage(err: unknown): string {
    if (err instanceof Error) {
        return err.message;
    }
    if (typeof err === "string") {
        return err;
    }
    if (err && typeof err === "object") {
        // WebAPI error object
        const errObj = err as Record<string, unknown>;
        if (typeof errObj.message === "string") {
            return errObj.message;
        }
        if (typeof errObj.errorCode === "string" || typeof errObj.errorCode === "number") {
            return `Error ${errObj.errorCode}: ${errObj.message || "Unknown error"}`;
        }
        // Try to stringify if all else fails
        try {
            return JSON.stringify(err);
        } catch {
            return "Unknown error occurred";
        }
    }
    return "Unknown error occurred";
}

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
    // Note: getAccessToken and isAuthReady props are available but not used
    // Component uses internal MSAL auth provider instead
    useLegacyChat = false,
    onWorkingDocumentChange,
    onChatHistoryChange,
    onStatusChange
}) => {
    const styles = useStyles();

    // State
    const [isLoading, setIsLoading] = React.useState(true);
    const [error, setError] = React.useState<string | null>(null);
    const [_analysis, setAnalysis] = React.useState<IAnalysis | null>(null);
    const [workingDocument, setWorkingDocument] = React.useState("");
    const [chatMessages, setChatMessages] = React.useState<IChatMessage[]>([]);
    const [isDirty, setIsDirty] = React.useState(false);
    const [isChatDirty, setIsChatDirty] = React.useState(false); // Tracks unsaved chat changes
    const [lastSaved, setLastSaved] = React.useState<Date | null>(null);
    const [isSaving, setIsSaving] = React.useState(false);
    const [streamingResponse, setStreamingResponse] = React.useState("");
    const [isConversationPanelVisible, setIsConversationPanelVisible] = React.useState(true);
    const [isDocumentPanelVisible, setIsDocumentPanelVisible] = React.useState(true);

    // Session management state
    const [isSessionResumed, setIsSessionResumed] = React.useState(false);
    const [isResumingSession, setIsResumingSession] = React.useState(false);
    const [showResumeDialog, setShowResumeDialog] = React.useState(false);
    const [pendingChatHistory, setPendingChatHistory] = React.useState<IChatMessage[] | null>(null);

    // Panel resize state
    const [leftPanelWidth, setLeftPanelWidth] = React.useState<number | null>(null);
    const [centerPanelWidth, setCenterPanelWidth] = React.useState<number | null>(null);
    const containerRef = React.useRef<HTMLDivElement>(null);
    const leftPanelRef = React.useRef<HTMLDivElement>(null);
    const centerPanelRef = React.useRef<HTMLDivElement>(null);
    const isDraggingRef = React.useRef<'left-center' | 'center-right' | null>(null);
    const dragStartXRef = React.useRef<number>(0);
    const dragStartWidthRef = React.useRef<number>(0);

    // Resolved document fields from expanded relationship
    // Note: documentId prop may be empty - we resolve the actual document ID from the Analysis record
    const [resolvedDocumentId, setResolvedDocumentId] = React.useState(documentId);
    const [resolvedContainerId, setResolvedContainerId] = React.useState(containerId);
    const [resolvedFileId, setResolvedFileId] = React.useState(fileId);
    const [resolvedDocumentName, setResolvedDocumentName] = React.useState("");

    // Playbook info (loaded from analysis record)
    const [playbookId, setPlaybookId] = React.useState<string | null>(null);

    // Auth state
    const [isAuthInitialized, setIsAuthInitialized] = React.useState(false);
    const authProviderRef = React.useRef<MsalAuthProvider | null>(null);

    // Ref to track current chatMessages for save operations (avoids stale closure)
    const chatMessagesRef = React.useRef<IChatMessage[]>([]);

    // SprkChat state (new chat system - used when useLegacyChat=false)
    const [sprkChatAccessToken, setSprkChatAccessToken] = React.useState<string>("");
    const [sprkChatSessionId, setSprkChatSessionId] = React.useState<string | undefined>(undefined);

    // Initial execution state - tracks if we're running the first AI analysis
    const [isExecuting, setIsExecuting] = React.useState(false);
    const [executionProgress, setExecutionProgress] = React.useState("");

    // Pending execution - stores analysis data when auth wasn't ready at load time
    const [pendingExecution, setPendingExecution] = React.useState<{ analysis: IAnalysis; docId: string } | null>(null);

    // Initialize MSAL auth provider
    React.useEffect(() => {
        const initAuth = async () => {
            try {
                logInfo("AnalysisWorkspaceApp", "Initializing MSAL auth provider...");
                const authProvider = MsalAuthProvider.getInstance();
                await authProvider.initialize();
                authProviderRef.current = authProvider;
                setIsAuthInitialized(true);
                logInfo("AnalysisWorkspaceApp", "MSAL auth initialized successfully");
            } catch (err) {
                logError("AnalysisWorkspaceApp", "Failed to initialize MSAL auth", err);
                // Continue without auth - will fail on API calls
                setIsAuthInitialized(true);
            }
        };
        initAuth();
    }, []);

    // Function to get access token for API calls
    const getAccessToken = React.useCallback(async (): Promise<string> => {
        if (!authProviderRef.current) {
            throw new Error("Auth provider not initialized");
        }
        return authProviderRef.current.getToken(loginRequest.scopes);
    }, []);

    // Acquire and refresh access token for SprkChat component (new chat system)
    React.useEffect(() => {
        if (!isAuthInitialized || useLegacyChat) return;
        let isMounted = true;
        const acquireToken = async () => {
            try {
                const token = await getAccessToken();
                if (isMounted) {
                    setSprkChatAccessToken(token);
                    logInfo("AnalysisWorkspaceApp", "SprkChat access token acquired");
                }
            } catch (err) {
                logError("AnalysisWorkspaceApp", "Failed to acquire SprkChat access token", err);
            }
        };
        acquireToken();
        // Refresh token every 45 minutes (tokens typically expire in 60min)
        const refreshInterval = setInterval(acquireToken, 45 * 60 * 1000);
        return () => { isMounted = false; clearInterval(refreshInterval); };
    }, [isAuthInitialized, useLegacyChat, getAccessToken]);

    // SprkChat session created handler
    const handleSprkChatSessionCreated = React.useCallback((session: IChatSession) => {
        logInfo("AnalysisWorkspaceApp", `SprkChat session created: ${session.sessionId}`);
        setSprkChatSessionId(session.sessionId);
    }, []);

    // SSE Stream Hook for AI Chat (legacy)
    const [sseState, sseActions] = useSseStream({
        apiBaseUrl,
        analysisId,
        getAccessToken: isAuthInitialized ? getAccessToken : undefined,
        onToken: (token) => {
            setStreamingResponse(prev => prev + token);
        },
        onComplete: (fullResponse) => {
            // Add assistant message to chat history
            const assistantMessage: IChatMessage = {
                id: `msg-${Date.now()}`,
                role: "assistant",
                content: fullResponse,
                timestamp: new Date().toISOString()
            };
            setChatMessages(prev => [...prev, assistantMessage]);
            setStreamingResponse("");
            setIsChatDirty(true); // Mark chat as dirty to trigger auto-save
        },
        onError: (err) => {
            logError("AnalysisWorkspaceApp", "SSE stream error", err);
            // Add error message to chat
            const errorMessage: IChatMessage = {
                id: `msg-${Date.now()}`,
                role: "assistant",
                content: `Sorry, I encountered an error: ${err.message}. Please try again.`,
                timestamp: new Date().toISOString()
            };
            setChatMessages(prev => [...prev, errorMessage]);
            setStreamingResponse("");
        }
    });

    /**
     * Execute the initial AI analysis via BFF API with SSE streaming.
     * Called when loading a Draft analysis with empty working document.
     */
    const executeAnalysis = React.useCallback(async (analysis: IAnalysis, docId: string): Promise<void> => {
        if (!analysis._sprk_actionid_value) {
            logError("AnalysisWorkspaceApp", "Cannot execute: no action ID set");
            setError("Cannot execute analysis: no action selected. Please edit the analysis and select an action.");
            return;
        }

        setIsExecuting(true);
        setExecutionProgress("Starting analysis...");
        logInfo("AnalysisWorkspaceApp", `Executing analysis for document ${docId} with action ${analysis._sprk_actionid_value}`);

        try {
            // Get access token
            let authHeaders: Record<string, string> = {};
            if (authProviderRef.current) {
                try {
                    const token = await authProviderRef.current.getToken(loginRequest.scopes);
                    authHeaders = { "Authorization": `Bearer ${token}` };
                } catch (authErr) {
                    logError("AnalysisWorkspaceApp", "Failed to acquire auth token for execute", authErr);
                    throw new Error("Authentication failed. Please refresh and try again.");
                }
            }

            // Build request body matching AnalysisExecuteRequest
            // Note: Skills, knowledge, and tools are resolved server-side from the action or playbook
            // Lookup fields use _fieldname_value format in OData responses
            // If playbook is set, include it - the API will resolve scopes from the playbook
            const requestBody: Record<string, unknown> = {
                documentIds: [docId],
                actionId: analysis._sprk_actionid_value,
                outputType: 0 // Document
            };

            // Add playbook ID if present (scopes will be resolved from playbook N:N relationships)
            if (playbookId) {
                requestBody.playbookId = playbookId;
                logInfo("AnalysisWorkspaceApp", `Including playbook ${playbookId} in execute request`);
            }

            logInfo("AnalysisWorkspaceApp", "Execute request body", requestBody);

            // Normalize apiBaseUrl - remove trailing /api if present
            const normalizedBaseUrl = apiBaseUrl.replace(/\/api\/?$/, "");

            // Make fetch request to execute endpoint with SSE
            const response = await fetch(`${normalizedBaseUrl}/api/ai/analysis/execute`, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "Accept": "text/event-stream",
                    ...authHeaders
                },
                body: JSON.stringify(requestBody)
            });

            if (!response.ok) {
                const errorText = await response.text();
                throw new Error(`HTTP ${response.status}: ${errorText || response.statusText}`);
            }

            // Get reader for streaming
            const reader = response.body?.getReader();
            if (!reader) {
                throw new Error("Response body is not readable");
            }

            const decoder = new TextDecoder();
            let accumulatedText = "";
            let buffer = "";

            setExecutionProgress("Analyzing document...");

            // Read stream
            while (true) {
                const { done, value } = await reader.read();

                if (done) {
                    logInfo("AnalysisWorkspaceApp", "Execute stream completed");
                    break;
                }

                // Decode chunk
                buffer += decoder.decode(value, { stream: true });

                // Process SSE events
                const lines = buffer.split("\n");
                buffer = lines.pop() || "";

                for (const line of lines) {
                    if (line.startsWith("data: ")) {
                        const data = line.slice(6).trim();
                        if (data === "[DONE]") continue;

                        try {
                            const parsed = JSON.parse(data);

                            if (parsed.type === "metadata") {
                                setExecutionProgress(`Analyzing: ${parsed.documentName || "document"}...`);
                            }

                            if (parsed.type === "chunk" && parsed.content) {
                                accumulatedText += parsed.content;
                                // Convert markdown to HTML for RichTextEditor display
                                const htmlContent = markdownToHtml(accumulatedText);
                                setWorkingDocument(htmlContent);
                            }

                            if (parsed.type === "error" || parsed.error) {
                                throw new Error(parsed.error || "Unknown error during analysis");
                            }

                            if (parsed.type === "done") {
                                logInfo("AnalysisWorkspaceApp", "Execute completed", parsed);
                            }
                        } catch (parseError) {
                            if (parseError instanceof Error && !parseError.message.includes("Unexpected token")) {
                                throw parseError;
                            }
                            // Non-JSON data, treat as content
                            if (data && data !== "[DONE]") {
                                accumulatedText += data;
                                // Convert markdown to HTML for RichTextEditor display
                                const htmlContent = markdownToHtml(accumulatedText);
                                setWorkingDocument(htmlContent);
                            }
                        }
                    }
                }
            }

            // Save the working document to Dataverse (as HTML for RichTextEditor compatibility)
            if (accumulatedText && isWebApiAvailable(webApi)) {
                logInfo("AnalysisWorkspaceApp", "Saving executed analysis to Dataverse");
                // Convert final markdown to HTML before saving
                const finalHtml = markdownToHtml(accumulatedText);
                await webApi.updateRecord("sprk_analysis", analysisId, {
                    sprk_workingdocument: finalHtml,
                    statuscode: 100000001 // In Progress
                });
                setIsDirty(false);
                setLastSaved(new Date());
            }

            setExecutionProgress("");
            logInfo("AnalysisWorkspaceApp", `Analysis execution completed: ${accumulatedText.length} chars`);

        } catch (err) {
            const errorMessage = err instanceof Error ? err.message : String(err);
            logError("AnalysisWorkspaceApp", "Analysis execution failed", err);
            setError(`Analysis failed: ${errorMessage}`);
            setExecutionProgress("");
        } finally {
            setIsExecuting(false);
        }
    }, [apiBaseUrl, analysisId, webApi, playbookId]);

    // Load analysis data on mount
    React.useEffect(() => {
        loadAnalysis();
    }, [analysisId]);

    // Execute pending analysis when auth becomes available
    React.useEffect(() => {
        if (isAuthInitialized && pendingExecution && !isExecuting) {
            logInfo("AnalysisWorkspaceApp", "Auth now initialized, executing pending analysis");
            setIsLoading(false);
            executeAnalysis(pendingExecution.analysis, pendingExecution.docId);
            setPendingExecution(null); // Clear pending
        }
    }, [isAuthInitialized, pendingExecution, isExecuting, executeAnalysis]);

    // Keep chatMessagesRef in sync with state (MUST run before auto-save effect)
    React.useEffect(() => {
        chatMessagesRef.current = chatMessages;
    }, [chatMessages]);

    // Auto-save effect for working document changes
    React.useEffect(() => {
        if (isDirty && !isSaving) {
            const timer = setTimeout(() => {
                saveAnalysisState();
            }, 3000); // Auto-save after 3 seconds of no changes
            return () => clearTimeout(timer);
        }
    }, [workingDocument, isDirty]);

    // Auto-save effect for chat message changes
    // Triggers save when isChatDirty is set (after user sends message or AI responds)
    React.useEffect(() => {
        if (!isChatDirty) return;

        const timer = setTimeout(() => {
            logInfo("AnalysisWorkspaceApp", `Auto-saving chat history (${chatMessages.length} messages)`);
            saveAnalysisState();
        }, 2000); // Save 2 seconds after chat changes
        return () => clearTimeout(timer);
    }, [isChatDirty, chatMessages.length]);

    // Notify parent of changes
    React.useEffect(() => {
        onWorkingDocumentChange(workingDocument);
    }, [workingDocument]);

    React.useEffect(() => {
        onChatHistoryChange(JSON.stringify(chatMessages));
    }, [chatMessages]);

    // Panel resize handlers - use refs to track initial state to avoid stale closure issues
    const handleResizeMouseDown = (handle: 'left-center' | 'center-right', panelElement: HTMLElement | null) => (e: React.MouseEvent) => {
        e.preventDefault();
        if (!panelElement) return;

        isDraggingRef.current = handle;
        dragStartXRef.current = e.clientX;
        dragStartWidthRef.current = panelElement.getBoundingClientRect().width;

        document.addEventListener('mousemove', handleResizeMouseMove);
        document.addEventListener('mouseup', handleResizeMouseUp);
        document.body.style.cursor = 'col-resize';
        document.body.style.userSelect = 'none';
    };

    const handleResizeMouseMove = (e: MouseEvent) => {
        if (!isDraggingRef.current || !containerRef.current) return;

        const containerWidth = containerRef.current.getBoundingClientRect().width;
        const delta = e.clientX - dragStartXRef.current;
        const rawWidth = dragStartWidthRef.current + delta;

        // Constants for minimum widths
        const MIN_LEFT = 250;
        const MIN_CENTER = 200;
        const MIN_RIGHT = 300;
        const HANDLE_WIDTH = 8; // 2 handles × 4px each

        if (isDraggingRef.current === 'left-center') {
            // Resize left panel - must leave room for center + right + handles
            const maxLeft = containerWidth - MIN_CENTER - MIN_RIGHT - HANDLE_WIDTH;
            const newWidth = Math.max(MIN_LEFT, Math.min(rawWidth, maxLeft));
            setLeftPanelWidth(newWidth);
        } else if (isDraggingRef.current === 'center-right') {
            // Resize center panel - must leave room for right panel + handle
            const currentLeftWidth = leftPanelRef.current?.getBoundingClientRect().width || MIN_LEFT;
            const maxCenter = containerWidth - currentLeftWidth - MIN_RIGHT - HANDLE_WIDTH;
            const newWidth = Math.max(MIN_CENTER, Math.min(rawWidth, maxCenter));
            setCenterPanelWidth(newWidth);
        }
    };

    const handleResizeMouseUp = () => {
        isDraggingRef.current = null;
        document.removeEventListener('mousemove', handleResizeMouseMove);
        document.removeEventListener('mouseup', handleResizeMouseUp);
        document.body.style.cursor = '';
        document.body.style.userSelect = '';
    };

    // Cleanup resize listeners on unmount
    React.useEffect(() => {
        return () => {
            document.removeEventListener('mousemove', handleResizeMouseMove);
            document.removeEventListener('mouseup', handleResizeMouseUp);
        };
    }, []);

    // ─────────────────────────────────────────────────────────────────────────
    // Data Loading
    // ─────────────────────────────────────────────────────────────────────────

    const loadAnalysis = async () => {
        try {
            setIsLoading(true);
            setError(null);

            logInfo("AnalysisWorkspaceApp", `Loading analysis: ${analysisId}`);

            if (!analysisId) {
                // New record - show helpful message instead of error
                logInfo("AnalysisWorkspaceApp", "New record - no analysis ID yet");
                setAnalysis({
                    sprk_analysisid: "",
                    sprk_name: "New Analysis",
                    sprk_documentid: documentId,
                    statuscode: 1, // Draft
                    sprk_workingdocument: "",
                    createdon: new Date().toISOString(),
                    modifiedon: new Date().toISOString()
                } as IAnalysis);
                setWorkingDocument("# New Analysis\n\n**Save the record first** to begin your analysis.\n\n1. Fill in the required fields (Name, Document, Action)\n2. Click **Save**\n3. Then click **Execute Analysis** to start");
                setIsLoading(false);
                return;
            }

            // Check if WebAPI is available (not in design-time/editor mode)
            if (!isWebApiAvailable(webApi)) {
                logInfo("AnalysisWorkspaceApp", "Design-time mode - showing placeholder");
                setAnalysis({
                    sprk_analysisid: "design-time-placeholder",
                    sprk_name: "Analysis Preview (Design Mode)",
                    sprk_documentid: documentId,
                    statuscode: 1, // Draft (standard statuscode field)
                    sprk_workingdocument: "# Design Mode Preview\n\nThis is a preview in the Custom Page editor.\n\nThe actual analysis content will load at runtime.",
                    createdon: new Date().toISOString(),
                    modifiedon: new Date().toISOString()
                } as IAnalysis);
                setWorkingDocument("# Design Mode Preview\n\nThis is a preview in the Custom Page editor.");
                return;
            }

            // Fetch analysis record from Dataverse (include fields needed for execute)
            // Note: Lookup fields use _fieldname_value format in OData responses
            const result = await webApi.retrieveRecord(
                "sprk_analysis",
                analysisId,
                "?$select=sprk_name,statuscode,sprk_workingdocument,sprk_chathistory,_sprk_actionid_value,_sprk_playbook_value,createdon,modifiedon,_sprk_documentid_value"
            );

            logInfo("AnalysisWorkspaceApp", "Analysis loaded", result);
            logInfo("AnalysisWorkspaceApp", `Chat history field: ${result.sprk_chathistory ? `exists (${result.sprk_chathistory.length} chars)` : "null/undefined"}`);

            // Store playbook ID if present (for execute request)
            if (result._sprk_playbook_value) {
                setPlaybookId(result._sprk_playbook_value);
                logInfo("AnalysisWorkspaceApp", `Playbook loaded: ${result._sprk_playbook_value}`);
            }

            setAnalysis(result as unknown as IAnalysis);
            // Load working document - convert markdown to HTML if needed for RichTextEditor
            const savedContent = result.sprk_workingdocument || "";
            const displayContent = savedContent && isMarkdown(savedContent)
                ? markdownToHtml(savedContent)
                : savedContent;
            setWorkingDocument(displayContent);

            // Fetch document details separately if we have a document ID
            const docId = result._sprk_documentid_value;
            if (docId) {
                // Store the document ID from analysis record
                setResolvedDocumentId(docId);

                try {
                    // Query document for SPE integration fields
                    // Document entity field names: sprk_containerid, sprk_graphitemid/sprk_driveitemid
                    // sprk_filename is used for display name (not sprk_name)
                    const docResult = await webApi.retrieveRecord(
                        "sprk_document",
                        docId,
                        "?$select=sprk_documentname,sprk_graphdriveid,sprk_graphitemid"
                    );
                    if (docResult.sprk_graphdriveid) {
                        setResolvedContainerId(docResult.sprk_graphdriveid);
                    }
                    if (docResult.sprk_graphitemid) {
                        setResolvedFileId(docResult.sprk_graphitemid);
                    }
                    if (docResult.sprk_documentname) {
                        setResolvedDocumentName(docResult.sprk_documentname);
                    }
                    logInfo("AnalysisWorkspaceApp", `Document fields resolved: docId=${docId}, container=${docResult.sprk_graphdriveid}, file=${docResult.sprk_graphitemid}`);
                } catch (docErr) {
                    logError("AnalysisWorkspaceApp", "Failed to load document details", docErr);
                }

                // Parse chat history FIRST - if exists, show choice dialog (ADR-023)
                // This must happen before draft execution check to ensure dialog is shown
                // Note: SprkChat manages its own sessions via Redis/Dataverse — skip legacy parsing
                let hasChatHistory = false;
                if (useLegacyChat && result.sprk_chathistory) {
                    try {
                        const parsed = JSON.parse(result.sprk_chathistory);
                        if (Array.isArray(parsed) && parsed.length > 0) {
                            // Store pending history and show choice dialog
                            setPendingChatHistory(parsed);
                            setShowResumeDialog(true);
                            hasChatHistory = true;
                            logInfo("AnalysisWorkspaceApp", `Found ${parsed.length} chat messages, showing resume dialog`);
                        } else {
                            logInfo("AnalysisWorkspaceApp", `Chat history parsed but empty or not array: ${JSON.stringify(parsed)}, enabling chat`);
                            setIsSessionResumed(true);
                        }
                    } catch (e) {
                        logError("AnalysisWorkspaceApp", "Failed to parse chat history, enabling chat anyway", e);
                        setIsSessionResumed(true);
                    }
                } else {
                    logInfo("AnalysisWorkspaceApp", "No chat history in analysis record, auto-resuming session");
                    // No previous chat history - auto-resume session so chat is immediately usable
                    setIsSessionResumed(true);
                }

                // Check if we need to auto-execute the analysis (Draft with empty working document)
                // Skip if we have chat history (user should choose to resume/fresh first)
                //
                // NOTE: "Draft" refers to the ANALYSIS record's statuscode (sprk_analysis.statuscode = 1),
                // NOT the Document record's status. A Draft analysis with no working document means:
                // - User selected action/playbook in AnalysisBuilder
                // - AnalysisBuilder created the analysis record but didn't execute yet
                // - AnalysisWorkspace should auto-execute when opened
                if (!hasChatHistory) {
                    const isDraft = result.statuscode === 1; // Analysis record statuscode: 1 = Draft
                    const hasEmptyWorkingDoc = !result.sprk_workingdocument || result.sprk_workingdocument.trim() === "";
                    // Note: Lookup fields use _fieldname_value format in OData responses
                    const actionId = result._sprk_actionid_value;
                    const hasAction = !!actionId;

                    logInfo("AnalysisWorkspaceApp", `Execute check: statuscode=${result.statuscode} (isDraft=${isDraft}), hasEmptyWorkingDoc=${hasEmptyWorkingDoc}, actionId=${actionId} (hasAction=${hasAction})`);

                    if (isDraft && hasEmptyWorkingDoc && hasAction) {
                        logInfo("AnalysisWorkspaceApp", `Draft analysis with empty working document - auth ready: ${isAuthInitialized}`);

                        if (isAuthInitialized) {
                            // Auth is ready, execute now
                            setIsLoading(false);
                            executeAnalysis(result as unknown as IAnalysis, docId);
                            return;
                        } else {
                            // Auth not ready, store for later execution
                            logInfo("AnalysisWorkspaceApp", "Auth not initialized yet, storing pending execution");
                            setPendingExecution({ analysis: result as unknown as IAnalysis, docId });
                        }
                    }
                }
            }

            onStatusChange(getStatusString(result.statuscode));

        } catch (err) {
            // Handle "not implemented" errors from design-time environment
            const errorMessage = getErrorMessage(err);
            if (errorMessage.toLowerCase().includes("not implemented")) {
                logInfo("AnalysisWorkspaceApp", "Design-time mode detected via error");
                setAnalysis({
                    sprk_analysisid: "design-time-placeholder",
                    sprk_name: "Analysis Preview (Design Mode)",
                    sprk_documentid: documentId,
                    statuscode: 1, // Draft (standard statuscode field)
                    sprk_workingdocument: "# Design Mode Preview\n\nThis is a preview in the Custom Page editor.",
                    createdon: new Date().toISOString(),
                    modifiedon: new Date().toISOString()
                } as IAnalysis);
                setWorkingDocument("# Design Mode Preview\n\nThis is a preview in the Custom Page editor.");
                return;
            }
            logError("AnalysisWorkspaceApp", "Failed to load analysis", err);
            setError(`Failed to load analysis: ${errorMessage}`);
        } finally {
            setIsLoading(false);
        }
    };

    /**
     * Resume session by calling the BFF API /resume endpoint.
     * Creates an in-memory session on the server so chat can work.
     */
    const resumeSession = async (includeChatHistory: boolean) => {
        if (!analysisId || !resolvedDocumentId) {
            logInfo("AnalysisWorkspaceApp", "Cannot resume - missing analysisId or documentId");
            setIsSessionResumed(true); // Allow chat anyway for new records
            return;
        }

        // IMPORTANT: Capture pending history in local variable BEFORE async operations
        // React state closures can become stale during async operations
        const historyToLoad = includeChatHistory ? pendingChatHistory : null;
        logInfo("AnalysisWorkspaceApp", `Resume session: includeChatHistory=${includeChatHistory}, historyToLoad has ${historyToLoad?.length ?? 0} messages`);

        setIsResumingSession(true);
        setShowResumeDialog(false);

        try {
            // Build request headers with authentication
            const headers: Record<string, string> = {
                "Content-Type": "application/json",
                "Accept": "application/json"
            };

            if (getAccessToken) {
                try {
                    const accessToken = await getAccessToken();
                    headers["Authorization"] = `Bearer ${accessToken}`;
                } catch (tokenError) {
                    logError("AnalysisWorkspaceApp", "Failed to acquire access token for resume", tokenError);
                    throw new Error("Authentication failed");
                }
            }

            // Build request body
            const requestBody = {
                documentId: resolvedDocumentId,
                documentName: resolvedDocumentName || "Unknown",
                workingDocument: workingDocument,
                chatHistory: historyToLoad,
                includeChatHistory
            };

            // Call the resume endpoint
            const baseUrl = apiBaseUrl.replace(/\/+$/, '');
            const apiPath = baseUrl.endsWith('/api') ? '' : '/api';
            const url = `${baseUrl}${apiPath}/ai/analysis/${analysisId}/resume`;

            logInfo("AnalysisWorkspaceApp", `Calling resume API: ${url}, includeChatHistory=${includeChatHistory}`);

            const response = await fetch(url, {
                method: "POST",
                headers,
                body: JSON.stringify(requestBody)
            });

            if (!response.ok) {
                const errorData = await response.json().catch(() => ({}));
                throw new Error(errorData.error || `HTTP ${response.status}`);
            }

            const result = await response.json();
            logInfo("AnalysisWorkspaceApp", `Session resumed: ${result.chatMessagesRestored} messages restored`);

            // Load chat history into UI if resuming with history
            if (historyToLoad && historyToLoad.length > 0) {
                logInfo("AnalysisWorkspaceApp", `Loading ${historyToLoad.length} chat messages into UI`);
                setChatMessages(historyToLoad);
            } else {
                // Starting fresh - clear chat messages
                logInfo("AnalysisWorkspaceApp", "Starting fresh - clearing chat messages");
                setChatMessages([]);
            }

            setIsSessionResumed(true);
            setPendingChatHistory(null);

        } catch (err) {
            logError("AnalysisWorkspaceApp", "Failed to resume session", err);
            // Still allow chat even if resume fails - the error will show on first message
            // Also load chat history into UI if user wanted to resume with history
            if (historyToLoad && historyToLoad.length > 0) {
                logInfo("AnalysisWorkspaceApp", `Loading ${historyToLoad.length} chat messages despite API error`);
                setChatMessages(historyToLoad);
            }
            setIsSessionResumed(true);
            setPendingChatHistory(null);
        } finally {
            setIsResumingSession(false);
        }
    };

    // Dialog handlers
    const handleResumeWithHistory = () => {
        resumeSession(true);
    };

    const handleStartFresh = () => {
        resumeSession(false);
    };

    const handleDismissResumeDialog = () => {
        // If user dismisses, start fresh by default
        resumeSession(false);
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Save Operations
    // ─────────────────────────────────────────────────────────────────────────

    /**
     * Save analysis state (working document + chat history) to Dataverse.
     * Called by auto-save effects when either workingDocument or chatMessages change.
     */
    const saveAnalysisState = async () => {
        if (!analysisId || isSaving) return;

        // Skip save in design-time mode
        if (!isWebApiAvailable(webApi)) {
            logInfo("AnalysisWorkspaceApp", "Design-time mode - save skipped");
            setIsDirty(false);
            setIsChatDirty(false);
            setLastSaved(new Date());
            return;
        }

        try {
            setIsSaving(true);
            logInfo("AnalysisWorkspaceApp", "Saving analysis state (working document + chat history)");

            await webApi.updateRecord("sprk_analysis", analysisId, {
                sprk_workingdocument: workingDocument,
                sprk_chathistory: JSON.stringify(chatMessagesRef.current)
            });

            setIsDirty(false);
            setIsChatDirty(false);
            setLastSaved(new Date());
            logInfo("AnalysisWorkspaceApp", "Analysis state saved");

        } catch (err) {
            // Handle "not implemented" errors from design-time environment
            const errorMessage = err instanceof Error ? err.message : String(err);
            if (errorMessage.toLowerCase().includes("not implemented")) {
                logInfo("AnalysisWorkspaceApp", "Design-time mode - save skipped");
                setIsDirty(false);
                setIsChatDirty(false);
                return;
            }
            logError("AnalysisWorkspaceApp", "Failed to save analysis state", err);
        } finally {
            setIsSaving(false);
        }
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Event Handlers
    // ─────────────────────────────────────────────────────────────────────────

    const handleDocumentChange = (content: string) => {
        setWorkingDocument(content);
        setIsDirty(true);
    };

    const handleManualSave = () => {
        saveAnalysisState();
    };

    /**
     * Handle manual re-execution of analysis.
     * Allows user to re-run the AI analysis on demand.
     */
    const handleReExecute = async () => {
        if (!_analysis || isExecuting) return;

        const docId = resolvedDocumentId;
        if (!docId) {
            setError("Cannot re-execute: no document associated with this analysis.");
            return;
        }

        logInfo("AnalysisWorkspaceApp", "User triggered re-execution of analysis");
        await executeAnalysis(_analysis, docId);
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /**
     * Convert statuscode value to display string
     * Uses standard Power Apps Status Reason (statuscode) field values
     */
    const getStatusString = (status: number): string => {
        switch (status) {
            case 1: return "Draft";
            case 100000001: return "In Progress";
            case 100000002: return "In Review";
            case 2: return "Closed";
            case 100000003: return "Completed";
            default: return "Unknown";
        }
    };

    const formatLastSaved = (): string => {
        if (!lastSaved) return "";
        return `Last saved: ${lastSaved.toLocaleTimeString()}`;
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Chat Handlers
    // ─────────────────────────────────────────────────────────────────────────

    const [chatInput, setChatInput] = React.useState("");

    const handleSendMessage = async () => {
        if (!chatInput.trim() || sseState.isStreaming) return;

        const userMessage: IChatMessage = {
            id: `msg-${Date.now()}`,
            role: "user",
            content: chatInput.trim(),
            timestamp: new Date().toISOString()
        };

        setChatMessages(prev => [...prev, userMessage]);
        const messageText = chatInput.trim();
        setChatInput("");
        setIsChatDirty(true); // Mark chat as dirty to trigger auto-save

        logInfo("AnalysisWorkspaceApp", "Chat message sent", userMessage);

        // Build chat history for context
        const history = chatMessages.map(msg => ({
            role: msg.role,
            content: msg.content
        }));

        // Start SSE stream for AI response
        await sseActions.sendMessage(messageText, history);
    };

    const handleChatKeyDown = (e: React.KeyboardEvent) => {
        if (e.key === "Enter" && !e.shiftKey) {
            e.preventDefault();
            handleSendMessage();
        }
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
                <div ref={leftPanelRef} className={styles.leftPanel} style={leftPanelWidth ? { flex: `0 0 ${leftPanelWidth}px` } : undefined}>
                    <div className={styles.panelHeader}>
                        <div className={styles.panelHeaderLeft}>
                            <Text weight="semibold">ANALYSIS OUTPUT</Text>
                            {isExecuting ? (
                                <span className={styles.streamingIndicator}>
                                    <Spinner size="tiny" />
                                    <Text size={200}>{executionProgress || "Analyzing..."}</Text>
                                </span>
                            ) : (
                                <span className={`${styles.statusIndicator} ${isDirty ? styles.unsavedIndicator : styles.savedIndicator}`}>
                                    {isDirty ? "• Unsaved" : formatLastSaved()}
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
                                title={isDocumentPanelVisible ? "Hide document" : "Show document"}
                                aria-label={isDocumentPanelVisible ? "Hide document" : "Show document"}
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
                                {resolvedDocumentName ? resolvedDocumentName.toUpperCase() : "ORIGINAL DOCUMENT"}
                            </Text>
                        </div>
                        <div className={styles.panelHeaderActions}>
                            <Button
                                icon={isConversationPanelVisible ? <ChevronDoubleRight20Regular /> : <ChevronDoubleLeft20Regular />}
                                appearance="subtle"
                                size="small"
                                onClick={() => setIsConversationPanelVisible(!isConversationPanelVisible)}
                                title={isConversationPanelVisible ? "Hide conversation" : "Show conversation"}
                                aria-label={isConversationPanelVisible ? "Hide conversation" : "Show conversation"}
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
                                    <div style={{
                                        textAlign: "center",
                                        padding: "24px",
                                        color: tokens.colorNeutralForeground3
                                    }}>
                                        <ChatRegular style={{ fontSize: "32px", marginBottom: "12px" }} />
                                        <Text block size={300}>Start a conversation</Text>
                                        <Text block size={200} style={{ marginTop: "8px" }}>
                                            Ask questions or request changes to refine your analysis
                                        </Text>
                                    </div>
                                ) : (
                                    chatMessages.map((msg) => (
                                        <div
                                            key={msg.id}
                                            className={`${styles.chatMessage} ${
                                                msg.role === "user"
                                                    ? styles.chatMessageUser
                                                    : styles.chatMessageAssistant
                                            }`}
                                        >
                                            <div className={styles.chatMessageRole}>
                                                {msg.role === "user" ? "You" : "AI Assistant"}
                                            </div>
                                            <div className={styles.chatMessageContent}>
                                                {msg.content}
                                            </div>
                                        </div>
                                    ))
                                )}
                                {/* Streaming response - show in real-time */}
                                {sseState.isStreaming && (
                                    <div
                                        className={`${styles.chatMessage} ${styles.chatMessageAssistant}`}
                                    >
                                        <div className={styles.chatMessageRole}>
                                            AI Assistant
                                        </div>
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
                                    <div style={{ display: "flex", alignItems: "center", gap: "8px", padding: "12px", justifyContent: "center" }}>
                                        <Spinner size="tiny" />
                                        <Text size={200}>Initializing session...</Text>
                                    </div>
                                ) : !isSessionResumed && showResumeDialog ? (
                                    <div style={{ padding: "12px", textAlign: "center", color: tokens.colorNeutralForeground3 }}>
                                        <Text size={200}>Choose how to continue...</Text>
                                    </div>
                                ) : (
                                    <div className={styles.chatInputWrapper}>
                                        <Textarea
                                            className={styles.chatTextarea}
                                            placeholder={isSessionResumed ? "Type a message..." : "Waiting for session..."}
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
                                playbookId={playbookId || ""}
                                apiBaseUrl={apiBaseUrl.replace(/\/+$/, "").replace(/\/api\/?$/, "")}
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
