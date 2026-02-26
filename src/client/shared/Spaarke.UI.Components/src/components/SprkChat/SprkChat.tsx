/**
 * SprkChat - Main reusable chat component
 *
 * Composes all SprkChat sub-components into a complete chat experience:
 * - Context selector (document + playbook)
 * - Predefined prompt suggestions
 * - Message list with streaming support
 * - Input area with Ctrl+Enter
 * - Highlight-and-refine on text selection
 *
 * Integrates with ChatEndpoints SSE API for real-time streaming responses.
 *
 * @see ADR-012 - Shared Component Library
 * @see ADR-021 - Fluent UI v9; makeStyles; design tokens; dark mode
 * @see ADR-022 - React 16 APIs only
 */

import * as React from "react";
import {
    makeStyles,
    shorthands,
    tokens,
    Spinner,
    Text,
    Button,
} from "@fluentui/react-components";
import { ISprkChatProps, IChatMessage, IPlaybookOption } from "./types";
import { SprkChatMessage } from "./SprkChatMessage";
import { SprkChatInput } from "./SprkChatInput";
import { SprkChatContextSelector } from "./SprkChatContextSelector";
import { SprkChatPredefinedPrompts } from "./SprkChatPredefinedPrompts";
import { SprkChatHighlightRefine } from "./SprkChatHighlightRefine";
import { SprkChatSuggestions } from "./SprkChatSuggestions";
import { useSseStream, parseSseEvent } from "./hooks/useSseStream";
import { useChatSession } from "./hooks/useChatSession";
import { useChatPlaybooks } from "./hooks/useChatPlaybooks";
import { useSelectionListener } from "./hooks/useSelectionListener";

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
    root: {
        display: "flex",
        flexDirection: "column",
        height: "100%",
        backgroundColor: tokens.colorNeutralBackground1,
        ...shorthands.overflow("hidden"),
    },
    messageList: {
        flexGrow: 1,
        overflowY: "auto",
        display: "flex",
        flexDirection: "column",
        ...shorthands.gap(tokens.spacingVerticalS),
        ...shorthands.padding(tokens.spacingVerticalM, tokens.spacingHorizontalM),
        position: "relative",
    },
    emptyState: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        flexGrow: 1,
        ...shorthands.gap(tokens.spacingVerticalS),
        color: tokens.colorNeutralForeground3,
    },
    loadingContainer: {
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        ...shorthands.padding(tokens.spacingVerticalL),
    },
    errorBanner: {
        ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
        backgroundColor: tokens.colorPaletteRedBackground1,
        color: tokens.colorPaletteRedForeground1,
        fontSize: tokens.fontSizeBase200,
        textAlign: "center",
    },
    playbookChips: {
        display: "flex",
        flexWrap: "wrap",
        ...shorthands.gap(tokens.spacingHorizontalS),
        ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
    },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * SprkChat - Full-featured chat component with SSE streaming.
 *
 * @example
 * ```tsx
 * <SprkChat
 *   playbookId="abc-123"
 *   apiBaseUrl="https://spe-api-dev-67e2xz.azurewebsites.net"
 *   accessToken={token}
 *   documentId={docId}
 *   onSessionCreated={(session) => console.log("Session:", session.sessionId)}
 * />
 * ```
 */
