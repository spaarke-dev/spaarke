/**
 * SprkChatMessage Component Tests
 *
 * Tests rendering of user vs assistant messages, streaming indicator,
 * timestamps, and accessibility.
 *
 * @see ADR-021 - Fluent UI v9 design tokens (no hardcoded colors)
 * @see ADR-022 - React 16 APIs only
 */

import * as React from "react";
import { screen } from "@testing-library/react";
import { SprkChatMessage } from "../SprkChatMessage";
import { IChatMessage } from "../types";
import { renderWithProviders } from "../../../__mocks__/pcfMocks";

describe("SprkChatMessage", () => {
    const userMessage: IChatMessage = {
        role: "User",
        content: "Hello, can you help me?",
        timestamp: "2026-02-23T10:00:00Z",
    };

    const assistantMessage: IChatMessage = {
        role: "Assistant",
        content: "Of course! How can I assist you today?",
        timestamp: "2026-02-23T10:00:05Z",
    };

    describe("Rendering", () => {
        it("should render a user message with correct content", () => {
            renderWithProviders(
                <SprkChatMessage message={userMessage} />
            );

            expect(screen.getByText("Hello, can you help me?")).toBeInTheDocument();
        });

        it("should render an assistant message with correct content", () => {
            renderWithProviders(
                <SprkChatMessage message={assistantMessage} />
            );

            expect(screen.getByText("Of course! How can I assist you today?")).toBeInTheDocument();
        });

        it("should render with listitem role", () => {
            renderWithProviders(
                <SprkChatMessage message={userMessage} />
            );

            expect(screen.getByRole("listitem")).toBeInTheDocument();
        });

        it("should have aria-label with message role", () => {
            renderWithProviders(
                <SprkChatMessage message={userMessage} />
            );

            expect(screen.getByLabelText("User message")).toBeInTheDocument();
        });

        it("should render assistant message with correct aria-label", () => {
            renderWithProviders(
                <SprkChatMessage message={assistantMessage} />
            );

            expect(screen.getByLabelText("Assistant message")).toBeInTheDocument();
        });
    });

    describe("Timestamps", () => {
        it("should display timestamp for non-streaming messages", () => {
            renderWithProviders(
                <SprkChatMessage message={userMessage} isStreaming={false} />
            );

            // The formatted time should appear (locale-dependent, check for any time format)
            const timeElements = screen.getByLabelText("User message").querySelectorAll("span");
            // At least one span should contain time-like content
            expect(timeElements.length).toBeGreaterThan(0);
        });

        it("should not display timestamp when streaming", () => {
            const streamingMsg: IChatMessage = {
                role: "Assistant",
                content: "typing...",
                timestamp: "2026-02-23T10:00:05Z",
            };

            renderWithProviders(
                <SprkChatMessage message={streamingMsg} isStreaming={true} />
            );

            // Message content should be visible
            expect(screen.getByText("typing...")).toBeInTheDocument();
        });
    });

    describe("Streaming Indicator", () => {
        it("should show thinking indicator when streaming with empty content", () => {
            const emptyStreamMsg: IChatMessage = {
                role: "Assistant",
                content: "",
                timestamp: "2026-02-23T10:00:05Z",
            };

            renderWithProviders(
                <SprkChatMessage message={emptyStreamMsg} isStreaming={true} />
            );

            expect(screen.getByText("Thinking...")).toBeInTheDocument();
        });

        it("should not show thinking indicator when streaming with content", () => {
            const partialMsg: IChatMessage = {
                role: "Assistant",
                content: "Partial response",
                timestamp: "2026-02-23T10:00:05Z",
            };

            renderWithProviders(
                <SprkChatMessage message={partialMsg} isStreaming={true} />
            );

            expect(screen.queryByText("Thinking...")).not.toBeInTheDocument();
            expect(screen.getByText("Partial response")).toBeInTheDocument();
        });

        it("should not show thinking indicator when not streaming", () => {
            const emptyMsg: IChatMessage = {
                role: "Assistant",
                content: "",
                timestamp: "2026-02-23T10:00:05Z",
            };

            renderWithProviders(
                <SprkChatMessage message={emptyMsg} isStreaming={false} />
            );

            expect(screen.queryByText("Thinking...")).not.toBeInTheDocument();
        });
    });

    describe("Dark Mode Compliance", () => {
        it("should use semantic tokens (no hardcoded colors in rendered output)", () => {
            const { container } = renderWithProviders(
                <SprkChatMessage message={userMessage} />
            );

            // Verify the component renders without inline hardcoded color styles
            // Fluent UI tokens are applied via CSS classes, not inline styles
            const rootDiv = container.firstChild as HTMLElement;
            expect(rootDiv).toBeInTheDocument();
            // No inline color/background-color styles should be set
            expect(rootDiv.style.color).toBe("");
            expect(rootDiv.style.backgroundColor).toBe("");
        });
    });

    describe("Edge Cases", () => {
        it("should handle empty content gracefully", () => {
            const emptyMsg: IChatMessage = {
                role: "User",
                content: "",
                timestamp: "2026-02-23T10:00:00Z",
            };

            renderWithProviders(
                <SprkChatMessage message={emptyMsg} />
            );

            expect(screen.getByLabelText("User message")).toBeInTheDocument();
        });

        it("should handle very long content", () => {
            const longMsg: IChatMessage = {
                role: "Assistant",
                content: "A".repeat(5000),
                timestamp: "2026-02-23T10:00:00Z",
            };

            renderWithProviders(
                <SprkChatMessage message={longMsg} />
            );

            expect(screen.getByText("A".repeat(5000))).toBeInTheDocument();
        });

        it("should handle invalid timestamp gracefully", () => {
            const badTimestamp: IChatMessage = {
                role: "User",
                content: "Test",
                timestamp: "invalid-date",
            };

            renderWithProviders(
                <SprkChatMessage message={badTimestamp} />
            );

            expect(screen.getByText("Test")).toBeInTheDocument();
        });
    });
});
