/**
 * Integration tests for AnalysisToolbar component
 *
 * Tests the toolbar providing Save, Export, Copy, Undo, and Redo
 * functionality. Covers:
 *   - Save button click triggers onForceSave
 *   - Export button click triggers onExport("docx")
 *   - Copy button copies editor HTML to clipboard
 *   - Undo button calls onUndo
 *   - Redo button calls onRedo
 *   - Undo/Redo disabled at stack boundaries
 *   - Keyboard shortcuts (Ctrl+S, Ctrl+Z, Ctrl+Y, Ctrl+Shift+C)
 *
 * @see components/AnalysisToolbar.tsx
 * @see ADR-021 - Fluent UI v9 design system
 */

import React from "react";
import { render, screen, fireEvent, act } from "@testing-library/react";
import { FluentProvider, webLightTheme } from "@fluentui/react-components";
import { AnalysisToolbar } from "../components/AnalysisToolbar";
import type { AnalysisToolbarProps } from "../components/AnalysisToolbar";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function renderToolbar(overrides: Partial<AnalysisToolbarProps> = {}) {
    const defaultProps: AnalysisToolbarProps = {
        saveState: "idle",
        onForceSave: jest.fn(),
        saveError: null,
        exportState: "idle",
        onExport: jest.fn(),
        getEditorHtml: jest.fn(() => "<p>Test content</p>"),
        onUndo: jest.fn(),
        onRedo: jest.fn(),
        canUndo: true,
        canRedo: true,
        historyLength: 3,
        ...overrides,
    };

    const result = render(
        <FluentProvider theme={webLightTheme}>
            <AnalysisToolbar {...defaultProps} />
        </FluentProvider>
    );

    return { ...result, props: defaultProps };
}

/**
 * Dispatch a keyboard event on the document to simulate keyboard shortcuts.
 */
function pressKeyboardShortcut(key: string, options: {
    ctrlKey?: boolean;
    shiftKey?: boolean;
    metaKey?: boolean;
} = {}) {
    const event = new KeyboardEvent("keydown", {
        key,
        ctrlKey: options.ctrlKey ?? false,
        shiftKey: options.shiftKey ?? false,
        metaKey: options.metaKey ?? false,
        bubbles: true,
        cancelable: true,
    });
    document.dispatchEvent(event);
}

// ---------------------------------------------------------------------------
// Test Suite
// ---------------------------------------------------------------------------

