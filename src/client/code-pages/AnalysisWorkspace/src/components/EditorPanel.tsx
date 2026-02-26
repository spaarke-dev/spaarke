/**
 * EditorPanel Component
 *
 * Left panel of the AnalysisWorkspace 2-panel layout. Hosts the RichTextEditor
 * (Lexical-based) from @spaarke/ui-components for viewing and editing analysis
 * output. This is the streaming write target where SprkChat writes token-by-token.
 *
 * Task 062: Toolbar with Save, Export, Copy, Undo/Redo (replaces PH-061-A).
 * Task 064: Selection broadcast via SprkChatBridge (useSelectionBroadcast).
 * Task 065: Analysis loading and content population from BFF API.
 *
 * @see ADR-012 - Shared component library (import from @spaarke/ui-components)
 * @see ADR-021 - Fluent UI v9 design system
 */

import { forwardRef } from "react";
import { makeStyles, Spinner, Text, tokens } from "@fluentui/react-components";
import { RichTextEditor } from "@spaarke/ui-components";
import type { RichTextEditorRef } from "@spaarke/ui-components";
import { AnalysisToolbar } from "./AnalysisToolbar";
import type { SaveState, ExportState, ExportFormat } from "../types";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface EditorPanelProps {
    /** HTML content to display in the editor */
    value: string;
    /** Callback when editor content changes (HTML string) */
    onChange: (html: string) => void;
    /** Whether the editor is in read-only mode */
    readOnly?: boolean;
    /** Placeholder text when the editor is empty */
    placeholder?: string;
    /** Whether the analysis content is currently loading */
    isLoading?: boolean;

    // ---- Toolbar props (task 062) ----
    /** Current auto-save state */
    saveState?: SaveState;
    /** Force save callback (Ctrl+S) */
    onForceSave?: () => void;
    /** Save error message */
    saveError?: string | null;
    /** Current export state */
    exportState?: ExportState;
    /** Export callback */
    onExport?: (format: ExportFormat) => void;
    /** Undo callback */
    onUndo?: () => void;
    /** Redo callback */
    onRedo?: () => void;
    /** Whether undo is available */
    canUndo?: boolean;
    /** Whether redo is available */
    canRedo?: boolean;
    /** History stack length */
    historyLength?: number;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
    root: {
        display: "flex",
        flexDirection: "column",
        height: "100%",
        overflow: "hidden",
        backgroundColor: tokens.colorNeutralBackground1,
    },
    header: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        paddingTop: tokens.spacingVerticalS,
        paddingBottom: tokens.spacingVerticalS,
        paddingLeft: tokens.spacingHorizontalM,
        paddingRight: tokens.spacingHorizontalM,
        borderBottom: `1px solid ${tokens.colorNeutralStroke1}`,
        backgroundColor: tokens.colorNeutralBackground3,
        minHeight: "40px",
        flexShrink: 0,
    },
    headerTitle: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
    },
    toolbar: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalXS,
    },
    editorContainer: {
        flex: 1,
        overflow: "auto",
        padding: tokens.spacingHorizontalM,
    },
    loadingContainer: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        flex: 1,
        gap: tokens.spacingVerticalM,
    },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const EditorPanel = forwardRef<RichTextEditorRef, EditorPanelProps>(
    function EditorPanel(
        {
            value,
            onChange,
            readOnly = false,
            placeholder = "Analysis output will appear here...",
            isLoading = false,
            // Toolbar props (task 062)
            saveState = "idle",
            onForceSave,
            saveError,
            exportState = "idle",
            onExport,
            onUndo,
            onRedo,
            canUndo = false,
            canRedo = false,
            historyLength = 0,
        },
        ref,
    ): JSX.Element {
        const styles = useStyles();

        // Determine if toolbar should render (all required callbacks provided)
        const hasToolbar = !!(onForceSave && onExport && onUndo && onRedo);

        /**
         * Get current editor HTML via the ref. Used by the Copy button.
         */
        const getEditorHtml = (): string => {
            if (ref && typeof ref === "object" && ref.current) {
                return ref.current.getHtml();
            }
            return value;
        };

        return (
            <div className={styles.root}>
                {/* Panel header with toolbar */}
                <div className={styles.header}>
                    <div className={styles.headerTitle}>
                        <Text weight="semibold">ANALYSIS OUTPUT</Text>
                    </div>
                    <div className={styles.toolbar}>
                        {hasToolbar ? (
                            <AnalysisToolbar
                                saveState={saveState}
                                onForceSave={onForceSave}
                                saveError={saveError}
                                exportState={exportState}
                                onExport={onExport}
                                getEditorHtml={getEditorHtml}
                                onUndo={onUndo}
                                onRedo={onRedo}
                                canUndo={canUndo}
                                canRedo={canRedo}
                                historyLength={historyLength}
                            />
                        ) : null}
                    </div>
                </div>

                {/* Loading state (task 065) */}
                {isLoading ? (
                    <div className={styles.loadingContainer}>
                        <Spinner size="medium" label="Loading analysis..." />
                    </div>
                ) : (
                    /* Editor area */
                    <div className={styles.editorContainer}>
                        <RichTextEditor
                            ref={ref}
                            value={value}
                            onChange={onChange}
                            readOnly={readOnly}
                            placeholder={placeholder}
                        />
                    </div>
                )}
            </div>
        );
    },
);
