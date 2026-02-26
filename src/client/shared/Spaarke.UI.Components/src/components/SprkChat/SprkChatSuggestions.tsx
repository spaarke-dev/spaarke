/**
 * SprkChatSuggestions - Follow-up suggestion chips for SprkChat
 *
 * Renders 2-3 clickable follow-up suggestion chips below the latest
 * assistant message. Chips use Fluent UI v9 InteractionTag for a
 * pill/chip appearance with keyboard navigation (Arrow Left/Right,
 * Enter/Space to select).
 *
 * Animation: fade-in + slide-up (200ms CSS transition) controlled
 * by the `visible` prop.
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
    InteractionTag,
    InteractionTagPrimary,
} from "@fluentui/react-components";
import { ISprkChatSuggestionsProps } from "./types";

// ─────────────────────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────────────────────

/** Maximum number of suggestion chips displayed at once. */
const MAX_SUGGESTIONS = 3;

/** Maximum character length before text is truncated with ellipsis. */
const MAX_TEXT_LENGTH = 50;

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
    root: {
        display: "flex",
        flexDirection: "row",
        flexWrap: "wrap",
        ...shorthands.gap(tokens.spacingHorizontalS),
        ...shorthands.padding(tokens.spacingVerticalXS, "0px"),
        alignItems: "center",
        transitionProperty: "opacity, transform",
        transitionDuration: "200ms",
        transitionTimingFunction: "ease-out",
    },
    visible: {
        opacity: 1,
        transform: "translateY(0)",
    },
    hidden: {
        opacity: 0,
        transform: "translateY(8px)",
        pointerEvents: "none",
    },
    chip: {
        cursor: "pointer",
        maxWidth: "280px",
    },
    chipText: {
        overflow: "hidden",
        textOverflow: "ellipsis",
        whiteSpace: "nowrap",
        display: "block",
    },
});

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Truncate text to `maxLen` characters, adding ellipsis if needed.
 */
function truncateText(text: string, maxLen: number): string {
    if (text.length <= maxLen) {
        return text;
    }
    return text.slice(0, maxLen - 1).trimEnd() + "\u2026";
}

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * SprkChatSuggestions - Renders clickable follow-up suggestion chips.
 *
 * @example
 * ```tsx
 * <SprkChatSuggestions
 *   suggestions={["Summarize the key points", "What are the risks?"]}
 *   onSelect={(text) => sendMessage(text)}
 *   visible={!isStreaming}
 * />
 * ```
 */
export const SprkChatSuggestions: React.FC<ISprkChatSuggestionsProps> = ({
    suggestions,
    onSelect,
    visible,
}) => {
    const styles = useStyles();
    const containerRef = React.useRef<HTMLDivElement>(null);

    // Limit to MAX_SUGGESTIONS chips
    const displaySuggestions = React.useMemo(
        () => suggestions.slice(0, MAX_SUGGESTIONS),
        [suggestions]
    );

    // Keyboard navigation: Arrow Left/Right between chips, Enter/Space to select
    const handleKeyDown = React.useCallback(
        (event: React.KeyboardEvent<HTMLDivElement>) => {
            const container = containerRef.current;
            if (!container) {
                return;
            }

            const focusable = Array.from(
                container.querySelectorAll<HTMLElement>("[data-suggestion-chip]")
            );
            const currentIndex = focusable.indexOf(event.target as HTMLElement);

            if (currentIndex === -1) {
                return;
            }

            let nextIndex = -1;

            if (event.key === "ArrowRight") {
                event.preventDefault();
                nextIndex = (currentIndex + 1) % focusable.length;
            } else if (event.key === "ArrowLeft") {
                event.preventDefault();
                nextIndex = (currentIndex - 1 + focusable.length) % focusable.length;
            }

            if (nextIndex >= 0) {
                focusable[nextIndex].focus();
            }
        },
        []
    );

    if (displaySuggestions.length === 0) {
        return null;
    }

    const rootClassName = `${styles.root} ${visible ? styles.visible : styles.hidden}`;

    return (
        <div
            ref={containerRef}
            className={rootClassName}
            role="group"
            aria-label="Follow-up suggestions"
            onKeyDown={handleKeyDown}
            data-testid="sprkchat-suggestions"
        >
            {displaySuggestions.map((suggestion, index) => {
                const displayText = truncateText(suggestion, MAX_TEXT_LENGTH);
                const isTruncated = suggestion.length > MAX_TEXT_LENGTH;

                return (
                    <InteractionTag
                        key={`suggestion-${index}`}
                        className={styles.chip}
                        appearance="brand"
                        shape="circular"
                        size="small"
                    >
                        <InteractionTagPrimary
                            role="button"
                            aria-label={isTruncated ? suggestion : undefined}
                            title={isTruncated ? suggestion : undefined}
                            onClick={() => onSelect(suggestion)}
                            data-suggestion-chip=""
                            data-testid={`suggestion-chip-${index}`}
                        >
                            <span className={styles.chipText}>
                                {displayText}
                            </span>
                        </InteractionTagPrimary>
                    </InteractionTag>
                );
            })}
        </div>
    );
};

export default SprkChatSuggestions;
