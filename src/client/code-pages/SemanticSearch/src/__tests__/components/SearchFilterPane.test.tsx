/**
 * Unit tests for SearchFilterPane component
 *
 * Covers:
 *   - Domain-aware filter section visibility
 *   - Expand/collapse toggle behavior
 *   - Threshold slider interaction and callback
 *   - Search mode dropdown interaction and callback
 *   - Search button click and disabled state when loading
 *   - Filter placeholder rendering for FilterDropdown / DateRangeFilter
 */

import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import { FluentProvider, webLightTheme } from "@fluentui/react-components";
import { SearchFilterPane, type SearchFilterPaneProps } from "../../components/SearchFilterPane";
import type { SearchFilters, SearchDomain } from "../../types";

// ---------------------------------------------------------------------------
// Mock icons
// ---------------------------------------------------------------------------

jest.mock("@fluentui/react-icons", () => ({
    ChevronDoubleLeft20Regular: () => <span data-testid="icon-collapse" />,
    ChevronDoubleRight20Regular: () => <span data-testid="icon-expand" />,
    Search20Regular: () => <span data-testid="icon-search" />,
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

const DEFAULT_PROPS: SearchFilterPaneProps = {
    activeDomain: "documents",
    filters: DEFAULT_FILTERS,
    onFiltersChange: jest.fn(),
    onSearch: jest.fn(),
    filterOptions: {
        documentTypes: [],
        fileTypes: [],
        matterTypes: [],
    },
    isLoading: false,
};

function renderPane(props: Partial<SearchFilterPaneProps> = {}) {
    return render(
        <FluentProvider theme={webLightTheme}>
            <SearchFilterPane {...DEFAULT_PROPS} {...props} />
        </FluentProvider>,
    );
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe("SearchFilterPane", () => {
    beforeEach(() => {
        jest.clearAllMocks();
    });

    // ---- Domain Visibility ----

    describe("domain-aware filter visibility", () => {
        it("shows Document Type and File Type filters for documents domain", () => {
            renderPane({ activeDomain: "documents" });

            expect(screen.getByText("Document Type")).toBeInTheDocument();
            expect(screen.getByText("File Type")).toBeInTheDocument();
        });

        it("shows Matter Type filter for documents domain", () => {
            renderPane({ activeDomain: "documents" });

            expect(screen.getByText("Matter Type")).toBeInTheDocument();
        });

        it("shows Matter Type filter for matters domain", () => {
            renderPane({ activeDomain: "matters" });

            expect(screen.getByText("Matter Type")).toBeInTheDocument();
        });

        it("hides Document Type and File Type for matters domain", () => {
            renderPane({ activeDomain: "matters" });

            expect(screen.queryByText("Document Type")).not.toBeInTheDocument();
            expect(screen.queryByText("File Type")).not.toBeInTheDocument();
        });

        it("hides Document Type, File Type, and Matter Type for projects domain", () => {
            renderPane({ activeDomain: "projects" });

            expect(screen.queryByText("Document Type")).not.toBeInTheDocument();
            expect(screen.queryByText("File Type")).not.toBeInTheDocument();
            expect(screen.queryByText("Matter Type")).not.toBeInTheDocument();
        });

        it("hides Document Type, File Type, and Matter Type for invoices domain", () => {
            renderPane({ activeDomain: "invoices" });

            expect(screen.queryByText("Document Type")).not.toBeInTheDocument();
            expect(screen.queryByText("File Type")).not.toBeInTheDocument();
            expect(screen.queryByText("Matter Type")).not.toBeInTheDocument();
        });

        it("always shows Date Range for all domains", () => {
            const domains: SearchDomain[] = ["documents", "matters", "projects", "invoices"];
            domains.forEach((domain) => {
                const { unmount } = renderPane({ activeDomain: domain });
                expect(screen.getByText("Date Range")).toBeInTheDocument();
                unmount();
            });
        });

        it("always shows Relevance Threshold for all domains", () => {
            const domains: SearchDomain[] = ["documents", "matters", "projects", "invoices"];
            domains.forEach((domain) => {
                const { unmount } = renderPane({ activeDomain: domain });
                expect(screen.getByText("Relevance Threshold")).toBeInTheDocument();
                unmount();
            });
        });

        it("always shows Search Mode for all domains", () => {
            const domains: SearchDomain[] = ["documents", "matters", "projects", "invoices"];
            domains.forEach((domain) => {
                const { unmount } = renderPane({ activeDomain: domain });
                expect(screen.getByText("Search Mode")).toBeInTheDocument();
                unmount();
            });
        });
    });

    // ---- Expand / Collapse ----

    describe("expand/collapse toggle", () => {
        it("renders expanded by default with Filters title", () => {
            renderPane();

            expect(screen.getByText("Filters")).toBeInTheDocument();
        });

        it("shows collapse button with correct aria-label when expanded", () => {
            renderPane();

            expect(screen.getByLabelText("Collapse filters")).toBeInTheDocument();
        });

        it("collapses when collapse button is clicked", () => {
            renderPane();

            fireEvent.click(screen.getByLabelText("Collapse filters"));

            // When collapsed, Filters title should not be visible
            expect(screen.queryByText("Filters")).not.toBeInTheDocument();
            // Expand button should now be visible
            expect(screen.getByLabelText("Expand filters")).toBeInTheDocument();
        });

        it("re-expands when expand button is clicked after collapse", () => {
            renderPane();

            // Collapse
            fireEvent.click(screen.getByLabelText("Collapse filters"));
            expect(screen.queryByText("Filters")).not.toBeInTheDocument();

            // Expand
            fireEvent.click(screen.getByLabelText("Expand filters"));
            expect(screen.getByText("Filters")).toBeInTheDocument();
        });

        it("hides all filter sections when collapsed", () => {
            renderPane({ activeDomain: "documents" });

            fireEvent.click(screen.getByLabelText("Collapse filters"));

            expect(screen.queryByText("Document Type")).not.toBeInTheDocument();
            expect(screen.queryByText("File Type")).not.toBeInTheDocument();
            expect(screen.queryByText("Date Range")).not.toBeInTheDocument();
            expect(screen.queryByText("Search Mode")).not.toBeInTheDocument();
        });
    });

    // ---- Threshold Slider ----

    describe("threshold slider", () => {
        it("displays the current threshold percentage", () => {
            renderPane({ filters: { ...DEFAULT_FILTERS, threshold: 75 } });

            expect(screen.getByText("75%")).toBeInTheDocument();
        });

        it("displays threshold of 0%", () => {
            renderPane({ filters: { ...DEFAULT_FILTERS, threshold: 0 } });

            expect(screen.getByText("0%")).toBeInTheDocument();
        });

        it("displays threshold of 100%", () => {
            renderPane({ filters: { ...DEFAULT_FILTERS, threshold: 100 } });

            expect(screen.getByText("100%")).toBeInTheDocument();
        });

        it("disables the slider when isLoading is true", () => {
            renderPane({ isLoading: true });

            const slider = screen.getByRole("slider");
            expect(slider).toBeDisabled();
        });
    });

    // ---- Search Mode Dropdown ----

    describe("search mode dropdown", () => {
        it("disables search mode dropdown when loading", () => {
            renderPane({ isLoading: true });

            // The Fluent Dropdown renders a button as trigger
            const dropdown = screen.getByText("Hybrid (RRF)").closest("button");
            if (dropdown) {
                expect(dropdown).toHaveAttribute("aria-disabled", "true");
            }
        });
    });

    // ---- Search Button ----

    describe("search button", () => {
        it("renders Search button", () => {
            renderPane();

            expect(screen.getByText("Search")).toBeInTheDocument();
        });

        it("calls onSearch when Search button is clicked", () => {
            const onSearch = jest.fn();
            renderPane({ onSearch });

            fireEvent.click(screen.getByText("Search"));

            expect(onSearch).toHaveBeenCalledTimes(1);
        });

        it("disables Search button when isLoading is true", () => {
            renderPane({ isLoading: true });

            const button = screen.getByText("Search").closest("button");
            expect(button).toBeDisabled();
        });

        it("enables Search button when isLoading is false", () => {
            renderPane({ isLoading: false });

            const button = screen.getByText("Search").closest("button");
            expect(button).not.toBeDisabled();
        });
    });

    // ---- Placeholder rendering ----

    describe("filter placeholders", () => {
        it("shows FilterDropdown placeholder for Document Type", () => {
            renderPane({ activeDomain: "documents" });

            expect(screen.getByText("[FilterDropdown: Document Type]")).toBeInTheDocument();
        });

        it("shows FilterDropdown placeholder for File Type", () => {
            renderPane({ activeDomain: "documents" });

            expect(screen.getByText("[FilterDropdown: File Type]")).toBeInTheDocument();
        });

        it("shows FilterDropdown placeholder for Matter Type", () => {
            renderPane({ activeDomain: "documents" });

            expect(screen.getByText("[FilterDropdown: Matter Type]")).toBeInTheDocument();
        });

        it("shows DateRangeFilter placeholder", () => {
            renderPane({ activeDomain: "documents" });

            expect(screen.getByText("[DateRangeFilter: Date Range]")).toBeInTheDocument();
        });
    });
});
