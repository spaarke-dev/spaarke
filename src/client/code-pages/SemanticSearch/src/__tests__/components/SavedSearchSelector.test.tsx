/**
 * Unit tests for SavedSearchSelector component
 *
 * Covers:
 *   - Default trigger button rendering with fallback text
 *   - Custom currentSearchName display
 *   - Default system searches in menu
 *   - Personal saved searches rendering
 *   - Loading state with spinner
 *   - Empty personal searches state
 *   - "Save Current Search" action item
 *   - Selection callback invocations
 */

import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import { FluentProvider, webLightTheme } from "@fluentui/react-components";
import { SavedSearchSelector, type SavedSearchSelectorProps } from "../../components/SavedSearchSelector";
import type { SavedSearch, SearchFilters } from "../../types";

// ---------------------------------------------------------------------------
// Mock icons
// ---------------------------------------------------------------------------

jest.mock("@fluentui/react-icons", () => ({
    ChevronDownRegular: () => <span data-testid="icon-chevron-down" />,
    SaveRegular: () => <span data-testid="icon-save" />,
    StarRegular: () => <span data-testid="icon-star" />,
}));

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const DEFAULT_FILTERS: SearchFilters = {
    documentTypes: [],
    fileTypes: [],
    matterTypes: [],
    dateRange: { from: null, to: null },
    threshold: 50,
    searchMode: "rrf",
};

function makeSavedSearch(overrides: Partial<SavedSearch> = {}): SavedSearch {
    return {
        id: "test-1",
        name: "Test Search",
        searchDomain: "documents",
        query: "test",
        filters: DEFAULT_FILTERS,
        viewMode: "grid",
        columns: ["name"],
        sortColumn: "name",
        sortDirection: "asc",
        ...overrides,
    };
}

const DEFAULT_PROPS: SavedSearchSelectorProps = {
    savedSearches: [],
    currentSearchName: null,
    onSelectSavedSearch: jest.fn(),
    onSaveCurrentSearch: jest.fn(),
    isLoading: false,
};

function renderSelector(props: Partial<SavedSearchSelectorProps> = {}) {
    return render(
        <FluentProvider theme={webLightTheme}>
            <SavedSearchSelector {...DEFAULT_PROPS} {...props} />
        </FluentProvider>,
    );
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe("SavedSearchSelector", () => {
    beforeEach(() => {
        jest.clearAllMocks();
    });

    // ---- Trigger Button ----

    it("renders trigger button with 'Saved Searches' when currentSearchName is null", () => {
        renderSelector({ currentSearchName: null });

        expect(screen.getByText("Saved Searches")).toBeInTheDocument();
    });

    it("renders trigger button with current search name when provided", () => {
        renderSelector({ currentSearchName: "My Custom Filter" });

        expect(screen.getByText("My Custom Filter")).toBeInTheDocument();
    });

    it("renders star icon on trigger button", () => {
        renderSelector();

        expect(screen.getByTestId("icon-star")).toBeInTheDocument();
    });

    it("renders chevron-down icon on trigger button", () => {
        renderSelector();

        expect(screen.getByTestId("icon-chevron-down")).toBeInTheDocument();
    });

    // ---- Menu Content (requires opening the menu) ----

    describe("when menu is opened", () => {
        function openMenu() {
            const triggerButton = screen.getByText("Saved Searches").closest("button");
            if (triggerButton) {
                fireEvent.click(triggerButton);
            }
        }

        it("shows Default Searches group header", () => {
            renderSelector();
            openMenu();

            expect(screen.getByText("Default Searches")).toBeInTheDocument();
        });

        it("shows all four default searches", () => {
            renderSelector();
            openMenu();

            expect(screen.getByText("All Documents")).toBeInTheDocument();
            expect(screen.getByText("All Matters")).toBeInTheDocument();
            expect(screen.getByText("Recent Documents")).toBeInTheDocument();
            expect(screen.getByText("High Similarity")).toBeInTheDocument();
        });

        it("shows My Searches group header", () => {
            renderSelector();
            openMenu();

            expect(screen.getByText("My Searches")).toBeInTheDocument();
        });

        it("shows 'No saved searches' when personal list is empty and not loading", () => {
            renderSelector({ savedSearches: [], isLoading: false });
            openMenu();

            expect(screen.getByText("No saved searches")).toBeInTheDocument();
        });

        it("shows spinner when personal searches are loading", () => {
            renderSelector({ isLoading: true });
            openMenu();

            expect(screen.getByText("Loading...")).toBeInTheDocument();
        });

        it("shows personal saved searches when provided", () => {
            const personal = [
                makeSavedSearch({ id: "p1", name: "Contracts Q1" }),
                makeSavedSearch({ id: "p2", name: "Invoices Review" }),
            ];
            renderSelector({ savedSearches: personal });
            openMenu();

            expect(screen.getByText("Contracts Q1")).toBeInTheDocument();
            expect(screen.getByText("Invoices Review")).toBeInTheDocument();
        });

        it("does not show 'No saved searches' when personal searches exist", () => {
            const personal = [makeSavedSearch({ id: "p1", name: "My Search" })];
            renderSelector({ savedSearches: personal });
            openMenu();

            expect(screen.queryByText("No saved searches")).not.toBeInTheDocument();
        });

        it("shows 'Save Current Search' action", () => {
            renderSelector();
            openMenu();

            expect(screen.getByText("Save Current Search")).toBeInTheDocument();
        });

        it("renders save icon on 'Save Current Search' item", () => {
            renderSelector();
            openMenu();

            expect(screen.getByTestId("icon-save")).toBeInTheDocument();
        });
    });

    // ---- Callbacks ----

    describe("callback invocations", () => {
        function openMenu() {
            const triggerButton = screen.getByText("Saved Searches").closest("button");
            if (triggerButton) {
                fireEvent.click(triggerButton);
            }
        }

        it("calls onSelectSavedSearch when a default search is clicked", () => {
            const onSelect = jest.fn();
            renderSelector({ onSelectSavedSearch: onSelect });
            openMenu();

            fireEvent.click(screen.getByText("All Documents"));

            expect(onSelect).toHaveBeenCalledTimes(1);
            expect(onSelect.mock.calls[0][0]).toMatchObject({
                id: "default-all-documents",
                name: "All Documents",
                searchDomain: "documents",
            });
        });

        it("calls onSelectSavedSearch when a personal search is clicked", () => {
            const onSelect = jest.fn();
            const personal = [makeSavedSearch({ id: "my-1", name: "My Filter" })];
            renderSelector({ onSelectSavedSearch: onSelect, savedSearches: personal });
            openMenu();

            fireEvent.click(screen.getByText("My Filter"));

            expect(onSelect).toHaveBeenCalledTimes(1);
            expect(onSelect.mock.calls[0][0]).toMatchObject({
                id: "my-1",
                name: "My Filter",
            });
        });

        it("calls onSaveCurrentSearch when 'Save Current Search' is clicked", () => {
            const onSave = jest.fn();
            renderSelector({ onSaveCurrentSearch: onSave });
            openMenu();

            fireEvent.click(screen.getByText("Save Current Search"));

            expect(onSave).toHaveBeenCalledTimes(1);
        });
    });

    // ---- Edge Cases ----

    it("handles long currentSearchName with truncation via CSS", () => {
        const longName = "This is a very long saved search name that should be truncated by CSS";
        renderSelector({ currentSearchName: longName });

        expect(screen.getByText(longName)).toBeInTheDocument();
    });
});
