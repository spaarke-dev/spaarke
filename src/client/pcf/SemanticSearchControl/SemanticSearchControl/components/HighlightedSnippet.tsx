/**
 * HighlightedSnippet component
 *
 * Displays content snippets with search term highlighting.
 * Safely renders highlighted text from API response.
 *
 * @see ADR-021 for Fluent UI v9 requirements
 */

import * as React from "react";
import { useMemo } from "react";
import {
    makeStyles,
    tokens,
    Text,
} from "@fluentui/react-components";

const useStyles = makeStyles({
    container: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXS,
    },
    snippet: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground2,
        lineHeight: tokens.lineHeightBase200,
    },
    highlight: {
        backgroundColor: tokens.colorPaletteYellowBackground2,
        color: tokens.colorNeutralForeground1,
        fontWeight: tokens.fontWeightSemibold,
        padding: "0 2px",
        borderRadius: tokens.borderRadiusSmall,
    },
    ellipsis: {
        color: tokens.colorNeutralForeground3,
        margin: `0 ${tokens.spacingHorizontalXS}`,
    },
});

interface IHighlightedSnippetProps {
    /** Snippet text containing highlight markers (e.g., <em> tags) */
    text: string;
    /** Maximum length before truncation (default: 200) */
    maxLength?: number;
}

/**
 * Parse and render highlighted text.
 * Highlights are marked with <em> tags from the API.
 */
function parseHighlights(
    text: string,
    styles: ReturnType<typeof useStyles>
): React.ReactNode[] {
    const parts: React.ReactNode[] = [];

    // Match <em> tags for highlights
    const regex = /<em>(.*?)<\/em>/gi;
    let lastIndex = 0;
    let match: RegExpExecArray | null;
    let keyIndex = 0;

    while ((match = regex.exec(text)) !== null) {
        // Add text before highlight
        if (match.index > lastIndex) {
            const beforeText = text.slice(lastIndex, match.index);
            parts.push(
                <span key={`text-${keyIndex++}`}>{beforeText}</span>
            );
        }

        // Add highlighted text
        parts.push(
            <span key={`highlight-${keyIndex++}`} className={styles.highlight}>
                {match[1]}
            </span>
        );

        lastIndex = match.index + match[0].length;
    }

    // Add remaining text after last highlight
    if (lastIndex < text.length) {
        parts.push(
            <span key={`text-${keyIndex++}`}>{text.slice(lastIndex)}</span>
        );
    }

    return parts;
}

/**
 * Sanitize text by removing potentially harmful HTML while preserving <em> tags.
 */
function sanitizeSnippet(text: string): string {
    // First, protect <em> tags by replacing them with placeholders
    let sanitized = text
        .replace(/<em>/gi, "{{EM_OPEN}}")
        .replace(/<\/em>/gi, "{{EM_CLOSE}}");

    // Remove all other HTML tags
    sanitized = sanitized.replace(/<[^>]*>/g, "");

    // Restore <em> tags
    sanitized = sanitized
        .replace(/\{\{EM_OPEN\}\}/g, "<em>")
        .replace(/\{\{EM_CLOSE\}\}/g, "</em>");

    return sanitized;
}

/**
 * Truncate text to max length, preserving word boundaries.
 */
function truncateText(text: string, maxLength: number): string {
    // Strip HTML for length calculation
    const plainText = text.replace(/<[^>]*>/g, "");

    if (plainText.length <= maxLength) {
        return text;
    }

    // Find last space before max length
    let truncateIndex = maxLength;
    while (truncateIndex > 0 && plainText[truncateIndex] !== " ") {
        truncateIndex--;
    }

    if (truncateIndex === 0) {
        truncateIndex = maxLength;
    }

    // Truncate the original text (with HTML) approximately
    // This is a simplified approach - for production, consider more sophisticated HTML truncation
    const ratio = truncateIndex / plainText.length;
    const htmlTruncateIndex = Math.floor(text.length * ratio);

    return text.slice(0, htmlTruncateIndex) + "...";
}

/**
 * HighlightedSnippet component for displaying search result snippets.
 *
 * @param props.text - Snippet text with highlight markers
 * @param props.maxLength - Maximum characters before truncation (default: 200)
 */
export const HighlightedSnippet: React.FC<IHighlightedSnippetProps> = ({
    text,
    maxLength = 200,
}) => {
    const styles = useStyles();

    // Process the snippet
    const processedContent = useMemo(() => {
        if (!text) {
            return null;
        }

        // Sanitize, truncate, then parse highlights
        const sanitized = sanitizeSnippet(text);
        const truncated = truncateText(sanitized, maxLength);
        return parseHighlights(truncated, styles);
    }, [text, maxLength, styles]);

    if (!text) {
        return null;
    }

    return (
        <div className={styles.container}>
            <Text className={styles.snippet}>
                {processedContent}
            </Text>
        </div>
    );
};

export default HighlightedSnippet;