export const SprkChat: React.FC<ISprkChatProps> = ({
    sessionId: initialSessionId,
    documentId,
    playbookId,
    apiBaseUrl,
    accessToken,
    onSessionCreated,
    className,
    documents = [],
    playbooks = [],
    predefinedPrompts = [],
    contentRef: externalContentRef,
    maxCharCount,
    hostContext,
    bridge,
}) => {
    const styles = useStyles();
    const messageListRef = React.useRef<HTMLDivElement>(null);
    const highlightContainerRef = externalContentRef || messageListRef;

    // Playbook discovery (fetches available playbooks for quick-action chips)
    const { playbooks: discoveredPlaybooks } = useChatPlaybooks({ apiBaseUrl, accessToken });

    // Merge passed-in playbooks with discovered ones (deduplicate by id)
    const allPlaybooks = React.useMemo((): IPlaybookOption[] => {
        const seen = new Set<string>(playbooks.map((p) => p.id));
        const merged = [...playbooks];
        for (const pb of discoveredPlaybooks) {
            if (!seen.has(pb.id)) {
                seen.add(pb.id);
                merged.push(pb);
            }
        }
        return merged;
    }, [playbooks, discoveredPlaybooks]);

    // Session management
    const chatSession = useChatSession({ apiBaseUrl, accessToken });
    const {
        session,
        messages,
        isLoading: isSessionLoading,
        error: sessionError,
        createSession,
        loadHistory,
        switchContext,
        addMessage,
        updateLastMessage,
    } = chatSession;

    // SSE streaming
    const sseStream = useSseStream();
    const {
        content: streamedContent,
        isDone: streamDone,
        isStreaming,
        error: streamError,
        suggestions,
        citations: streamCitations,
        startStream,
        clearSuggestions,
    } = sseStream;

    // Track current streaming state
    const isStreamingRef = React.useRef<boolean>(false);

    // Editor-sourced refine state (separate from chat SSE stream)
    const [isEditorRefining, setIsEditorRefining] = React.useState(false);
    const [editorRefineError, setEditorRefineError] = React.useState<Error | null>(null);
    const editorRefineAbortRef = React.useRef<AbortController | null>(null);

    // Additional documents state for multi-document context
    const [additionalDocumentIds, setAdditionalDocumentIds] = React.useState<string[]>([]);
    const additionalDocsDebounceRef = React.useRef<ReturnType<typeof setTimeout> | null>(null);

    // Cross-pane selection listener (receives selection_changed from Analysis Workspace editor)
    const { selection: crossPaneSelection, clearSelection: clearCrossPaneSelection } =
        useSelectionListener({
            bridge: bridge ?? null,
            enabled: !!bridge,
        });

    // Initialize session on mount
    React.useEffect(() => {
        const initSession = async () => {
            if (initialSessionId) {
                // Resume existing session
                const newSession = await createSession(documentId, playbookId, hostContext);
                if (newSession) {
                    // loadHistory needs the session to be set first - handled by useChatSession
                }
            } else {
                // Create new session
                const newSession = await createSession(documentId, playbookId, hostContext);
                if (newSession && onSessionCreated) {
                    onSessionCreated(newSession);
                }
            }
        };

        initSession();
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, []);

    // Load history when session is available
    React.useEffect(() => {
        if (session && initialSessionId) {
            loadHistory();
        }
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [session?.sessionId]);

    // Update streaming message content as tokens arrive
    React.useEffect(() => {
        if (isStreaming && streamedContent) {
            updateLastMessage(streamedContent);
        }
    }, [streamedContent, isStreaming, updateLastMessage]);

    // Handle stream completion
    React.useEffect(() => {
        if (streamDone && isStreamingRef.current) {
            isStreamingRef.current = false;
            // Final content is already set by the token updates
        }
    }, [streamDone]);

    // Auto-scroll to bottom on new messages
    React.useEffect(() => {
        if (messageListRef.current) {
            messageListRef.current.scrollTop = messageListRef.current.scrollHeight;
        }
    }, [messages, streamedContent]);

    // Send a message and start streaming the response
    const handleSend = React.useCallback(
        (messageText: string) => {
            if (!session || isStreaming) {
                return;
            }

            // Clear follow-up suggestions from previous response
            clearSuggestions();

            // Add user message
            const userMessage: IChatMessage = {
                role: "User",
                content: messageText,
                timestamp: new Date().toISOString(),
            };
            addMessage(userMessage);

            // Add placeholder assistant message for streaming
            const assistantMessage: IChatMessage = {
                role: "Assistant",
                content: "",
                timestamp: new Date().toISOString(),
            };
            addMessage(assistantMessage);
            isStreamingRef.current = true;

            // Start SSE stream — normalize URL to prevent double /api prefix
            const baseUrl = apiBaseUrl.replace(/\/+$/, "").replace(/\/api\/?$/, "");
            startStream(
                `${baseUrl}/api/ai/chat/sessions/${session.sessionId}/messages`,
                { message: messageText, documentId },
                accessToken
            );
        },
        [session, isStreaming, addMessage, startStream, clearSuggestions, apiBaseUrl, documentId, accessToken]
    );

    // Handle predefined prompt selection
    const handlePromptSelect = React.useCallback(
        (prompt: string) => {
            handleSend(prompt);
        },
        [handleSend]
    );

    // Handle follow-up suggestion selection — sends suggestion text as a new user message
    const handleSuggestionSelect = React.useCallback(
        (suggestion: string) => {
            handleSend(suggestion);
        },
        [handleSend]
    );

    // Handle context changes — clear cross-pane selection when document/record changes
    const handleDocumentChange = React.useCallback(
        (newDocId: string) => {
            clearCrossPaneSelection();
            switchContext(newDocId || undefined, playbookId);
        },
        [switchContext, playbookId, clearCrossPaneSelection]
    );

    const handlePlaybookChange = React.useCallback(
        (newPlaybookId: string) => {
            switchContext(documentId, newPlaybookId);
        },
        [switchContext, documentId]
    );

    // Handle additional documents change with debounce (300ms)
    const handleAdditionalDocumentsChange = React.useCallback(
        (newIds: string[]) => {
            setAdditionalDocumentIds(newIds);

            // Debounce the API call to avoid rapid-fire PATCH requests
            if (additionalDocsDebounceRef.current) {
                clearTimeout(additionalDocsDebounceRef.current);
            }
            additionalDocsDebounceRef.current = setTimeout(() => {
                switchContext(documentId, playbookId, hostContext, newIds);
            }, 300);
        },
        [switchContext, documentId, playbookId, hostContext]
    );

    // Clean up debounce timer and editor refine abort controller on unmount
    React.useEffect(() => {
        return () => {
            if (additionalDocsDebounceRef.current) {
                clearTimeout(additionalDocsDebounceRef.current);
            }
            if (editorRefineAbortRef.current) {
                editorRefineAbortRef.current.abort();
            }
        };
    }, []);

    // Extract tenant ID from JWT for X-Tenant-Id header (same logic as useSseStream)
    const extractTenantIdFromToken = React.useCallback((token: string): string | null => {
        try {
            const parts = token.split(".");
            if (parts.length !== 3) return null;
            const payload = JSON.parse(atob(parts[1]));
            return payload.tid || null;
        } catch {
            return null;
        }
    }, []);

    /**
     * Handle editor-sourced refine requests by streaming SSE response through the bridge.
     *
     * When the selection comes from the Analysis Workspace editor (source="editor"),
     * the revised text is routed through SprkChatBridge as document_stream_* events
     * so the Analysis Workspace can handle it via its diff review or streaming pipeline.
     *
     * The operationType is set to "diff" for selection-revision operations, which causes
     * the Analysis Workspace's useDiffReview hook to buffer tokens and show the
     * DiffReviewPanel for user review (Accept/Reject/Edit).
     */
    const handleEditorRefine = React.useCallback(
        (selectedText: string, instruction: string) => {
            if (!session || !bridge || isEditorRefining || isStreaming) {
                return;
            }

            // Cancel any existing editor refine
            if (editorRefineAbortRef.current) {
                editorRefineAbortRef.current.abort();
            }

            // Add a brief user message in the chat for awareness
            const refineMessage: IChatMessage = {
                role: "User",
                content: `Refining editor selection: "${selectedText.substring(0, 100)}${selectedText.length > 100 ? "\u2026" : ""}"\n\nInstruction: ${instruction}`,
                timestamp: new Date().toISOString(),
            };
            addMessage(refineMessage);

            // Add a placeholder assistant message that will show status
            const statusMessage: IChatMessage = {
                role: "Assistant",
                content: "Generating revision\u2026",
                timestamp: new Date().toISOString(),
            };
            addMessage(statusMessage);

            setIsEditorRefining(true);
            setEditorRefineError(null);

            const controller = new AbortController();
            editorRefineAbortRef.current = controller;

            const operationId = `refine-${Date.now()}-${Math.random().toString(36).substring(2, 8)}`;
            let tokenIndex = 0;

            const runRefineStream = async () => {
                try {
                    // Emit document_stream_start through the bridge with operationType "diff"
                    // so the Analysis Workspace's useDiffReview hook captures and buffers tokens.
                    bridge.emit("document_stream_start", {
                        operationId,
                        targetPosition: "selection",
                        operationType: "diff",
                    });

                    const tenantId = extractTenantIdFromToken(accessToken);
                    const baseUrl = apiBaseUrl.replace(/\/+$/, "").replace(/\/api\/?$/, "");
                    const refineUrl = `${baseUrl}/api/ai/chat/sessions/${session.sessionId}/refine`;

                    const response = await fetch(refineUrl, {
                        method: "POST",
                        headers: {
                            "Content-Type": "application/json",
                            Authorization: `Bearer ${accessToken}`,
                            ...(tenantId ? { "X-Tenant-Id": tenantId } : {}),
                        },
                        body: JSON.stringify({
                            selectedText,
                            instruction,
                            // TODO: PH-112-A — surroundingContext not yet available from editor
                            surroundingContext: null,
                        }),
                        signal: controller.signal,
                    });

                    if (!response.ok) {
                        const errorText = await response.text();
                        throw new Error(
                            `Refine request failed (${response.status}): ${errorText}`
                        );
                    }

                    if (!response.body) {
                        throw new Error("Response body is empty");
                    }

                    const reader = response.body.getReader();
                    const decoder = new TextDecoder();
                    let buffer = "";

                    while (true) {
                        const { done, value } = await reader.read();
                        if (done) break;

                        buffer += decoder.decode(value, { stream: true });

                        // Parse SSE events separated by double newlines
                        const parts = buffer.split("\n\n");
                        buffer = parts.pop() || "";

                        for (const part of parts) {
                            const lines = part.split("\n");
                            for (const line of lines) {
                                const event = parseSseEvent(line);
                                if (!event) continue;

                                if (event.type === "token" && event.content) {
                                    // Route token through bridge for Analysis Workspace consumption
                                    bridge.emit("document_stream_token", {
                                        operationId,
                                        token: event.content,
                                        index: tokenIndex++,
                                    });
                                } else if (event.type === "done") {
                                    // Stream completed successfully
                                    bridge.emit("document_stream_end", {
                                        operationId,
                                        cancelled: false,
                                        totalTokens: tokenIndex,
                                    });
                                } else if (event.type === "error") {
                                    throw new Error(event.content || "Refinement stream error");
                                }
                            }
                        }
                    }

                    // Process any remaining buffer
                    if (buffer.trim()) {
                        const lines = buffer.split("\n");
                        for (const line of lines) {
                            const event = parseSseEvent(line);
                            if (!event) continue;
                            if (event.type === "token" && event.content) {
                                bridge.emit("document_stream_token", {
                                    operationId,
                                    token: event.content,
                                    index: tokenIndex++,
                                });
                            } else if (event.type === "done") {
                                bridge.emit("document_stream_end", {
                                    operationId,
                                    cancelled: false,
                                    totalTokens: tokenIndex,
                                });
                            } else if (event.type === "error") {
                                throw new Error(event.content || "Refinement stream error");
                            }
                        }
                    }

                    // Update the chat status message
                    if (tokenIndex === 0) {
                        updateLastMessage("No changes suggested.");
                    } else {
                        updateLastMessage("Revision sent to editor for review.");
                    }

                    // Clear cross-pane selection after successful submission
                    clearCrossPaneSelection();
                } catch (err: unknown) {
                    if (err instanceof DOMException && err.name === "AbortError") {
                        // Cancelled by user, not an error
                        bridge.emit("document_stream_end", {
                            operationId,
                            cancelled: true,
                            totalTokens: tokenIndex,
                        });
                        updateLastMessage("Refinement cancelled.");
                        return;
                    }

                    const errorObj = err instanceof Error ? err : new Error("Unknown refine error");
                    setEditorRefineError(errorObj);

                    // Emit stream end with cancel to clean up Analysis Workspace state
                    bridge.emit("document_stream_end", {
                        operationId,
                        cancelled: true,
                        totalTokens: tokenIndex,
                    });

                    // Show error in the chat status message
                    updateLastMessage(`Refinement error: ${errorObj.message}`);
                } finally {
                    setIsEditorRefining(false);
                    editorRefineAbortRef.current = null;
                }
            };

            runRefineStream();
        },
        [session, bridge, isEditorRefining, isStreaming, addMessage, updateLastMessage,
         apiBaseUrl, accessToken, clearCrossPaneSelection, extractTenantIdFromToken]
    );

    /**
     * Handle chat-sourced refine requests (local text selection in chat messages).
     * Uses the standard useSseStream hook to show the refined text as a chat response.
     */
    const handleChatRefine = React.useCallback(
        (selectedText: string, instruction: string) => {
            if (!session || isStreaming) {
                return;
            }

            // Add the refinement request as a user message
            const refineMessage: IChatMessage = {
                role: "User",
                content: `Refine the following text: "${selectedText}"\n\nInstruction: ${instruction}`,
                timestamp: new Date().toISOString(),
            };
            addMessage(refineMessage);

            // Add placeholder for assistant response
            const assistantMessage: IChatMessage = {
                role: "Assistant",
                content: "",
                timestamp: new Date().toISOString(),
            };
            addMessage(assistantMessage);
            isStreamingRef.current = true;

            // Start SSE stream to refine endpoint — normalize URL to prevent double /api prefix
            const baseUrl = apiBaseUrl.replace(/\/+$/, "").replace(/\/api\/?$/, "");
            startStream(
                `${baseUrl}/api/ai/chat/sessions/${session.sessionId}/refine`,
                { selectedText, instruction },
                accessToken
            );
        },
        [session, isStreaming, addMessage, startStream, apiBaseUrl, accessToken]
    );

    /**
     * Handle highlight-and-refine: routes to either editor-sourced or chat-sourced handler.
     *
     * Task 112: Selection-based revision flow
     * - Editor source (cross-pane): SSE tokens are routed through SprkChatBridge
     *   as document_stream_* events for diff review in the Analysis Workspace.
     * - Chat source (local DOM): SSE tokens are displayed as a chat response.
     */
    const handleRefine = React.useCallback(
        (selectedText: string, instruction: string) => {
            // Determine source from crossPaneSelection state
            const isEditorSource = !!crossPaneSelection && crossPaneSelection.text.length > 0;

            if (isEditorSource && bridge) {
                handleEditorRefine(selectedText, instruction);
            } else {
                handleChatRefine(selectedText, instruction);
            }
        },
        [crossPaneSelection, bridge, handleEditorRefine, handleChatRefine]
    );

    // Handle playbook chip selection — switches context to the selected playbook
    const handlePlaybookChipClick = React.useCallback(
        (pb: IPlaybookOption) => {
            switchContext(documentId, pb.id, hostContext);
        },
        [switchContext, documentId, hostContext]
    );

    const displayError = sessionError || streamError || editorRefineError;
    const showPredefinedPrompts = messages.length === 0 && predefinedPrompts.length > 0 && !isSessionLoading;
    const showPlaybookChips = messages.length === 0 && allPlaybooks.length > 1 && !isSessionLoading;

    return (
        <div className={className ? `${styles.root} ${className}` : styles.root} data-testid="sprkchat-root">
            {/* Context selector bar */}
            {(documents.length > 0 || allPlaybooks.length > 0) && (
                <SprkChatContextSelector
                    selectedDocumentId={documentId}
                    selectedPlaybookId={playbookId}
                    documents={documents}
                    playbooks={allPlaybooks}
                    onDocumentChange={handleDocumentChange}
                    onPlaybookChange={handlePlaybookChange}
                    disabled={isStreaming || isEditorRefining}
                    additionalDocumentIds={additionalDocumentIds}
                    onAdditionalDocumentsChange={handleAdditionalDocumentsChange}
                />
            )}

            {/* Error banner */}
            {displayError && (
                <div className={styles.errorBanner} role="alert" data-testid="chat-error-banner">
                    {displayError.message}
                </div>
            )}

            {/* Message list */}
            <div
                className={styles.messageList}
                ref={messageListRef}
                role="list"
                aria-label="Chat messages"
                data-testid="chat-message-list"
            >
                {isSessionLoading && messages.length === 0 && (
                    <div className={styles.loadingContainer}>
                        <Spinner label="Loading chat..." />
                    </div>
                )}

                {showPlaybookChips && (
                    <div className={styles.playbookChips} data-testid="playbook-chips">
                        {allPlaybooks
                            .filter((pb) => pb.id !== playbookId)
                            .map((pb) => (
                                <Button
                                    key={pb.id}
                                    appearance="outline"
                                    size="small"
                                    onClick={() => handlePlaybookChipClick(pb)}
                                    disabled={isStreaming || !session}
                                    title={pb.description || pb.name}
                                >
                                    {pb.name}
                                </Button>
                            ))}
                    </div>
                )}

                {showPredefinedPrompts && (
                    <SprkChatPredefinedPrompts
                        prompts={predefinedPrompts}
                        onSelect={handlePromptSelect}
                        disabled={isStreaming || !session}
                    />
                )}

                {!isSessionLoading && messages.length === 0 && !showPredefinedPrompts && (
                    <div className={styles.emptyState}>
                        <Text size={300}>No messages yet</Text>
                        <Text size={200}>Send a message to start the conversation</Text>
                    </div>
                )}

                {messages.map((msg, index) => {
                    // Citations apply to the last assistant message (the one that was just streamed).
                    // They are cleared on each new startStream call, so they always correspond
                    // to the most recent response.
                    const isLastAssistant =
                        index === messages.length - 1 && msg.role === "Assistant";
                    const messageCitations =
                        isLastAssistant && streamCitations.length > 0
                            ? streamCitations
                            : undefined;

                    return (
                        <SprkChatMessage
                            key={`msg-${index}`}
                            message={msg}
                            isStreaming={
                                isStreaming && isLastAssistant
                            }
                            citations={messageCitations}
                        />
                    );
                })}

                {/* Follow-up suggestions shown after the latest assistant response */}
                {suggestions.length > 0 && (
                    <SprkChatSuggestions
                        suggestions={suggestions}
                        onSelect={handleSuggestionSelect}
                        visible={!isStreaming && suggestions.length > 0}
                    />
                )}

                {/* Highlight-and-refine floating toolbar (local DOM selection + cross-pane bridge selection) */}
                <SprkChatHighlightRefine
                    contentRef={highlightContainerRef}
                    onRefine={handleRefine}
                    isRefining={isStreaming || isEditorRefining}
                    crossPaneSelection={crossPaneSelection}
                />
            </div>

            {/* Input area */}
            <SprkChatInput
                onSend={handleSend}
                disabled={isStreaming || !session || isSessionLoading}
                maxCharCount={maxCharCount}
            />
        </div>
    );
};

export default SprkChat;
