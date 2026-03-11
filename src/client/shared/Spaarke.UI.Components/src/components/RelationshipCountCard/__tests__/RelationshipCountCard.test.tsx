/**
 * RelationshipCountCard Component Tests
 *
 * Tests all visual states: loading, error, zero count, normal count,
 * click handler, lastUpdated display, default title, and custom title.
 *
 * @see ADR-012 - Shared component library
 * @see ADR-021 - Fluent UI v9 design tokens
 */

import * as React from "react";
import { screen, fireEvent } from "@testing-library/react";
import { renderWithProviders } from "../../../__mocks__/pcfMocks";
import {
    RelationshipCountCard,
    IRelationshipCountCardProps,
} from "../RelationshipCountCard";

describe("RelationshipCountCard", () => {
    let mockOnOpen: jest.Mock;

    const defaultProps: IRelationshipCountCardProps = {
        count: 5,
        onOpen: jest.fn(),
    };

    beforeEach(() => {
        mockOnOpen = jest.fn();
        defaultProps.onOpen = mockOnOpen;
    });

    afterEach(() => {
        jest.clearAllMocks();
    });

    // ─────────────────────────────────────────────────────────────────────
    // Default Rendering
    // ─────────────────────────────────────────────────────────────────────

    describe("Default Rendering", () => {
        it("render_Default_ShowsDefaultTitle", () => {
            renderWithProviders(<RelationshipCountCard {...defaultProps} />);

            expect(
                screen.getByText("RELATED DOCUMENTS")
            ).toBeInTheDocument();
        });

        it("render_CustomTitle_ShowsCustomTitle", () => {
            renderWithProviders(
                <RelationshipCountCard
                    {...defaultProps}
                    title="SIMILAR DOCS"
                />
            );

            expect(screen.getByText("SIMILAR DOCS")).toBeInTheDocument();
        });

        it("render_WithCount_ShowsCountValue", () => {
            renderWithProviders(
                <RelationshipCountCard {...defaultProps} count={12} />
            );

            expect(screen.getByText("12")).toBeInTheDocument();
        });

        it("render_WithCount_ShowsFoundBadge", () => {
            renderWithProviders(
                <RelationshipCountCard {...defaultProps} count={3} />
            );

            expect(screen.getByText("found")).toBeInTheDocument();
        });

        it("render_WithCount_ShowsViewButton", () => {
            renderWithProviders(
                <RelationshipCountCard {...defaultProps} count={7} />
            );

            expect(
                screen.getByRole("button", { name: /view/i })
            ).toBeInTheDocument();
        });
    });

    // ─────────────────────────────────────────────────────────────────────
    // Loading State
    // ─────────────────────────────────────────────────────────────────────

    describe("Loading State", () => {
        it("render_Loading_ShowsSpinner", () => {
            renderWithProviders(
                <RelationshipCountCard
                    {...defaultProps}
                    isLoading={true}
                />
            );

            expect(screen.getByText("Loading...")).toBeInTheDocument();
        });

        it("render_Loading_DoesNotShowCount", () => {
            renderWithProviders(
                <RelationshipCountCard
                    {...defaultProps}
                    count={5}
                    isLoading={true}
                />
            );

            expect(screen.queryByText("5")).not.toBeInTheDocument();
        });

        it("render_Loading_DoesNotShowViewButton", () => {
            renderWithProviders(
                <RelationshipCountCard
                    {...defaultProps}
                    isLoading={true}
                />
            );

            expect(
                screen.queryByRole("button", { name: /view/i })
            ).not.toBeInTheDocument();
        });

        it("render_Loading_StillShowsTitle", () => {
            renderWithProviders(
                <RelationshipCountCard
                    {...defaultProps}
                    isLoading={true}
                />
            );

            expect(
                screen.getByText("RELATED DOCUMENTS")
            ).toBeInTheDocument();
        });
    });

    // ─────────────────────────────────────────────────────────────────────
    // Error State
    // ─────────────────────────────────────────────────────────────────────

    describe("Error State", () => {
        it("render_Error_ShowsErrorMessage", () => {
            renderWithProviders(
                <RelationshipCountCard
                    {...defaultProps}
                    error="Failed to load relationships"
                />
            );

            expect(
                screen.getByText("Failed to load relationships")
            ).toBeInTheDocument();
        });

        it("render_Error_DoesNotShowCount", () => {
            renderWithProviders(
                <RelationshipCountCard
                    {...defaultProps}
                    count={5}
                    error="Error occurred"
                />
            );

            expect(screen.queryByText("5")).not.toBeInTheDocument();
        });

        it("render_Error_DoesNotShowViewButton", () => {
            renderWithProviders(
                <RelationshipCountCard
                    {...defaultProps}
                    error="Error occurred"
                />
            );

            expect(
                screen.queryByRole("button", { name: /view/i })
            ).not.toBeInTheDocument();
        });

        it("render_Error_StillShowsTitle", () => {
            renderWithProviders(
                <RelationshipCountCard
                    {...defaultProps}
                    error="Something went wrong"
                />
            );

            expect(
                screen.getByText("RELATED DOCUMENTS")
            ).toBeInTheDocument();
        });

        it("render_NullError_ShowsNormalState", () => {
            renderWithProviders(
                <RelationshipCountCard
                    {...defaultProps}
                    count={3}
                    error={null}
                />
            );

            expect(screen.getByText("3")).toBeInTheDocument();
            expect(screen.getByText("found")).toBeInTheDocument();
        });
    });

    // ─────────────────────────────────────────────────────────────────────
    // Zero Count State
    // ─────────────────────────────────────────────────────────────────────

    describe("Zero Count State", () => {
        it("render_ZeroCount_ShowsZero", () => {
            renderWithProviders(
                <RelationshipCountCard {...defaultProps} count={0} />
            );

            expect(screen.getByText("0")).toBeInTheDocument();
        });

        it("render_ZeroCount_ShowsNoRelatedMessage", () => {
            renderWithProviders(
                <RelationshipCountCard {...defaultProps} count={0} />
            );

            expect(
                screen.getByText("No related documents found")
            ).toBeInTheDocument();
        });

        it("render_ZeroCount_DoesNotShowFoundBadge", () => {
            renderWithProviders(
                <RelationshipCountCard {...defaultProps} count={0} />
            );

            expect(screen.queryByText("found")).not.toBeInTheDocument();
        });

        it("render_ZeroCount_DoesNotShowViewButton", () => {
            renderWithProviders(
                <RelationshipCountCard {...defaultProps} count={0} />
            );

            expect(
                screen.queryByRole("button", { name: /view/i })
            ).not.toBeInTheDocument();
        });
    });

    // ─────────────────────────────────────────────────────────────────────
    // Click Handler
    // ─────────────────────────────────────────────────────────────────────

    describe("Click Handler", () => {
        it("click_ViewButton_CallsOnOpen", () => {
            renderWithProviders(
                <RelationshipCountCard
                    {...defaultProps}
                    count={5}
                />
            );

            const viewButton = screen.getByRole("button", { name: /view/i });
            fireEvent.click(viewButton);

            expect(mockOnOpen).toHaveBeenCalledTimes(1);
        });

        it("click_ViewButtonMultiple_CallsOnOpenEachTime", () => {
            renderWithProviders(
                <RelationshipCountCard
                    {...defaultProps}
                    count={5}
                />
            );

            const viewButton = screen.getByRole("button", { name: /view/i });
            fireEvent.click(viewButton);
            fireEvent.click(viewButton);

            expect(mockOnOpen).toHaveBeenCalledTimes(2);
        });
    });

    // ─────────────────────────────────────────────────────────────────────
    // Last Updated
    // ─────────────────────────────────────────────────────────────────────

    describe("Last Updated", () => {
        it("render_WithLastUpdated_ShowsFormattedDate", () => {
            const date = new Date(2026, 2, 10, 14, 30); // Mar 10, 2026, 2:30 PM
            renderWithProviders(
                <RelationshipCountCard
                    {...defaultProps}
                    count={5}
                    lastUpdated={date}
                />
            );

            // Intl.DateTimeFormat with month: "short", day: "numeric", hour: "numeric", minute: "2-digit"
            expect(screen.getByText(/Updated/)).toBeInTheDocument();
            expect(screen.getByText(/Mar/)).toBeInTheDocument();
        });

        it("render_WithoutLastUpdated_DoesNotShowUpdatedText", () => {
            renderWithProviders(
                <RelationshipCountCard
                    {...defaultProps}
                    count={5}
                />
            );

            expect(screen.queryByText(/Updated/)).not.toBeInTheDocument();
        });

        it("render_ZeroCountWithLastUpdated_ShowsUpdatedText", () => {
            const date = new Date(2026, 0, 15, 9, 0);
            renderWithProviders(
                <RelationshipCountCard
                    {...defaultProps}
                    count={0}
                    lastUpdated={date}
                />
            );

            expect(screen.getByText(/Updated/)).toBeInTheDocument();
        });
    });
});
