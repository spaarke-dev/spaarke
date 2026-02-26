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
import { ISprkChatMessageProps, ICitation } from "./types";
import { CitationMarker } from "./SprkChatCitationPopover";

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
// Citation Rendering
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Regex to match citation markers like [1], [2], [12], etc. in message text.
 * Captures the numeric ID inside the brackets.
 */
const CITATION_MARKER_REGEX = /\[(\d+)\]/g;

/**
 * Builds a lookup map from citation ID to ICitation for O(1) access.
 */
function buildCitationMap(citations: ICitation[]): Map<number, ICitation> {
    const map = new Map<number, ICitation>();
    for (const c of citations) {
        map.set(c.id, c);
    }
    return map;
}

/**
 * Parses message text and replaces [N] markers with CitationMarker components
 * when a matching citation exists.
 *
 * Returns an array of React nodes: plain text strings interspersed with
 * CitationMarker elements. If no citations are provided or no markers match,
 * returns the original text as a single-element array.
 */
function renderContentWithCitations(
    text: string,
    citations: ICitation[] | undefined
): React.ReactNode[] {
    if (!citations || citations.length === 0) {
        return [text];
    }

    const citationMap = buildCitationMap(citations);
    const nodes: React.ReactNode[] = [];
    let lastIndex = 0;

    // Reset regex state (global regex retains lastIndex between calls)
    CITATION_MARKER_REGEX.lastIndex = 0;

    let match: RegExpExecArray | null;
    while ((match = CITATION_MARKER_REGEX.exec(text)) !== null) {
        const citationId = parseInt(match[1], 10);
        const citation = citationMap.get(citationId);

        if (!citation) {
            // No matching citation metadata — leave the [N] marker as plain text
            continue;
        }

        // Add text before this marker
        if (match.index > lastIndex) {
            nodes.push(text.slice(lastIndex, match.index));
        }

        // Add the CitationMarker component
        nodes.push(
            React.createElement(CitationMarker, {
                key: `citation-${citationId}-${match.index}`,
                citation,
            })
        );

        lastIndex = match.index + match[0].length;
    }

    // Add remaining text after the last marker
    if (lastIndex < text.length) {
        nodes.push(text.slice(lastIndex));
    }

    // If no markers were replaced, return original text
    if (nodes.length === 0) {
        return [text];
    }

    return nodes;
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
    citations,
}) => {
    const styles = useStyles();
    const isUser = message.role === "User";
    const isAssistant = message.role === "Assistant";

    const containerClass = mergeClasses(
        styles.container,
        isUser ? styles.userContainer : styles.assistantContainer
    );

    const timestampClass = mergeClasses(
        styles.timestamp,
        isUser ? styles.userTimestamp : undefined
    );

    // For assistant messages with citations, parse [N] markers and render
    // interactive CitationMarker components. User messages are always plain text.
    const renderedContent = React.useMemo(() => {
        if (isAssistant && citations && citations.length > 0 && !isStreaming) {
            return renderContentWithCitations(message.content, citations);
        }
        return message.content;
    }, [message.content, citations, isAssistant, isStreaming]);

    return (
        <div
            className={containerClass}
            role="listitem"
            aria-label={`${message.role} message`}
        >
            <Text className={styles.messageContent}>
                {renderedContent}
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
