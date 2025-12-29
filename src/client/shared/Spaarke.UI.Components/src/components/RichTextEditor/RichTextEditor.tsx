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

import * as React from "react";
import { useCallback, useEffect, useImperativeHandle, forwardRef } from "react";
import { makeStyles, tokens, Spinner } from "@fluentui/react-components";

// Lexical core
import { LexicalComposer } from "@lexical/react/LexicalComposer";
import { RichTextPlugin } from "@lexical/react/LexicalRichTextPlugin";
import { ContentEditable } from "@lexical/react/LexicalContentEditable";
import { HistoryPlugin } from "@lexical/react/LexicalHistoryPlugin";
import { OnChangePlugin } from "@lexical/react/LexicalOnChangePlugin";
import { LexicalErrorBoundary } from "@lexical/react/LexicalErrorBoundary";
import { useLexicalComposerContext } from "@lexical/react/LexicalComposerContext";
import { ListPlugin } from "@lexical/react/LexicalListPlugin";

// Lexical nodes
import { HeadingNode, QuoteNode } from "@lexical/rich-text";
import { ListNode, ListItemNode } from "@lexical/list";
import { LinkNode } from "@lexical/link";

// Lexical utilities
import { $generateHtmlFromNodes, $generateNodesFromDOM } from "@lexical/html";
import { $getRoot, $insertNodes, EditorState, LexicalEditor } from "lexical";

// Local components
import { ToolbarPlugin } from "./plugins/ToolbarPlugin";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface IRichTextEditorProps {
    /** Initial HTML content */
    value: string;
    /** Callback when content changes (HTML string) */
    onChange: (html: string) => void;
    /** Placeholder text when empty */
    placeholder?: string;
    /** Read-only mode */
    readOnly?: boolean;
    /** Use dark theme */
    isDarkMode?: boolean;
    /** Disable the toolbar */
    hideToolbar?: boolean;
    /** Minimum height in pixels */
    minHeight?: number;
    /** Maximum height in pixels (scrolls beyond) */
    maxHeight?: number;
}

export interface RichTextEditorRef {
    /** Focus the editor */
    focus: () => void;
    /** Get current HTML content */
    getHtml: () => string;
    /** Set HTML content programmatically */
    setHtml: (html: string) => void;
    /** Clear all content */
    clear: () => void;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
    container: {
        display: "flex",
        flexDirection: "column",
        width: "100%",
        height: "100%",
        border: `1px solid ${tokens.colorNeutralStroke1}`,
        borderRadius: tokens.borderRadiusMedium,
        backgroundColor: tokens.colorNeutralBackground1,
        overflow: "hidden"
    },
    containerDark: {
        backgroundColor: tokens.colorNeutralBackground3
    },
    editorContainer: {
        flex: 1,
        position: "relative" as const,
        overflow: "auto"
    },
    contentEditable: {
        outline: "none",
        padding: "12px 16px",
        minHeight: "200px",
        fontFamily: tokens.fontFamilyBase,
        fontSize: tokens.fontSizeBase300,
        lineHeight: tokens.lineHeightBase300,
        color: tokens.colorNeutralForeground1,
        "& p": {
            margin: "0 0 8px 0"
        },
        "& h1": {
            fontSize: tokens.fontSizeBase600,
            fontWeight: tokens.fontWeightSemibold,
            margin: "16px 0 8px 0"
        },
        "& h2": {
            fontSize: tokens.fontSizeBase500,
            fontWeight: tokens.fontWeightSemibold,
            margin: "12px 0 8px 0"
        },
        "& h3": {
            fontSize: tokens.fontSizeBase400,
            fontWeight: tokens.fontWeightSemibold,
            margin: "8px 0 8px 0"
        },
        "& ul, & ol": {
            margin: "8px 0",
            paddingLeft: "24px"
        },
        "& li": {
            marginBottom: "4px"
        },
        "& a": {
            color: tokens.colorBrandForegroundLink,
            textDecoration: "underline"
        },
        "& blockquote": {
            borderLeft: `3px solid ${tokens.colorNeutralStroke2}`,
            marginLeft: 0,
            paddingLeft: "16px",
            color: tokens.colorNeutralForeground2
        },
        "& strong": {
            fontWeight: tokens.fontWeightBold
        },
        "& em": {
            fontStyle: "italic"
        },
        "& u": {
            textDecoration: "underline"
        },
        "& s": {
            textDecoration: "line-through"
        }
    },
    placeholder: {
        position: "absolute" as const,
        top: "12px",
        left: "16px",
        color: tokens.colorNeutralForeground3,
        pointerEvents: "none" as const,
        userSelect: "none",
        fontFamily: tokens.fontFamilyBase,
        fontSize: tokens.fontSizeBase300
    },
    loading: {
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        height: "100%",
        minHeight: "200px"
    }
});

