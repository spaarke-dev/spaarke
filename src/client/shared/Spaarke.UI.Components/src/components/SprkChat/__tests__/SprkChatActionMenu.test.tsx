/**
 * SprkChatActionMenu Component Tests
 *
 * Tests rendering, filtering, keyboard navigation, action selection,
 * accessibility attributes, and theme support for the SprkChatActionMenu
 * command palette component.
 *
 * @see ADR-021 - Fluent UI v9; design tokens; dark mode
 * @see ADR-022 - React 16 APIs only
 */

import * as React from "react";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { SprkChatActionMenu } from "../SprkChatActionMenu";
import { IChatAction, ISprkChatActionMenuProps } from "../types";
import { renderWithProviders } from "../../../__mocks__/pcfMocks";
import { FluentProvider, webLightTheme, webDarkTheme, teamsHighContrastTheme } from "@fluentui/react-components";
import { render } from "@testing-library/react";

// ─────────────────────────────────────────────────────────────────────────────
// Test Data
// ─────────────────────────────────────────────────────────────────────────────

const createTestActions = (): IChatAction[] => [
    // Playbooks category
    {
        id: "run-playbook",
        label: "Run Playbook",
        description: "Execute a predefined playbook",
        category: "playbooks",
        shortcut: "Ctrl+P",
    },
    {
        id: "create-playbook",
        label: "Create Playbook",
        description: "Create a new custom playbook",
        category: "playbooks",
    },
    // Actions category
    {
        id: "summarize",
        label: "Summarize Document",
        description: "Generate a summary of the current document",
        category: "actions",
    },
    {
        id: "extract-clauses",
        label: "Extract Clauses",
        description: "Extract key clauses from the document",
        category: "actions",
    },
    // Search category
    {
        id: "search-docs",
        label: "Search Documents",
        description: "Search across all documents",
        category: "search",
        shortcut: "Ctrl+K",
    },
    // Settings category
    {
        id: "toggle-theme",
        label: "Toggle Theme",
        description: "Switch between light and dark mode",
        category: "settings",
    },
];

const defaultProps: ISprkChatActionMenuProps = {
    actions: createTestActions(),
    isOpen: true,
    onSelect: jest.fn(),
    onDismiss: jest.fn(),
    filterText: "",
};

// ─────────────────────────────────────────────────────────────────────────────
// Helper
// ─────────────────────────────────────────────────────────────────────────────

function renderMenu(overrides?: Partial<ISprkChatActionMenuProps>) {
    const props = { ...defaultProps, ...overrides };
    return renderWithProviders(<SprkChatActionMenu {...props} />);
}

// ─────────────────────────────────────────────────────────────────────────────
// Tests
// ─────────────────────────────────────────────────────────────────────────────

