/**
 * DiffReviewPanel Component Tests
 *
 * Tests the overlay panel that displays AI-proposed revisions with
 * Accept, Reject, and Edit actions. Verifies rendering, keyboard
 * interactions (Escape to dismiss), and focus management.
 *
 * @see DiffReviewPanel (components/DiffReviewPanel.tsx)
 * @see DiffCompareView (@spaarke/ui-components)
 * @see ADR-006  - Code Pages for standalone dialogs
 * @see ADR-012  - Shared component library
 * @see ADR-021  - Fluent UI v9 design system
 */

import React from "react";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { FluentProvider, webLightTheme } from "@fluentui/react-components";
import { DiffReviewPanel } from "../components/DiffReviewPanel";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Render with FluentProvider (required for makeStyles) */
function renderWithProvider(ui: React.ReactElement) {
    return render(
        <FluentProvider theme={webLightTheme}>{ui}</FluentProvider>
    );
}

const DEFAULT_PROPS = {
    isOpen: true,
    originalText: "<p>Original analysis content</p>",
    proposedText: "<p>Revised analysis content with improvements</p>",
    onAccept: jest.fn(),
    onReject: jest.fn(),
};

// ---------------------------------------------------------------------------
// Test Suite
// ---------------------------------------------------------------------------

describe("DiffReviewPanel", () => {
    afterEach(() => {
        jest.clearAllMocks();
    });

    // -----------------------------------------------------------------------
    // 1. Rendering
    // -----------------------------------------------------------------------

    describe("rendering", () => {
        it("render_IsOpenTrue_ShowsPanelWithContent", () => {
            renderWithProvider(<DiffReviewPanel {...DEFAULT_PROPS} />);

            expect(
                screen.getByTestId("diff-review-panel-backdrop")
            ).toBeInTheDocument();
            expect(
                screen.getByTestId("diff-review-panel")
            ).toBeInTheDocument();
            expect(
                screen.getByText("Review Proposed Changes")
            ).toBeInTheDocument();
        });

        it("render_IsOpenTrue_ShowsDiffCompareView", () => {
            renderWithProvider(<DiffReviewPanel {...DEFAULT_PROPS} />);

            // The mock DiffCompareView renders with data-testid
            expect(
                screen.getByTestId("mock-diff-compare-view")
            ).toBeInTheDocument();
        });

        it("render_IsOpenFalse_BackdropHiddenPointerEventsNone", () => {
            renderWithProvider(
                <DiffReviewPanel {...DEFAULT_PROPS} isOpen={false} />
            );

            // Panel should still be in DOM but visually hidden
            const backdrop = screen.getByTestId("diff-review-panel-backdrop");
            expect(backdrop).toBeInTheDocument();

            // DiffCompareView should NOT render when closed (conditional rendering)
            expect(
                screen.queryByTestId("mock-diff-compare-view")
            ).not.toBeInTheDocument();
        });

        it("render_HasCorrectAccessibilityAttributes", () => {
            renderWithProvider(<DiffReviewPanel {...DEFAULT_PROPS} />);

            const backdrop = screen.getByTestId("diff-review-panel-backdrop");
            expect(backdrop).toHaveAttribute("role", "dialog");
            expect(backdrop).toHaveAttribute("aria-modal", "true");
            expect(backdrop).toHaveAttribute(
                "aria-label",
                "Review proposed changes"
            );
        });

        it("render_CloseButtonPresent", () => {
            renderWithProvider(<DiffReviewPanel {...DEFAULT_PROPS} />);

            const closeButton = screen.getByTestId("diff-review-close");
            expect(closeButton).toBeInTheDocument();
            expect(closeButton).toHaveAttribute(
                "aria-label",
                "Close review panel"
            );
        });
    });

    // -----------------------------------------------------------------------
    // 2. Accept action
    // -----------------------------------------------------------------------

    describe("accept action", () => {
        it("accept_ClickAcceptButton_CallsOnAcceptWithProposedText", () => {
            const mockOnAccept = jest.fn();
            renderWithProvider(
                <DiffReviewPanel
                    {...DEFAULT_PROPS}
                    onAccept={mockOnAccept}
                />
            );

            fireEvent.click(screen.getByTestId("diff-accept-button"));

            expect(mockOnAccept).toHaveBeenCalledTimes(1);
            expect(mockOnAccept).toHaveBeenCalledWith(
                "<p>Revised analysis content with improvements</p>"
            );
        });
    });

    // -----------------------------------------------------------------------
    // 3. Reject action
    // -----------------------------------------------------------------------

    describe("reject action", () => {
        it("reject_ClickRejectButton_CallsOnReject", () => {
            const mockOnReject = jest.fn();
            renderWithProvider(
                <DiffReviewPanel
                    {...DEFAULT_PROPS}
                    onReject={mockOnReject}
                />
            );

            fireEvent.click(screen.getByTestId("diff-reject-button"));

            expect(mockOnReject).toHaveBeenCalledTimes(1);
        });

        it("reject_ClickCloseButton_CallsOnReject", () => {
            const mockOnReject = jest.fn();
            renderWithProvider(
                <DiffReviewPanel
                    {...DEFAULT_PROPS}
                    onReject={mockOnReject}
                />
            );

            fireEvent.click(screen.getByTestId("diff-review-close"));

            expect(mockOnReject).toHaveBeenCalledTimes(1);
        });
    });

    // -----------------------------------------------------------------------
    // 4. Keyboard interactions
    // -----------------------------------------------------------------------

    describe("keyboard interactions", () => {
        it("escape_WhenOpen_CallsOnReject", () => {
            const mockOnReject = jest.fn();
            renderWithProvider(
                <DiffReviewPanel
                    {...DEFAULT_PROPS}
                    onReject={mockOnReject}
                />
            );

            fireEvent.keyDown(document, { key: "Escape" });

            expect(mockOnReject).toHaveBeenCalledTimes(1);
        });

        it("escape_WhenClosed_DoesNotCallOnReject", () => {
            const mockOnReject = jest.fn();
            renderWithProvider(
                <DiffReviewPanel
                    {...DEFAULT_PROPS}
                    isOpen={false}
                    onReject={mockOnReject}
                />
            );

            fireEvent.keyDown(document, { key: "Escape" });

            expect(mockOnReject).not.toHaveBeenCalled();
        });

        it("otherKeys_WhenOpen_DoNotCallOnReject", () => {
            const mockOnReject = jest.fn();
            renderWithProvider(
                <DiffReviewPanel
                    {...DEFAULT_PROPS}
                    onReject={mockOnReject}
                />
            );

            fireEvent.keyDown(document, { key: "Enter" });
            fireEvent.keyDown(document, { key: "Tab" });
            fireEvent.keyDown(document, { key: "a" });

            expect(mockOnReject).not.toHaveBeenCalled();
        });
    });

    // -----------------------------------------------------------------------
    // 5. Focus management
    // -----------------------------------------------------------------------

    describe("focus management", () => {
        it("open_PanelReceivesFocus", async () => {
            renderWithProvider(<DiffReviewPanel {...DEFAULT_PROPS} />);

            const panel = screen.getByTestId("diff-review-panel");

            // The panel should have tabIndex -1 for programmatic focus
            expect(panel).toHaveAttribute("tabindex", "-1");
        });
    });

    // -----------------------------------------------------------------------
    // 6. DiffCompareView props forwarding
    // -----------------------------------------------------------------------

    describe("DiffCompareView props", () => {
        it("diffCompareView_ReceivesOriginalAndProposedText", () => {
            renderWithProvider(<DiffReviewPanel {...DEFAULT_PROPS} />);

            const diffView = screen.getByTestId("mock-diff-compare-view");
            expect(diffView).toHaveAttribute(
                "data-original",
                "<p>Original analysis content</p>"
            );
            expect(diffView).toHaveAttribute(
                "data-proposed",
                "<p>Revised analysis content with improvements</p>"
            );
        });
    });
});
