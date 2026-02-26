/**
 * SprkChatCitationPopover - Citation superscript marker + popover
 *
 * Two sub-components:
 *
 * 1. **CitationMarker** - Inline clickable superscript [N] rendered in
 *    brand color. Triggers the popover on click.
 *
 * 2. **SprkChatCitationPopover** - Fluent UI v9 Popover showing source
 *    name, page, excerpt (truncated to 200 chars), and an "Open Source"
 *    link. Dismisses on click-outside or Escape.
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
    Popover,
    PopoverTrigger,
    PopoverSurface,
    Text,
    Link,
} from "@fluentui/react-components";
import { Open16Regular } from "@fluentui/react-icons";
import {
    ICitation,
    ICitationMarkerProps,
    ISprkChatCitationPopoverProps,
} from "./types";

// ─────────────────────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────────────────────

/** Maximum excerpt length before truncation. */
const MAX_EXCERPT_LENGTH = 200;

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useMarkerStyles = makeStyles({
    marker: {
        color: tokens.colorBrandForeground1,
        cursor: "pointer",
        fontWeight: tokens.fontWeightSemibold,
        fontSize: tokens.fontSizeBase200,
        verticalAlign: "super",
        lineHeight: "1",
        textDecorationLine: "none",
        ...shorthands.padding("0px", "2px"),
        ...shorthands.borderRadius(tokens.borderRadiusSmall),
        ":hover": {
            color: tokens.colorBrandForeground2,
            backgroundColor: tokens.colorNeutralBackground1Hover,
            textDecorationLine: "underline",
        },
        ":focus-visible": {
            outlineWidth: "2px",
            outlineStyle: "solid",
            outlineColor: tokens.colorStrokeFocus2,
            outlineOffset: "1px",
        },
    },
});

const usePopoverStyles = makeStyles({
    surface: {
        maxWidth: "320px",
        display: "flex",
        flexDirection: "column",
        ...shorthands.gap(tokens.spacingVerticalS),
        ...shorthands.padding(tokens.spacingVerticalM, tokens.spacingHorizontalM),
    },
    sourceName: {
        fontWeight: tokens.fontWeightSemibold,
        fontSize: tokens.fontSizeBase300,
        color: tokens.colorNeutralForeground1,
    },
    pageInfo: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground3,
    },
    excerpt: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground2,
        lineHeight: tokens.lineHeightBase200,
    },
    linkRow: {
        display: "flex",
        alignItems: "center",
        ...shorthands.gap(tokens.spacingHorizontalXS),
    },
});

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Truncate text to `maxLen` characters with ellipsis.
 */
function truncateExcerpt(text: string, maxLen: number): string {
    if (text.length <= maxLen) {
        return text;
    }
    return text.slice(0, maxLen - 1).trimEnd() + "\u2026";
}

// ─────────────────────────────────────────────────────────────────────────────
// CitationMarker
// ─────────────────────────────────────────────────────────────────────────────

/**
 * CitationMarker - Inline clickable superscript [N] for citations.
 *
 * Designed to be embedded inside message text. Renders as a brand-colored
 * superscript that opens the citation popover on click.
 *
 * Keyboard accessible: Tab to focus, Enter or Space to activate.
 *
 * @example
 * ```tsx
 * <CitationMarker citation={citation} />
 * ```
 */
export const CitationMarker: React.FC<ICitationMarkerProps> = ({
    citation,
}) => {
    const styles = useMarkerStyles();

    return (
        <Popover
            positioning="below"
            withArrow
            trapFocus
        >
            <PopoverTrigger disableButtonEnhancement>
                <span
                    className={styles.marker}
                    role="button"
                    tabIndex={0}
                    aria-label={`Citation ${citation.id}, source: ${citation.source}`}
                    data-testid={`citation-marker-${citation.id}`}
                >
                    [{citation.id}]
                </span>
            </PopoverTrigger>

            <CitationPopoverContent citation={citation} />
        </Popover>
    );
};

// ─────────────────────────────────────────────────────────────────────────────
// CitationPopoverContent (internal)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Internal popover surface content. Extracted so that both CitationMarker
 * (self-contained) and SprkChatCitationPopover (controlled) can reuse it.
 */
const CitationPopoverContent: React.FC<{ citation: ICitation }> = ({
    citation,
}) => {
    const styles = usePopoverStyles();
    const excerptText = truncateExcerpt(citation.excerpt, MAX_EXCERPT_LENGTH);

    return (
        <PopoverSurface
            className={styles.surface}
            aria-label={`Citation details for source: ${citation.source}`}
            data-testid={`citation-popover-${citation.id}`}
        >
            {/* Source name */}
            <Text className={styles.sourceName}>
                {citation.source}
            </Text>

            {/* Page number (optional) */}
            {citation.page !== undefined && (
                <Text className={styles.pageInfo}>
                    Page {citation.page}
                </Text>
            )}

            {/* Excerpt */}
            <Text className={styles.excerpt}>
                {excerptText}
            </Text>

            {/* Open Source link (optional) */}
            {citation.sourceUrl && (
                <div className={styles.linkRow}>
                    <Open16Regular />
                    <Link
                        href={citation.sourceUrl}
                        target="_blank"
                        rel="noopener noreferrer"
                        aria-label={`Open source: ${citation.source}`}
                        data-testid={`citation-link-${citation.id}`}
                    >
                        Open Source
                    </Link>
                </div>
            )}
        </PopoverSurface>
    );
};

// ─────────────────────────────────────────────────────────────────────────────
// SprkChatCitationPopover
// ─────────────────────────────────────────────────────────────────────────────

/**
 * SprkChatCitationPopover - Controlled popover for citation details.
 *
 * Use this when you need explicit open/close control (e.g., the parent
 * manages popover state). For the simpler self-contained version, use
 * `CitationMarker` which wraps its own Popover.
 *
 * @example
 * ```tsx
 * const [open, setOpen] = React.useState(false);
 *
 * <SprkChatCitationPopover
 *   citation={citation}
 *   open={open}
 *   onOpenChange={setOpen}
 * >
 *   <span onClick={() => setOpen(true)}>[1]</span>
 * </SprkChatCitationPopover>
 * ```
 */
export const SprkChatCitationPopover: React.FC<ISprkChatCitationPopoverProps> = ({
    citation,
    open,
    onOpenChange,
    children,
}) => {
    const handleOpenChange = React.useCallback(
        (_event: unknown, data: { open: boolean }) => {
            if (onOpenChange) {
                onOpenChange(data.open);
            }
        },
        [onOpenChange]
    );

    return (
        <Popover
            positioning="below"
            withArrow
            trapFocus
            open={open}
            onOpenChange={handleOpenChange}
        >
            <PopoverTrigger disableButtonEnhancement>
                {children}
            </PopoverTrigger>

            <CitationPopoverContent citation={citation} />
        </Popover>
    );
};

export default SprkChatCitationPopover;
