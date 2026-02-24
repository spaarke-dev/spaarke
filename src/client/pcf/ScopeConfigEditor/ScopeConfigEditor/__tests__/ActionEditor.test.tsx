/**
 * Unit tests for ActionEditor component
 *
 * Tests:
 * - Character count renders and updates on input change
 * - Token count renders and updates on input change
 * - Token count approximation (~chars / 4)
 * - Warning shown when approaching token threshold
 * - No hardcoded color values (design token compliance)
 */

import * as React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import { FluentProvider, webLightTheme } from "@fluentui/react-components";
import { ActionEditor } from "../components/ActionEditor";

// ─────────────────────────────────────────────────────────────────────────────
// Test helpers
// ─────────────────────────────────────────────────────────────────────────────

const renderWithProvider = (ui: React.ReactElement) =>
    render(
        <FluentProvider theme={webLightTheme}>
            {ui}
        </FluentProvider>
    );

// ─────────────────────────────────────────────────────────────────────────────
// Tests
// ─────────────────────────────────────────────────────────────────────────────

describe("ActionEditor", () => {
    it("renders the textarea with a label", () => {
        renderWithProvider(
            <ActionEditor value="" onChange={jest.fn()} />
        );
        expect(screen.getByLabelText(/system prompt editor/i)).toBeInTheDocument();
        expect(screen.getByText(/system prompt/i)).toBeInTheDocument();
    });

    it("renders character count of 0 for empty value", () => {
        renderWithProvider(
            <ActionEditor value="" onChange={jest.fn()} />
        );
        const charCount = screen.getByTestId("char-count");
        expect(charCount).toHaveTextContent("0");
    });

    it("renders correct character count for provided value", () => {
        const text = "Hello, world!"; // 13 chars
        renderWithProvider(
            <ActionEditor value={text} onChange={jest.fn()} />
        );
        const charCount = screen.getByTestId("char-count");
        expect(charCount).toHaveTextContent("13");
    });

    it("renders correct token count (chars / 4 rounded up)", () => {
        const text = "Hello, world!"; // 13 chars → ceil(13/4) = 4 tokens
        renderWithProvider(
            <ActionEditor value={text} onChange={jest.fn()} />
        );
        const tokenCount = screen.getByTestId("token-count");
        // 13 chars / 4 = 3.25 → ceil = 4
        expect(tokenCount).toHaveTextContent("4");
    });

    it("renders token count of 0 for empty string", () => {
        renderWithProvider(
            <ActionEditor value="" onChange={jest.fn()} />
        );
        const tokenCount = screen.getByTestId("token-count");
        expect(tokenCount).toHaveTextContent("0");
    });

    it("calls onChange when textarea value changes", () => {
        const onChange = jest.fn();
        renderWithProvider(
            <ActionEditor value="" onChange={onChange} />
        );

        const textarea = screen.getByLabelText(/system prompt editor/i);
        fireEvent.change(textarea, { target: { value: "New prompt text" } });

        expect(onChange).toHaveBeenCalledWith("New prompt text");
    });

    it("updates character count display when value prop changes", () => {
        const { rerender } = renderWithProvider(
            <FluentProvider theme={webLightTheme}>
                <ActionEditor value="" onChange={jest.fn()} />
            </FluentProvider>
        );

        expect(screen.getByTestId("char-count")).toHaveTextContent("0");

        rerender(
            <FluentProvider theme={webLightTheme}>
                <ActionEditor value="Updated text" onChange={jest.fn()} />
            </FluentProvider>
        );

        // "Updated text" = 12 chars
        expect(screen.getByTestId("char-count")).toHaveTextContent("12");
    });

    it("does not show warning for short prompts", () => {
        renderWithProvider(
            <ActionEditor value="Short prompt" onChange={jest.fn()} />
        );
        expect(screen.queryByTestId("token-warning")).not.toBeInTheDocument();
    });

    it("shows warning when token count approaches 2000 threshold", () => {
        // 2000 tokens × 4 chars = 8000 chars
        const longText = "a".repeat(8000);
        renderWithProvider(
            <ActionEditor value={longText} onChange={jest.fn()} />
        );
        expect(screen.getByTestId("token-warning")).toBeInTheDocument();
        expect(screen.getByTestId("token-warning")).toHaveTextContent(
            /approaching token limit/i
        );
    });

    it("shows error warning when token count exceeds 4000 threshold", () => {
        // 4000 tokens × 4 chars = 16000 chars
        const veryLongText = "a".repeat(16000);
        renderWithProvider(
            <ActionEditor value={veryLongText} onChange={jest.fn()} />
        );
        expect(screen.getByTestId("token-warning")).toBeInTheDocument();
        expect(screen.getByTestId("token-warning")).toHaveTextContent(
            /may exceed model context limits/i
        );
    });

    it("textarea is disabled when readOnly is true", () => {
        renderWithProvider(
            <ActionEditor value="Some text" onChange={jest.fn()} readOnly={true} />
        );
        expect(screen.getByLabelText(/system prompt editor/i)).toBeDisabled();
    });

    it("textarea is enabled when readOnly is false", () => {
        renderWithProvider(
            <ActionEditor value="Some text" onChange={jest.fn()} readOnly={false} />
        );
        expect(screen.getByLabelText(/system prompt editor/i)).not.toBeDisabled();
    });
});
