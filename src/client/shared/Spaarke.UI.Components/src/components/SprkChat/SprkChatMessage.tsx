/**
 * SprkChatMessage - Individual chat message bubble
 *
 * Renders user messages (right-aligned, accent) and assistant messages (left-aligned, subtle).
 * Shows a typing indicator during streaming.
 *
 * @see ADR-021 - Fluent UI v9; makeStyles; design tokens; dark mode
 * @see ADR-022 - React 16 APIs only
 */

import * as React from "react";
import {
    makeStyles,
    shorthands,
    tokens,
    mergeClasses,
    Text,
    Spinner,
} from "@fluentui/react-components";
import { ISprkChatMessageProps } from "./types";

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
    container: {
        display: "flex",
        flexDirection: "column",
        maxWidth: "80%",
        ...shorthands.padding("8px", "12px"),
        ...shorthands.borderRadius(tokens.borderRadiusMedium),
        wordBreak: "break-word",
        whiteSpace: "pre-wrap",
    },
    userContainer: {
        alignSelf: "flex-end",
        backgroundColor: tokens.colorBrandBackground,
        color: tokens.colorNeutralForegroundOnBrand,
    },
    assistantContainer: {
        alignSelf: "flex-start",
        backgroundColor: tokens.colorNeutralBackground3,
        color: tokens.colorNeutralForeground1,
    },
    messageContent: {
        fontSize: tokens.fontSizeBase300,
        lineHeight: tokens.lineHeightBase300,
    },
    timestamp: {
        fontSize: tokens.fontSizeBase100,
        color: tokens.colorNeutralForeground3,
        marginTop: "4px",
        alignSelf: "flex-end",
    },
    userTimestamp: {
        color: tokens.colorNeutralForegroundOnBrand,
        opacity: 0.7,
    },
    streamingIndicator: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalXS,
        marginTop: "4px",
    },
});

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Format a timestamp string for display.
 * Shows time in the user's local timezone.
 */
function formatTimestamp(timestamp: string): string {
    try {
        const date = new Date(timestamp);
        return date.toLocaleTimeString(undefined, {
            hour: "2-digit",
            minute: "2-digit",
        });
    } catch {
        return "";
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * SprkChatMessage - Renders a single chat message with role-appropriate styling.
 *
 * @example
 * ```tsx
 * <SprkChatMessage
 *   message={{ role: "User", content: "Hello!", timestamp: "2026-01-01T00:00:00Z" }}
 * />
 * <SprkChatMessage
 *   message={{ role: "Assistant", content: "Hi there!", timestamp: "2026-01-01T00:00:01Z" }}
 *   isStreaming={true}
 * />
 * ```
 */
export const SprkChatMessage: React.FC<ISprkChatMessageProps> = ({
    message,
    isStreaming = false,
}) => {
    const styles = useStyles();
    const isUser = message.role === "User";

    const containerClass = mergeClasses(
        styles.container,
        isUser ? styles.userContainer : styles.assistantContainer
    );

    const timestampClass = mergeClasses(
        styles.timestamp,
        isUser ? styles.userTimestamp : undefined
    );

    return (
        <div
            className={containerClass}
            role="listitem"
            aria-label={`${message.role} message`}
        >
            <Text className={styles.messageContent}>
                {message.content}
            </Text>

            {isStreaming && !message.content && (
                <div className={styles.streamingIndicator}>
                    <Spinner size="tiny" />
                    <Text size={200}>Thinking...</Text>
                </div>
            )}

            {message.timestamp && !isStreaming && (
                <span className={timestampClass}>
                    {formatTimestamp(message.timestamp)}
                </span>
            )}
        </div>
    );
};

export default SprkChatMessage;
