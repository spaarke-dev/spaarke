/**
 * Unit tests for ViewToggleToolbar component
 *
 * Covers:
 *   - Grid/Graph toggle button rendering and checked state
 *   - View mode toggle callbacks
 *   - Cluster-by dropdown visibility (only in graph mode)
 *   - Cluster-by option selection callback
 *   - SavedSearchSelector integration (rendered as child)
 */

import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import { FluentProvider, webLightTheme } from "@fluentui/react-components";
import { ViewToggleToolbar, type ViewToggleToolbarProps } from "../../components/ViewToggleToolbar";
import type { SavedSearch, SearchFilters } from "../../types";

// ---------------------------------------------------------------------------
// Mock icons
// ---------------------------------------------------------------------------

jest.mock("@fluentui/react-icons", () => ({
    GridRegular: () => <span data-testid="icon-graph" />,
    TextBulletListSquareRegular: () => <span data-testid="icon-grid" />,
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

const MOCK_SAVED_SEARCH: SavedSearch = {
    id: "saved-1",
    name: "My Search",
    searchDomain: "documents",
    query: "test",
    filters: DEFAULT_FILTERS,
    viewMode: "grid",
    columns: ["name"],
    sortColumn: "name",
    sortDirection: "asc",
};

const DEFAULT_PROPS: ViewToggleToolbarProps = {
    viewMode: "grid",
    onViewModeChange: jest.fn(),
    clusterBy: "MatterType",
    onClusterByChange: jest.fn(),
    savedSearches: [],
    currentSearchName: null,
    onSelectSavedSearch: jest.fn(),
    onSaveCurrentSearch: jest.fn(),
    isSavedSearchesLoading: false,
};

function renderToolbar(props: Partial<ViewToggleToolbarProps> = {}) {
    return render(
        <FluentProvider theme={webLightTheme}>
            <ViewToggleToolbar {...DEFAULT_PROPS} {...props} />
        </FluentProvider>,
    );
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe("ViewToggleToolbar", () => {
    beforeEach(() => {
        jest.clearAllMocks();
    });

    // ---- Rendering ----

    it("renders Grid and Graph toggle buttons", () => {
        renderToolbar();

        expect(screen.getByText("Grid")).toBeInTheDocument();
        expect(screen.getByText("Graph")).toBeInTheDocument();
    });

    it("renders Grid button with primary appearance when in grid mode", () => {
        renderToolbar({ viewMode: "grid" });

        const gridButton = screen.getByText("Grid").closest("button");
        // Fluent ToggleButton sets aria-pressed when checked
        expect(gridButton).toHaveAttribute("aria-pressed", "true");
    });

    it("renders Graph button with primary appearance when in graph mode", () => {
        renderToolbar({ viewMode: "graph" });

        const graphButton = screen.getByText("Graph").closest("button");
        expect(graphButton).toHaveAttribute("aria-pressed", "true");
    });

    // ---- View Mode Toggle ----

    it("calls onViewModeChange with 'grid' when Grid button is clicked", () => {
        const onViewModeChange = jest.fn();
        renderToolbar({ onViewModeChange, viewMode: "graph" });

        fireEvent.click(screen.getByText("Grid"));

        expect(onViewModeChange).toHaveBeenCalledTimes(1);
        expect(onViewModeChange).toHaveBeenCalledWith("grid");
    });

    it("calls onViewModeChange with 'graph' when Graph button is clicked", () => {
        const onViewModeChange = jest.fn();
        renderToolbar({ onViewModeChange, viewMode: "grid" });

        fireEvent.click(screen.getByText("Graph"));

        expect(onViewModeChange).toHaveBeenCalledTimes(1);
        expect(onViewModeChange).toHaveBeenCalledWith("graph");
    });

    // ---- Cluster-by Dropdown Visibility ----

    it("does NOT show cluster-by dropdown in grid mode", () => {
        renderToolbar({ viewMode: "grid" });

        expect(screen.queryByText("Matter Type")).not.toBeInTheDocument();
    });

    it("shows cluster-by dropdown in graph mode", () => {
        renderToolbar({ viewMode: "graph" });

        // The default clusterBy is "MatterType", which displays as "Matter Type"
        expect(screen.getByText("Matter Type")).toBeInTheDocument();
    });

    it("displays the selected cluster-by label in graph mode", () => {
        renderToolbar({ viewMode: "graph", clusterBy: "DocumentType" });

        expect(screen.getByText("Document Type")).toBeInTheDocument();
    });

    it("displays all cluster-by options when dropdown is opened in graph mode", () => {
        renderToolbar({ viewMode: "graph", clusterBy: "MatterType" });

        // Click to open the dropdown
        const dropdownTrigger = screen.getByText("Matter Type").closest("button");
        if (dropdownTrigger) {
            fireEvent.click(dropdownTrigger);
        }

        // All five options should appear in the listbox
        const expectedLabels = [
            "Matter Type",
            "Practice Area",
            "Document Type",
            "Organization",
            "Person/Contact",
        ];
        expectedLabels.forEach((label) => {
            expect(screen.getAllByText(label).length).toBeGreaterThanOrEqual(1);
        });
    });

    // ---- Saved Search Selector Integration ----

    it("renders SavedSearchSelector with 'Saved Searches' default text", () => {
        renderToolbar({ currentSearchName: null });

        expect(screen.getByText("Saved Searches")).toBeInTheDocument();
    });

    it("renders SavedSearchSelector with current search name", () => {
        renderToolbar({ currentSearchName: "My Custom Search" });

        expect(screen.getByText("My Custom Search")).toBeInTheDocument();
    });
});
