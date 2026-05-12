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
 * Supports two citation source types:
 * - **document** (default) — internal SPE file reference with page + excerpt
 * - **web** — external web search result with title, clickable URL, snippet,
 *   and an "[External Source]" badge (ADR-015)
 *
 * @see ADR-012 - Shared Component Library
 * @see ADR-015 - Data governance: mark external sources
 * @see ADR-021 - Fluent UI v9; makeStyles; design tokens; dark mode
 * @see ADR-022 - React 16 APIs only
 */
import * as React from 'react';
import { makeStyles, shorthands, tokens, Popover, PopoverTrigger, PopoverSurface, Text, Link, Badge, } from '@fluentui/react-components';
import { Open16Regular, Globe16Regular, Document16Regular } from '@fluentui/react-icons';
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
        cursor: 'pointer',
        fontWeight: tokens.fontWeightSemibold,
        fontSize: tokens.fontSizeBase200,
        verticalAlign: 'super',
        lineHeight: '1',
        textDecorationLine: 'none',
        ...shorthands.padding('0px', '2px'),
        ...shorthands.borderRadius(tokens.borderRadiusSmall),
        ':hover': {
            color: tokens.colorBrandForeground2,
            backgroundColor: tokens.colorNeutralBackground1Hover,
            textDecorationLine: 'underline',
        },
        ':focus-visible': {
            outlineWidth: '2px',
            outlineStyle: 'solid',
            outlineColor: tokens.colorStrokeFocus2,
            outlineOffset: '1px',
        },
    },
});
const usePopoverStyles = makeStyles({
    surface: {
        maxWidth: '320px',
        display: 'flex',
        flexDirection: 'column',
        ...shorthands.gap(tokens.spacingVerticalS),
        ...shorthands.padding(tokens.spacingVerticalM, tokens.spacingHorizontalM),
    },
    headerRow: {
        display: 'flex',
        alignItems: 'center',
        ...shorthands.gap(tokens.spacingHorizontalXS),
    },
    documentIcon: {
        color: tokens.colorBrandForeground1,
        flexShrink: 0,
    },
    webIcon: {
        color: tokens.colorPaletteBerryForeground1,
        flexShrink: 0,
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
    snippet: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground2,
        lineHeight: tokens.lineHeightBase200,
        fontStyle: 'italic',
    },
    linkRow: {
        display: 'flex',
        alignItems: 'center',
        ...shorthands.gap(tokens.spacingHorizontalXS),
    },
    urlText: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground3,
        overflow: 'hidden',
        textOverflow: 'ellipsis',
        whiteSpace: 'nowrap',
        maxWidth: '250px',
    },
    badgeRow: {
        display: 'flex',
        alignItems: 'center',
        ...shorthands.gap(tokens.spacingHorizontalXS),
    },
});
// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────
/**
 * Truncate text to `maxLen` characters with ellipsis.
 */
function truncateExcerpt(text, maxLen) {
    if (text.length <= maxLen) {
        return text;
    }
    return text.slice(0, maxLen - 1).trimEnd() + '\u2026';
}
/**
 * Determine if a citation is a web source.
 * Defaults to 'document' when sourceType is absent for backward compatibility.
 */
function isWebCitation(citation) {
    return citation.sourceType === 'web';
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
export const CitationMarker = ({ citation }) => {
    const styles = useMarkerStyles();
    const isWeb = isWebCitation(citation);
    return (React.createElement(Popover, { positioning: "below", withArrow: true, trapFocus: true },
        React.createElement(PopoverTrigger, { disableButtonEnhancement: true },
            React.createElement("span", { className: styles.marker, role: "button", tabIndex: 0, "aria-label": `Citation ${citation.id}, ${isWeb ? 'web' : 'document'} source: ${citation.source}`, "data-testid": `citation-marker-${citation.id}` },
                "[",
                citation.id,
                "]")),
        React.createElement(CitationPopoverContent, { citation: citation })));
};
// ─────────────────────────────────────────────────────────────────────────────
// CitationPopoverContent (internal)
// ─────────────────────────────────────────────────────────────────────────────
/**
 * Internal popover surface content. Extracted so that both CitationMarker
 * (self-contained) and SprkChatCitationPopover (controlled) can reuse it.
 *
 * Renders differently based on citation sourceType:
 * - 'document' (default): source name, page, excerpt, optional open link
 * - 'web': globe icon, source title, [External Source] badge, snippet, clickable URL
 */
const CitationPopoverContent = ({ citation }) => {
    const styles = usePopoverStyles();
    const isWeb = isWebCitation(citation);
    if (isWeb) {
        return (React.createElement(PopoverSurface, { className: styles.surface, "aria-label": `Web citation details for source: ${citation.source}`, "data-testid": `citation-popover-${citation.id}` },
            React.createElement("div", { className: styles.headerRow },
                React.createElement(Globe16Regular, { className: styles.webIcon }),
                React.createElement(Text, { className: styles.sourceName }, citation.source)),
            React.createElement("div", { className: styles.badgeRow },
                React.createElement(Badge, { appearance: "tint", color: "important", size: "small", "data-testid": `citation-external-badge-${citation.id}` }, "External Source")),
            citation.snippet && (React.createElement(Text, { className: styles.snippet }, truncateExcerpt(citation.snippet, MAX_EXCERPT_LENGTH))),
            citation.url && (React.createElement("div", { className: styles.linkRow },
                React.createElement(Open16Regular, null),
                React.createElement(Link, { href: citation.url, target: "_blank", rel: "noopener noreferrer", "aria-label": `Open web source: ${citation.source}`, "data-testid": `citation-link-${citation.id}` }, "Visit Source"))),
            citation.url && (React.createElement(Text, { className: styles.urlText, title: citation.url }, citation.url))));
    }
    // ── Document citation (default / backward-compatible) ──────────────────────
    const excerptText = truncateExcerpt(citation.excerpt, MAX_EXCERPT_LENGTH);
    return (React.createElement(PopoverSurface, { className: styles.surface, "aria-label": `Citation details for source: ${citation.source}`, "data-testid": `citation-popover-${citation.id}` },
        React.createElement("div", { className: styles.headerRow },
            React.createElement(Document16Regular, { className: styles.documentIcon }),
            React.createElement(Text, { className: styles.sourceName }, citation.source)),
        citation.page !== undefined && React.createElement(Text, { className: styles.pageInfo },
            "Page ",
            citation.page),
        React.createElement(Text, { className: styles.excerpt }, excerptText),
        citation.sourceUrl && (React.createElement("div", { className: styles.linkRow },
            React.createElement(Open16Regular, null),
            React.createElement(Link, { href: citation.sourceUrl, target: "_blank", rel: "noopener noreferrer", "aria-label": `Open source: ${citation.source}`, "data-testid": `citation-link-${citation.id}` }, "Open Source")))));
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
 * Supports both document and web citation types — the popover content
 * adapts automatically based on `citation.sourceType`.
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
export const SprkChatCitationPopover = ({ citation, open, onOpenChange, children, }) => {
    const handleOpenChange = React.useCallback((_event, data) => {
        if (onOpenChange) {
            onOpenChange(data.open);
        }
    }, [onOpenChange]);
    return (React.createElement(Popover, { positioning: "below", withArrow: true, trapFocus: true, open: open, onOpenChange: handleOpenChange },
        React.createElement(PopoverTrigger, { disableButtonEnhancement: true }, children),
        React.createElement(CitationPopoverContent, { citation: citation })));
};
export default SprkChatCitationPopover;
//# sourceMappingURL=SprkChatCitationPopover.js.map