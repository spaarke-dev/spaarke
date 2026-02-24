/**
 * SprkChatPredefinedPrompts Component Tests
 *
 * Tests rendering of prompt suggestions, selection behavior, and disabled state.
 *
 * @see ADR-022 - React 16 APIs only
 */

import * as React from "react";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { SprkChatPredefinedPrompts } from "../SprkChatPredefinedPrompts";
import { IPredefinedPrompt } from "../types";
import { renderWithProviders } from "../../../__mocks__/pcfMocks";

describe("SprkChatPredefinedPrompts", () => {
    const mockPrompts: IPredefinedPrompt[] = [
        { key: "summary", label: "Summarize this document", prompt: "Please provide a summary of this document." },
        { key: "review", label: "Review for issues", prompt: "Review this document for potential issues." },
        { key: "extract", label: "Extract key terms", prompt: "Extract the key terms and definitions." },
    ];

    let mockOnSelect: jest.Mock;

    beforeEach(() => {
        mockOnSelect = jest.fn();
    });

    afterEach(() => {
        jest.clearAllMocks();
    });

    describe("Rendering", () => {
        it("should render all prompt buttons", () => {
            renderWithProviders(
                <SprkChatPredefinedPrompts prompts={mockPrompts} onSelect={mockOnSelect} />
            );

            expect(screen.getByText("Summarize this document")).toBeInTheDocument();
            expect(screen.getByText("Review for issues")).toBeInTheDocument();
            expect(screen.getByText("Extract key terms")).toBeInTheDocument();
        });

        it("should render Try asking heading", () => {
            renderWithProviders(
                <SprkChatPredefinedPrompts prompts={mockPrompts} onSelect={mockOnSelect} />
            );

            expect(screen.getByText("Try asking")).toBeInTheDocument();
        });

        it("should render nothing when prompts array is empty", () => {
            const { container } = renderWithProviders(
                <SprkChatPredefinedPrompts prompts={[]} onSelect={mockOnSelect} />
            );

            expect(container.firstChild).toBeNull();
        });

        it("should render with region role", () => {
            renderWithProviders(
                <SprkChatPredefinedPrompts prompts={mockPrompts} onSelect={mockOnSelect} />
            );

            expect(screen.getByRole("region")).toBeInTheDocument();
        });

        it("should have aria-label on region", () => {
            renderWithProviders(
                <SprkChatPredefinedPrompts prompts={mockPrompts} onSelect={mockOnSelect} />
            );

            expect(screen.getByLabelText("Suggested prompts")).toBeInTheDocument();
        });

        it("should render data-testid for each prompt", () => {
            renderWithProviders(
                <SprkChatPredefinedPrompts prompts={mockPrompts} onSelect={mockOnSelect} />
            );

            expect(screen.getByTestId("predefined-prompt-summary")).toBeInTheDocument();
            expect(screen.getByTestId("predefined-prompt-review")).toBeInTheDocument();
            expect(screen.getByTestId("predefined-prompt-extract")).toBeInTheDocument();
        });
    });

    describe("Selection Behavior", () => {
        it("should call onSelect with the full prompt text when clicked", async () => {
            const user = userEvent.setup();
            renderWithProviders(
                <SprkChatPredefinedPrompts prompts={mockPrompts} onSelect={mockOnSelect} />
            );

            await user.click(screen.getByText("Summarize this document"));

            expect(mockOnSelect).toHaveBeenCalledWith("Please provide a summary of this document.");
        });

        it("should call onSelect with the correct prompt for each button", async () => {
            const user = userEvent.setup();
            renderWithProviders(
                <SprkChatPredefinedPrompts prompts={mockPrompts} onSelect={mockOnSelect} />
            );

            await user.click(screen.getByText("Review for issues"));

            expect(mockOnSelect).toHaveBeenCalledWith("Review this document for potential issues.");
        });
    });

    describe("Disabled State", () => {
        it("should disable all prompt buttons when disabled is true", () => {
            renderWithProviders(
                <SprkChatPredefinedPrompts
                    prompts={mockPrompts}
                    onSelect={mockOnSelect}
                    disabled={true}
                />
            );

            const buttons = screen.getAllByRole("button");
            buttons.forEach((button) => {
                expect(button).toBeDisabled();
            });
        });

        it("should enable all prompt buttons when disabled is false", () => {
            renderWithProviders(
                <SprkChatPredefinedPrompts
                    prompts={mockPrompts}
                    onSelect={mockOnSelect}
                    disabled={false}
                />
            );

            const buttons = screen.getAllByRole("button");
            buttons.forEach((button) => {
                expect(button).not.toBeDisabled();
            });
        });
    });
});
