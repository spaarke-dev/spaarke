/**
 * DiffCompareView Component Tests
 *
 * Tests rendering modes (side-by-side, inline), action buttons (Accept, Reject, Edit),
 * keyboard shortcuts, HTML mode, and edge cases.
 *
 * @see ADR-012 - Shared component library
 * @see ADR-021 - Fluent UI v9 design tokens
 */

import * as React from "react";
import { screen, fireEvent, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { DiffCompareView } from "../DiffCompareView";
import { renderWithProviders } from "../../../__mocks__/pcfMocks";

describe("DiffCompareView", () => {
    let mockOnAccept: jest.Mock;
    let mockOnReject: jest.Mock;
    let mockOnEdit: jest.Mock;

    const defaultProps = {
        originalText: "The quick brown fox",
        proposedText: "The fast brown fox jumps",
    };

    beforeEach(() => {
        mockOnAccept = jest.fn();
        mockOnReject = jest.fn();
        mockOnEdit = jest.fn();
    });

    afterEach(() => {
        jest.clearAllMocks();
    });

    // ─────────────────────────────────────────────────────────────────────
    // Rendering - Side-by-Side Mode (default)
    // ─────────────────────────────────────────────────────────────────────

    describe("Side-by-Side Mode", () => {
        it("render_DefaultMode_ShowsSideBySideLabels", () => {
            renderWithProviders(
                <DiffCompareView
                    {...defaultProps}
                    onAccept={mockOnAccept}
                    onReject={mockOnReject}
                />
            );

            expect(screen.getByText("Original")).toBeInTheDocument();
            expect(screen.getByText("Proposed")).toBeInTheDocument();
        });

        it("render_SideBySideMode_ShowsOriginalAndProposedPanes", () => {
            renderWithProviders(
                <DiffCompareView
                    {...defaultProps}
                    mode="side-by-side"
                    onAccept={mockOnAccept}
                    onReject={mockOnReject}
                />
            );

            expect(screen.getByRole("region", { name: "Original text" })).toBeInTheDocument();
            expect(screen.getByRole("region", { name: "Proposed text" })).toBeInTheDocument();
        });

        it("render_WithTitle_DisplaysTitle", () => {
            renderWithProviders(
                <DiffCompareView
                    {...defaultProps}
                    title="AI Revision"
                    onAccept={mockOnAccept}
                    onReject={mockOnReject}
                />
            );

            expect(screen.getByText("AI Revision")).toBeInTheDocument();
        });
    });

    // ─────────────────────────────────────────────────────────────────────
    // Rendering - Inline Mode
    // ─────────────────────────────────────────────────────────────────────

    describe("Inline Mode", () => {
        it("render_InlineMode_ShowsInlineDiffView", () => {
            renderWithProviders(
                <DiffCompareView
                    {...defaultProps}
                    mode="inline"
                    onAccept={mockOnAccept}
                    onReject={mockOnReject}
                />
            );

            expect(screen.getByRole("region", { name: "Inline diff view" })).toBeInTheDocument();
            // Should not show side-by-side labels
            expect(screen.queryByText("Original")).not.toBeInTheDocument();
        });
    });

    // ─────────────────────────────────────────────────────────────────────
    // Mode Toggle
    // ─────────────────────────────────────────────────────────────────────

    describe("Mode Toggle", () => {
        it("modeToggle_ClickInlineButton_SwitchesToInlineMode", async () => {
            renderWithProviders(
                <DiffCompareView
                    {...defaultProps}
                    mode="side-by-side"
                    onAccept={mockOnAccept}
                    onReject={mockOnReject}
                />
            );

            // In side-by-side mode, the toggle button text says "Inline"
            const toggleButton = screen.getByRole("button", { name: /switch to inline/i });
            await userEvent.click(toggleButton);

            // After toggle, should show inline view
            expect(screen.getByRole("region", { name: "Inline diff view" })).toBeInTheDocument();
        });

        it("modeToggle_ClickSideBySideButton_SwitchesToSideBySide", async () => {
            renderWithProviders(
                <DiffCompareView
                    {...defaultProps}
                    mode="inline"
                    onAccept={mockOnAccept}
                    onReject={mockOnReject}
                />
            );

            // In inline mode, the toggle button says "Side-by-side"
            const toggleButton = screen.getByRole("button", { name: /switch to side-by-side/i });
            await userEvent.click(toggleButton);

            // After toggle, should show side-by-side labels
            expect(screen.getByText("Original")).toBeInTheDocument();
            expect(screen.getByText("Proposed")).toBeInTheDocument();
        });
    });

    // ─────────────────────────────────────────────────────────────────────
    // Action Buttons
    // ─────────────────────────────────────────────────────────────────────

    describe("Action Buttons", () => {
        it("acceptButton_Click_CallsOnAcceptWithProposedText", async () => {
            renderWithProviders(
                <DiffCompareView
                    {...defaultProps}
                    onAccept={mockOnAccept}
                    onReject={mockOnReject}
                />
            );

            const acceptButton = screen.getByRole("button", { name: "Accept changes" });
            await userEvent.click(acceptButton);

            expect(mockOnAccept).toHaveBeenCalledTimes(1);
            expect(mockOnAccept).toHaveBeenCalledWith(defaultProps.proposedText);
        });

        it("rejectButton_Click_CallsOnReject", async () => {
            renderWithProviders(
                <DiffCompareView
                    {...defaultProps}
                    onAccept={mockOnAccept}
                    onReject={mockOnReject}
                />
            );

            const rejectButton = screen.getByRole("button", { name: "Reject changes" });
            await userEvent.click(rejectButton);

            expect(mockOnReject).toHaveBeenCalledTimes(1);
        });

        it("readOnly_True_HidesActionButtons", () => {
            renderWithProviders(
                <DiffCompareView
                    {...defaultProps}
                    readOnly={true}
                    onAccept={mockOnAccept}
                    onReject={mockOnReject}
                />
            );

            expect(screen.queryByRole("button", { name: "Accept changes" })).not.toBeInTheDocument();
            expect(screen.queryByRole("button", { name: "Reject changes" })).not.toBeInTheDocument();
        });

        it("editButton_OnEditProvided_ShowsEditButton", () => {
            renderWithProviders(
                <DiffCompareView
                    {...defaultProps}
                    onAccept={mockOnAccept}
                    onReject={mockOnReject}
                    onEdit={mockOnEdit}
                />
            );

            expect(screen.getByRole("button", { name: "Edit proposed text" })).toBeInTheDocument();
        });

        it("editButton_OnEditNotProvided_HidesEditButton", () => {
            renderWithProviders(
                <DiffCompareView
                    {...defaultProps}
                    onAccept={mockOnAccept}
                    onReject={mockOnReject}
                />
            );

            expect(screen.queryByRole("button", { name: "Edit proposed text" })).not.toBeInTheDocument();
        });
    });

    // ─────────────────────────────────────────────────────────────────────
    // Edit Mode
    // ─────────────────────────────────────────────────────────────────────

    describe("Edit Mode", () => {
        it("editMode_ClickEdit_ShowsTextareaAndSaveCancel", async () => {
            renderWithProviders(
                <DiffCompareView
                    {...defaultProps}
                    onAccept={mockOnAccept}
                    onReject={mockOnReject}
                    onEdit={mockOnEdit}
                />
            );

            const editButton = screen.getByRole("button", { name: "Edit proposed text" });
            await userEvent.click(editButton);

            expect(screen.getByRole("textbox", { name: "Edit proposed text" })).toBeInTheDocument();
            expect(screen.getByRole("button", { name: "Save edits" })).toBeInTheDocument();
            expect(screen.getByRole("button", { name: "Cancel editing" })).toBeInTheDocument();
        });

        it("editMode_ClickSave_CallsOnEditWithEditedText", async () => {
            renderWithProviders(
                <DiffCompareView
                    {...defaultProps}
                    onAccept={mockOnAccept}
                    onReject={mockOnReject}
                    onEdit={mockOnEdit}
                />
            );

            // Enter edit mode
            await userEvent.click(screen.getByRole("button", { name: "Edit proposed text" }));

            // The textarea should contain the proposed text
            const textarea = screen.getByRole("textbox", { name: "Edit proposed text" });
            expect(textarea).toHaveValue(defaultProps.proposedText);

            // Save
            await userEvent.click(screen.getByRole("button", { name: "Save edits" }));
            expect(mockOnEdit).toHaveBeenCalledWith(defaultProps.proposedText);
        });

        it("editMode_ClickCancel_ExitsEditModeWithoutSaving", async () => {
            renderWithProviders(
                <DiffCompareView
                    {...defaultProps}
                    onAccept={mockOnAccept}
                    onReject={mockOnReject}
                    onEdit={mockOnEdit}
                />
            );

            // Enter edit mode
            await userEvent.click(screen.getByRole("button", { name: "Edit proposed text" }));
            expect(screen.getByRole("textbox", { name: "Edit proposed text" })).toBeInTheDocument();

            // Cancel
            await userEvent.click(screen.getByRole("button", { name: "Cancel editing" }));

            // Should exit edit mode (no textarea visible)
            expect(screen.queryByRole("textbox", { name: "Edit proposed text" })).not.toBeInTheDocument();
            expect(mockOnEdit).not.toHaveBeenCalled();
        });

        it("editMode_AcceptAfterEdit_CallsOnAcceptWithEditedText", async () => {
            renderWithProviders(
                <DiffCompareView
                    {...defaultProps}
                    onAccept={mockOnAccept}
                    onReject={mockOnReject}
                    onEdit={mockOnEdit}
                />
            );

            // Enter edit mode
            await userEvent.click(screen.getByRole("button", { name: "Edit proposed text" }));

            // Modify text in textarea
            const textarea = screen.getByRole("textbox", { name: "Edit proposed text" });
            await userEvent.clear(textarea);
            await userEvent.type(textarea, "Custom text");

            // Click Accept (not Save)
            await userEvent.click(screen.getByRole("button", { name: "Accept changes" }));

            expect(mockOnAccept).toHaveBeenCalledWith("Custom text");
        });
    });

    // ─────────────────────────────────────────────────────────────────────
    // Keyboard Shortcuts
    // ─────────────────────────────────────────────────────────────────────

    describe("Keyboard Shortcuts", () => {
        it("keyboardShortcut_CtrlEnter_CallsOnAccept", () => {
            const { container } = renderWithProviders(
                <DiffCompareView
                    {...defaultProps}
                    onAccept={mockOnAccept}
                    onReject={mockOnReject}
                />
            );

            const rootRegion = screen.getByRole("region", { name: "Diff comparison view" });
            fireEvent.keyDown(rootRegion, { key: "Enter", ctrlKey: true });

            expect(mockOnAccept).toHaveBeenCalledTimes(1);
            expect(mockOnAccept).toHaveBeenCalledWith(defaultProps.proposedText);
        });

        it("keyboardShortcut_Escape_CallsOnReject", () => {
            renderWithProviders(
                <DiffCompareView
                    {...defaultProps}
                    onAccept={mockOnAccept}
                    onReject={mockOnReject}
                />
            );

            const rootRegion = screen.getByRole("region", { name: "Diff comparison view" });
            fireEvent.keyDown(rootRegion, { key: "Escape" });

            expect(mockOnReject).toHaveBeenCalledTimes(1);
        });

        it("keyboardShortcut_ReadOnly_DoesNotFireCallbacks", () => {
            renderWithProviders(
                <DiffCompareView
                    {...defaultProps}
                    readOnly={true}
                    onAccept={mockOnAccept}
                    onReject={mockOnReject}
                />
            );

            const rootRegion = screen.getByRole("region", { name: "Diff comparison view" });
            fireEvent.keyDown(rootRegion, { key: "Enter", ctrlKey: true });
            fireEvent.keyDown(rootRegion, { key: "Escape" });

            expect(mockOnAccept).not.toHaveBeenCalled();
            expect(mockOnReject).not.toHaveBeenCalled();
        });

        it("keyboardShortcut_ShowsHintText", () => {
            renderWithProviders(
                <DiffCompareView
                    {...defaultProps}
                    onAccept={mockOnAccept}
                    onReject={mockOnReject}
                />
            );

            expect(screen.getByText("Ctrl+Enter: Accept | Esc: Reject")).toBeInTheDocument();
        });
    });

    // ─────────────────────────────────────────────────────────────────────
    // HTML Mode
    // ─────────────────────────────────────────────────────────────────────

    describe("HTML Mode", () => {
        it("htmlMode_SideBySide_ShowsAnnotatedHtmlPanes", () => {
            renderWithProviders(
                <DiffCompareView
                    originalText="<p>The quick brown fox</p>"
                    proposedText="<p>The fast brown fox</p>"
                    htmlMode={true}
                    mode="side-by-side"
                    onAccept={mockOnAccept}
                    onReject={mockOnReject}
                />
            );

            // HTML mode uses "Original content" / "Proposed content" aria labels
            expect(screen.getByRole("region", { name: "Original content" })).toBeInTheDocument();
            expect(screen.getByRole("region", { name: "Proposed content" })).toBeInTheDocument();
        });

        it("htmlMode_Inline_ShowsAnnotatedHtmlInline", () => {
            renderWithProviders(
                <DiffCompareView
                    originalText="<p>Old text</p>"
                    proposedText="<p>New text</p>"
                    htmlMode={true}
                    mode="inline"
                    onAccept={mockOnAccept}
                    onReject={mockOnReject}
                />
            );

            expect(screen.getByRole("region", { name: "Inline HTML diff view" })).toBeInTheDocument();
        });
    });

    // ─────────────────────────────────────────────────────────────────────
    // Edge Cases
    // ─────────────────────────────────────────────────────────────────────

    describe("Edge Cases", () => {
        it("emptyContent_BothEmpty_RendersWithoutError", () => {
            renderWithProviders(
                <DiffCompareView
                    originalText=""
                    proposedText=""
                    onAccept={mockOnAccept}
                    onReject={mockOnReject}
                />
            );

            expect(screen.getByRole("region", { name: "Diff comparison view" })).toBeInTheDocument();
        });

        it("identicalContent_NoDiffHighlights_NoStatsBar", () => {
            renderWithProviders(
                <DiffCompareView
                    originalText="Same text"
                    proposedText="Same text"
                    onAccept={mockOnAccept}
                    onReject={mockOnReject}
                />
            );

            // Stats bar only shows when there are additions or removals
            // With identical content, there should be no "+X words" or "-X words" text
            expect(screen.queryByText(/\+\d+ word/)).not.toBeInTheDocument();
            expect(screen.queryByText(/-\d+ word/)).not.toBeInTheDocument();
        });

        it("diffStats_WordChanges_ShowsStatsBar", () => {
            renderWithProviders(
                <DiffCompareView
                    {...defaultProps}
                    onAccept={mockOnAccept}
                    onReject={mockOnReject}
                />
            );

            // Should show stats for additions and/or removals
            const statsRegion = screen.getByLabelText("Diff comparison view");
            // At least one of +N words or -N words should appear
            const addedText = screen.queryByText(/\+\d+ word/);
            const removedText = screen.queryByText(/-\d+ word/);
            expect(addedText || removedText).toBeTruthy();
        });

        it("ariaLabel_CustomAriaLabel_AppliedToRoot", () => {
            renderWithProviders(
                <DiffCompareView
                    {...defaultProps}
                    ariaLabel="Custom diff label"
                    onAccept={mockOnAccept}
                    onReject={mockOnReject}
                />
            );

            expect(screen.getByRole("region", { name: "Custom diff label" })).toBeInTheDocument();
        });
    });
});
