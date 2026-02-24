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
} from "@fluentui/react-components";
import { ISprkChatProps, IChatMessage } from "./types";
import { SprkChatMessage } from "./SprkChatMessage";
import { SprkChatInput } from "./SprkChatInput";
import { SprkChatContextSelector } from "./SprkChatContextSelector";
import { SprkChatPredefinedPrompts } from "./SprkChatPredefinedPrompts";
import { SprkChatHighlightRefine } from "./SprkChatHighlightRefine";
import { useSseStream } from "./hooks/useSseStream";
import { useChatSession } from "./hooks/useChatSession";

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
}) => {
    const styles = useStyles();
    const messageListRef = React.useRef<HTMLDivElement>(null);
    const highlightContainerRef = externalContentRef || messageListRef;

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
        startStream,
        cancelStream,
    } = sseStream;

    // Track current streaming state
    const isStreamingRef = React.useRef<boolean>(false);

    // Initialize session on mount
    React.useEffect(() => {
        const initSession = async () => {
            if (initialSessionId) {
                // Resume existing session
                const newSession = await createSession(documentId, playbookId);
                if (newSession) {
                    // loadHistory needs the session to be set first - handled by useChatSession
                }
            } else {
                // Create new session
                const newSession = await createSession(documentId, playbookId);
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
        [session, isStreaming, addMessage, startStream, apiBaseUrl, documentId, accessToken]
    );

    // Handle predefined prompt selection
    const handlePromptSelect = React.useCallback(
        (prompt: string) => {
            handleSend(prompt);
        },
        [handleSend]
    );

    // Handle context changes
    const handleDocumentChange = React.useCallback(
        (newDocId: string) => {
            switchContext(newDocId || undefined, playbookId);
        },
        [switchContext, playbookId]
    );

    const handlePlaybookChange = React.useCallback(
        (newPlaybookId: string) => {
            switchContext(documentId, newPlaybookId);
        },
        [switchContext, documentId]
    );

    // Handle highlight-and-refine
    const handleRefine = React.useCallback(
        (selectedText: string, instruction: string) => {
            if (!session) {
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
        [session, addMessage, startStream, apiBaseUrl, accessToken]
    );

    const displayError = sessionError || streamError;
    const showPredefinedPrompts = messages.length === 0 && predefinedPrompts.length > 0 && !isSessionLoading;

    return (
        <div className={className ? `${styles.root} ${className}` : styles.root} data-testid="sprkchat-root">
            {/* Context selector bar */}
            {(documents.length > 0 || playbooks.length > 0) && (
                <SprkChatContextSelector
                    selectedDocumentId={documentId}
                    selectedPlaybookId={playbookId}
                    documents={documents}
                    playbooks={playbooks}
                    onDocumentChange={handleDocumentChange}
                    onPlaybookChange={handlePlaybookChange}
                    disabled={isStreaming}
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

                {messages.map((msg, index) => (
                    <SprkChatMessage
                        key={`msg-${index}`}
                        message={msg}
                        isStreaming={
                            isStreaming &&
                            index === messages.length - 1 &&
                            msg.role === "Assistant"
                        }
                    />
                ))}

                {/* Highlight-and-refine floating toolbar */}
                <SprkChatHighlightRefine
                    contentRef={highlightContainerRef}
                    onRefine={handleRefine}
                    isRefining={isStreaming}
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