// ─────────────────────────────────────────────────────────────────────────────
// Lexical Configuration
// ─────────────────────────────────────────────────────────────────────────────

const theme = {
    paragraph: "editor-paragraph",
    heading: {
        h1: "editor-heading-h1",
        h2: "editor-heading-h2",
        h3: "editor-heading-h3"
    },
    list: {
        ul: "editor-list-ul",
        ol: "editor-list-ol",
        listitem: "editor-list-item"
    },
    text: {
        bold: "editor-text-bold",
        italic: "editor-text-italic",
        underline: "editor-text-underline",
        strikethrough: "editor-text-strikethrough"
    },
    link: "editor-link",
    quote: "editor-quote"
};

function onError(error: Error): void {
    console.error("[RichTextEditor] Lexical error:", error);
}

// ─────────────────────────────────────────────────────────────────────────────
// Internal Plugins
// ─────────────────────────────────────────────────────────────────────────────

interface InitialContentPluginProps {
    html: string;
}

function InitialContentPlugin({ html }: InitialContentPluginProps): null {
    const [editor] = useLexicalComposerContext();
    const hasInitialized = React.useRef(false);

    useEffect(() => {
        if (html && !hasInitialized.current) {
            hasInitialized.current = true;
            editor.update(() => {
                const parser = new DOMParser();
                const dom = parser.parseFromString(html, "text/html");
                const nodes = $generateNodesFromDOM(editor, dom);
                const root = $getRoot();
                root.clear();
                $insertNodes(nodes);
            });
        }
    }, [editor, html]);

    return null;
}

interface EditorRefPluginProps {
    editorRef: React.MutableRefObject<LexicalEditor | null>;
}

function EditorRefPlugin({ editorRef }: EditorRefPluginProps): null {
    const [editor] = useLexicalComposerContext();

    useEffect(() => {
        editorRef.current = editor;
    }, [editor, editorRef]);

    return null;
}

// ─────────────────────────────────────────────────────────────────────────────
// Main Component
// ─────────────────────────────────────────────────────────────────────────────

export const RichTextEditor = forwardRef<RichTextEditorRef, IRichTextEditorProps>(
    function RichTextEditor(props, ref) {
        const {
            value,
            onChange,
            placeholder = "Start typing...",
            readOnly = false,
            isDarkMode = false,
            hideToolbar = false,
            minHeight = 200,
            maxHeight
        } = props;

        const styles = useStyles();
        const editorRef = React.useRef<LexicalEditor | null>(null);
        const [isReady, setIsReady] = React.useState(false);

        // Editor configuration
        const initialConfig = React.useMemo(() => ({
            namespace: "SpaarkeRichTextEditor",
            theme,
            onError,
            editable: !readOnly,
            nodes: [
                HeadingNode,
                QuoteNode,
                ListNode,
                ListItemNode,
                LinkNode
            ]
        }), [readOnly]);

        // Handle content changes
        const handleChange = useCallback((editorState: EditorState) => {
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
                let html = "";
                if (editorRef.current) {
                    editorRef.current.getEditorState().read(() => {
                        html = $generateHtmlFromNodes(editorRef.current!);
                    });
                }
                return html;
            },
            setHtml: (html: string) => {
                if (editorRef.current) {
                    editorRef.current.update(() => {
                        const parser = new DOMParser();
                        const dom = parser.parseFromString(html, "text/html");
                        const nodes = $generateNodesFromDOM(editorRef.current!, dom);
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
            }
        }), []);

        // Set ready state after mount
        useEffect(() => {
            setIsReady(true);
        }, []);

        const containerClass = isDarkMode
            ? `${styles.container} ${styles.containerDark}`
            : styles.container;

        const editorStyle: React.CSSProperties = {
            minHeight: `${minHeight}px`,
            ...(maxHeight && { maxHeight: `${maxHeight}px` })
        };

        if (!isReady) {
            return (
                <div className={styles.loading}>
                    <Spinner size="medium" label="Loading editor..." />
                </div>
            );
        }

        return (
            <div className={containerClass}>
                <LexicalComposer initialConfig={initialConfig}>
                    {!hideToolbar && <ToolbarPlugin isDarkMode={isDarkMode} />}
                    <div className={styles.editorContainer} style={editorStyle}>
                        <RichTextPlugin
                            contentEditable={
                                <ContentEditable className={styles.contentEditable} />
                            }
                            placeholder={
                                <div className={styles.placeholder}>{placeholder}</div>
                            }
                            ErrorBoundary={LexicalErrorBoundary}
                        />
                        <HistoryPlugin />
                        <ListPlugin />
                        <OnChangePlugin onChange={handleChange} />
                        <InitialContentPlugin html={value} />
                        <EditorRefPlugin editorRef={editorRef} />
                    </div>
                </LexicalComposer>
            </div>
        );
    }
);

export default RichTextEditor;
