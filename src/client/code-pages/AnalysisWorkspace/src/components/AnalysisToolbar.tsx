/**
 * AnalysisToolbar Component
 *
 * Toolbar for the EditorPanel providing Save, Export, Copy, Undo, and Redo
 * functionality. Uses Fluent UI v9 Toolbar and ToolbarButton components with
 * tooltips showing keyboard shortcuts.
 *
 * Save button shows contextual status: idle, saving (spinner), saved (checkmark),
 * error (warning). Undo/Redo buttons are disabled at stack boundaries.
 *
 * Keyboard shortcuts:
 *   Ctrl+S       - Force save
 *   Ctrl+Z       - Undo (document-level, via useDocumentHistory)
 *   Ctrl+Y       - Redo
 *   Ctrl+Shift+C - Copy analysis to clipboard
 *
 * @see ADR-021 - Fluent UI v9 design system
 * @see ADR-012 - Shared component library (useDocumentHistory)
 */

import { useCallback, useEffect } from "react";
import {
    Toolbar,
    ToolbarButton,
    ToolbarDivider,
    Tooltip,
    Spinner,
    makeStyles,
    tokens,
} from "@fluentui/react-components";
import {
    SaveRegular,
    ArrowExportRegular,
    CopyRegular,
    ArrowUndoRegular,
    ArrowRedoRegular,
    CheckmarkRegular,
    WarningRegular,
} from "@fluentui/react-icons";
import type { SaveState, ExportState, ExportFormat } from "../types";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface AnalysisToolbarProps {
    // ---- Save ----
    /** Current save state: idle | saving | saved | error */
    saveState: SaveState;
    /** Force save callback (Ctrl+S) */
    onForceSave: () => void;
    /** Error message from the last failed save */
    saveError?: string | null;

    // ---- Export ----
    /** Current export state */
    exportState: ExportState;
    /** Trigger export to a given format */
    onExport: (format: ExportFormat) => void;

    // ---- Copy ----
    /** Callback to get the current editor HTML content */
    getEditorHtml: () => string;

    // ---- Undo/Redo (from useDocumentHistory) ----
    /** Execute undo */
    onUndo: () => void;
    /** Execute redo */
    onRedo: () => void;
    /** Whether undo is available */
    canUndo: boolean;
    /** Whether redo is available */
    canRedo: boolean;
    /** Number of snapshots in the history stack */
    historyLength: number;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
    toolbar: {
        paddingTop: 0,
        paddingBottom: 0,
        minHeight: "32px",
    },
    saveStatusIcon: {
        display: "flex",
        alignItems: "center",
    },
    spinnerIcon: {
        width: "16px",
        height: "16px",
    },
    savedIcon: {
        color: tokens.colorPaletteGreenForeground1,
    },
    errorIcon: {
        color: tokens.colorPaletteRedForeground1,
    },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export function AnalysisToolbar({
    saveState,
    onForceSave,
    saveError,
    exportState,
    onExport,
    getEditorHtml,
    onUndo,
    onRedo,
    canUndo,
    canRedo,
    historyLength,
}: AnalysisToolbarProps): JSX.Element {
    const styles = useStyles();

    // ---- Copy to Clipboard ----
    const handleCopy = useCallback(async () => {
        const html = getEditorHtml();
        if (!html) return;

        try {
            // Try modern Clipboard API with both MIME types
            if (navigator.clipboard && typeof ClipboardItem !== "undefined") {
                const htmlBlob = new Blob([html], { type: "text/html" });
                const textBlob = new Blob([stripHtml(html)], { type: "text/plain" });
                const item = new ClipboardItem({
                    "text/html": htmlBlob,
                    "text/plain": textBlob,
                });
                await navigator.clipboard.write([item]);
            } else {
                // Fallback: copy plain text only
                const plainText = stripHtml(html);
                await navigator.clipboard.writeText(plainText);
            }
        } catch {
            // Last resort fallback: execCommand (deprecated but widely supported)
            try {
                const textarea = document.createElement("textarea");
                textarea.value = stripHtml(html);
                textarea.style.position = "fixed";
                textarea.style.opacity = "0";
                document.body.appendChild(textarea);
                textarea.select();
                document.execCommand("copy");
                document.body.removeChild(textarea);
            } catch {
                console.error("[AnalysisToolbar] Copy to clipboard failed");
            }
        }
    }, [getEditorHtml]);

    // ---- Keyboard Shortcuts ----
    useEffect(() => {
        const handleKeyDown = (event: KeyboardEvent) => {
            const isCtrl = event.ctrlKey || event.metaKey;

            // Ctrl+S — Force save
            if (isCtrl && event.key === "s") {
                event.preventDefault();
                onForceSave();
                return;
            }

            // Ctrl+Shift+C — Copy analysis to clipboard
            if (isCtrl && event.shiftKey && event.key === "C") {
                event.preventDefault();
                handleCopy();
                return;
            }

            // Ctrl+Z — Undo (document-level)
            // Only fire when the event target is not inside a contentEditable
            // (the editor has its own Lexical undo for text-level changes)
            if (isCtrl && !event.shiftKey && event.key === "z") {
                const target = event.target as HTMLElement | null;
                if (target && !isInsideContentEditable(target)) {
                    event.preventDefault();
                    onUndo();
                    return;
                }
            }

            // Ctrl+Y — Redo (document-level)
            if (isCtrl && event.key === "y") {
                const target = event.target as HTMLElement | null;
                if (target && !isInsideContentEditable(target)) {
                    event.preventDefault();
                    onRedo();
                    return;
                }
            }
        };

        document.addEventListener("keydown", handleKeyDown);
        return () => document.removeEventListener("keydown", handleKeyDown);
    }, [onForceSave, handleCopy, onUndo, onRedo]);

    // ---- Save button icon based on state ----
    const saveIcon = getSaveIcon(saveState, styles);
    const saveTooltip = getSaveTooltip(saveState, saveError ?? null);

    return (
        <Toolbar className={styles.toolbar} size="small">
            {/* Save */}
            <Tooltip content={saveTooltip} relationship="label">
                <ToolbarButton
                    icon={saveIcon}
                    onClick={onForceSave}
                    disabled={saveState === "saving"}
                    aria-label={saveTooltip}
                />
            </Tooltip>

            {/* Export to Word */}
            <Tooltip content="Export to Word" relationship="label">
                <ToolbarButton
                    icon={<ArrowExportRegular />}
                    onClick={() => onExport("docx")}
                    disabled={exportState === "exporting"}
                    aria-label="Export to Word"
                />
            </Tooltip>

            {/* Copy */}
            <Tooltip content="Copy to clipboard (Ctrl+Shift+C)" relationship="label">
                <ToolbarButton
                    icon={<CopyRegular />}
                    onClick={handleCopy}
                    aria-label="Copy to clipboard"
                />
            </Tooltip>

            <ToolbarDivider />

            {/* Undo */}
            <Tooltip
                content={`Undo (Ctrl+Z)${historyLength > 0 ? ` — ${historyLength} snapshots` : ""}`}
                relationship="label"
            >
                <ToolbarButton
                    icon={<ArrowUndoRegular />}
                    onClick={onUndo}
                    disabled={!canUndo}
                    aria-label="Undo"
                />
            </Tooltip>

            {/* Redo */}
            <Tooltip content="Redo (Ctrl+Y)" relationship="label">
                <ToolbarButton
                    icon={<ArrowRedoRegular />}
                    onClick={onRedo}
                    disabled={!canRedo}
                    aria-label="Redo"
                />
            </Tooltip>
        </Toolbar>
    );
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * Get the appropriate icon element for the current save state.
 */
function getSaveIcon(
    state: SaveState,
    styles: ReturnType<typeof useStyles>
): JSX.Element {
    switch (state) {
        case "saving":
            return (
                <span className={styles.saveStatusIcon}>
                    <Spinner className={styles.spinnerIcon} size="tiny" />
                </span>
            );
        case "saved":
            return <CheckmarkRegular className={styles.savedIcon} />;
        case "error":
            return <WarningRegular className={styles.errorIcon} />;
        case "idle":
        default:
            return <SaveRegular />;
    }
}

/**
 * Get the tooltip text for the save button based on current state.
 */
function getSaveTooltip(state: SaveState, error: string | null): string {
    switch (state) {
        case "saving":
            return "Saving...";
        case "saved":
            return "Saved (Ctrl+S)";
        case "error":
            return `Save failed: ${error ?? "Unknown error"} (Ctrl+S to retry)`;
        case "idle":
        default:
            return "Save (Ctrl+S)";
    }
}

/**
 * Strip HTML tags from a string to produce plain text.
 */
function stripHtml(html: string): string {
    const div = document.createElement("div");
    div.innerHTML = html;
    return div.textContent ?? div.innerText ?? "";
}

/**
 * Check if a DOM element is inside a contentEditable container.
 * Used to avoid intercepting Ctrl+Z/Y when the editor has focus.
 */
function isInsideContentEditable(element: HTMLElement): boolean {
    let current: HTMLElement | null = element;
    while (current) {
        if (current.contentEditable === "true") {
            return true;
        }
        current = current.parentElement;
    }
    return false;
}
