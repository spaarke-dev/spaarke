/**
 * ToolbarPlugin - Rich text formatting toolbar for Lexical editor
 *
 * Provides formatting buttons using Fluent UI v9 components:
 * - Text formatting: Bold, Italic, Underline, Strikethrough
 * - Block formatting: Headings (H1, H2, H3), Quote
 * - Lists: Ordered, Unordered
 * - History: Undo, Redo
 *
 * Standards: ADR-012 (shared component library)
 */
import * as React from 'react';
import { useCallback, useEffect, useState } from 'react';
import { makeStyles, tokens, Toolbar, ToolbarButton, ToolbarDivider, Tooltip } from '@fluentui/react-components';
import { TextBoldRegular, TextItalicRegular, TextUnderlineRegular, TextStrikethroughRegular, TextHeader1Regular, TextHeader2Regular, TextHeader3Regular, TextBulletListRegular, TextNumberListLtrRegular, ArrowUndoRegular, ArrowRedoRegular, } from '@fluentui/react-icons';
import { useLexicalComposerContext } from '@lexical/react/LexicalComposerContext';
import { $getSelection, $isRangeSelection, CAN_REDO_COMMAND, CAN_UNDO_COMMAND, COMMAND_PRIORITY_CRITICAL, FORMAT_TEXT_COMMAND, REDO_COMMAND, UNDO_COMMAND, SELECTION_CHANGE_COMMAND, } from 'lexical';
import { $isHeadingNode, $createHeadingNode } from '@lexical/rich-text';
import { INSERT_ORDERED_LIST_COMMAND, INSERT_UNORDERED_LIST_COMMAND, REMOVE_LIST_COMMAND, $isListNode, ListNode, } from '@lexical/list';
import { $setBlocksType } from '@lexical/selection';
import { $getNearestNodeOfType } from '@lexical/utils';
// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────
const useStyles = makeStyles({
    toolbar: {
        borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
        backgroundColor: tokens.colorNeutralBackground2,
        padding: '4px 8px',
        flexWrap: 'wrap',
        gap: '2px',
    },
    toolbarDark: {
        backgroundColor: tokens.colorNeutralBackground4,
    },
    buttonActive: {
        backgroundColor: tokens.colorNeutralBackground1Pressed,
    },
});
// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────
export function ToolbarPlugin({ isDarkMode = false }) {
    const [editor] = useLexicalComposerContext();
    const styles = useStyles();
    // State for formatting
    const [isBold, setIsBold] = useState(false);
    const [isItalic, setIsItalic] = useState(false);
    const [isUnderline, setIsUnderline] = useState(false);
    const [isStrikethrough, setIsStrikethrough] = useState(false);
    const [blockType, setBlockType] = useState('paragraph');
    const [canUndo, setCanUndo] = useState(false);
    const [canRedo, setCanRedo] = useState(false);
    // Update toolbar state based on selection
    const updateToolbar = useCallback(() => {
        const selection = $getSelection();
        if ($isRangeSelection(selection)) {
            // Text formatting
            setIsBold(selection.hasFormat('bold'));
            setIsItalic(selection.hasFormat('italic'));
            setIsUnderline(selection.hasFormat('underline'));
            setIsStrikethrough(selection.hasFormat('strikethrough'));
            // Block type
            const anchorNode = selection.anchor.getNode();
            const element = anchorNode.getKey() === 'root' ? anchorNode : anchorNode.getTopLevelElementOrThrow();
            const elementKey = element.getKey();
            const elementDOM = editor.getElementByKey(elementKey);
            if (elementDOM !== null) {
                if ($isListNode(element)) {
                    const parentList = $getNearestNodeOfType(anchorNode, ListNode);
                    const type = parentList ? parentList.getListType() : element.getListType();
                    setBlockType(type === 'number' ? 'ol' : 'ul');
                }
                else {
                    const type = $isHeadingNode(element) ? element.getTag() : element.getType();
                    if (type === 'h1' || type === 'h2' || type === 'h3') {
                        setBlockType(type);
                    }
                    else if (type === 'quote') {
                        setBlockType('quote');
                    }
                    else {
                        setBlockType('paragraph');
                    }
                }
            }
        }
    }, [editor]);
    // Register listeners
    useEffect(() => {
        return editor.registerCommand(SELECTION_CHANGE_COMMAND, () => {
            updateToolbar();
            return false;
        }, COMMAND_PRIORITY_CRITICAL);
    }, [editor, updateToolbar]);
    useEffect(() => {
        return editor.registerCommand(CAN_UNDO_COMMAND, payload => {
            setCanUndo(payload);
            return false;
        }, COMMAND_PRIORITY_CRITICAL);
    }, [editor]);
    useEffect(() => {
        return editor.registerCommand(CAN_REDO_COMMAND, payload => {
            setCanRedo(payload);
            return false;
        }, COMMAND_PRIORITY_CRITICAL);
    }, [editor]);
    // Format handlers
    const formatBold = () => {
        editor.dispatchCommand(FORMAT_TEXT_COMMAND, 'bold');
    };
    const formatItalic = () => {
        editor.dispatchCommand(FORMAT_TEXT_COMMAND, 'italic');
    };
    const formatUnderline = () => {
        editor.dispatchCommand(FORMAT_TEXT_COMMAND, 'underline');
    };
    const formatStrikethrough = () => {
        editor.dispatchCommand(FORMAT_TEXT_COMMAND, 'strikethrough');
    };
    const formatHeading = (headingType) => {
        editor.update(() => {
            const selection = $getSelection();
            if ($isRangeSelection(selection)) {
                $setBlocksType(selection, () => $createHeadingNode(headingType));
            }
        });
    };
    const formatBulletList = () => {
        if (blockType !== 'ul') {
            editor.dispatchCommand(INSERT_UNORDERED_LIST_COMMAND, undefined);
        }
        else {
            editor.dispatchCommand(REMOVE_LIST_COMMAND, undefined);
        }
    };
    const formatNumberedList = () => {
        if (blockType !== 'ol') {
            editor.dispatchCommand(INSERT_ORDERED_LIST_COMMAND, undefined);
        }
        else {
            editor.dispatchCommand(REMOVE_LIST_COMMAND, undefined);
        }
    };
    const undo = () => {
        editor.dispatchCommand(UNDO_COMMAND, undefined);
    };
    const redo = () => {
        editor.dispatchCommand(REDO_COMMAND, undefined);
    };
    const toolbarClass = isDarkMode ? `${styles.toolbar} ${styles.toolbarDark}` : styles.toolbar;
    return (React.createElement(Toolbar, { className: toolbarClass, size: "small" },
        React.createElement(Tooltip, { content: "Undo (Ctrl+Z)", relationship: "label" },
            React.createElement(ToolbarButton, { icon: React.createElement(ArrowUndoRegular, null), onClick: undo, disabled: !canUndo, "aria-label": "Undo" })),
        React.createElement(Tooltip, { content: "Redo (Ctrl+Y)", relationship: "label" },
            React.createElement(ToolbarButton, { icon: React.createElement(ArrowRedoRegular, null), onClick: redo, disabled: !canRedo, "aria-label": "Redo" })),
        React.createElement(ToolbarDivider, null),
        React.createElement(Tooltip, { content: "Bold (Ctrl+B)", relationship: "label" },
            React.createElement(ToolbarButton, { icon: React.createElement(TextBoldRegular, null), onClick: formatBold, className: isBold ? styles.buttonActive : undefined, "aria-label": "Bold", "aria-pressed": isBold })),
        React.createElement(Tooltip, { content: "Italic (Ctrl+I)", relationship: "label" },
            React.createElement(ToolbarButton, { icon: React.createElement(TextItalicRegular, null), onClick: formatItalic, className: isItalic ? styles.buttonActive : undefined, "aria-label": "Italic", "aria-pressed": isItalic })),
        React.createElement(Tooltip, { content: "Underline (Ctrl+U)", relationship: "label" },
            React.createElement(ToolbarButton, { icon: React.createElement(TextUnderlineRegular, null), onClick: formatUnderline, className: isUnderline ? styles.buttonActive : undefined, "aria-label": "Underline", "aria-pressed": isUnderline })),
        React.createElement(Tooltip, { content: "Strikethrough", relationship: "label" },
            React.createElement(ToolbarButton, { icon: React.createElement(TextStrikethroughRegular, null), onClick: formatStrikethrough, className: isStrikethrough ? styles.buttonActive : undefined, "aria-label": "Strikethrough", "aria-pressed": isStrikethrough })),
        React.createElement(ToolbarDivider, null),
        React.createElement(Tooltip, { content: "Heading 1", relationship: "label" },
            React.createElement(ToolbarButton, { icon: React.createElement(TextHeader1Regular, null), onClick: () => formatHeading('h1'), className: blockType === 'h1' ? styles.buttonActive : undefined, "aria-label": "Heading 1", "aria-pressed": blockType === 'h1' })),
        React.createElement(Tooltip, { content: "Heading 2", relationship: "label" },
            React.createElement(ToolbarButton, { icon: React.createElement(TextHeader2Regular, null), onClick: () => formatHeading('h2'), className: blockType === 'h2' ? styles.buttonActive : undefined, "aria-label": "Heading 2", "aria-pressed": blockType === 'h2' })),
        React.createElement(Tooltip, { content: "Heading 3", relationship: "label" },
            React.createElement(ToolbarButton, { icon: React.createElement(TextHeader3Regular, null), onClick: () => formatHeading('h3'), className: blockType === 'h3' ? styles.buttonActive : undefined, "aria-label": "Heading 3", "aria-pressed": blockType === 'h3' })),
        React.createElement(ToolbarDivider, null),
        React.createElement(Tooltip, { content: "Bullet List", relationship: "label" },
            React.createElement(ToolbarButton, { icon: React.createElement(TextBulletListRegular, null), onClick: formatBulletList, className: blockType === 'ul' ? styles.buttonActive : undefined, "aria-label": "Bullet List", "aria-pressed": blockType === 'ul' })),
        React.createElement(Tooltip, { content: "Numbered List", relationship: "label" },
            React.createElement(ToolbarButton, { icon: React.createElement(TextNumberListLtrRegular, null), onClick: formatNumberedList, className: blockType === 'ol' ? styles.buttonActive : undefined, "aria-label": "Numbered List", "aria-pressed": blockType === 'ol' }))));
}
export default ToolbarPlugin;
//# sourceMappingURL=ToolbarPlugin.js.map