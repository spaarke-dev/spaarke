/**
 * SprkChatInput Action Menu Integration Tests
 *
 * Tests the "/" trigger detection, keyboard delegation to SprkChatActionMenu,
 * menu lifecycle (open/close/select), and backward compatibility when the
 * `actions` prop is not provided.
 *
 * @see ADR-021 - Fluent UI v9; design tokens; dark mode
 * @see ADR-022 - React 16 APIs only
 * @see spec-FR-10 - "/" command palette trigger
 */

import * as React from "react";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { SprkChatInput } from "../SprkChatInput";
import { IChatAction } from "../types";
import { renderWithProviders } from "../../../__mocks__/pcfMocks";
import { FluentProvider, webLightTheme, webDarkTheme, teamsHighContrastTheme } from "@fluentui/react-components";
import { render } from "@testing-library/react";

// ─────────────────────────────────────────────────────────────────────────────
// Test Data
// ─────────────────────────────────────────────────────────────────────────────

const createTestActions = (): IChatAction[] => [
    {
        id: "run-playbook",
        label: "Run Playbook",
        description: "Execute a predefined playbook",
        category: "playbooks",
        shortcut: "Ctrl+P",
    },
    {
        id: "summarize",
        label: "Summarize Document",
        description: "Generate a summary of the current document",
        category: "actions",
    },
    {
        id: "search-docs",
        label: "Search Documents",
        description: "Search across all documents",
        category: "search",
        shortcut: "Ctrl+K",
    },
    {
        id: "toggle-theme",
        label: "Toggle Theme",
        description: "Switch between light and dark mode",
        category: "settings",
    },
];

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Gets the native textarea element from the Fluent UI Textarea wrapper.
 */
function getNativeTextarea(): HTMLTextAreaElement {
    const wrapper = screen.getByTestId("chat-input-textarea");
    return (wrapper.querySelector("textarea") || wrapper) as HTMLTextAreaElement;
}

// ─────────────────────────────────────────────────────────────────────────────
// Tests
// ─────────────────────────────────────────────────────────────────────────────

