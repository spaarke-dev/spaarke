/**
 * Monaco Editor Component
 *
 * Rich markdown editor for the Analysis Working Document.
 * Uses @monaco-editor/react for VS Code-like editing experience.
 *
 * Features:
 * - Markdown syntax highlighting
 * - Word wrap for readability
 * - Theme integration (light/dark)
 * - Keyboard shortcuts
 * - Auto-resize
 *
 * Task 054: Integrate Monaco Editor for Working Document
 */

import * as React from "react";
import Editor, { OnMount, OnChange } from "@monaco-editor/react";
import { makeStyles, tokens, Spinner } from "@fluentui/react-components";
import { logInfo } from "../utils/logger";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface IMonacoEditorProps {
    /** Current document content */
    value: string;
    /** Callback when content changes */
    onChange: (value: string) => void;
    /** Language for syntax highlighting (default: markdown) */
    language?: string;
    /** Read-only mode */
    readOnly?: boolean;
    /** Use dark theme */
    isDarkMode?: boolean;
    /** Placeholder text when empty */
    placeholder?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
    container: {
        width: "100%",
        height: "100%",
        position: "relative" as const
    },
    loading: {
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        height: "100%",
        backgroundColor: tokens.colorNeutralBackground1
    },
    placeholder: {
        position: "absolute" as const,
        top: "12px",
        left: "64px", // Account for line numbers
        color: tokens.colorNeutralForeground3,
        pointerEvents: "none" as const,
        zIndex: 1,
        fontFamily: "Consolas, 'Courier New', monospace",
        fontSize: "14px"
    }
});

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export const MonacoEditor: React.FC<IMonacoEditorProps> = ({
    value,
    onChange,
    language = "markdown",
    readOnly = false,
    isDarkMode = false,
    placeholder = "Start typing..."
}) => {
    const styles = useStyles();
    const [isLoading, setIsLoading] = React.useState(true);
    const editorRef = React.useRef<unknown>(null);

    // Handle editor mount
    const handleEditorMount: OnMount = (editor, monaco) => {
        editorRef.current = editor;
        setIsLoading(false);
        logInfo("MonacoEditor", "Editor mounted");

        // Configure markdown-specific settings
        monaco.languages.setLanguageConfiguration("markdown", {
            wordPattern: /(-?\d*\.\d\w*)|([^\`\~\!\@\#\%\^\&\*\(\)\-\=\+\[\{\]\}\\\|\;\:\'\"\,\.\<\>\/\?\s]+)/g
        });

        // Add custom keyboard shortcuts
        editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyS, () => {
            logInfo("MonacoEditor", "Save shortcut triggered");
            // Save is handled by parent via auto-save
        });

        // Focus the editor
        editor.focus();
    };

    // Handle content changes
    const handleChange: OnChange = (newValue) => {
        onChange(newValue || "");
    };

    // Determine theme
    const theme = isDarkMode ? "vs-dark" : "vs-light";

    return (
        <div className={styles.container}>
            {/* Loading spinner */}
            {isLoading && (
                <div className={styles.loading}>
                    <Spinner size="medium" label="Loading editor..." />
                </div>
            )}

            {/* Placeholder text */}
            {!isLoading && !value && (
                <div className={styles.placeholder}>{placeholder}</div>
            )}

            {/* Monaco Editor */}
            <Editor
                height="100%"
                language={language}
                value={value}
                theme={theme}
                onChange={handleChange}
                onMount={handleEditorMount}
                loading={null} // We handle loading ourselves
                options={{
                    // Layout
                    minimap: { enabled: false },
                    scrollBeyondLastLine: false,
                    automaticLayout: true,

                    // Text
                    wordWrap: "on",
                    wrappingIndent: "same",
                    fontSize: 14,
                    fontFamily: "Consolas, 'Courier New', monospace",
                    lineHeight: 1.6,

                    // Line numbers
                    lineNumbers: "on",
                    lineNumbersMinChars: 3,
                    glyphMargin: false,
                    folding: true,

                    // Editing
                    readOnly: readOnly,
                    quickSuggestions: false,
                    suggestOnTriggerCharacters: false,
                    acceptSuggestionOnEnter: "off",
                    tabSize: 2,
                    insertSpaces: true,

                    // Scrolling
                    scrollbar: {
                        vertical: "auto",
                        horizontal: "hidden",
                        verticalScrollbarSize: 10
                    },

                    // Selection
                    selectionHighlight: true,
                    occurrencesHighlight: "off",
                    renderLineHighlight: "line",

                    // Formatting
                    formatOnPaste: false,
                    formatOnType: false,

                    // Accessibility
                    accessibilitySupport: "auto",

                    // Cursor
                    cursorBlinking: "smooth",
                    cursorSmoothCaretAnimation: "on",

                    // Padding
                    padding: { top: 12, bottom: 12 }
                }}
            />
        </div>
    );
};

export default MonacoEditor;