describe("AnalysisToolbar", () => {
    // -----------------------------------------------------------------------
    // 1. Save Button
    // -----------------------------------------------------------------------

    it("saveButton_Clicked_CallsOnForceSave", () => {
        // Arrange
        const { props } = renderToolbar();

        // Act: find the save button by aria-label and click it
        const saveButton = screen.getByRole("button", { name: /save/i });
        fireEvent.click(saveButton);

        // Assert
        expect(props.onForceSave).toHaveBeenCalledTimes(1);
    });

    it("saveButton_SavingState_DisablesButton", () => {
        // Arrange
        renderToolbar({ saveState: "saving" });

        // Act & Assert: save button should be disabled during saving
        const saveButton = screen.getByRole("button", { name: /saving/i });
        expect(saveButton).toBeDisabled();
    });

    // -----------------------------------------------------------------------
    // 2. Export Button
    // -----------------------------------------------------------------------

    it("exportButton_Clicked_CallsOnExportWithDocx", () => {
        // Arrange
        const { props } = renderToolbar();

        // Act: find the Export to Word button and click it
        const exportButton = screen.getByRole("button", { name: /export to word/i });
        fireEvent.click(exportButton);

        // Assert
        expect(props.onExport).toHaveBeenCalledTimes(1);
        expect(props.onExport).toHaveBeenCalledWith("docx");
    });

    it("exportButton_ExportingState_DisablesButton", () => {
        // Arrange
        renderToolbar({ exportState: "exporting" });

        // Act & Assert
        const exportButton = screen.getByRole("button", { name: /export to word/i });
        expect(exportButton).toBeDisabled();
    });

    // -----------------------------------------------------------------------
    // 3. Copy Button
    // -----------------------------------------------------------------------

    it("copyButton_Clicked_CopiesEditorHtmlToClipboard", async () => {
        // Arrange: mock the clipboard API
        const writeTextMock = jest.fn().mockResolvedValue(undefined);
        Object.defineProperty(navigator, "clipboard", {
            value: { writeText: writeTextMock, write: jest.fn() },
            writable: true,
            configurable: true,
        });
        // ClipboardItem may not be available in jsdom, so test the fallback
        (globalThis as Record<string, unknown>).ClipboardItem = undefined;

        const getEditorHtml = jest.fn(() => "<p>HTML content to copy</p>");
        renderToolbar({ getEditorHtml });

        // Act
        const copyButton = screen.getByRole("button", { name: /copy to clipboard/i });
        await act(async () => {
            fireEvent.click(copyButton);
        });

        // Assert: getEditorHtml was called and clipboard was written to
        expect(getEditorHtml).toHaveBeenCalled();
        // The fallback uses writeText with plain text (stripped HTML)
        expect(writeTextMock).toHaveBeenCalledWith("HTML content to copy");
    });

    // -----------------------------------------------------------------------
    // 4. Undo Button
    // -----------------------------------------------------------------------

    it("undoButton_Clicked_CallsOnUndo", () => {
        // Arrange
        const { props } = renderToolbar({ canUndo: true });

        // Act
        const undoButton = screen.getByRole("button", { name: /undo/i });
        fireEvent.click(undoButton);

        // Assert
        expect(props.onUndo).toHaveBeenCalledTimes(1);
    });

    // -----------------------------------------------------------------------
    // 5. Redo Button
    // -----------------------------------------------------------------------

    it("redoButton_Clicked_CallsOnRedo", () => {
        // Arrange
        const { props } = renderToolbar({ canRedo: true });

        // Act
        const redoButton = screen.getByRole("button", { name: /redo/i });
        fireEvent.click(redoButton);

        // Assert
        expect(props.onRedo).toHaveBeenCalledTimes(1);
    });

    // -----------------------------------------------------------------------
    // 6. Boundary Disable
    // -----------------------------------------------------------------------

    it("undoRedoButtons_AtStackBoundaries_DisabledCorrectly", () => {
        // Arrange: no undo or redo available
        renderToolbar({ canUndo: false, canRedo: false, historyLength: 0 });

        // Act & Assert
        const undoButton = screen.getByRole("button", { name: /undo/i });
        const redoButton = screen.getByRole("button", { name: /redo/i });

        expect(undoButton).toBeDisabled();
        expect(redoButton).toBeDisabled();
    });

    it("undoButton_CanUndoTrue_EnabledCorrectly", () => {
        // Arrange
        renderToolbar({ canUndo: true, canRedo: false });

        // Act & Assert
        const undoButton = screen.getByRole("button", { name: /undo/i });
        const redoButton = screen.getByRole("button", { name: /redo/i });

        expect(undoButton).not.toBeDisabled();
        expect(redoButton).toBeDisabled();
    });

    // -----------------------------------------------------------------------
    // 7. Keyboard Shortcuts
    // -----------------------------------------------------------------------

    it("keyboardShortcut_CtrlS_CallsOnForceSave", () => {
        // Arrange
        const { props } = renderToolbar();

        // Act: simulate Ctrl+S
        act(() => {
            pressKeyboardShortcut("s", { ctrlKey: true });
        });

        // Assert
        expect(props.onForceSave).toHaveBeenCalledTimes(1);
    });

    it("keyboardShortcut_CtrlZ_CallsOnUndo_WhenNotInContentEditable", () => {
        // Arrange
        const { props } = renderToolbar();

        // Act: simulate Ctrl+Z on a non-contentEditable target
        const event = new KeyboardEvent("keydown", {
            key: "z",
            ctrlKey: true,
            bubbles: true,
            cancelable: true,
        });
        // Dispatch from document body (not inside a contenteditable)
        act(() => {
            document.dispatchEvent(event);
        });

        // Assert: onUndo called because target is NOT inside contentEditable
        expect(props.onUndo).toHaveBeenCalledTimes(1);
    });

    it("keyboardShortcut_CtrlY_CallsOnRedo_WhenNotInContentEditable", () => {
        // Arrange
        const { props } = renderToolbar();

        // Act: simulate Ctrl+Y
        act(() => {
            pressKeyboardShortcut("y", { ctrlKey: true });
        });

        // Assert
        expect(props.onRedo).toHaveBeenCalledTimes(1);
    });

    // -----------------------------------------------------------------------
    // 8. Copy with ClipboardItem API
    // -----------------------------------------------------------------------

    it("copyButton_WithClipboardItemAvailable_CopiesHtmlAndPlainText", async () => {
        // Arrange: mock the modern ClipboardItem API
        const writeMock = jest.fn().mockResolvedValue(undefined);
        Object.defineProperty(navigator, "clipboard", {
            value: { write: writeMock, writeText: jest.fn() },
            writable: true,
            configurable: true,
        });
        (globalThis as Record<string, unknown>).ClipboardItem = class MockClipboardItem {
            items: Record<string, Blob>;
            constructor(items: Record<string, Blob>) {
                this.items = items;
            }
        };

        const getEditorHtml = jest.fn(() => "<p>Rich content</p>");
        renderToolbar({ getEditorHtml });

        // Act
        const copyButton = screen.getByRole("button", { name: /copy to clipboard/i });
        await act(async () => {
            fireEvent.click(copyButton);
        });

        // Assert: clipboard.write was called with a ClipboardItem
        expect(writeMock).toHaveBeenCalledTimes(1);
        expect(writeMock).toHaveBeenCalledWith([expect.any(Object)]);

        // Cleanup
        delete (globalThis as Record<string, unknown>).ClipboardItem;
    });

    it("copyButton_ClipboardApiThrows_FallsBackToExecCommand", async () => {
        // Arrange: clipboard API throws, forcing execCommand fallback
        Object.defineProperty(navigator, "clipboard", {
            value: {
                writeText: jest.fn().mockRejectedValue(new Error("Not allowed")),
                write: jest.fn(),
            },
            writable: true,
            configurable: true,
        });
        (globalThis as Record<string, unknown>).ClipboardItem = undefined;

        // jsdom doesn't provide document.execCommand, so define it as a mock
        const execCommandMock = jest.fn().mockReturnValue(true);
        (document as Record<string, unknown>).execCommand = execCommandMock;

        const getEditorHtml = jest.fn(() => "<p>Fallback content</p>");
        renderToolbar({ getEditorHtml });

        // Act
        const copyButton = screen.getByRole("button", { name: /copy to clipboard/i });
        await act(async () => {
            fireEvent.click(copyButton);
        });

        // Assert: execCommand('copy') was called as fallback
        expect(execCommandMock).toHaveBeenCalledWith("copy");

        // Cleanup
        delete (document as Record<string, unknown>).execCommand;
    });

    it("copyButton_EmptyEditorHtml_DoesNotAttemptCopy", async () => {
        // Arrange: editor returns empty HTML
        const writeTextMock = jest.fn();
        Object.defineProperty(navigator, "clipboard", {
            value: { writeText: writeTextMock, write: jest.fn() },
            writable: true,
            configurable: true,
        });

        const getEditorHtml = jest.fn(() => "");
        renderToolbar({ getEditorHtml });

        // Act
        const copyButton = screen.getByRole("button", { name: /copy to clipboard/i });
        await act(async () => {
            fireEvent.click(copyButton);
        });

        // Assert: getEditorHtml was called but clipboard was NOT written to
        expect(getEditorHtml).toHaveBeenCalled();
        expect(writeTextMock).not.toHaveBeenCalled();
    });

    // -----------------------------------------------------------------------
    // 9. Save state icons
    // -----------------------------------------------------------------------

    it("saveButton_SavedState_ShowsSavedTooltip", () => {
        // Arrange
        renderToolbar({ saveState: "saved" });

        // Assert: tooltip text for saved state
        const savedButton = screen.getByRole("button", { name: /saved/i });
        expect(savedButton).toBeDefined();
    });

    it("saveButton_ErrorState_ShowsErrorTooltipWithMessage", () => {
        // Arrange
        renderToolbar({ saveState: "error", saveError: "Connection timeout" });

        // Assert: tooltip includes error message
        const errorButton = screen.getByRole("button", { name: /save failed.*connection timeout/i });
        expect(errorButton).toBeDefined();
    });
});
