/**
 * RelationshipCountCard - Displays a count of semantically related documents
 * with drill-through capability.
 *
 * Callback-based component with zero service dependencies.
 * Supports loading, error, zero-count, and normal states.
 *
 * @see ADR-012 - Shared component library (callback-based props)
 * @see ADR-021 - Fluent UI v9 design tokens
 */
import * as React from 'react';
import { makeStyles, tokens, Card, Text, Spinner, Button, mergeClasses } from '@fluentui/react-components';
import { Open16Regular, ArrowSync20Regular, Warning20Regular } from '@fluentui/react-icons';
// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------
const useStyles = makeStyles({
    card: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalS,
        padding: tokens.spacingVerticalM + ' ' + tokens.spacingHorizontalM,
        minWidth: '200px',
        cursor: 'default',
    },
    topRow: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'flex-end',
        gap: tokens.spacingHorizontalXS,
    },
    /** When graph preview is present: count left, graph right */
    graphRow: {
        display: 'flex',
        alignItems: 'stretch',
        gap: '0px',
        borderRadius: tokens.borderRadiusMedium,
        overflow: 'hidden',
    },
    countColumn: {
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        flexShrink: 0,
        flexBasis: '50%',
        width: '50%',
        padding: tokens.spacingVerticalS + ' ' + tokens.spacingHorizontalM,
    },
    graphPreviewContainer: {
        display: 'flex',
        justifyContent: 'center',
        alignItems: 'center',
        flexBasis: '50%',
        width: '50%',
        minHeight: '120px',
        overflow: 'hidden',
    },
    /** Fallback body when no graph preview */
    body: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        gap: tokens.spacingHorizontalM,
    },
    countContainer: {
        display: 'flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalS,
    },
    count: {
        fontSize: '42px',
        fontWeight: tokens.fontWeightBold,
        lineHeight: '48px',
        color: tokens.colorNeutralForeground1,
    },
    countLabel: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground3,
    },
    zeroCount: {
        color: tokens.colorNeutralForeground3,
    },
    zeroLabel: {
        color: tokens.colorNeutralForeground3,
    },
    spinnerContainer: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        minHeight: '48px',
    },
    errorContainer: {
        display: 'flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalS,
        color: tokens.colorPaletteRedForeground1,
    },
    errorIcon: {
        color: tokens.colorPaletteRedForeground1,
        flexShrink: 0,
    },
    footer: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'flex-end',
    },
    lastUpdated: {
        color: tokens.colorNeutralForeground3,
    },
});
// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------
/**
 * Format a Date for display as a relative or short timestamp.
 */
function formatLastUpdated(date) {
    return new Intl.DateTimeFormat('en-US', {
        month: 'short',
        day: 'numeric',
        hour: 'numeric',
        minute: '2-digit',
    }).format(date);
}
// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------
export const RelationshipCountCard = ({ count, isLoading = false, error, onOpen, onRefresh, lastUpdated, graphPreview, }) => {
    const styles = useStyles();
    // Action buttons row (top-right: refresh + open)
    const isZero = count === 0;
    const actionRow = (React.createElement("div", { className: styles.topRow },
        onRefresh && (React.createElement(Button, { appearance: "subtle", icon: React.createElement(ArrowSync20Regular, null), size: "small", onClick: onRefresh, title: "Refresh" })),
        !isZero && !isLoading && !error && (React.createElement(Button, { appearance: "subtle", icon: React.createElement(Open16Regular, null), size: "small", onClick: onOpen, title: "Open full viewer" }))));
    // ── Loading state ────────────────────────────────────────────────────
    if (isLoading) {
        return (React.createElement(Card, { className: styles.card },
            actionRow,
            React.createElement("div", { className: styles.spinnerContainer },
                React.createElement(Spinner, { size: "small", label: "Loading..." }))));
    }
    // ── Error state ──────────────────────────────────────────────────────
    if (error) {
        return (React.createElement(Card, { className: styles.card },
            actionRow,
            React.createElement("div", { className: styles.errorContainer },
                React.createElement(Warning20Regular, { className: styles.errorIcon }),
                React.createElement(Text, { size: 200 }, error))));
    }
    // ── Normal / Zero-count state ────────────────────────────────────────
    const hasGraph = !isZero && graphPreview;
    return (React.createElement(Card, { className: styles.card },
        actionRow,
        hasGraph ? (
        /* Graph layout: count left | graph right */
        React.createElement("div", { className: styles.graphRow },
            React.createElement("div", { className: styles.countColumn },
                React.createElement(Text, { className: styles.count }, count > 99 ? '99+' : count),
                React.createElement(Text, { className: styles.countLabel }, "Similar")),
            React.createElement("div", { className: styles.graphPreviewContainer }, graphPreview))) : (
        /* No-graph layout: count only */
        React.createElement("div", { className: styles.body },
            React.createElement("div", { className: styles.countContainer },
                React.createElement(Text, { className: mergeClasses(styles.count, isZero && styles.zeroCount) }, count),
                isZero && (React.createElement(Text, { size: 200, className: styles.zeroLabel }, "No related documents found"))))),
        lastUpdated && (React.createElement("div", { className: styles.footer },
            React.createElement(Text, { className: styles.lastUpdated, size: 100 },
                "Updated ",
                formatLastUpdated(lastUpdated))))));
};
export default RelationshipCountCard;
//# sourceMappingURL=RelationshipCountCard.js.map