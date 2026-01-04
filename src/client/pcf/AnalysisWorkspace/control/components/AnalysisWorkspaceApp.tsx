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
    Tooltip,
    Badge,
    Textarea,
    Dialog,
    DialogSurface,
    DialogTitle,
    DialogBody,
    DialogActions,
    DialogContent
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
    HistoryRegular,
    DocumentAddRegular,
    ArrowSync24Regular
} from "@fluentui/react-icons";
import { IAnalysisWorkspaceAppProps, IChatMessage, IAnalysis } from "../types";
import { logInfo, logError } from "../utils/logger";
import { markdownToHtml, isMarkdown } from "../utils/markdownToHtml";
import { RichTextEditor } from "./RichTextEditor";
import { SourceDocumentViewer } from "./SourceDocumentViewer";
import { useSseStream } from "../hooks/useSseStream";
import { MsalAuthProvider, loginRequest } from "../services/auth";

// Build info for version footer
const VERSION = "1.2.14";
const BUILD_DATE = "2026-01-03";

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
        gap: tokens.spacingHorizontalXS
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
        color: tokens.colorNeutralForeground3
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
        color: tokens.colorBrandForeground1
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
    const [lastSaved, setLastSaved] = React.useState<Date | null>(null);
    const [isSaving, setIsSaving] = React.useState(false);
    const [streamingResponse, setStreamingResponse] = React.useState("");
    const [isConversationPanelVisible, setIsConversationPanelVisible] = React.useState(true);
    const [isDocumentPanelVisible, setIsDocumentPanelVisible] = React.useState(true);

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
    const [resolvedDocumentId, setResolvedDocumentId] = React.useState(documentId);
    const [resolvedContainerId, setResolvedContainerId] = React.useState(containerId);
    const [resolvedFileId, setResolvedFileId] = React.useState(fileId);
    const [resolvedDocumentName, setResolvedDocumentName] = React.useState("");

    // Auth state
    const [isAuthInitialized, setIsAuthInitialized] = React.useState(false);
    const authProviderRef = React.useRef<MsalAuthProvider | null>(null);

    // Ref to track current chatMessages for save operations (avoids stale closure)
    const chatMessagesRef = React.useRef<IChatMessage[]>([]);

    // Choice dialog state (ADR-023: Resume vs Start Fresh)
    const [showResumeDialog, setShowResumeDialog] = React.useState(false);
    const [pendingChatHistory, setPendingChatHistory] = React.useState<IChatMessage[] | null>(null);

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

    // SSE Stream Hook for AI Chat
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
            setIsDirty(true);
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
            // Note: Skills, knowledge, and tools are resolved server-side from the action
            // Lookup fields use _fieldname_value format in OData responses
            const requestBody = {
                documentIds: [docId],
                actionId: analysis._sprk_actionid_value,
                outputType: 0 // Document
            };

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
    }, [apiBaseUrl, analysisId, webApi]);

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

    // Auto-save effect
    React.useEffect(() => {
        if (isDirty && !isSaving) {
            const timer = setTimeout(() => {
                saveWorkingDocument();
            }, 3000); // Auto-save after 3 seconds of no changes
            return () => clearTimeout(timer);
        }
    }, [workingDocument, isDirty]);

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
            const result = await webApi.retrieveRecord(
                "sprk_analysis",
                analysisId,
                "?$select=sprk_name,statuscode,sprk_workingdocument,sprk_chathistory,_sprk_actionid_value,createdon,modifiedon,_sprk_documentid_value"
            );

            logInfo("AnalysisWorkspaceApp", "Analysis loaded", result);
            logInfo("AnalysisWorkspaceApp", `Chat history field: ${result.sprk_chathistory ? `exists (${result.sprk_chathistory.length} chars)` : "null/undefined"}`);

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

                // Check if we need to execute the analysis (Draft with empty working document)
                const isDraft = result.statuscode === 1; // Draft status
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

            // Parse chat history if exists - show choice dialog (ADR-023)
            if (result.sprk_chathistory) {
                try {
                    const parsed = JSON.parse(result.sprk_chathistory);
                    if (Array.isArray(parsed) && parsed.length > 0) {
                        // Store pending history and show choice dialog
                        setPendingChatHistory(parsed);
                        setShowResumeDialog(true);
                        logInfo("AnalysisWorkspaceApp", `Found ${parsed.length} chat messages, showing resume dialog`);
                    } else {
                        logInfo("AnalysisWorkspaceApp", `Chat history parsed but empty or not array: ${JSON.stringify(parsed)}`);
                    }
                } catch (e) {
                    logError("AnalysisWorkspaceApp", "Failed to parse chat history", e);
                }
            } else {
                logInfo("AnalysisWorkspaceApp", "No chat history in analysis record");
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

    // ─────────────────────────────────────────────────────────────────────────
    // Save Operations
    // ─────────────────────────────────────────────────────────────────────────

    const saveWorkingDocument = async () => {
        if (!analysisId || isSaving) return;

        // Skip save in design-time mode
        if (!isWebApiAvailable(webApi)) {
            logInfo("AnalysisWorkspaceApp", "Design-time mode - save skipped");
            setIsDirty(false);
            setLastSaved(new Date());
            return;
        }

        try {
            setIsSaving(true);
            logInfo("AnalysisWorkspaceApp", "Saving working document");

            await webApi.updateRecord("sprk_analysis", analysisId, {
                sprk_workingdocument: workingDocument,
                sprk_chathistory: JSON.stringify(chatMessagesRef.current)
            });

            setIsDirty(false);
            setLastSaved(new Date());
            logInfo("AnalysisWorkspaceApp", "Working document saved");

        } catch (err) {
            // Handle "not implemented" errors from design-time environment
            const errorMessage = err instanceof Error ? err.message : String(err);
            if (errorMessage.toLowerCase().includes("not implemented")) {
                logInfo("AnalysisWorkspaceApp", "Design-time mode - save skipped");
                setIsDirty(false);
                return;
            }
            logError("AnalysisWorkspaceApp", "Failed to save working document", err);
        } finally {
            setIsSaving(false);
        }
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Event Handlers
    // ─────────────────────────────────────────────────────────────────────────

    // Choice dialog handlers (ADR-023)
    const handleResumeSession = () => {
        if (pendingChatHistory) {
            setChatMessages(pendingChatHistory);
            logInfo("AnalysisWorkspaceApp", `Resumed session with ${pendingChatHistory.length} messages`);
        }
        setShowResumeDialog(false);
        setPendingChatHistory(null);
    };

    const handleStartFresh = async () => {
        // Clear chat history in Dataverse
        if (analysisId && isWebApiAvailable(webApi)) {
            try {
                await webApi.updateRecord("sprk_analysis", analysisId, {
                    sprk_chathistory: null
                });
                logInfo("AnalysisWorkspaceApp", "Chat history cleared in Dataverse");
            } catch (err) {
                logError("AnalysisWorkspaceApp", "Failed to clear chat history", err);
            }
        }
        setChatMessages([]);
        setShowResumeDialog(false);
        setPendingChatHistory(null);
        logInfo("AnalysisWorkspaceApp", "Started fresh session");
    };

    // Dismiss dialog without clearing history (Cancel/Escape/click outside)
    const handleDismissDialog = () => {
        // Just close the dialog - don't clear history in Dataverse
        // The history remains in Dataverse for next time
        setShowResumeDialog(false);
        setPendingChatHistory(null);
        logInfo("AnalysisWorkspaceApp", "Dialog dismissed - history preserved in Dataverse");
    };

    const handleDocumentChange = (content: string) => {
        setWorkingDocument(content);
        setIsDirty(true);
    };

    const handleManualSave = () => {
        saveWorkingDocument();
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
        setIsDirty(true);

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
            {/* Resume Session Choice Dialog (ADR-023) */}
            <Dialog open={showResumeDialog} onOpenChange={(_, data) => !data.open && handleDismissDialog()}>
                <DialogSurface>
                    <DialogBody>
                        <DialogTitle>Resume Previous Session?</DialogTitle>
                        <DialogContent className={styles.choiceDialogContent}>
                            <Text>
                                This analysis has an existing conversation with{" "}
                                <strong>{pendingChatHistory?.length || 0} messages</strong>.
                            </Text>

                            <div className={styles.choiceOptionsContainer}>
                                <Button
                                    appearance="outline"
                                    className={styles.choiceOptionButton}
                                    onClick={handleResumeSession}
                                >
                                    <span className={styles.choiceOptionIcon}><HistoryRegular /></span>
                                    <div className={styles.choiceOptionText}>
                                        <span className={styles.choiceOptionTitle}>Resume Session</span>
                                        <span className={styles.choiceOptionDescription}>
                                            Continue with your previous conversation history
                                        </span>
                                    </div>
                                </Button>

                                <Button
                                    appearance="outline"
                                    className={styles.choiceOptionButton}
                                    onClick={handleStartFresh}
                                >
                                    <span className={styles.choiceOptionIcon}><DocumentAddRegular /></span>
                                    <div className={styles.choiceOptionText}>
                                        <span className={styles.choiceOptionTitle}>Start Fresh</span>
                                        <span className={styles.choiceOptionDescription}>
                                            Begin a new conversation (previous history will be cleared)
                                        </span>
                                    </div>
                                </Button>
                            </div>
                        </DialogContent>
                        <DialogActions>
                            <Button appearance="secondary" onClick={handleDismissDialog}>Cancel</Button>
                        </DialogActions>
                    </DialogBody>
                </DialogSurface>
            </Dialog>

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
                            <Tooltip content="Re-execute analysis" relationship="label">
                                <Button
                                    icon={<ArrowSync24Regular />}
                                    appearance="subtle"
                                    size="small"
                                    onClick={handleReExecute}
                                    disabled={isExecuting || !_analysis?._sprk_actionid_value}
                                />
                            </Tooltip>
                            <Tooltip content="Save" relationship="label">
                                <Button
                                    icon={<SaveRegular />}
                                    appearance="subtle"
                                    size="small"
                                    onClick={handleManualSave}
                                    disabled={!isDirty || isSaving || isExecuting}
                                />
                            </Tooltip>
                            <Tooltip content="Copy to clipboard" relationship="label">
                                <Button
                                    icon={<Copy24Regular />}
                                    appearance="subtle"
                                    size="small"
                                    onClick={() => navigator.clipboard.writeText(workingDocument)}
                                />
                            </Tooltip>
                            <Tooltip content="Download" relationship="label">
                                <Button
                                    icon={<ArrowDownload24Regular />}
                                    appearance="subtle"
                                    size="small"
                                />
                            </Tooltip>
                            <Tooltip content={isDocumentPanelVisible ? "Hide document" : "Show document"} relationship="label">
                                <Button
                                    icon={isDocumentPanelVisible ? <ChevronRight20Regular /> : <ChevronLeft20Regular />}
                                    appearance="subtle"
                                    size="small"
                                    onClick={() => setIsDocumentPanelVisible(!isDocumentPanelVisible)}
                                />
                            </Tooltip>
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
                            <Tooltip content={isConversationPanelVisible ? "Hide conversation" : "Show conversation"} relationship="label">
                                <Button
                                    icon={isConversationPanelVisible ? <ChevronDoubleRight20Regular /> : <ChevronDoubleLeft20Regular />}
                                    appearance="subtle"
                                    size="small"
                                    onClick={() => setIsConversationPanelVisible(!isConversationPanelVisible)}
                                />
                            </Tooltip>
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
                            {chatMessages.length > 0 && (
                                <Badge appearance="filled" color="brand" size="small">
                                    {chatMessages.length}
                                </Badge>
                            )}
                        </div>
                    </div>
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
                            <div className={styles.chatInputWrapper}>
                                <Textarea
                                    className={styles.chatTextarea}
                                    placeholder="Type a message..."
                                    value={chatInput}
                                    onChange={(_e, data) => setChatInput(data.value)}
                                    onKeyDown={handleChatKeyDown}
                                    disabled={sseState.isStreaming}
                                    resize="vertical"
                                    rows={2}
                                />
                                <Button
                                    icon={<Send24Regular />}
                                    appearance="primary"
                                    onClick={handleSendMessage}
                                    disabled={!chatInput.trim() || sseState.isStreaming}
                                />
                            </div>
                        </div>
                    </div>
                </div>
            </div>

            {/* Version Footer */}
            <div className={styles.versionFooter}>
                v{VERSION} • Built {BUILD_DATE}
            </div>
        </div>
    );
};
