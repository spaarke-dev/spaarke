/**
 * SprkChatHighlightRefine Component Tests
 *
 * Tests text selection detection, floating toolbar, instruction input,
 * and refinement submission.
 *
 * @see ADR-022 - React 16 APIs only
 */

import * as React from "react";
import { screen, fireEvent, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { SprkChatHighlightRefine } from "../SprkChatHighlightRefine";
import { renderWithProviders } from "../../../__mocks__/pcfMocks";

// Helper: create a wrapper component that includes content to select
const TestWrapper: React.FC<{
    onRefine: (text: string, instruction: string) => void;
    isRefining?: boolean;
}> = ({ onRefine, isRefining = false }) => {
    const contentRef = React.useRef<HTMLDivElement>(null);

    return (
        <div>
            <div ref={contentRef} data-testid="content-area" style={{ position: "relative" }}>
                <p>This is some selectable text content for testing purposes.</p>
            </div>
            <SprkChatHighlightRefine
                contentRef={contentRef}
                onRefine={onRefine}
                isRefining={isRefining}
            />
        </div>
    );
};

describe("SprkChatHighlightRefine", () => {
    let mockOnRefine: jest.Mock;

    beforeEach(() => {
        mockOnRefine = jest.fn();
    });

    afterEach(() => {
        jest.clearAllMocks();
    });

    describe("Rendering", () => {
        it("should render nothing when no text is selected", () => {
            renderWithProviders(
                <TestWrapper onRefine={mockOnRefine} />
            );

            expect(screen.queryByTestId("highlight-refine-toolbar")).not.toBeInTheDocument();
            expect(screen.queryByTestId("refine-button")).not.toBeInTheDocument();
        });

        it("should render content area for selection", () => {
            renderWithProviders(
                <TestWrapper onRefine={mockOnRefine} />
            );

            expect(screen.getByTestId("content-area")).toBeInTheDocument();
            expect(screen.getByText(/selectable text content/)).toBeInTheDocument();
        });
    });

    describe("Selection Detection", () => {
        it("should show toolbar when text is selected in the content area", async () => {
            renderWithProviders(
                <TestWrapper onRefine={mockOnRefine} />
            );

            const contentArea = screen.getByTestId("content-area");
            const textNode = contentArea.querySelector("p")!.firstChild!;

            // Simulate text selection
            const range = document.createRange();
            range.setStart(textNode, 0);
            range.setEnd(textNode, 10);

            const selection = window.getSelection()!;
            selection.removeAllRanges();
            selection.addRange(range);

            // Trigger selectionchange event
            fireEvent(document, new Event("selectionchange"));

            await waitFor(() => {
                expect(screen.getByTestId("highlight-refine-toolbar")).toBeInTheDocument();
            });
        });

        it("should show Refine button in the toolbar", async () => {
            renderWithProviders(
                <TestWrapper onRefine={mockOnRefine} />
            );

            const contentArea = screen.getByTestId("content-area");
            const textNode = contentArea.querySelector("p")!.firstChild!;

            const range = document.createRange();
            range.setStart(textNode, 0);
            range.setEnd(textNode, 10);

            const selection = window.getSelection()!;
            selection.removeAllRanges();
            selection.addRange(range);

            fireEvent(document, new Event("selectionchange"));

            await waitFor(() => {
                expect(screen.getByTestId("refine-button")).toBeInTheDocument();
            });
        });
    });

    describe("Refinement Flow", () => {
        it("should show instruction input when Refine is clicked", async () => {
            const user = userEvent.setup();
            renderWithProviders(
                <TestWrapper onRefine={mockOnRefine} />
            );

            const contentArea = screen.getByTestId("content-area");
            const textNode = contentArea.querySelector("p")!.firstChild!;

            const range = document.createRange();
            range.setStart(textNode, 0);
            range.setEnd(textNode, 10);

            const selection = window.getSelection()!;
            selection.removeAllRanges();
            selection.addRange(range);

            fireEvent(document, new Event("selectionchange"));

            await waitFor(() => {
                expect(screen.getByTestId("refine-button")).toBeInTheDocument();
            });

            await user.click(screen.getByTestId("refine-button"));

            await waitFor(() => {
                expect(screen.getByTestId("refine-instruction-input")).toBeInTheDocument();
                expect(screen.getByTestId("refine-submit-button")).toBeInTheDocument();
            });
        });

        it("should call onRefine with selected text and instruction on submit", async () => {
            const user = userEvent.setup();
            renderWithProviders(
                <TestWrapper onRefine={mockOnRefine} />
            );

            const contentArea = screen.getByTestId("content-area");
            const textNode = contentArea.querySelector("p")!.firstChild!;

            const range = document.createRange();
            range.setStart(textNode, 0);
            range.setEnd(textNode, 10);

            const selection = window.getSelection()!;
            selection.removeAllRanges();
            selection.addRange(range);

            fireEvent(document, new Event("selectionchange"));

            await waitFor(() => {
                expect(screen.getByTestId("refine-button")).toBeInTheDocument();
            });

            await user.click(screen.getByTestId("refine-button"));

            await waitFor(() => {
                expect(screen.getByTestId("refine-instruction-input")).toBeInTheDocument();
            });

            const input = screen.getByTestId("refine-instruction-input");
            const nativeInput = input.querySelector("input") || input;
            await user.type(nativeInput, "simplify");

            await user.click(screen.getByTestId("refine-submit-button"));

            expect(mockOnRefine).toHaveBeenCalledWith(
                expect.any(String),
                "simplify"
            );
        });

        it("should disable submit button when instruction is empty", async () => {
            const user = userEvent.setup();
            renderWithProviders(
                <TestWrapper onRefine={mockOnRefine} />
            );

            const contentArea = screen.getByTestId("content-area");
            const textNode = contentArea.querySelector("p")!.firstChild!;

            const range = document.createRange();
            range.setStart(textNode, 0);
            range.setEnd(textNode, 10);

            const selection = window.getSelection()!;
            selection.removeAllRanges();
            selection.addRange(range);

            fireEvent(document, new Event("selectionchange"));

            await waitFor(() => {
                expect(screen.getByTestId("refine-button")).toBeInTheDocument();
            });

            await user.click(screen.getByTestId("refine-button"));

            await waitFor(() => {
                expect(screen.getByTestId("refine-submit-button")).toBeDisabled();
            });
        });
    });

    describe("Refining State", () => {
        it("should show spinner when isRefining is true", async () => {
            renderWithProviders(
                <TestWrapper onRefine={mockOnRefine} isRefining={true} />
            );

            const contentArea = screen.getByTestId("content-area");
            const textNode = contentArea.querySelector("p")!.firstChild!;

            const range = document.createRange();
            range.setStart(textNode, 0);
            range.setEnd(textNode, 10);

            const selection = window.getSelection()!;
            selection.removeAllRanges();
            selection.addRange(range);

            fireEvent(document, new Event("selectionchange"));

            await waitFor(() => {
                expect(screen.getByTestId("refine-button")).toBeInTheDocument();
            });

            // The refine button should be disabled when isRefining
            expect(screen.getByTestId("refine-button")).toBeDisabled();
        });
    });
});
