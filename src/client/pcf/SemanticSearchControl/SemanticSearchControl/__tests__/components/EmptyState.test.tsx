/**
 * Unit tests for EmptyState component
 *
 * @see EmptyState.tsx for implementation
 */
import * as React from "react";
import { render, screen } from "@testing-library/react";
import { FluentProvider, webLightTheme } from "@fluentui/react-components";
import { EmptyState } from "../../components/EmptyState";

// Wrapper for Fluent Provider
const renderWithProvider = (ui: React.ReactElement) => {
    return render(
        <FluentProvider theme={webLightTheme}>
            {ui}
        </FluentProvider>
    );
};

describe("EmptyState", () => {
    it("should render no results heading", () => {
        renderWithProvider(<EmptyState query="test documents" hasFilters={false} />);

        expect(screen.getByText("No results found")).toBeInTheDocument();
    });

    it("should show query in the message", () => {
        renderWithProvider(<EmptyState query="test documents" hasFilters={false} />);

        expect(screen.getByText(/test documents/i)).toBeInTheDocument();
    });

    it("should suggest clearing filters when filters are active", () => {
        renderWithProvider(<EmptyState query="contract" hasFilters={true} />);

        // The actual message is "Clear filters to broaden search"
        expect(screen.getByText(/clear filters/i)).toBeInTheDocument();
    });

    it("should suggest using different keywords", () => {
        renderWithProvider(<EmptyState query="xyz123" hasFilters={false} />);

        // The actual message is "Use different or fewer keywords"
        expect(screen.getByText(/different or fewer keywords/i)).toBeInTheDocument();
    });

    it("should handle empty query gracefully", () => {
        renderWithProvider(<EmptyState query="" hasFilters={false} />);

        expect(screen.getByText("No results found")).toBeInTheDocument();
    });

    it("should render search info icon", () => {
        const { container } = renderWithProvider(
            <EmptyState query="test" hasFilters={false} />
        );

        // Check for SVG icon (SearchInfo icon from Fluent)
        const svg = container.querySelector("svg");
        expect(svg).toBeInTheDocument();
    });

    it("should show suggestions list", () => {
        renderWithProvider(<EmptyState query="test" hasFilters={false} />);

        expect(screen.getByText(/try the following/i)).toBeInTheDocument();
        expect(screen.getByText(/check spelling/i)).toBeInTheDocument();
    });

    it("should show clear filters suggestion only when filters active", () => {
        // Without filters
        const { rerender } = renderWithProvider(
            <EmptyState query="test" hasFilters={false} />
        );
        expect(screen.queryByText(/clear filters/i)).not.toBeInTheDocument();

        // With filters
        rerender(
            <FluentProvider theme={webLightTheme}>
                <EmptyState query="test" hasFilters={true} />
            </FluentProvider>
        );
        expect(screen.getByText(/clear filters/i)).toBeInTheDocument();
    });
});
