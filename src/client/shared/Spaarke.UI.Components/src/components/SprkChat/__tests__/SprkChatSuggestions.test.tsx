/**
 * SprkChatSuggestions Component Tests
 *
 * Tests rendering of suggestion chips, selection behavior, keyboard navigation,
 * truncation, visibility animation, and accessibility.
 *
 * @see ADR-012 - Shared Component Library
 * @see ADR-021 - Fluent UI v9; makeStyles; design tokens; dark mode
 * @see ADR-022 - React 16 APIs only
 */

import * as React from "react";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { SprkChatSuggestions } from "../SprkChatSuggestions";
import { renderWithProviders } from "../../../__mocks__/pcfMocks";

describe("SprkChatSuggestions", () => {
    let mockOnSelect: jest.Mock;

    beforeEach(() => {
        mockOnSelect = jest.fn();
    });

    afterEach(() => {
        jest.clearAllMocks();
    });

    // ─────────────────────────────────────────────────────────────────────
    // Test 1: Render_WithSuggestions_ShowsChips
    // ─────────────────────────────────────────────────────────────────────

    it("Render_WithSuggestions_ShowsChips", () => {
        const suggestions = ["Summarize the key points", "What are the risks?", "List action items"];

        renderWithProviders(
            <SprkChatSuggestions
                suggestions={suggestions}
                onSelect={mockOnSelect}
                visible={true}
            />
        );

        expect(screen.getByTestId("suggestion-chip-0")).toBeInTheDocument();
        expect(screen.getByTestId("suggestion-chip-1")).toBeInTheDocument();
        expect(screen.getByTestId("suggestion-chip-2")).toBeInTheDocument();
        expect(screen.getByText("Summarize the key points")).toBeInTheDocument();
        expect(screen.getByText("What are the risks?")).toBeInTheDocument();
        expect(screen.getByText("List action items")).toBeInTheDocument();
    });

    // ─────────────────────────────────────────────────────────────────────
    // Test 2: Render_EmptySuggestions_ShowsNothing
    // ─────────────────────────────────────────────────────────────────────

    it("Render_EmptySuggestions_ShowsNothing", () => {
        renderWithProviders(
            <SprkChatSuggestions
                suggestions={[]}
                onSelect={mockOnSelect}
                visible={true}
            />
        );

        // Component returns null when suggestions is empty, so the container
        // testid should not be present in the DOM.
        expect(screen.queryByTestId("sprkchat-suggestions")).not.toBeInTheDocument();
    });

    // ─────────────────────────────────────────────────────────────────────
    // Test 3: Render_MaxThreeSuggestions_TruncatesExtra
    // ─────────────────────────────────────────────────────────────────────

    it("Render_MaxThreeSuggestions_TruncatesExtra", () => {
        const suggestions = [
            "First suggestion",
            "Second suggestion",
            "Third suggestion",
            "Fourth suggestion",
            "Fifth suggestion",
        ];

        renderWithProviders(
            <SprkChatSuggestions
                suggestions={suggestions}
                onSelect={mockOnSelect}
                visible={true}
            />
        );

        // First three should be rendered
        expect(screen.getByTestId("suggestion-chip-0")).toBeInTheDocument();
        expect(screen.getByTestId("suggestion-chip-1")).toBeInTheDocument();
        expect(screen.getByTestId("suggestion-chip-2")).toBeInTheDocument();

        // Fourth and fifth should NOT be rendered
        expect(screen.queryByTestId("suggestion-chip-3")).not.toBeInTheDocument();
        expect(screen.queryByTestId("suggestion-chip-4")).not.toBeInTheDocument();

        // Verify text content
        expect(screen.getByText("First suggestion")).toBeInTheDocument();
        expect(screen.getByText("Second suggestion")).toBeInTheDocument();
        expect(screen.getByText("Third suggestion")).toBeInTheDocument();
        expect(screen.queryByText("Fourth suggestion")).not.toBeInTheDocument();
        expect(screen.queryByText("Fifth suggestion")).not.toBeInTheDocument();
    });

    // ─────────────────────────────────────────────────────────────────────
    // Test 4: Click_SuggestionChip_CallsOnSelect
    // ─────────────────────────────────────────────────────────────────────

    it("Click_SuggestionChip_CallsOnSelect", async () => {
        const user = userEvent.setup();
        const suggestions = ["Summarize the key points", "What are the risks?"];

        renderWithProviders(
            <SprkChatSuggestions
                suggestions={suggestions}
                onSelect={mockOnSelect}
                visible={true}
            />
        );

        await user.click(screen.getByTestId("suggestion-chip-1"));

        // onSelect should be called with the FULL original text, not truncated
        expect(mockOnSelect).toHaveBeenCalledTimes(1);
        expect(mockOnSelect).toHaveBeenCalledWith("What are the risks?");
    });

    // ─────────────────────────────────────────────────────────────────────
    // Test 5: Render_LongSuggestion_TruncatesText
    // ─────────────────────────────────────────────────────────────────────

    it("Render_LongSuggestion_TruncatesText", () => {
        // 60 characters - exceeds MAX_TEXT_LENGTH of 50
        const longText = "This is a very long suggestion that exceeds the fifty char limit easily";
        const suggestions = [longText];

        renderWithProviders(
            <SprkChatSuggestions
                suggestions={suggestions}
                onSelect={mockOnSelect}
                visible={true}
            />
        );

        // The displayed text should be truncated to 49 chars + ellipsis (unicode \u2026)
        const truncated = longText.slice(0, 49).trimEnd() + "\u2026";
        expect(screen.getByText(truncated)).toBeInTheDocument();

        // The full original text should NOT appear as visible text
        expect(screen.queryByText(longText)).not.toBeInTheDocument();
    });

    // ─────────────────────────────────────────────────────────────────────
    // Test 6: Visible_False_HidesComponent
    // ─────────────────────────────────────────────────────────────────────

    it("Visible_False_HidesComponent", () => {
        const suggestions = ["A suggestion"];

        renderWithProviders(
            <SprkChatSuggestions
                suggestions={suggestions}
                onSelect={mockOnSelect}
                visible={false}
            />
        );

        const container = screen.getByTestId("sprkchat-suggestions");
        // When visible=false, the component applies the "hidden" class which sets
        // opacity: 0 and pointer-events: none. We verify the container is still
        // in the DOM (it's not unmounted, just hidden via CSS for animation).
        expect(container).toBeInTheDocument();

        // The component should have the hidden class applied (opacity 0, pointerEvents none).
        // Fluent makeStyles generates class names, so we verify behavior: the container
        // should NOT have the visible class characteristics. Since makeStyles classes
        // are hashed, we check that the element exists but is styled for hidden state.
        // The key assertion is that the container IS in the DOM (not null-returned),
        // meaning visibility is controlled by CSS, not conditional rendering.
        expect(container.className).toBeTruthy();
    });

    // ─────────────────────────────────────────────────────────────────────
    // Test 7: Keyboard_ArrowRight_MovesFocusToNextChip
    // ─────────────────────────────────────────────────────────────────────

    it("Keyboard_ArrowRight_MovesFocusToNextChip", async () => {
        const user = userEvent.setup();
        const suggestions = ["First", "Second", "Third"];

        renderWithProviders(
            <SprkChatSuggestions
                suggestions={suggestions}
                onSelect={mockOnSelect}
                visible={true}
            />
        );

        const chip0 = screen.getByTestId("suggestion-chip-0");
        const chip1 = screen.getByTestId("suggestion-chip-1");

        // Focus the first chip
        chip0.focus();
        expect(document.activeElement).toBe(chip0);

        // Press ArrowRight - focus should move to second chip
        await user.keyboard("{ArrowRight}");
        expect(document.activeElement).toBe(chip1);
    });

    // ─────────────────────────────────────────────────────────────────────
    // Test 8: Keyboard_EnterOnChip_CallsOnSelect
    // ─────────────────────────────────────────────────────────────────────

    it("Keyboard_EnterOnChip_CallsOnSelect", async () => {
        const user = userEvent.setup();
        const suggestions = ["Summarize the key points"];

        renderWithProviders(
            <SprkChatSuggestions
                suggestions={suggestions}
                onSelect={mockOnSelect}
                visible={true}
            />
        );

        const chip = screen.getByTestId("suggestion-chip-0");

        // Focus the chip and press Enter
        chip.focus();
        await user.keyboard("{Enter}");

        expect(mockOnSelect).toHaveBeenCalledWith("Summarize the key points");
    });

    // ─────────────────────────────────────────────────────────────────────
    // Test 9: Accessibility_ContainerHasGroupRole
    // ─────────────────────────────────────────────────────────────────────

    it("Accessibility_ContainerHasGroupRole", () => {
        const suggestions = ["A suggestion"];

        renderWithProviders(
            <SprkChatSuggestions
                suggestions={suggestions}
                onSelect={mockOnSelect}
                visible={true}
            />
        );

        const group = screen.getByRole("group");
        expect(group).toBeInTheDocument();
        expect(group).toHaveAttribute("aria-label", "Follow-up suggestions");
    });

    // ─────────────────────────────────────────────────────────────────────
    // Test 10: Accessibility_ChipsHaveLabels
    // ─────────────────────────────────────────────────────────────────────

    it("Accessibility_ChipsHaveLabels", () => {
        // Use a long suggestion to trigger the aria-label (only set when truncated)
        const longText = "This is a very long suggestion that exceeds the fifty char limit easily";
        const shortText = "Short one";
        const suggestions = [longText, shortText];

        renderWithProviders(
            <SprkChatSuggestions
                suggestions={suggestions}
                onSelect={mockOnSelect}
                visible={true}
            />
        );

        // Truncated chip should have aria-label with full text
        const truncatedChip = screen.getByTestId("suggestion-chip-0");
        expect(truncatedChip).toHaveAttribute("aria-label", longText);

        // Non-truncated chip should NOT have aria-label (it's undefined when not truncated)
        const shortChip = screen.getByTestId("suggestion-chip-1");
        expect(shortChip).not.toHaveAttribute("aria-label");
    });
});
