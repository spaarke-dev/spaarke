/**
 * PlaybookCardGrid Component
 *
 * Responsive card grid selector for AI playbooks (Code Page / React 18).
 * Ported from PCF PlaybookSelector with a full grid layout replacing the
 * horizontal scroll strip used in the narrow PCF context.
 *
 * Features:
 * - Responsive CSS Grid: 3 columns (wide) → 2 (medium) → 1 (narrow)
 * - Selected card highlight using Fluent v9 brand tokens
 * - Info icon Popover for full description on hover/click
 * - Loading state (Fluent Spinner) and empty state message
 * - Zero hard-coded colors — all Fluent v9 semantic tokens
 */
import React from 'react';
import { Card, CardHeader, Text, Spinner, makeStyles, tokens, mergeClasses, Popover, PopoverTrigger, PopoverSurface, Button, } from '@fluentui/react-components';
import { Lightbulb24Regular, Document24Regular, Certificate24Regular, Shield24Regular, Settings24Regular, Notebook24Regular, Info16Regular, } from '@fluentui/react-icons';
// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------
const useStyles = makeStyles({
    container: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalM,
        width: '100%',
    },
    // Responsive CSS Grid — 3 / 2 / 1 columns
    grid: {
        display: 'grid',
        gridTemplateColumns: 'repeat(3, 1fr)',
        gap: tokens.spacingHorizontalM,
        // Fallback narrow breakpoints via container queries aren't universally
        // available yet — use minmax so columns naturally wrap below ~200px each.
        // Explicit responsive overrides are provided via media queries below.
        '@media (max-width: 680px)': {
            gridTemplateColumns: 'repeat(2, 1fr)',
        },
        '@media (max-width: 420px)': {
            gridTemplateColumns: '1fr',
        },
    },
    // Card base
    card: {
        cursor: 'pointer',
        position: 'relative',
        border: `1px solid ${tokens.colorNeutralStroke1}`,
        borderRadius: tokens.borderRadiusMedium,
        backgroundColor: tokens.colorNeutralBackground1,
        transition: 'background-color 0.1s ease, border-color 0.1s ease',
        ':hover': {
            backgroundColor: tokens.colorNeutralBackground1Hover,
        },
        ':focus-visible': {
            outlineColor: tokens.colorBrandStroke1,
            outlineWidth: '2px',
            outlineStyle: 'solid',
            outlineOffset: '2px',
        },
    },
    // Selected state
    cardSelected: {
        backgroundColor: tokens.colorBrandBackground2,
        borderTopColor: tokens.colorBrandStroke1,
        borderRightColor: tokens.colorBrandStroke1,
        borderBottomColor: tokens.colorBrandStroke1,
        borderLeftColor: tokens.colorBrandStroke1,
    },
    // Card body layout
    cardContent: {
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        paddingTop: tokens.spacingVerticalM,
        paddingBottom: tokens.spacingVerticalM,
        paddingLeft: tokens.spacingHorizontalM,
        paddingRight: tokens.spacingHorizontalM,
        textAlign: 'center',
        gap: tokens.spacingVerticalXS,
    },
    // Icon wrapper
    iconWrapper: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        width: '40px',
        height: '40px',
        color: tokens.colorBrandForeground1,
        marginBottom: tokens.spacingVerticalXS,
    },
    // Playbook name
    name: {
        fontSize: tokens.fontSizeBase300,
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground1,
        lineHeight: tokens.lineHeightBase300,
    },
    // Truncated description (2-line clamp)
    description: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground2,
        lineHeight: tokens.lineHeightBase200,
        display: '-webkit-box',
        '-webkit-line-clamp': '2',
        '-webkit-box-orient': 'vertical',
        overflow: 'hidden',
        textOverflow: 'ellipsis',
    },
    // Info button (top-right corner of card)
    infoButtonWrapper: {
        position: 'absolute',
        top: tokens.spacingVerticalXS,
        right: tokens.spacingHorizontalXS,
        zIndex: 1,
    },
    infoButton: {
        minWidth: '24px',
        width: '24px',
        height: '24px',
        padding: '0',
    },
    // Popover content
    popoverContent: {
        maxWidth: '280px',
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalS,
        padding: tokens.spacingVerticalM,
    },
    popoverTitle: {
        fontSize: tokens.fontSizeBase300,
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground1,
    },
    popoverDescription: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground2,
        lineHeight: tokens.lineHeightBase200,
    },
    // Loading state
    loading: {
        display: 'flex',
        justifyContent: 'center',
        alignItems: 'center',
        paddingTop: tokens.spacingVerticalXXL,
        paddingBottom: tokens.spacingVerticalXXL,
    },
    // Empty state
    empty: {
        display: 'flex',
        justifyContent: 'center',
        alignItems: 'center',
        paddingTop: tokens.spacingVerticalXXL,
        paddingBottom: tokens.spacingVerticalXXL,
        color: tokens.colorNeutralForeground3,
        fontSize: tokens.fontSizeBase300,
    },
    // ── Compact overrides ────────────────────────────────────────────────
    gridCompact: {
        gridTemplateColumns: 'repeat(4, 1fr)',
        gap: tokens.spacingHorizontalS,
        '@media (max-width: 680px)': {
            gridTemplateColumns: 'repeat(3, 1fr)',
        },
        '@media (max-width: 420px)': {
            gridTemplateColumns: 'repeat(2, 1fr)',
        },
    },
    cardContentCompact: {
        paddingTop: tokens.spacingVerticalXS,
        paddingBottom: tokens.spacingVerticalXS,
        paddingLeft: tokens.spacingHorizontalS,
        paddingRight: tokens.spacingHorizontalS,
    },
    iconWrapperCompact: {
        width: '24px',
        height: '24px',
        marginBottom: '0px',
    },
    nameCompact: {
        fontSize: tokens.fontSizeBase200,
    },
});
// ---------------------------------------------------------------------------
// Icon registry
// ---------------------------------------------------------------------------
const ICON_MAP = {
    Lightbulb: React.createElement(Lightbulb24Regular, null),
    DocumentText: React.createElement(Document24Regular, null),
    Certificate: React.createElement(Certificate24Regular, null),
    Shield: React.createElement(Shield24Regular, null),
    Settings: React.createElement(Settings24Regular, null),
    default: React.createElement(Notebook24Regular, null),
};
function resolveIcon(iconName) {
    if (iconName && ICON_MAP[iconName]) {
        return ICON_MAP[iconName];
    }
    return ICON_MAP['default'];
}
const PlaybookCard = ({ playbook, isSelected, onSelect, styles, compact }) => {
    const handleClick = () => {
        onSelect(playbook);
    };
    const handleKeyDown = (event) => {
        if (event.key === 'Enter' || event.key === ' ') {
            event.preventDefault();
            onSelect(playbook);
        }
    };
    const stopPropagation = (e) => {
        e.stopPropagation();
    };
    return (React.createElement(Card, { className: mergeClasses(styles.card, isSelected && styles.cardSelected), onClick: handleClick, onKeyDown: handleKeyDown, tabIndex: 0, role: "button", "aria-pressed": isSelected, "aria-label": playbook.name },
        playbook.description && (React.createElement("div", { className: styles.infoButtonWrapper },
            React.createElement(Popover, { withArrow: true, positioning: "above-end" },
                React.createElement(PopoverTrigger, { disableButtonEnhancement: true },
                    React.createElement(Button, { className: styles.infoButton, appearance: "subtle", icon: React.createElement(Info16Regular, null), size: "small", onClick: stopPropagation, "aria-label": `More info about ${playbook.name}` })),
                React.createElement(PopoverSurface, null,
                    React.createElement("div", { className: styles.popoverContent },
                        React.createElement(Text, { className: styles.popoverTitle }, playbook.name),
                        React.createElement(Text, { className: styles.popoverDescription }, playbook.description)))))),
        React.createElement(CardHeader, { header: React.createElement("div", { className: mergeClasses(styles.cardContent, compact && styles.cardContentCompact) },
                React.createElement("div", { className: mergeClasses(styles.iconWrapper, compact && styles.iconWrapperCompact) }, resolveIcon(playbook.icon)),
                React.createElement(Text, { className: mergeClasses(styles.name, compact && styles.nameCompact) }, playbook.name),
                !compact && playbook.description && React.createElement(Text, { className: styles.description }, playbook.description)) })));
};
// ---------------------------------------------------------------------------
// PlaybookCardGrid
// ---------------------------------------------------------------------------
export const PlaybookCardGrid = ({ playbooks, selectedId, onSelect, isLoading, compact, }) => {
    const styles = useStyles();
    if (isLoading) {
        return (React.createElement("div", { className: styles.container },
            React.createElement("div", { className: styles.loading },
                React.createElement(Spinner, { size: "medium", label: "Loading playbooks..." }))));
    }
    if (playbooks.length === 0) {
        return (React.createElement("div", { className: styles.container },
            React.createElement(Text, { className: styles.empty }, "No playbooks available")));
    }
    return (React.createElement("div", { className: styles.container },
        React.createElement("div", { className: mergeClasses(styles.grid, compact && styles.gridCompact) }, playbooks.map(playbook => (React.createElement(PlaybookCard, { key: playbook.id, playbook: playbook, isSelected: selectedId === playbook.id, onSelect: onSelect, styles: styles, compact: compact }))))));
};
export default PlaybookCardGrid;
//# sourceMappingURL=PlaybookCardGrid.js.map