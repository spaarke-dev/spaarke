/**
 * RichTextEditor Component
 *
 * A reusable WYSIWYG rich text editor built with Lexical.
 * Designed for use in PCF controls across Model-Driven Apps,
 * Power Apps Custom Pages, and standalone web applications.
 *
 * Features:
 * - Full rich text editing (bold, italic, underline, strikethrough)
 * - Headings (H1, H2, H3)
 * - Lists (ordered, unordered)
 * - Links
 * - Undo/Redo
 * - HTML import/export
 * - Fluent UI v9 styling
 * - Dark mode support
 *
 * Standards: ADR-012 (shared component library)
 */
import * as React from 'react';
import { useCallback, useEffect, useImperativeHandle, forwardRef } from 'react';
import { makeStyles, tokens, Spinner } from '@fluentui/react-components';
// Lexical core
import { LexicalComposer } from '@lexical/react/LexicalComposer';
import { RichTextPlugin } from '@lexical/react/LexicalRichTextPlugin';
import { ContentEditable } from '@lexical/react/LexicalContentEditable';
import { HistoryPlugin } from '@lexical/react/LexicalHistoryPlugin';
import { OnChangePlugin } from '@lexical/react/LexicalOnChangePlugin';
import { LexicalErrorBoundary } from '@lexical/react/LexicalErrorBoundary';
import { useLexicalComposerContext } from '@lexical/react/LexicalComposerContext';
import { ListPlugin } from '@lexical/react/LexicalListPlugin';
// Lexical nodes
import { HeadingNode, QuoteNode } from '@lexical/rich-text';
import { ListNode, ListItemNode } from '@lexical/list';
import { LinkNode } from '@lexical/link';
// Lexical utilities
import { $generateHtmlFromNodes, $generateNodesFromDOM } from '@lexical/html';
import { $createParagraphNode, $createTextNode, $getRoot, $getSelection, $insertNodes, $isRangeSelection, } from 'lexical';
// Local components
import { ToolbarPlugin } from './plugins/ToolbarPlugin';
// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────
const useStyles = makeStyles({
    container: {
        display: 'flex',
        flexDirection: 'column',
        width: '100%',
        height: '100%',
        border: `1px solid ${tokens.colorNeutralStroke1}`,
        borderRadius: tokens.borderRadiusMedium,
        backgroundColor: tokens.colorNeutralBackground1,
        overflow: 'hidden',
    },
    containerDark: {
        backgroundColor: tokens.colorNeutralBackground3,
    },
    editorContainer: {
        flex: 1,
        position: 'relative',
        overflow: 'auto',
    },
    contentEditable: {
        outline: 'none',
        padding: '12px 16px',
        minHeight: '200px',
        fontFamily: tokens.fontFamilyBase,
        fontSize: tokens.fontSizeBase300,
        lineHeight: tokens.lineHeightBase300,
        color: tokens.colorNeutralForeground1,
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
        '& a': {
            color: tokens.colorBrandForegroundLink,
            textDecoration: 'underline',
        },
        '& blockquote': {
            borderLeft: `3px solid ${tokens.colorNeutralStroke2}`,
            marginLeft: 0,
            paddingLeft: '16px',
            color: tokens.colorNeutralForeground2,
        },
        '& strong': {
            fontWeight: tokens.fontWeightBold,
        },
        '& em': {
            fontStyle: 'italic',
        },
        '& u': {
            textDecoration: 'underline',
        },
        '& s': {
            textDecoration: 'line-through',
        },
    },
    placeholder: {
        position: 'absolute',
        top: '12px',
        left: '16px',
        color: tokens.colorNeutralForeground3,
        pointerEvents: 'none',
        userSelect: 'none',
        fontFamily: tokens.fontFamilyBase,
        fontSize: tokens.fontSizeBase300,
    },
    loading: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        height: '100%',
        minHeight: '200px',
    },
});
// ─────────────────────────────────────────────────────────────────────────────
// Lexical Configuration
// ─────────────────────────────────────────────────────────────────────────────
const theme = {
    paragraph: 'editor-paragraph',
    heading: {
        h1: 'editor-heading-h1',
        h2: 'editor-heading-h2',
        h3: 'editor-heading-h3',
    },
    list: {
        ul: 'editor-list-ul',
        ol: 'editor-list-ol',
        listitem: 'editor-list-item',
    },
    text: {
        bold: 'editor-text-bold',
        italic: 'editor-text-italic',
        underline: 'editor-text-underline',
        strikethrough: 'editor-text-strikethrough',
    },
    link: 'editor-link',
    quote: 'editor-quote',
};
function onError(error) {
    console.error('[RichTextEditor] Lexical error:', error);
}
function InitialContentPlugin({ html }) {
    const [editor] = useLexicalComposerContext();
    const hasInitialized = React.useRef(false);
    useEffect(() => {
        if (html && !hasInitialized.current) {
            hasInitialized.current = true;
            editor.update(() => {
                const parser = new DOMParser();
                const dom = parser.parseFromString(html, 'text/html');
                const nodes = $generateNodesFromDOM(editor, dom);
                const root = $getRoot();
                root.clear();
                $insertNodes(nodes);
            });
        }
    }, [editor, html]);
    return null;
}
function EditorRefPlugin({ editorRef }) {
    const [editor] = useLexicalComposerContext();
    useEffect(() => {
        editorRef.current = editor;
    }, [editor, editorRef]);
    return null;
}
// ─────────────────────────────────────────────────────────────────────────────
// Main Component
// ─────────────────────────────────────────────────────────────────────────────
export const RichTextEditor = forwardRef(function RichTextEditor(props, ref) {
    const { value, onChange, placeholder = 'Start typing...', readOnly = false, isDarkMode = false, hideToolbar = false, minHeight = 200, maxHeight, } = props;
    const styles = useStyles();
    const editorRef = React.useRef(null);
    const [isReady, setIsReady] = React.useState(false);
    // Editor configuration
    const initialConfig = React.useMemo(() => ({
        namespace: 'SpaarkeRichTextEditor',
        theme,
        onError,
        editable: !readOnly,
        nodes: [HeadingNode, QuoteNode, ListNode, ListItemNode, LinkNode],
    }), [readOnly]);
    // Handle content changes
    const handleChange = useCallback((editorState) => {
        editorState.read(() => {
            if (editorRef.current) {
                const html = $generateHtmlFromNodes(editorRef.current);
                onChange(html);
            }
        });
    }, [onChange]);
    // Expose ref methods
    useImperativeHandle(ref, () => ({
        focus: () => {
            editorRef.current?.focus();
        },
        getHtml: () => {
            let html = '';
            if (editorRef.current) {
                editorRef.current.getEditorState().read(() => {
                    html = $generateHtmlFromNodes(editorRef.current);
                });
            }
            return html;
        },
        setHtml: (html) => {
            if (editorRef.current) {
                editorRef.current.update(() => {
                    const parser = new DOMParser();
                    const dom = parser.parseFromString(html, 'text/html');
                    const nodes = $generateNodesFromDOM(editorRef.current, dom);
                    const root = $getRoot();
                    root.clear();
                    $insertNodes(nodes);
                });
            }
        },
        clear: () => {
            if (editorRef.current) {
                editorRef.current.update(() => {
                    const root = $getRoot();
                    root.clear();
                });
            }
        },
        insertAtCursor: (content, contentType) => {
            if (!editorRef.current) {
                return;
            }
            editorRef.current.update(() => {
                if (contentType === 'html') {
                    // Parse HTML and insert the resulting Lexical nodes.
                    // $insertNodes handles selection: if a RangeSelection is active
                    // and non-collapsed, it replaces the selection; if collapsed
                    // (cursor only) it inserts at the cursor position.
                    const parser = new DOMParser();
                    const dom = parser.parseFromString(content, 'text/html');
                    const nodes = $generateNodesFromDOM(editorRef.current, dom);
                    if (nodes.length > 0) {
                        const selection = $getSelection();
                        if ($isRangeSelection(selection)) {
                            // Insert at/replace current selection
                            selection.insertNodes(nodes);
                        }
                        else {
                            // No selection — append to document root
                            const root = $getRoot();
                            const paragraph = $createParagraphNode();
                            root.append(paragraph);
                            $insertNodes(nodes);
                        }
                    }
                }
                else {
                    // Plain text insertion
                    const selection = $getSelection();
                    if ($isRangeSelection(selection)) {
                        // insertText replaces any non-collapsed selection and inserts
                        // at cursor for collapsed selections — single Lexical API handles both.
                        selection.insertText(content);
                    }
                    else {
                        // No selection — append a new paragraph with the text
                        const root = $getRoot();
                        const paragraph = $createParagraphNode();
                        const textNode = $createTextNode(content);
                        paragraph.append(textNode);
                        root.append(paragraph);
                    }
                }
            }, 
            // discrete: true creates a separate undo history entry so the user
            // can Ctrl+Z to remove only this insert, independent of prior edits.
            { discrete: true });
        },
        beginStreamingInsert: (position) => {
            // Returns a stub handle; the real streaming is managed by StreamingInsertPlugin.
            // The RichTextEditor does not embed StreamingInsertPlugin by default —
            // consumers that need streaming should use StreamingInsertPlugin directly.
            // This method exists so useDocumentStreamConsumer can type-check against RichTextEditorRef.
            console.warn('[RichTextEditor] beginStreamingInsert called on base editor without StreamingInsertPlugin. ' +
                `Position: ${position}. Use StreamingInsertPlugin for streaming support.`);
            return {
                startStream: () => { },
                insertToken: () => { },
                endStream: () => { },
            };
        },
        appendStreamToken: (_handle, _token) => {
            // Delegated to the StreamingInsertHandle returned by beginStreamingInsert.
            // This method is a pass-through; real token insertion is handled by StreamingInsertPlugin.
            _handle.insertToken(_token);
        },
        endStreamingInsert: (handle, cancelled) => {
            handle.endStream(cancelled);
        },
    }), []);
    // Set ready state after mount
    useEffect(() => {
        setIsReady(true);
    }, []);
    const containerClass = isDarkMode ? `${styles.container} ${styles.containerDark}` : styles.container;
    const editorStyle = {
        minHeight: `${minHeight}px`,
        ...(maxHeight && { maxHeight: `${maxHeight}px` }),
    };
    if (!isReady) {
        return (React.createElement("div", { className: styles.loading },
            React.createElement(Spinner, { size: "medium", label: "Loading editor..." })));
    }
    return (React.createElement("div", { className: containerClass },
        React.createElement(LexicalComposer, { initialConfig: initialConfig },
            !hideToolbar && React.createElement(ToolbarPlugin, { isDarkMode: isDarkMode }),
            React.createElement("div", { className: styles.editorContainer, style: editorStyle },
                React.createElement(RichTextPlugin, { contentEditable: React.createElement(ContentEditable, { className: styles.contentEditable }), placeholder: React.createElement("div", { className: styles.placeholder }, placeholder), ErrorBoundary: LexicalErrorBoundary }),
                React.createElement(HistoryPlugin, null),
                React.createElement(ListPlugin, null),
                React.createElement(OnChangePlugin, { onChange: handleChange }),
                React.createElement(InitialContentPlugin, { html: value }),
                React.createElement(EditorRefPlugin, { editorRef: editorRef })))));
});
export default RichTextEditor;
//# sourceMappingURL=RichTextEditor.js.map