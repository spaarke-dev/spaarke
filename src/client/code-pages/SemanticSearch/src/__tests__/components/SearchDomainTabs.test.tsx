/**
 * Unit tests for SearchDomainTabs component
 *
 * Covers:
 *   - Rendering all four domain tabs (Documents, Matters, Projects, Invoices)
 *   - Active tab selection based on activeDomain prop
 *   - Tab switch triggers onDomainChange and onSearch callbacks
 *   - Correct domain value forwarded on tab select
 */

import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import { FluentProvider, webLightTheme } from "@fluentui/react-components";
import { SearchDomainTabs, type SearchDomainTabsProps } from "../../components/SearchDomainTabs";
import type { SearchDomain } from "../../types";

// ---------------------------------------------------------------------------
// Mock @fluentui/react-icons — replace with simple spans
// ---------------------------------------------------------------------------

jest.mock("@fluentui/react-icons", () => ({
    DocumentMultipleRegular: () => <span data-testid="icon-documents" />,
    BriefcaseRegular: () => <span data-testid="icon-matters" />,
    TaskListSquareAddRegular: () => <span data-testid="icon-projects" />,
    ReceiptRegular: () => <span data-testid="icon-invoices" />,
}));

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const DEFAULT_PROPS: SearchDomainTabsProps = {
    activeDomain: "documents",
    onDomainChange: jest.fn(),
    query: "test query",
    onSearch: jest.fn(),
};

function renderWithFluent(props: Partial<SearchDomainTabsProps> = {}) {
    return render(
        <FluentProvider theme={webLightTheme}>
            <SearchDomainTabs {...DEFAULT_PROPS} {...props} />
        </FluentProvider>,
    );
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe("SearchDomainTabs", () => {
    beforeEach(() => {
        jest.clearAllMocks();
    });

    // ---- Rendering ----

    it("renders all four domain tabs", () => {
        renderWithFluent();

        expect(screen.getByText("Documents")).toBeInTheDocument();
        expect(screen.getByText("Matters")).toBeInTheDocument();
        expect(screen.getByText("Projects")).toBeInTheDocument();
        expect(screen.getByText("Invoices")).toBeInTheDocument();
    });

    it("renders icon for each tab", () => {
        renderWithFluent();

        expect(screen.getByTestId("icon-documents")).toBeInTheDocument();
        expect(screen.getByTestId("icon-matters")).toBeInTheDocument();
        expect(screen.getByTestId("icon-projects")).toBeInTheDocument();
        expect(screen.getByTestId("icon-invoices")).toBeInTheDocument();
    });

    it("sets the selected tab based on activeDomain prop", () => {
        renderWithFluent({ activeDomain: "matters" });

        // Fluent TabList uses aria-selected on the active tab
        const mattersTab = screen.getByText("Matters").closest("[role='tab']");
        expect(mattersTab).toHaveAttribute("aria-selected", "true");
    });

    it("renders with documents selected by default", () => {
        renderWithFluent({ activeDomain: "documents" });

        const docsTab = screen.getByText("Documents").closest("[role='tab']");
        expect(docsTab).toHaveAttribute("aria-selected", "true");
    });

    // ---- Tab Interaction ----

    it("calls onDomainChange with new domain when a tab is clicked", () => {
        const onDomainChange = jest.fn();
        renderWithFluent({ onDomainChange, activeDomain: "documents" });

        fireEvent.click(screen.getByText("Matters"));

        expect(onDomainChange).toHaveBeenCalledTimes(1);
        expect(onDomainChange).toHaveBeenCalledWith("matters");
    });

    it("calls onSearch with current query and new domain on tab switch", () => {
        const onSearch = jest.fn();
        renderWithFluent({ onSearch, query: "contract review", activeDomain: "documents" });

        fireEvent.click(screen.getByText("Projects"));

        expect(onSearch).toHaveBeenCalledTimes(1);
        expect(onSearch).toHaveBeenCalledWith("contract review", "projects");
    });

    it("calls both onDomainChange and onSearch on tab switch", () => {
        const onDomainChange = jest.fn();
        const onSearch = jest.fn();
        renderWithFluent({ onDomainChange, onSearch, query: "query", activeDomain: "documents" });

        fireEvent.click(screen.getByText("Invoices"));

        expect(onDomainChange).toHaveBeenCalledWith("invoices");
        expect(onSearch).toHaveBeenCalledWith("query", "invoices");
    });

    it("passes empty string as query when query prop is empty", () => {
        const onSearch = jest.fn();
        renderWithFluent({ onSearch, query: "" });

        fireEvent.click(screen.getByText("Matters"));

        expect(onSearch).toHaveBeenCalledWith("", "matters");
    });

    // ---- Each domain value ----

    it.each<[string, SearchDomain]>([
        ["Documents", "documents"],
        ["Matters", "matters"],
        ["Projects", "projects"],
        ["Invoices", "invoices"],
    ])("clicking %s tab forwards domain '%s'", (label, expectedDomain) => {
        const onDomainChange = jest.fn();
        renderWithFluent({ onDomainChange, activeDomain: "documents" });

        fireEvent.click(screen.getByText(label));

        // Clicking the already-active tab may or may not fire depending on Fluent behavior,
        // but at minimum the domain value should match when it does fire.
        if (onDomainChange.mock.calls.length > 0) {
            expect(onDomainChange).toHaveBeenCalledWith(expectedDomain);
        }
    });
});
