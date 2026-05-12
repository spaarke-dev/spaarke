/**
 * DiffCompareView Component
 *
 * Renders side-by-side and inline diff views with Accept, Reject, and Edit
 * actions. Designed for showing AI-proposed revisions against existing document
 * content.
 *
 * Features:
 * - Side-by-side and inline diff display modes
 * - Accept, Reject, Edit action buttons
 * - Inline text editing with Save/Cancel
 * - Keyboard shortcuts: Ctrl+Enter (Accept), Escape (Reject)
 * - Fluent UI v9 styling with design tokens
 * - Dark mode and high-contrast support
 * - ARIA labels and keyboard navigation
 *
 * Standards: ADR-012 (shared component library), ADR-021 (Fluent UI v9)
 */
import * as React from 'react';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { makeStyles, tokens, Button, Textarea, Tooltip, mergeClasses, shorthands } from '@fluentui/react-components';
import { CheckmarkRegular, DismissRegular, EditRegular, SaveRegular, DismissCircleRegular, SplitHorizontalRegular, TextAlignLeftRegular, } from '@fluentui/react-icons';
import { diffWords } from 'diff';
import { computeHtmlDiff } from './diffUtils';
// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────
const useStyles = makeStyles({
    root: {
        display: 'flex',
        flexDirection: 'column',
        width: '100%',
        border: `1px solid ${tokens.colorNeutralStroke1}`,
        borderRadius: tokens.borderRadiusMedium,
        backgroundColor: tokens.colorNeutralBackground1,
        overflow: 'hidden',
    },
    // Header row: title + mode toggle
    header: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        ...shorthands.padding('8px', '12px'),
        ...shorthands.borderBottom('1px', 'solid', tokens.colorNeutralStroke2),
        backgroundColor: tokens.colorNeutralBackground2,
    },
    headerTitle: {
        fontFamily: tokens.fontFamilyBase,
        fontSize: tokens.fontSizeBase400,
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground1,
    },
    modeToggle: {
        display: 'flex',
        alignItems: 'center',
        columnGap: '4px',
    },
    // Content area
    content: {
        flex: 1,
        overflow: 'auto',
        maxHeight: '500px',
    },
    // Side-by-side layout: uses a 2-column grid for header labels and content panes
    sideBySideContainer: {
        display: 'grid',
        gridTemplateColumns: '1fr 1fr',
        minHeight: '100px',
    },
    paneLabel: {
        fontFamily: tokens.fontFamilyBase,
        fontSize: tokens.fontSizeBase200,
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground3,
        textTransform: 'uppercase',
        letterSpacing: '0.05em',
        ...shorthands.padding('6px', '12px'),
        ...shorthands.borderBottom('1px', 'solid', tokens.colorNeutralStroke2),
        backgroundColor: tokens.colorNeutralBackground3,
    },
    paneLabelOriginal: {
        ...shorthands.borderRight('1px', 'solid', tokens.colorNeutralStroke2),
    },
    pane: {
        ...shorthands.padding('12px'),
        fontFamily: tokens.fontFamilyBase,
        fontSize: tokens.fontSizeBase300,
        lineHeight: tokens.lineHeightBase300,
        color: tokens.colorNeutralForeground1,
        whiteSpace: 'pre-wrap',
        wordBreak: 'break-word',
    },
    paneOriginal: {
        ...shorthands.borderRight('1px', 'solid', tokens.colorNeutralStroke2),
    },
    // Inline layout
    inlineContent: {
        ...shorthands.padding('12px'),
        fontFamily: tokens.fontFamilyBase,
        fontSize: tokens.fontSizeBase300,
        lineHeight: tokens.lineHeightBase300,
        color: tokens.colorNeutralForeground1,
        whiteSpace: 'pre-wrap',
        wordBreak: 'break-word',
    },
    // Diff highlight segments
    segmentAdded: {
        backgroundColor: tokens.colorPaletteGreenBackground2,
        color: tokens.colorPaletteGreenForeground2,
        borderRadius: tokens.borderRadiusSmall,
        ...shorthands.padding('0', '2px'),
    },
    segmentRemoved: {
        backgroundColor: tokens.colorPaletteRedBackground2,
        color: tokens.colorPaletteRedForeground2,
        textDecoration: 'line-through',
        borderRadius: tokens.borderRadiusSmall,
        ...shorthands.padding('0', '2px'),
    },
    // High contrast overrides: use border outlines instead of background colors
    segmentAddedHighContrast: {
        '@media (forced-colors: active)': {
            backgroundColor: 'transparent',
            ...shorthands.borderBottom('2px', 'solid', 'Highlight'),
            color: 'Highlight',
        },
    },
    segmentRemovedHighContrast: {
        '@media (forced-colors: active)': {
            backgroundColor: 'transparent',
            ...shorthands.borderBottom('2px', 'solid', 'LinkText'),
            color: 'LinkText',
            textDecoration: 'line-through',
        },
    },
    // Action bar
    actionBar: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'flex-end',
        columnGap: '8px',
        ...shorthands.padding('8px', '12px'),
        ...shorthands.borderTop('1px', 'solid', tokens.colorNeutralStroke2),
        backgroundColor: tokens.colorNeutralBackground2,
    },
    actionBarLeft: {
        display: 'flex',
        alignItems: 'center',
        columnGap: '4px',
        marginRight: 'auto',
    },
    shortcutHint: {
        fontFamily: tokens.fontFamilyBase,
        fontSize: tokens.fontSizeBase100,
        color: tokens.colorNeutralForeground4,
    },
    // Edit mode
    editArea: {
        ...shorthands.padding('12px'),
    },
    editActions: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'flex-end',
        columnGap: '8px',
        ...shorthands.padding('8px', '12px', '0'),
    },
    // HTML diff content panes (rendered via dangerouslySetInnerHTML)
    htmlPane: {
        ...shorthands.padding('12px'),
        fontFamily: tokens.fontFamilyBase,
        fontSize: tokens.fontSizeBase300,
        lineHeight: tokens.lineHeightBase300,
        color: tokens.colorNeutralForeground1,
        wordBreak: 'break-word',
        '& p': {
            margin: '0 0 8px 0',
        },
        '& h1': {
            fontSize: tokens.fontSizeBase600,
            fontWeight: tokens.fontWeightSemibold,
            margin: '16px 0 8px 0',
        },
        '& h2': {
            fontSize: tokens.fontSizeBase500,
            fontWeight: tokens.fontWeightSemibold,
            margin: '12px 0 8px 0',
        },
        '& h3': {
            fontSize: tokens.fontSizeBase400,
            fontWeight: tokens.fontWeightSemibold,
            margin: '8px 0 8px 0',
        },
        '& ul, & ol': {
            margin: '8px 0',
            paddingLeft: '24px',
        },
        '& li': {
            marginBottom: '4px',
        },
        '& blockquote': {
            borderLeft: `3px solid ${tokens.colorNeutralStroke2}`,
            marginLeft: '0',
            paddingLeft: '16px',
            color: tokens.colorNeutralForeground2,
        },
        '& .diff-added': {
            backgroundColor: tokens.colorPaletteGreenBackground2,
            color: tokens.colorPaletteGreenForeground2,
            borderRadius: tokens.borderRadiusSmall,
            ...shorthands.padding('0', '2px'),
        },
        '& .diff-removed': {
            backgroundColor: tokens.colorPaletteRedBackground2,
            color: tokens.colorPaletteRedForeground2,
            textDecoration: 'line-through',
            borderRadius: tokens.borderRadiusSmall,
            ...shorthands.padding('0', '2px'),
        },
    },
    htmlPaneOriginal: {
        ...shorthands.borderRight('1px', 'solid', tokens.colorNeutralStroke2),
    },
    htmlInlineContent: {
        ...shorthands.padding('12px'),
        fontFamily: tokens.fontFamilyBase,
        fontSize: tokens.fontSizeBase300,
        lineHeight: tokens.lineHeightBase300,
        color: tokens.colorNeutralForeground1,
        wordBreak: 'break-word',
        '& p': {
            margin: '0 0 8px 0',
        },
        '& h1, & h2, & h3': {
            fontWeight: tokens.fontWeightSemibold,
            margin: '12px 0 8px 0',
        },
        '& ul, & ol': {
            margin: '8px 0',
            paddingLeft: '24px',
        },
        '& .diff-added': {
            backgroundColor: tokens.colorPaletteGreenBackground2,
            color: tokens.colorPaletteGreenForeground2,
            borderRadius: tokens.borderRadiusSmall,
            ...shorthands.padding('0', '2px'),
        },
        '& .diff-removed': {
            backgroundColor: tokens.colorPaletteRedBackground2,
            color: tokens.colorPaletteRedForeground2,
            textDecoration: 'line-through',
            borderRadius: tokens.borderRadiusSmall,
            ...shorthands.padding('0', '2px'),
        },
    },
    // Stats badge
    statsBadge: {
        display: 'inline-flex',
        alignItems: 'center',
        columnGap: '8px',
        fontFamily: tokens.fontFamilyBase,
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground3,
        ...shorthands.padding('4px', '12px'),
        ...shorthands.borderTop('1px', 'solid', tokens.colorNeutralStroke2),
    },
    statsAdded: {
        color: tokens.colorPaletteGreenForeground2,
    },
    statsRemoved: {
        color: tokens.colorPaletteRedForeground2,
    },
});
// ─────────────────────────────────────────────────────────────────────────────
// Helper: Compute diff segments from jsdiff output
// ─────────────────────────────────────────────────────────────────────────────
function computeDiffSegments(original, proposed) {
    const changes = diffWords(original, proposed);
    return changes.map(change => ({
        type: change.added ? 'added' : change.removed ? 'removed' : 'unchanged',
        value: change.value,
    }));
}
function computeDiffStats(segments) {
    let additions = 0;
    let removals = 0;
    let unchanged = 0;
    for (const seg of segments) {
        const wordCount = seg.value.trim().split(/\s+/).filter(Boolean).length;
        if (seg.type === 'added')
            additions += wordCount;
        else if (seg.type === 'removed')
            removals += wordCount;
        else
            unchanged += wordCount;
    }
    return { additions, removals, unchanged };
}
function DiffSegmentSpan({ segment, styles }) {
    if (segment.type === 'added') {
        return (React.createElement("span", { className: mergeClasses(styles.segmentAdded, styles.segmentAddedHighContrast), "aria-label": `Added: ${segment.value}` }, segment.value));
    }
    if (segment.type === 'removed') {
        return (React.createElement("span", { className: mergeClasses(styles.segmentRemoved, styles.segmentRemovedHighContrast), "aria-label": `Removed: ${segment.value}` }, segment.value));
    }
    return React.createElement("span", null, segment.value);
}
function InlineView({ segments, styles }) {
    return (React.createElement("div", { className: styles.inlineContent, role: "region", "aria-label": "Inline diff view" }, segments.map((seg, i) => (React.createElement(DiffSegmentSpan, { key: i, segment: seg, styles: styles })))));
}
function SideBySideView({ segments, styles }) {
    // Original side shows unchanged + removed segments
    const originalSegments = segments.filter(s => s.type !== 'added');
    // Proposed side shows unchanged + added segments
    const proposedSegments = segments.filter(s => s.type !== 'removed');
    return (React.createElement("div", { className: styles.sideBySideContainer },
        React.createElement("div", { className: mergeClasses(styles.paneLabel, styles.paneLabelOriginal) }, "Original"),
        React.createElement("div", { className: styles.paneLabel }, "Proposed"),
        React.createElement("div", { className: mergeClasses(styles.pane, styles.paneOriginal), role: "region", "aria-label": "Original text" }, originalSegments.map((seg, i) => (React.createElement(DiffSegmentSpan, { key: i, segment: seg, styles: styles })))),
        React.createElement("div", { className: styles.pane, role: "region", "aria-label": "Proposed text" }, proposedSegments.map((seg, i) => (React.createElement(DiffSegmentSpan, { key: i, segment: seg, styles: styles }))))));
}
function HtmlSideBySideView({ diffResult, styles }) {
    return (React.createElement("div", { className: styles.sideBySideContainer },
        React.createElement("div", { className: mergeClasses(styles.paneLabel, styles.paneLabelOriginal) }, "Original"),
        React.createElement("div", { className: styles.paneLabel }, "Proposed"),
        React.createElement("div", { className: mergeClasses(styles.htmlPane, styles.htmlPaneOriginal), role: "region", "aria-label": "Original content", dangerouslySetInnerHTML: { __html: diffResult.originalAnnotatedHtml } }),
        React.createElement("div", { className: styles.htmlPane, role: "region", "aria-label": "Proposed content", dangerouslySetInnerHTML: { __html: diffResult.proposedAnnotatedHtml } })));
}
function HtmlInlineView({ diffResult, styles }) {
    // For inline mode, show the proposed annotated HTML which contains both
    // additions and all unchanged content in the proposed document structure.
    // We use the proposed side because it represents the "merged" view.
    return (React.createElement("div", { className: styles.htmlInlineContent, role: "region", "aria-label": "Inline HTML diff view", dangerouslySetInnerHTML: { __html: diffResult.proposedAnnotatedHtml } }));
}
// ─────────────────────────────────────────────────────────────────────────────
// Main Component
// ─────────────────────────────────────────────────────────────────────────────
/**
 * DiffCompareView renders a diff comparison between original and proposed text.
 *
 * Supports side-by-side and inline display modes with Accept, Reject, and Edit
 * actions. Designed for reviewing AI-proposed revisions.
 *
 * @param props - Component configuration (see IDiffCompareViewProps)
 *
 * @example
 * ```tsx
 * <DiffCompareView
 *     originalText="The quick brown fox"
 *     proposedText="The fast brown fox jumps"
 *     mode="side-by-side"
 *     onAccept={(text) => saveRevision(text)}
 *     onReject={() => discardRevision()}
 * />
 * ```
 */
