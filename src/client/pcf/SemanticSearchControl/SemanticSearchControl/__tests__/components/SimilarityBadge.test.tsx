/**
 * Unit tests for SimilarityBadge component
 *
 * @see SimilarityBadge.tsx for implementation
 */
import * as React from "react";
import { render, screen } from "@testing-library/react";
import { FluentProvider, webLightTheme } from "@fluentui/react-components";
import { SimilarityBadge } from "../../components/SimilarityBadge";

// Wrapper for Fluent Provider
const renderWithProvider = (ui: React.ReactElement) => {
    return render(
        <FluentProvider theme={webLightTheme}>
            {ui}
        </FluentProvider>
    );
};

describe("SimilarityBadge", () => {
    it("should render high score (>= 0.8) with percentage", () => {
        renderWithProvider(<SimilarityBadge score={0.95} />);

        expect(screen.getByText("95%")).toBeInTheDocument();
    });

    it("should render medium score (0.6 - 0.8) with percentage", () => {
        renderWithProvider(<SimilarityBadge score={0.75} />);

        expect(screen.getByText("75%")).toBeInTheDocument();
    });

    it("should render low score (< 0.6) with percentage", () => {
        renderWithProvider(<SimilarityBadge score={0.45} />);

        expect(screen.getByText("45%")).toBeInTheDocument();
    });

    it("should handle edge case of 100% score", () => {
        renderWithProvider(<SimilarityBadge score={1.0} />);

        expect(screen.getByText("100%")).toBeInTheDocument();
    });

    it("should handle edge case of 0% score", () => {
        renderWithProvider(<SimilarityBadge score={0} />);

        expect(screen.getByText("0%")).toBeInTheDocument();
    });

    it("should round to nearest integer", () => {
        renderWithProvider(<SimilarityBadge score={0.856} />);

        expect(screen.getByText("86%")).toBeInTheDocument();
    });

    it("should handle score just below high threshold", () => {
        renderWithProvider(<SimilarityBadge score={0.79} />);

        expect(screen.getByText("79%")).toBeInTheDocument();
    });

    it("should handle score just above medium threshold", () => {
        renderWithProvider(<SimilarityBadge score={0.61} />);

        expect(screen.getByText("61%")).toBeInTheDocument();
    });
});