describe("SprkChatInput - Action Menu Integration", () => {
    let mockOnSend: jest.Mock;
    let mockOnActionSelected: jest.Mock;
    let testActions: IChatAction[];

    beforeEach(() => {
        mockOnSend = jest.fn();
        mockOnActionSelected = jest.fn();
        testActions = createTestActions();
    });

    afterEach(() => {
        jest.clearAllMocks();
    });

    // ─────────────────────────────────────────────────────────────────────────
    // Trigger Detection
    // ─────────────────────────────────────────────────────────────────────────

    describe("Trigger Detection", () => {
        it("should open action menu when '/' is typed as first character", async () => {
            const user = userEvent.setup();
            renderWithProviders(
                <SprkChatInput
                    onSend={mockOnSend}
                    actions={testActions}
                    onActionSelected={mockOnActionSelected}
                />
            );

            const textarea = getNativeTextarea();
            await user.type(textarea, "/");

            await waitFor(() => {
                expect(screen.getByTestId("sprkchat-action-menu")).toBeInTheDocument();
            });
        });

        it("should NOT open action menu when '/' is typed in the middle of text", async () => {
            const user = userEvent.setup();
            renderWithProviders(
                <SprkChatInput
                    onSend={mockOnSend}
                    actions={testActions}
                    onActionSelected={mockOnActionSelected}
                />
            );

            const textarea = getNativeTextarea();
            await user.type(textarea, "hello /world");

            expect(screen.queryByTestId("sprkchat-action-menu")).not.toBeInTheDocument();
        });

        it("should close action menu when '/' is removed by backspace", async () => {
            const user = userEvent.setup();
            renderWithProviders(
                <SprkChatInput
                    onSend={mockOnSend}
                    actions={testActions}
                    onActionSelected={mockOnActionSelected}
                />
            );

            const textarea = getNativeTextarea();

            // Type "/" to open menu
            await user.type(textarea, "/");
            await waitFor(() => {
                expect(screen.getByTestId("sprkchat-action-menu")).toBeInTheDocument();
            });

            // Backspace to remove "/"
            await user.keyboard("{Backspace}");
            await waitFor(() => {
                expect(screen.queryByTestId("sprkchat-action-menu")).not.toBeInTheDocument();
            });
        });

        it("should filter action menu as user types after '/'", async () => {
            const user = userEvent.setup();
            renderWithProviders(
                <SprkChatInput
                    onSend={mockOnSend}
                    actions={testActions}
                    onActionSelected={mockOnActionSelected}
                />
            );

            const textarea = getNativeTextarea();
            await user.type(textarea, "/summ");

            await waitFor(() => {
                expect(screen.getByTestId("sprkchat-action-menu")).toBeInTheDocument();
                expect(screen.getByTestId("action-menu-item-summarize")).toBeInTheDocument();
            });

            // Other items should be filtered out
            expect(screen.queryByTestId("action-menu-item-run-playbook")).not.toBeInTheDocument();
            expect(screen.queryByTestId("action-menu-item-search-docs")).not.toBeInTheDocument();
        });

        it("should show hint text about '/' commands when actions are provided", () => {
            renderWithProviders(
                <SprkChatInput
                    onSend={mockOnSend}
                    actions={testActions}
                    onActionSelected={mockOnActionSelected}
                />
            );

            expect(screen.getByText(/\/ for commands/)).toBeInTheDocument();
        });

        it("should set aria-haspopup='listbox' when actions are provided", () => {
            renderWithProviders(
                <SprkChatInput
                    onSend={mockOnSend}
                    actions={testActions}
                    onActionSelected={mockOnActionSelected}
                />
            );

            // The Fluent UI Textarea wrapper has the aria attribute
            const wrapper = screen.getByTestId("chat-input-textarea");
            // aria-haspopup is on the Fluent Textarea root
            expect(wrapper.getAttribute("aria-haspopup")).toBe("listbox");
        });

        it("should set aria-expanded when action menu is open", async () => {
            const user = userEvent.setup();
            renderWithProviders(
                <SprkChatInput
                    onSend={mockOnSend}
                    actions={testActions}
                    onActionSelected={mockOnActionSelected}
                />
            );

            const wrapper = screen.getByTestId("chat-input-textarea");

            // Initially not expanded
            expect(wrapper.getAttribute("aria-expanded")).toBe("false");

            // Type "/" to open
            const textarea = getNativeTextarea();
            await user.type(textarea, "/");

            await waitFor(() => {
                expect(wrapper.getAttribute("aria-expanded")).toBe("true");
            });
        });
    });

    // ─────────────────────────────────────────────────────────────────────────
    // Keyboard Delegation
    // ─────────────────────────────────────────────────────────────────────────

    describe("Keyboard Delegation", () => {
        it("should navigate menu down with ArrowDown when menu is open", async () => {
            const user = userEvent.setup();
            renderWithProviders(
                <SprkChatInput
                    onSend={mockOnSend}
                    actions={testActions}
                    onActionSelected={mockOnActionSelected}
                />
            );

            const textarea = getNativeTextarea();
            await user.type(textarea, "/");

            await waitFor(() => {
                expect(screen.getByTestId("sprkchat-action-menu")).toBeInTheDocument();
            });

            // First item should be active
            expect(
                screen.getByTestId("action-menu-item-run-playbook").getAttribute("aria-selected")
            ).toBe("true");

            // ArrowDown should move to second item
            await user.keyboard("{ArrowDown}");

            await waitFor(() => {
                expect(
                    screen.getByTestId("action-menu-item-summarize").getAttribute("aria-selected")
                ).toBe("true");
            });
        });

        it("should navigate menu up with ArrowUp when menu is open", async () => {
            const user = userEvent.setup();
            renderWithProviders(
                <SprkChatInput
                    onSend={mockOnSend}
                    actions={testActions}
                    onActionSelected={mockOnActionSelected}
                />
            );

            const textarea = getNativeTextarea();
            await user.type(textarea, "/");

            await waitFor(() => {
                expect(screen.getByTestId("sprkchat-action-menu")).toBeInTheDocument();
            });

            // ArrowUp from first item should wrap to last
            await user.keyboard("{ArrowUp}");

            await waitFor(() => {
                expect(
                    screen.getByTestId("action-menu-item-toggle-theme").getAttribute("aria-selected")
                ).toBe("true");
            });
        });

        it("should select active action on Enter when menu is open (not send message)", async () => {
            const user = userEvent.setup();
            renderWithProviders(
                <SprkChatInput
                    onSend={mockOnSend}
                    actions={testActions}
                    onActionSelected={mockOnActionSelected}
                />
            );

            const textarea = getNativeTextarea();
            await user.type(textarea, "/");

            await waitFor(() => {
                expect(screen.getByTestId("sprkchat-action-menu")).toBeInTheDocument();
            });

            // Press Enter to select the first (active) action
            await user.keyboard("{Enter}");

            // onActionSelected should be called, NOT onSend
            expect(mockOnActionSelected).toHaveBeenCalledWith(
                expect.objectContaining({ id: "run-playbook" })
            );
            expect(mockOnSend).not.toHaveBeenCalled();
        });

        it("should close menu and clear '/' on Escape", async () => {
            const user = userEvent.setup();
            renderWithProviders(
                <SprkChatInput
                    onSend={mockOnSend}
                    actions={testActions}
                    onActionSelected={mockOnActionSelected}
                />
            );

            const textarea = getNativeTextarea();
            await user.type(textarea, "/");

            await waitFor(() => {
                expect(screen.getByTestId("sprkchat-action-menu")).toBeInTheDocument();
            });

            await user.keyboard("{Escape}");

            await waitFor(() => {
                expect(screen.queryByTestId("sprkchat-action-menu")).not.toBeInTheDocument();
            });

            // Input should be cleared (the "/" prefix is removed)
            await waitFor(() => {
                expect(screen.getByText("0/2000")).toBeInTheDocument();
            });
        });

        it("should allow Ctrl+Enter to send when menu is NOT open", async () => {
            const user = userEvent.setup();
            renderWithProviders(
                <SprkChatInput
                    onSend={mockOnSend}
                    actions={testActions}
                    onActionSelected={mockOnActionSelected}
                />
            );

            const textarea = getNativeTextarea();
            await user.type(textarea, "Hello world");
            await user.keyboard("{Control>}{Enter}{/Control}");

            expect(mockOnSend).toHaveBeenCalledWith("Hello world");
        });

        it("should not send message on Ctrl+Enter when menu IS open", async () => {
            const user = userEvent.setup();
            renderWithProviders(
                <SprkChatInput
                    onSend={mockOnSend}
                    actions={testActions}
                    onActionSelected={mockOnActionSelected}
                />
            );

            const textarea = getNativeTextarea();
            await user.type(textarea, "/");

            await waitFor(() => {
                expect(screen.getByTestId("sprkchat-action-menu")).toBeInTheDocument();
            });

            // Enter (without Ctrl) should select, not send
            await user.keyboard("{Enter}");

            expect(mockOnSend).not.toHaveBeenCalled();
        });
    });

    // ─────────────────────────────────────────────────────────────────────────
    // Menu Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    describe("Menu Lifecycle", () => {
        it("should clear input text after action selection", async () => {
            const user = userEvent.setup();
            renderWithProviders(
                <SprkChatInput
                    onSend={mockOnSend}
                    actions={testActions}
                    onActionSelected={mockOnActionSelected}
                />
            );

            const textarea = getNativeTextarea();
            await user.type(textarea, "/run");

            await waitFor(() => {
                expect(screen.getByTestId("sprkchat-action-menu")).toBeInTheDocument();
            });

            // Select the first action
            await user.keyboard("{Enter}");

            // Input should be cleared
            await waitFor(() => {
                expect(screen.getByText("0/2000")).toBeInTheDocument();
            });
        });

        it("should call onActionSelected with the selected action", async () => {
            const user = userEvent.setup();
            renderWithProviders(
                <SprkChatInput
                    onSend={mockOnSend}
                    actions={testActions}
                    onActionSelected={mockOnActionSelected}
                />
            );

            const textarea = getNativeTextarea();
            await user.type(textarea, "/");

            await waitFor(() => {
                expect(screen.getByTestId("sprkchat-action-menu")).toBeInTheDocument();
            });

            // Navigate to second item and select
            await user.keyboard("{ArrowDown}");
            await user.keyboard("{Enter}");

            expect(mockOnActionSelected).toHaveBeenCalledTimes(1);
            expect(mockOnActionSelected).toHaveBeenCalledWith(
                expect.objectContaining({ id: "summarize", label: "Summarize Document" })
            );
        });

        it("should close menu after action is selected", async () => {
            const user = userEvent.setup();
            renderWithProviders(
                <SprkChatInput
                    onSend={mockOnSend}
                    actions={testActions}
                    onActionSelected={mockOnActionSelected}
                />
            );

            const textarea = getNativeTextarea();
            await user.type(textarea, "/");

            await waitFor(() => {
                expect(screen.getByTestId("sprkchat-action-menu")).toBeInTheDocument();
            });

            await user.keyboard("{Enter}");

            await waitFor(() => {
                expect(screen.queryByTestId("sprkchat-action-menu")).not.toBeInTheDocument();
            });
        });

        it("should gracefully handle action selection without onActionSelected callback", async () => {
            const user = userEvent.setup();
            // Render without onActionSelected
            renderWithProviders(
                <SprkChatInput
                    onSend={mockOnSend}
                    actions={testActions}
                />
            );

            const textarea = getNativeTextarea();
            await user.type(textarea, "/");

            await waitFor(() => {
                expect(screen.getByTestId("sprkchat-action-menu")).toBeInTheDocument();
            });

            // This should not throw
            await user.keyboard("{Enter}");

            // Menu should still close
            await waitFor(() => {
                expect(screen.queryByTestId("sprkchat-action-menu")).not.toBeInTheDocument();
            });
        });
    });

    // ─────────────────────────────────────────────────────────────────────────
    // Backward Compatibility
    // ─────────────────────────────────────────────────────────────────────────

    describe("Backward Compatibility", () => {
        it("should not render action menu when actions prop is not provided", async () => {
            const user = userEvent.setup();
            renderWithProviders(
                <SprkChatInput onSend={mockOnSend} />
            );

            const textarea = getNativeTextarea();
            await user.type(textarea, "/");

            // No action menu should appear
            expect(screen.queryByTestId("sprkchat-action-menu")).not.toBeInTheDocument();
        });

        it("should not render action menu when actions prop is an empty array", async () => {
            const user = userEvent.setup();
            renderWithProviders(
                <SprkChatInput onSend={mockOnSend} actions={[]} />
            );

            const textarea = getNativeTextarea();
            await user.type(textarea, "/");

            expect(screen.queryByTestId("sprkchat-action-menu")).not.toBeInTheDocument();
        });

        it("should send '/' as part of a normal message when no actions prop", async () => {
            const user = userEvent.setup();
            renderWithProviders(
                <SprkChatInput onSend={mockOnSend} />
            );

            const textarea = getNativeTextarea();
            await user.type(textarea, "/hello");
            await user.keyboard("{Control>}{Enter}{/Control}");

            expect(mockOnSend).toHaveBeenCalledWith("/hello");
        });

        it("should show standard hint (no '/' commands mention) without actions", () => {
            renderWithProviders(
                <SprkChatInput onSend={mockOnSend} />
            );

            expect(screen.getByText("Ctrl+Enter to send")).toBeInTheDocument();
            expect(screen.queryByText(/\/ for commands/)).not.toBeInTheDocument();
        });

        it("should handle normal keyboard events without actions", async () => {
            const user = userEvent.setup();
            renderWithProviders(
                <SprkChatInput onSend={mockOnSend} />
            );

            const textarea = getNativeTextarea();
            await user.type(textarea, "Hello");
            await user.keyboard("{Control>}{Enter}{/Control}");

            expect(mockOnSend).toHaveBeenCalledWith("Hello");
        });

        it("should not interfere with Enter/ArrowDown behavior without actions", async () => {
            const user = userEvent.setup();
            renderWithProviders(
                <SprkChatInput onSend={mockOnSend} />
            );

            const textarea = getNativeTextarea();
            await user.type(textarea, "/test");

            // ArrowDown should not cause issues (no menu to delegate to)
            await user.keyboard("{ArrowDown}");
            await user.keyboard("{ArrowUp}");

            // Escape should not clear input (no menu to dismiss)
            await user.keyboard("{Escape}");

            // Input should still have the typed text
            expect(textarea.value).toContain("/test");
        });
    });

    // ─────────────────────────────────────────────────────────────────────────
    // Theme Support (ADR-021)
    // ─────────────────────────────────────────────────────────────────────────

    describe("Theme Support", () => {
        const themes = [
            { name: "light", theme: webLightTheme },
            { name: "dark", theme: webDarkTheme },
            { name: "high-contrast", theme: teamsHighContrastTheme },
        ];

        themes.forEach(({ name, theme }) => {
            it(`should render with action menu support in ${name} theme without errors`, () => {
                render(
                    <FluentProvider theme={theme}>
                        <SprkChatInput
                            onSend={mockOnSend}
                            actions={testActions}
                            onActionSelected={mockOnActionSelected}
                        />
                    </FluentProvider>
                );

                expect(screen.getByTestId("chat-input-textarea")).toBeInTheDocument();
                expect(screen.getByTestId("chat-send-button")).toBeInTheDocument();
            });
        });
    });
});