export const DiffCompareView = props => {
    const { originalText, proposedText, htmlMode = false, diffOptions, mode: initialMode = 'side-by-side', onAccept, onReject, onEdit, title, readOnly = false, ariaLabel = 'Diff comparison view', } = props;
    const styles = useStyles();
    const containerRef = useRef(null);
    // Current display mode (can be toggled)
    const [mode, setMode] = useState(initialMode);
    // Edit mode state
    const [isEditing, setIsEditing] = useState(false);
    const [editText, setEditText] = useState(proposedText);
    const textareaRef = useRef(null);
    // Sync initialMode prop changes
    useEffect(() => {
        setMode(initialMode);
    }, [initialMode]);
    // Sync proposedText prop changes to edit buffer
    useEffect(() => {
        if (!isEditing) {
            setEditText(proposedText);
        }
    }, [proposedText, isEditing]);
    // Compute HTML diff result (only when htmlMode is enabled)
    const htmlDiffResult = useMemo(() => {
        if (!htmlMode)
            return null;
        return computeHtmlDiff(originalText, proposedText, diffOptions);
    }, [htmlMode, originalText, proposedText, diffOptions]);
    // Compute plain-text diff segments (used when htmlMode is off, or for stats fallback)
    const segments = useMemo(() => (htmlDiffResult ? htmlDiffResult.segments : computeDiffSegments(originalText, proposedText)), [htmlDiffResult, originalText, proposedText]);
    // Compute stats
    const stats = useMemo(() => {
        if (htmlDiffResult) {
            return {
                additions: htmlDiffResult.stats.additions,
                removals: htmlDiffResult.stats.deletions,
                unchanged: htmlDiffResult.stats.unchanged,
            };
        }
        return computeDiffStats(segments);
    }, [htmlDiffResult, segments]);
    // ─── Actions ──────────────────────────────────────────────────────────
    const handleAccept = useCallback(() => {
        onAccept(isEditing ? editText : proposedText);
        setIsEditing(false);
    }, [onAccept, isEditing, editText, proposedText]);
    const handleReject = useCallback(() => {
        setIsEditing(false);
        onReject();
    }, [onReject]);
    const handleEditStart = useCallback(() => {
        setEditText(proposedText);
        setIsEditing(true);
    }, [proposedText]);
    const handleEditSave = useCallback(() => {
        if (onEdit) {
            onEdit(editText);
        }
        setIsEditing(false);
    }, [onEdit, editText]);
    const handleEditCancel = useCallback(() => {
        setEditText(proposedText);
        setIsEditing(false);
    }, [proposedText]);
    const handleToggleMode = useCallback(() => {
        setMode(prev => (prev === 'side-by-side' ? 'inline' : 'side-by-side'));
    }, []);
    // Focus textarea when entering edit mode
    useEffect(() => {
        if (isEditing && textareaRef.current) {
            textareaRef.current.focus();
        }
    }, [isEditing]);
    // ─── Keyboard Shortcuts ───────────────────────────────────────────────
    useEffect(() => {
        const container = containerRef.current;
        if (!container || readOnly)
            return;
        const handleKeyDown = (e) => {
            // Ctrl+Enter: Accept
            if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
                e.preventDefault();
                handleAccept();
                return;
            }
            // Escape: Reject (if not editing), Cancel edit (if editing)
            if (e.key === 'Escape') {
                e.preventDefault();
                if (isEditing) {
                    handleEditCancel();
                }
                else {
                    handleReject();
                }
            }
        };
        container.addEventListener('keydown', handleKeyDown);
        return () => container.removeEventListener('keydown', handleKeyDown);
    }, [readOnly, isEditing, handleAccept, handleReject, handleEditCancel]);
    // ─── Render ───────────────────────────────────────────────────────────
    return (React.createElement("div", { ref: containerRef, className: styles.root, role: "region", "aria-label": ariaLabel, tabIndex: 0 },
        (title || !readOnly) && (React.createElement("div", { className: styles.header },
            title && React.createElement("span", { className: styles.headerTitle }, title),
            !title && React.createElement("span", null),
            React.createElement("div", { className: styles.modeToggle },
                React.createElement(Tooltip, { content: mode === 'side-by-side' ? 'Switch to inline view' : 'Switch to side-by-side view', relationship: "label" },
                    React.createElement(Button, { appearance: "subtle", size: "small", icon: mode === 'side-by-side' ? React.createElement(TextAlignLeftRegular, null) : React.createElement(SplitHorizontalRegular, null), onClick: handleToggleMode, "aria-label": mode === 'side-by-side' ? 'Switch to inline view' : 'Switch to side-by-side view' }, mode === 'side-by-side' ? 'Inline' : 'Side-by-side'))))),
        React.createElement("div", { className: styles.content }, isEditing ? (React.createElement("div", { className: styles.editArea },
            React.createElement(Textarea, { ref: textareaRef, value: editText, onChange: (_e, data) => setEditText(data.value), resize: "vertical", style: { width: '100%', minHeight: '150px' }, "aria-label": "Edit proposed text" }),
            React.createElement("div", { className: styles.editActions },
                React.createElement(Button, { appearance: "primary", size: "small", icon: React.createElement(SaveRegular, null), onClick: handleEditSave, "aria-label": "Save edits" }, "Save"),
                React.createElement(Button, { appearance: "subtle", size: "small", icon: React.createElement(DismissCircleRegular, null), onClick: handleEditCancel, "aria-label": "Cancel editing" }, "Cancel")))) : htmlMode && htmlDiffResult ? (
        // HTML diff mode: render annotated HTML with preserved structure
        mode === 'side-by-side' ? (React.createElement(HtmlSideBySideView, { diffResult: htmlDiffResult, styles: styles })) : (React.createElement(HtmlInlineView, { diffResult: htmlDiffResult, styles: styles }))) : mode === 'side-by-side' ? (React.createElement(SideBySideView, { segments: segments, styles: styles })) : (React.createElement(InlineView, { segments: segments, styles: styles }))),
        !isEditing && (stats.additions > 0 || stats.removals > 0) && (React.createElement("div", { className: styles.statsBadge, "aria-live": "polite" },
            stats.additions > 0 && (React.createElement("span", { className: styles.statsAdded },
                "+",
                stats.additions,
                " word",
                stats.additions !== 1 ? 's' : '')),
            stats.removals > 0 && (React.createElement("span", { className: styles.statsRemoved },
                "-",
                stats.removals,
                " word",
                stats.removals !== 1 ? 's' : '')))),
        !readOnly && (React.createElement("div", { className: styles.actionBar, role: "toolbar", "aria-label": "Diff actions" },
            React.createElement("div", { className: styles.actionBarLeft },
                React.createElement("span", { className: styles.shortcutHint }, "Ctrl+Enter: Accept | Esc: Reject")),
            onEdit && !isEditing && (React.createElement(Tooltip, { content: "Edit the proposed text", relationship: "label" },
                React.createElement(Button, { appearance: "subtle", size: "small", icon: React.createElement(EditRegular, null), onClick: handleEditStart, "aria-label": "Edit proposed text" }, "Edit"))),
            React.createElement(Tooltip, { content: "Reject changes (Escape)", relationship: "label" },
                React.createElement(Button, { appearance: "subtle", size: "small", icon: React.createElement(DismissRegular, null), onClick: handleReject, "aria-label": "Reject changes" }, "Reject")),
            React.createElement(Tooltip, { content: "Accept changes (Ctrl+Enter)", relationship: "label" },
                React.createElement(Button, { appearance: "primary", size: "small", icon: React.createElement(CheckmarkRegular, null), onClick: handleAccept, "aria-label": "Accept changes" }, "Accept"))))));
};
export default DiffCompareView;
//# sourceMappingURL=DiffCompareView.js.map