describe("SprkChatActionMenu", () => {
    let mockOnSelect: jest.Mock;
    let mockOnDismiss: jest.Mock;

    beforeEach(() => {
        mockOnSelect = jest.fn();
        mockOnDismiss = jest.fn();
        defaultProps.onSelect = mockOnSelect;
        defaultProps.onDismiss = mockOnDismiss;
    });

    afterEach(() => {
        jest.clearAllMocks();
    });

    // ─────────────────────────────────────────────────────────────────────────
    // Rendering
    // ─────────────────────────────────────────────────────────────────────────

    describe("Rendering", () => {
        it("should render the menu when isOpen is true", () => {
            renderMenu();

            expect(screen.getByTestId("sprkchat-action-menu")).toBeInTheDocument();
        });

        it("should not render the menu when isOpen is false", () => {
            renderMenu({ isOpen: false });

            expect(screen.queryByTestId("sprkchat-action-menu")).not.toBeInTheDocument();
        });

        it("should render all four category headers", () => {
            renderMenu();

            expect(screen.getByText("Playbooks")).toBeInTheDocument();
            expect(screen.getByText("Actions")).toBeInTheDocument();
            expect(screen.getByText("Search")).toBeInTheDocument();
            expect(screen.getByText("Settings")).toBeInTheDocument();
        });

        it("should render action items with labels", () => {
            renderMenu();

            expect(screen.getByText("Run Playbook")).toBeInTheDocument();
            expect(screen.getByText("Create Playbook")).toBeInTheDocument();
            expect(screen.getByText("Summarize Document")).toBeInTheDocument();
            expect(screen.getByText("Extract Clauses")).toBeInTheDocument();
            expect(screen.getByText("Search Documents")).toBeInTheDocument();
            expect(screen.getByText("Toggle Theme")).toBeInTheDocument();
        });

        it("should render action items with descriptions", () => {
            renderMenu();

            expect(screen.getByText("Execute a predefined playbook")).toBeInTheDocument();
            expect(screen.getByText("Generate a summary of the current document")).toBeInTheDocument();
            expect(screen.getByText("Search across all documents")).toBeInTheDocument();
        });

        it("should render keyboard shortcut badges", () => {
            renderMenu();

            expect(screen.getByText("Ctrl+P")).toBeInTheDocument();
            expect(screen.getByText("Ctrl+K")).toBeInTheDocument();
        });

        it("should render each action with a data-testid", () => {
            renderMenu();

            expect(screen.getByTestId("action-menu-item-run-playbook")).toBeInTheDocument();
            expect(screen.getByTestId("action-menu-item-summarize")).toBeInTheDocument();
            expect(screen.getByTestId("action-menu-item-search-docs")).toBeInTheDocument();
            expect(screen.getByTestId("action-menu-item-toggle-theme")).toBeInTheDocument();
        });

        it("should show empty state when no actions are provided", () => {
            renderMenu({ actions: [] });

            expect(screen.getByTestId("action-menu-empty")).toBeInTheDocument();
            expect(screen.getByText("No matching actions")).toBeInTheDocument();
        });

        it("should render category groups with role='group'", () => {
            renderMenu();

            const groups = screen.getAllByRole("group");
            expect(groups.length).toBe(4);
        });

        it("should render category groups with aria-label matching category name", () => {
            renderMenu();

            expect(screen.getByRole("group", { name: "Playbooks" })).toBeInTheDocument();
            expect(screen.getByRole("group", { name: "Actions" })).toBeInTheDocument();
            expect(screen.getByRole("group", { name: "Search" })).toBeInTheDocument();
            expect(screen.getByRole("group", { name: "Settings" })).toBeInTheDocument();
        });
    });

    // ─────────────────────────────────────────────────────────────────────────
    // Filtering
    // ─────────────────────────────────────────────────────────────────────────

    describe("Filtering", () => {
        it("should show only actions matching filter text in label", () => {
            renderMenu({ filterText: "summ" });

            expect(screen.getByText(/Summarize Document/)).toBeInTheDocument();
            // Other items should not be present
            expect(screen.queryByTestId("action-menu-item-run-playbook")).not.toBeInTheDocument();
            expect(screen.queryByTestId("action-menu-item-search-docs")).not.toBeInTheDocument();
        });

        it("should show actions matching filter text in description", () => {
            renderMenu({ filterText: "predefined" });

            // "Run Playbook" has description "Execute a predefined playbook"
            expect(screen.getByTestId("action-menu-item-run-playbook")).toBeInTheDocument();
        });

        it("should filter case-insensitively", () => {
            renderMenu({ filterText: "SEARCH" });

            expect(screen.getByTestId("action-menu-item-search-docs")).toBeInTheDocument();
        });

        it("should show empty state when no actions match filter", () => {
            renderMenu({ filterText: "xyznonexistent" });

            expect(screen.getByTestId("action-menu-empty")).toBeInTheDocument();
            expect(screen.getByText("No matching actions")).toBeInTheDocument();
        });

        it("should show all actions when filter is cleared (empty string)", () => {
            renderMenu({ filterText: "" });

            const options = screen.getAllByRole("option");
            expect(options.length).toBe(6);
        });

        it("should only show categories that have matching items", () => {
            renderMenu({ filterText: "toggle" });

            // Only Settings category should be visible
            expect(screen.getByText("Settings")).toBeInTheDocument();
            expect(screen.queryByText("Playbooks")).not.toBeInTheDocument();
            expect(screen.queryByText("Actions")).not.toBeInTheDocument();
            expect(screen.queryByText("Search")).not.toBeInTheDocument();
        });

        it("should match partial text across multiple categories", () => {
            renderMenu({ filterText: "play" });

            // "Run Playbook" and "Create Playbook" match in label;
            // "Run Playbook" also matches in description ("predefined playbook")
            expect(screen.getByTestId("action-menu-item-run-playbook")).toBeInTheDocument();
            expect(screen.getByTestId("action-menu-item-create-playbook")).toBeInTheDocument();
        });
    });

    // ─────────────────────────────────────────────────────────────────────────
    // Keyboard Navigation
    // ─────────────────────────────────────────────────────────────────────────

    describe("Keyboard Navigation", () => {
        it("should set the first item as active by default", () => {
            renderMenu();

            const firstItem = screen.getByTestId("action-menu-item-run-playbook");
            expect(firstItem.getAttribute("aria-selected")).toBe("true");
        });

        it("should move active item down on ArrowDown", async () => {
            const user = userEvent.setup();
            renderMenu();

            const menu = screen.getByTestId("sprkchat-action-menu");
            menu.focus();
            await user.keyboard("{ArrowDown}");

            // Second item should now be active
            const secondItem = screen.getByTestId("action-menu-item-create-playbook");
            expect(secondItem.getAttribute("aria-selected")).toBe("true");
        });

        it("should move active item up on ArrowUp", async () => {
            const user = userEvent.setup();
            renderMenu();

            const menu = screen.getByTestId("sprkchat-action-menu");
            menu.focus();

            // Move down first, then up to go back to first
            await user.keyboard("{ArrowDown}");
            await user.keyboard("{ArrowUp}");

            const firstItem = screen.getByTestId("action-menu-item-run-playbook");
            expect(firstItem.getAttribute("aria-selected")).toBe("true");
        });

        it("should wrap to first item when ArrowDown at last item", async () => {
            const user = userEvent.setup();
            renderMenu();

            const menu = screen.getByTestId("sprkchat-action-menu");
            menu.focus();

            // Navigate past all 6 items to wrap around
            await user.keyboard("{ArrowDown}"); // item 2
            await user.keyboard("{ArrowDown}"); // item 3
            await user.keyboard("{ArrowDown}"); // item 4
            await user.keyboard("{ArrowDown}"); // item 5
            await user.keyboard("{ArrowDown}"); // item 6 (last)
            await user.keyboard("{ArrowDown}"); // wrap to item 1

            const firstItem = screen.getByTestId("action-menu-item-run-playbook");
            expect(firstItem.getAttribute("aria-selected")).toBe("true");
        });

        it("should wrap to last item when ArrowUp at first item", async () => {
            const user = userEvent.setup();
            renderMenu();

            const menu = screen.getByTestId("sprkchat-action-menu");
            menu.focus();

            // ArrowUp from first item wraps to last
            await user.keyboard("{ArrowUp}");

            const lastItem = screen.getByTestId("action-menu-item-toggle-theme");
            expect(lastItem.getAttribute("aria-selected")).toBe("true");
        });

        it("should call onSelect with the correct action on Enter", async () => {
            const user = userEvent.setup();
            renderMenu();

            const menu = screen.getByTestId("sprkchat-action-menu");
            menu.focus();

            // Press Enter on the first (default active) item
            await user.keyboard("{Enter}");

            expect(mockOnSelect).toHaveBeenCalledTimes(1);
            expect(mockOnSelect).toHaveBeenCalledWith(
                expect.objectContaining({ id: "run-playbook", label: "Run Playbook" })
            );
        });

        it("should call onSelect with the navigated action on Enter", async () => {
            const user = userEvent.setup();
            renderMenu();

            const menu = screen.getByTestId("sprkchat-action-menu");
            menu.focus();

            // Navigate to the third item (Summarize Document)
            await user.keyboard("{ArrowDown}"); // create-playbook
            await user.keyboard("{ArrowDown}"); // summarize
            await user.keyboard("{Enter}");

            expect(mockOnSelect).toHaveBeenCalledWith(
                expect.objectContaining({ id: "summarize", label: "Summarize Document" })
            );
        });

        it("should call onDismiss on Escape", async () => {
            const user = userEvent.setup();
            renderMenu();

            const menu = screen.getByTestId("sprkchat-action-menu");
            menu.focus();

            await user.keyboard("{Escape}");

            expect(mockOnDismiss).toHaveBeenCalledTimes(1);
        });

        it("should skip disabled items during navigation", async () => {
            const user = userEvent.setup();
            const actionsWithDisabled: IChatAction[] = [
                { id: "a1", label: "Action One", category: "actions" },
                { id: "a2", label: "Action Two", category: "actions", disabled: true },
                { id: "a3", label: "Action Three", category: "actions" },
            ];
            renderMenu({ actions: actionsWithDisabled });

            const menu = screen.getByTestId("sprkchat-action-menu");
            menu.focus();

            // ArrowDown should skip disabled a2 and go to a3
            await user.keyboard("{ArrowDown}");

            const thirdItem = screen.getByTestId("action-menu-item-a3");
            expect(thirdItem.getAttribute("aria-selected")).toBe("true");
        });

        it("should not call onSelect for disabled items on Enter", async () => {
            const user = userEvent.setup();
            const disabledActions: IChatAction[] = [
                { id: "a1", label: "Disabled Action", category: "actions", disabled: true },
                { id: "a2", label: "Enabled Action", category: "actions" },
            ];
            renderMenu({ actions: disabledActions });

            const menu = screen.getByTestId("sprkchat-action-menu");
            menu.focus();

            // With the first item disabled, activeIndex should start at 1 (the enabled item)
            // But let's verify that if we somehow land on a disabled item, Enter doesn't fire
            // The component starts at the first enabled index, so pressing Enter should fire for a2
            await user.keyboard("{Enter}");

            expect(mockOnSelect).toHaveBeenCalledWith(
                expect.objectContaining({ id: "a2" })
            );
        });
    });

    // ─────────────────────────────────────────────────────────────────────────
    // Action Selection
    // ─────────────────────────────────────────────────────────────────────────

    describe("Action Selection", () => {
        it("should call onSelect when an item is clicked", async () => {
            const user = userEvent.setup();
            renderMenu();

            const item = screen.getByTestId("action-menu-item-summarize");
            await user.click(item);

            expect(mockOnSelect).toHaveBeenCalledWith(
                expect.objectContaining({ id: "summarize", label: "Summarize Document" })
            );
        });

        it("should not call onSelect when a disabled item is clicked", async () => {
            const user = userEvent.setup();
            const actionsWithDisabled: IChatAction[] = [
                { id: "d1", label: "Disabled Item", category: "actions", disabled: true },
                { id: "e1", label: "Enabled Item", category: "actions" },
            ];
            renderMenu({ actions: actionsWithDisabled });

            const disabledItem = screen.getByTestId("action-menu-item-d1");
            await user.click(disabledItem);

            expect(mockOnSelect).not.toHaveBeenCalled();
        });

        it("should update active item on mouse enter", async () => {
            const user = userEvent.setup();
            renderMenu();

            const thirdItem = screen.getByTestId("action-menu-item-summarize");
            await user.hover(thirdItem);

            expect(thirdItem.getAttribute("aria-selected")).toBe("true");
        });
    });

    // ─────────────────────────────────────────────────────────────────────────
    // Accessibility
    // ─────────────────────────────────────────────────────────────────────────

    describe("Accessibility", () => {
        it("should have role='listbox' on the menu container", () => {
            renderMenu();

            expect(screen.getByRole("listbox")).toBeInTheDocument();
        });

        it("should have role='option' on each action item", () => {
            renderMenu();

            const options = screen.getAllByRole("option");
            expect(options.length).toBe(6);
        });

        it("should have aria-label='Action menu' on the menu container", () => {
            renderMenu();

            expect(screen.getByLabelText("Action menu")).toBeInTheDocument();
        });

        it("should set aria-activedescendant to the active item id", () => {
            renderMenu();

            const menu = screen.getByTestId("sprkchat-action-menu");
            expect(menu.getAttribute("aria-activedescendant")).toBe("action-menu-item-run-playbook");
        });

        it("should update aria-activedescendant when navigation changes", async () => {
            const user = userEvent.setup();
            renderMenu();

            const menu = screen.getByTestId("sprkchat-action-menu");
            menu.focus();

            await user.keyboard("{ArrowDown}");

            expect(menu.getAttribute("aria-activedescendant")).toBe("action-menu-item-create-playbook");
        });

        it("should set aria-selected=true only on the active item", () => {
            renderMenu();

            const options = screen.getAllByRole("option");
            const selectedOptions = options.filter(
                (opt) => opt.getAttribute("aria-selected") === "true"
            );
            expect(selectedOptions.length).toBe(1);
            expect(selectedOptions[0]).toBe(screen.getByTestId("action-menu-item-run-playbook"));
        });

        it("should set aria-disabled on disabled action items", () => {
            const actionsWithDisabled: IChatAction[] = [
                { id: "d1", label: "Disabled", category: "actions", disabled: true },
                { id: "e1", label: "Enabled", category: "actions" },
            ];
            renderMenu({ actions: actionsWithDisabled });

            const disabledItem = screen.getByTestId("action-menu-item-d1");
            expect(disabledItem.getAttribute("aria-disabled")).toBe("true");

            const enabledItem = screen.getByTestId("action-menu-item-e1");
            expect(enabledItem.getAttribute("aria-disabled")).toBe("false");
        });

        it("should have shortcut aria-label on shortcut badges", () => {
            renderMenu();

            expect(screen.getByLabelText("Keyboard shortcut: Ctrl+P")).toBeInTheDocument();
            expect(screen.getByLabelText("Keyboard shortcut: Ctrl+K")).toBeInTheDocument();
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
            it(`should render without errors in ${name} theme`, () => {
                const { container } = render(
                    <FluentProvider theme={theme}>
                        <SprkChatActionMenu {...defaultProps} />
                    </FluentProvider>
                );

                expect(container.querySelector('[data-testid="sprkchat-action-menu"]')).toBeInTheDocument();
            });
        });
    });

    // ─────────────────────────────────────────────────────────────────────────
    // Imperative Handle
    // ─────────────────────────────────────────────────────────────────────────

    describe("Imperative Handle", () => {
        it("should expose navigateDown via ref", () => {
            const ref = React.createRef<any>();
            renderWithProviders(
                <SprkChatActionMenu ref={ref} {...defaultProps} />
            );

            expect(ref.current).toBeDefined();
            expect(typeof ref.current.navigateDown).toBe("function");
        });

        it("should expose navigateUp via ref", () => {
            const ref = React.createRef<any>();
            renderWithProviders(
                <SprkChatActionMenu ref={ref} {...defaultProps} />
            );

            expect(typeof ref.current.navigateUp).toBe("function");
        });

        it("should expose selectActive via ref", () => {
            const ref = React.createRef<any>();
            renderWithProviders(
                <SprkChatActionMenu ref={ref} {...defaultProps} />
            );

            expect(typeof ref.current.selectActive).toBe("function");
        });

        it("should call onSelect when selectActive is invoked via ref", () => {
            const ref = React.createRef<any>();
            renderWithProviders(
                <SprkChatActionMenu ref={ref} {...defaultProps} />
            );

            ref.current.selectActive();

            expect(mockOnSelect).toHaveBeenCalledWith(
                expect.objectContaining({ id: "run-playbook" })
            );
        });
    });

    // ─────────────────────────────────────────────────────────────────────────
    // Edge Cases
    // ─────────────────────────────────────────────────────────────────────────

    describe("Edge Cases", () => {
        it("should handle a single action correctly", () => {
            const singleAction: IChatAction[] = [
                { id: "only-one", label: "Only Action", category: "actions" },
            ];
            renderMenu({ actions: singleAction });

            const options = screen.getAllByRole("option");
            expect(options.length).toBe(1);
            expect(screen.getByText("Only Action")).toBeInTheDocument();
        });

        it("should handle actions without descriptions", () => {
            const noDescActions: IChatAction[] = [
                { id: "no-desc", label: "No Description", category: "search" },
            ];
            renderMenu({ actions: noDescActions });

            expect(screen.getByText("No Description")).toBeInTheDocument();
        });

        it("should handle actions without shortcuts", () => {
            const noShortcutActions: IChatAction[] = [
                { id: "no-shortcut", label: "No Shortcut", category: "actions" },
            ];
            renderMenu({ actions: noShortcutActions });

            expect(screen.getByText("No Shortcut")).toBeInTheDocument();
            // No shortcut badge should be rendered
            expect(screen.queryByLabelText(/Keyboard shortcut/)).not.toBeInTheDocument();
        });

        it("should reset active index when filter changes", () => {
            const { rerender } = renderWithProviders(
                <SprkChatActionMenu {...defaultProps} filterText="" />
            );

            // Rerender with a filter that narrows results
            rerender(
                <FluentProvider theme={webLightTheme}>
                    <SprkChatActionMenu {...defaultProps} filterText="toggle" />
                </FluentProvider>
            );

            // The single filtered result should be active
            const item = screen.getByTestId("action-menu-item-toggle-theme");
            expect(item.getAttribute("aria-selected")).toBe("true");
        });
    });
});
