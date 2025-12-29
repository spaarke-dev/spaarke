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
    ChevronLeft20Regular
} from "@fluentui/react-icons";
import { IAnalysisWorkspaceAppProps, IChatMessage, IAnalysis } from "../types";
import { logInfo, logError } from "../utils/logger";
import { RichTextEditor } from "./RichTextEditor";
import { SourceDocumentViewer } from "./SourceDocumentViewer";
import { useSseStream } from "../hooks/useSseStream";

// Build info for version footer
const VERSION = "1.0.18";
const BUILD_DATE = "2025-12-14";

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
    const [resolvedContainerId, setResolvedContainerId] = React.useState(containerId);
    const [resolvedFileId, setResolvedFileId] = React.useState(fileId);
    const [resolvedDocumentName, setResolvedDocumentName] = React.useState("");

    // SSE Stream Hook for AI Chat
    const [sseState, sseActions] = useSseStream({
        apiBaseUrl,
        analysisId,
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

    // Load analysis data on mount
    React.useEffect(() => {
        loadAnalysis();
    }, [analysisId]);

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

            // Fetch analysis record from Dataverse
            const result = await webApi.retrieveRecord(
                "sprk_analysis",
                analysisId,
                "?$select=sprk_name,statuscode,sprk_workingdocument,sprk_chathistory,createdon,modifiedon,_sprk_documentid_value"
            );

            logInfo("AnalysisWorkspaceApp", "Analysis loaded", result);

            setAnalysis(result as unknown as IAnalysis);
            setWorkingDocument(result.sprk_workingdocument || "");

            // Fetch document details separately if we have a document ID
            const docId = result._sprk_documentid_value;
            if (docId) {
                try {
                    const docResult = await webApi.retrieveRecord(
                        "sprk_document",
                        docId,
                        "?$select=sprk_name,sprk_containerid,sprk_fileid"
                    );
                    if (docResult.sprk_containerid) {
                        setResolvedContainerId(docResult.sprk_containerid);
                    }
                    if (docResult.sprk_fileid) {
                        setResolvedFileId(docResult.sprk_fileid);
                    }
                    if (docResult.sprk_name) {
                        setResolvedDocumentName(docResult.sprk_name);
                    }
                    logInfo("AnalysisWorkspaceApp", `Document fields resolved: container=${docResult.sprk_containerid}, file=${docResult.sprk_fileid}`);
                } catch (docErr) {
                    logError("AnalysisWorkspaceApp", "Failed to load document details", docErr);
                }
            }

            // Parse chat history if exists
            if (result.sprk_chathistory) {
                try {
                    const parsed = JSON.parse(result.sprk_chathistory);
                    if (Array.isArray(parsed)) {
                        setChatMessages(parsed);
                        logInfo("AnalysisWorkspaceApp", `Loaded ${parsed.length} chat messages`);
                    }
                } catch (e) {
                    logError("AnalysisWorkspaceApp", "Failed to parse chat history", e);
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
                sprk_chathistory: JSON.stringify(chatMessages)
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

    const handleDocumentChange = (content: string) => {
        setWorkingDocument(content);
        setIsDirty(true);
    };

    const handleManualSave = () => {
        saveWorkingDocument();
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
            {/* Content - 3 Column Layout (no header - form already shows name/status) */}
            <div className={styles.content} ref={containerRef}>
                {/* LEFT PANEL - Analysis Output / Working Document */}
                <div ref={leftPanelRef} className={styles.leftPanel} style={leftPanelWidth ? { flex: `0 0 ${leftPanelWidth}px` } : undefined}>
                    <div className={styles.panelHeader}>
                        <div className={styles.panelHeaderLeft}>
                            <Text weight="semibold">ANALYSIS OUTPUT</Text>
                            <span className={`${styles.statusIndicator} ${isDirty ? styles.unsavedIndicator : styles.savedIndicator}`}>
                                {isDirty ? "• Unsaved" : formatLastSaved()}
                            </span>
                        </div>
                        <div className={styles.panelHeaderActions}>
                            <Tooltip content="Save" relationship="label">
                                <Button
                                    icon={<SaveRegular />}
                                    appearance="subtle"
                                    size="small"
                                    onClick={handleManualSave}
                                    disabled={!isDirty || isSaving}
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
                            documentId={documentId}
                            containerId={resolvedContainerId}
                            fileId={resolvedFileId}
                            apiBaseUrl={apiBaseUrl}
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
