/**
 * RecordCardShell — shared card shell for all entity record cards.
 *
 * Provides consistent layout, sizing, hover/focus states, and accessibility
 * across all card types (Documents, Matters, Projects, Todos, Events, etc.).
 * Entity-specific cards are thin wrappers that pass content + tools.
 *
 * Layout:
 *   ┌─ accent border ──────────────────────────────────────────────┐
 *   │ [icon]  Row 1: title + primary fields     [tools] [menu]   │
 *   │         Row 2: secondary content                             │
 *   └─────────────────────────────────────────────────────────────┘
 *
 * The `tools` slot renders inline action buttons (preview, pin, summary,
 * etc.) — different per entity type. The `overflowMenu` slot renders the
 * ⋮ overflow menu. Both are optional.
 *
 * @see ADR-012 - Shared component library
 * @see ADR-021 - Fluent UI v9 design system
 */
import * as React from 'react';
import { makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------
const useStyles = makeStyles({
    root: {
        display: 'flex',
        flexDirection: 'row',
        alignItems: 'flex-start',
        gap: tokens.spacingHorizontalL,
        paddingTop: tokens.spacingVerticalM,
        paddingBottom: tokens.spacingVerticalM,
        paddingLeft: tokens.spacingHorizontalL,
        paddingRight: tokens.spacingHorizontalL,
        backgroundColor: tokens.colorNeutralBackground1,
        borderRadius: tokens.borderRadiusMedium,
        border: `1px solid ${tokens.colorNeutralStroke2}`,
        boxShadow: tokens.shadow2,
        cursor: 'default',
        position: 'relative',
        transitionProperty: 'background-color, box-shadow',
        transitionDuration: tokens.durationNormal,
        transitionTimingFunction: tokens.curveEasyEase,
    },
    interactive: {
        cursor: 'pointer',
        '&:hover': {
            backgroundColor: tokens.colorNeutralBackground1Hover,
            boxShadow: tokens.shadow4,
        },
        '&:active': {
            backgroundColor: tokens.colorNeutralBackground1Pressed,
        },
        '&:focus-visible': {
            outlineStyle: 'solid',
            outlineWidth: '2px',
            outlineColor: tokens.colorBrandStroke1,
            outlineOffset: '-2px',
        },
    },
    accent: {
        borderLeftWidth: '3px',
        borderLeftStyle: 'solid',
    },
    iconColumn: {
        flexShrink: 0,
        display: 'flex',
        alignItems: 'flex-start',
        paddingTop: '2px',
    },
    contentColumn: {
        flex: '1 1 0',
        minWidth: 0,
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalXS,
    },
    primaryRow: {
        display: 'flex',
        flexDirection: 'row',
        alignItems: 'center',
        gap: tokens.spacingHorizontalS,
        minWidth: 0,
    },
    secondaryRow: {
        display: 'flex',
        flexDirection: 'row',
        alignItems: 'center',
        gap: tokens.spacingHorizontalS,
        minWidth: 0,
    },
    toolsColumn: {
        flexShrink: 0,
        display: 'flex',
        flexDirection: 'row',
        alignItems: 'center',
        gap: tokens.spacingHorizontalXXS,
    },
    loadingOverlay: {
        position: 'absolute',
        inset: 0,
        backgroundColor: 'rgba(255, 255, 255, 0.6)',
        borderRadius: tokens.borderRadiusMedium,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        pointerEvents: 'none',
    },
});
// ---------------------------------------------------------------------------
// RecordCardShell
// ---------------------------------------------------------------------------
export const RecordCardShell = ({ icon, primaryContent, secondaryContent, tools, overflowMenu, accentColor, onClick, onDoubleClick, ariaLabel, isLoading, className, 'data-testid': testId, }) => {
    const styles = useStyles();
    const isInteractive = !!onClick || !!onDoubleClick;
    const effectiveAccent = accentColor ?? tokens.colorBrandStroke1;
    const showAccent = accentColor !== 'none';
    const handleKeyDown = React.useCallback((e) => {
        if (onClick && (e.key === 'Enter' || e.key === ' ')) {
            e.preventDefault();
            onClick(e);
        }
    }, [onClick]);
    const handleToolsClick = React.useCallback((e) => {
        e.stopPropagation();
    }, []);
    return (React.createElement("div", { className: mergeClasses(styles.root, isInteractive && styles.interactive, showAccent && styles.accent, className), style: showAccent ? { borderLeftColor: effectiveAccent } : undefined, role: isInteractive ? 'button' : 'listitem', tabIndex: isInteractive ? 0 : undefined, "aria-label": ariaLabel, onClick: onClick, onDoubleClick: onDoubleClick, onKeyDown: isInteractive ? handleKeyDown : undefined, "data-testid": testId },
        React.createElement("div", { className: styles.iconColumn }, icon),
        React.createElement("div", { className: styles.contentColumn },
            React.createElement("div", { className: styles.primaryRow }, primaryContent),
            secondaryContent && (React.createElement("div", { className: styles.secondaryRow }, secondaryContent))),
        (tools || overflowMenu) && (
        // eslint-disable-next-line jsx-a11y/click-events-have-key-events, jsx-a11y/no-static-element-interactions
        React.createElement("div", { className: styles.toolsColumn, onClick: handleToolsClick },
            tools,
            overflowMenu)),
        isLoading && React.createElement("div", { className: styles.loadingOverlay })));
};
export default RecordCardShell;
//# sourceMappingURL=RecordCardShell.js.map