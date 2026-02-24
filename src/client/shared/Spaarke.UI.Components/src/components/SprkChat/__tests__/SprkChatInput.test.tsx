/**
 * SprkChatInput Component Tests
 *
 * Tests input rendering, Ctrl+Enter shortcut, character counting,
 * disabled state, and send behavior.
 *
 * @see ADR-022 - React 16 APIs only
 */

import * as React from "react";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { SprkChatInput } from "../SprkChatInput";
import { renderWithProviders } from "../../../__mocks__/pcfMocks";

describe("SprkChatInput", () => {
    let mockOnSend: jest.Mock;

    beforeEach(() => {
        mockOnSend = jest.fn();
    });

    afterEach(() => {
        jest.clearAllMocks();
    });

    describe("Rendering", () => {
        it("should render the input form", () => {
            renderWithProviders(
                <SprkChatInput onSend={mockOnSend} />
            );

            expect(screen.getByRole("form")).toBeInTheDocument();
            expect(screen.getByTestId("chat-input-textarea")).toBeInTheDocument();
            expect(screen.getByTestId("chat-send-button")).toBeInTheDocument();
        });

        it("should show default placeholder text", () => {
            renderWithProviders(
                <SprkChatInput onSend={mockOnSend} />
            );

            expect(screen.getByPlaceholderText("Type a message...")).toBeInTheDocument();
        });

        it("should show custom placeholder text", () => {
            renderWithProviders(
                <SprkChatInput onSend={mockOnSend} placeholder="Ask a question..." />
            );

            expect(screen.getByPlaceholderText("Ask a question...")).toBeInTheDocument();
        });

        it("should display Ctrl+Enter hint", () => {
            renderWithProviders(
                <SprkChatInput onSend={mockOnSend} />
            );

            expect(screen.getByText("Ctrl+Enter to send")).toBeInTheDocument();
        });

        it("should display character count as 0/2000 initially", () => {
            renderWithProviders(
                <SprkChatInput onSend={mockOnSend} />
            );

            expect(screen.getByText("0/2000")).toBeInTheDocument();
        });

        it("should display custom max character count", () => {
            renderWithProviders(
                <SprkChatInput onSend={mockOnSend} maxCharCount={500} />
            );

            expect(screen.getByText("0/500")).toBeInTheDocument();
        });
    });

    describe("Send Behavior", () => {
        it("should call onSend when send button is clicked", async () => {
            const user = userEvent.setup();
            renderWithProviders(
                <SprkChatInput onSend={mockOnSend} />
            );

            const textarea = screen.getByTestId("chat-input-textarea");
            // Fluent UI Textarea wraps a native textarea - find it
            const nativeTextarea = textarea.querySelector("textarea") || textarea;
            await user.type(nativeTextarea, "Hello world");

            const sendButton = screen.getByTestId("chat-send-button");
            await user.click(sendButton);

            expect(mockOnSend).toHaveBeenCalledWith("Hello world");
        });

        it("should clear input after sending", async () => {
            const user = userEvent.setup();
            renderWithProviders(
                <SprkChatInput onSend={mockOnSend} />
            );

            const textarea = screen.getByTestId("chat-input-textarea");
            const nativeTextarea = textarea.querySelector("textarea") || textarea;
            await user.type(nativeTextarea, "Hello");

            const sendButton = screen.getByTestId("chat-send-button");
            await user.click(sendButton);

            await waitFor(() => {
                expect(screen.getByText("0/2000")).toBeInTheDocument();
            });
        });

        it("should send message on Ctrl+Enter", async () => {
            const user = userEvent.setup();
            renderWithProviders(
                <SprkChatInput onSend={mockOnSend} />
            );

            const textarea = screen.getByTestId("chat-input-textarea");
            const nativeTextarea = textarea.querySelector("textarea") || textarea;
            await user.type(nativeTextarea, "Hello");
            await user.keyboard("{Control>}{Enter}{/Control}");

            expect(mockOnSend).toHaveBeenCalledWith("Hello");
        });

        it("should not send empty messages", async () => {
            const user = userEvent.setup();
            renderWithProviders(
                <SprkChatInput onSend={mockOnSend} />
            );

            const sendButton = screen.getByTestId("chat-send-button");
            await user.click(sendButton);

            expect(mockOnSend).not.toHaveBeenCalled();
        });

        it("should not send whitespace-only messages", async () => {
            const user = userEvent.setup();
            renderWithProviders(
                <SprkChatInput onSend={mockOnSend} />
            );

            const textarea = screen.getByTestId("chat-input-textarea");
            const nativeTextarea = textarea.querySelector("textarea") || textarea;
            await user.type(nativeTextarea, "   ");

            const sendButton = screen.getByTestId("chat-send-button");
            await user.click(sendButton);

            expect(mockOnSend).not.toHaveBeenCalled();
        });

        it("should trim whitespace from sent messages", async () => {
            const user = userEvent.setup();
            renderWithProviders(
                <SprkChatInput onSend={mockOnSend} />
            );

            const textarea = screen.getByTestId("chat-input-textarea");
            const nativeTextarea = textarea.querySelector("textarea") || textarea;
            await user.type(nativeTextarea, "  Hello  ");

            const sendButton = screen.getByTestId("chat-send-button");
            await user.click(sendButton);

            expect(mockOnSend).toHaveBeenCalledWith("Hello");
        });
    });

    describe("Disabled State", () => {
        it("should disable textarea when disabled prop is true", () => {
            renderWithProviders(
                <SprkChatInput onSend={mockOnSend} disabled={true} />
            );

            const textarea = screen.getByTestId("chat-input-textarea");
            const nativeTextarea = textarea.querySelector("textarea") || textarea;
            expect(nativeTextarea).toBeDisabled();
        });

        it("should disable send button when disabled", () => {
            renderWithProviders(
                <SprkChatInput onSend={mockOnSend} disabled={true} />
            );

            expect(screen.getByTestId("chat-send-button")).toBeDisabled();
        });

        it("should not send on Ctrl+Enter when disabled", async () => {
            const user = userEvent.setup();
            renderWithProviders(
                <SprkChatInput onSend={mockOnSend} disabled={true} />
            );

            await user.keyboard("{Control>}{Enter}{/Control}");
            expect(mockOnSend).not.toHaveBeenCalled();
        });
    });

    describe("Character Count", () => {
        it("should update character count as user types", async () => {
            const user = userEvent.setup();
            renderWithProviders(
                <SprkChatInput onSend={mockOnSend} maxCharCount={100} />
            );

            const textarea = screen.getByTestId("chat-input-textarea");
            const nativeTextarea = textarea.querySelector("textarea") || textarea;
            await user.type(nativeTextarea, "Hello");

            expect(screen.getByText("5/100")).toBeInTheDocument();
        });

        it("should disable send button when over character limit", async () => {
            const user = userEvent.setup();
            renderWithProviders(
                <SprkChatInput onSend={mockOnSend} maxCharCount={5} />
            );

            const textarea = screen.getByTestId("chat-input-textarea");
            const nativeTextarea = textarea.querySelector("textarea") || textarea;
            await user.type(nativeTextarea, "Hello World!");

            expect(screen.getByTestId("chat-send-button")).toBeDisabled();
        });
    });

    describe("Accessibility", () => {
        it("should have aria-label on form", () => {
            renderWithProviders(
                <SprkChatInput onSend={mockOnSend} />
            );

            expect(screen.getByLabelText("Chat input")).toBeInTheDocument();
        });

        it("should have aria-label on send button", () => {
            renderWithProviders(
                <SprkChatInput onSend={mockOnSend} />
            );

            expect(screen.getByLabelText("Send message")).toBeInTheDocument();
        });
    });
});
