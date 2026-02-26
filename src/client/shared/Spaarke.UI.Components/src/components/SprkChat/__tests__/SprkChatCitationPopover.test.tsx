/**
 * SprkChatCitationPopover Component Tests
 *
 * Tests CitationMarker rendering, popover open/close behavior, content display
 * (source name, page, excerpt truncation, open source link), keyboard dismiss,
 * and accessibility attributes.
 *
 * @see ADR-012 - Shared Component Library
 * @see ADR-021 - Fluent UI v9; makeStyles; design tokens; dark mode
 * @see ADR-022 - React 16 APIs only
 */

import * as React from "react";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CitationMarker, SprkChatCitationPopover } from "../SprkChatCitationPopover";
import { ICitation } from "../types";
import { renderWithProviders } from "../../../__mocks__/pcfMocks";

describe("SprkChatCitationPopover", () => {
    /** Citation with all optional fields populated. */
    const fullCitation: ICitation = {
        id: 1,
        source: "Company Policy Handbook",
        page: 42,
        excerpt: "Employees must comply with all applicable regulations and standards.",
        chunkId: "chunk-abc-123",
        sourceUrl: "https://example.com/policy-handbook",
    };

    /** Citation without optional page and sourceUrl. */
    const minimalCitation: ICitation = {
        id: 2,
        source: "Internal Memo",
        excerpt: "This is a brief excerpt from the memo.",
        chunkId: "chunk-def-456",
    };

    /** Citation with a very long excerpt (> 200 chars) for truncation testing. */
    const longExcerptCitation: ICitation = {
        id: 3,
        source: "Annual Report",
        page: 7,
        excerpt: "A".repeat(250),
        chunkId: "chunk-ghi-789",
        sourceUrl: "https://example.com/annual-report",
    };

    afterEach(() => {
        jest.clearAllMocks();
    });

    // ─────────────────────────────────────────────────────────────────────
    // Test 1: Render_CitationMarker_ShowsSuperscript
    // ─────────────────────────────────────────────────────────────────────

    it("Render_CitationMarker_ShowsSuperscript", () => {
        renderWithProviders(
            <CitationMarker citation={fullCitation} />
        );

        const marker = screen.getByTestId("citation-marker-1");
        expect(marker).toBeInTheDocument();
        // The marker should display [N] text
        expect(marker.textContent).toContain("[1]");
    });

    // ─────────────────────────────────────────────────────────────────────
    // Test 2: Click_Marker_OpensPopover
    // ─────────────────────────────────────────────────────────────────────

    it("Click_Marker_OpensPopover", async () => {
        const user = userEvent.setup();

        renderWithProviders(
            <CitationMarker citation={fullCitation} />
        );

        const marker = screen.getByTestId("citation-marker-1");

        // Popover should not be visible initially
        expect(screen.queryByTestId("citation-popover-1")).not.toBeInTheDocument();

        // Click the marker to open the popover
        await user.click(marker);

        // Popover should now be visible
        await waitFor(() => {
            expect(screen.getByTestId("citation-popover-1")).toBeInTheDocument();
        });
    });

    // ─────────────────────────────────────────────────────────────────────
    // Test 3: Popover_ShowsSourceName
    // ─────────────────────────────────────────────────────────────────────

    it("Popover_ShowsSourceName", async () => {
        const user = userEvent.setup();

        renderWithProviders(
            <CitationMarker citation={fullCitation} />
        );

        // Open the popover
        await user.click(screen.getByTestId("citation-marker-1"));

        await waitFor(() => {
            expect(screen.getByText("Company Policy Handbook")).toBeInTheDocument();
        });
    });

    // ─────────────────────────────────────────────────────────────────────
    // Test 4: Popover_ShowsPageNumber_WhenProvided
    // ─────────────────────────────────────────────────────────────────────

    it("Popover_ShowsPageNumber_WhenProvided", async () => {
        const user = userEvent.setup();

        renderWithProviders(
            <CitationMarker citation={fullCitation} />
        );

        await user.click(screen.getByTestId("citation-marker-1"));

        await waitFor(() => {
            expect(screen.getByText("Page 42")).toBeInTheDocument();
        });
    });

    // ─────────────────────────────────────────────────────────────────────
    // Test 5: Popover_HidesPageNumber_WhenNotProvided
    // ─────────────────────────────────────────────────────────────────────

    it("Popover_HidesPageNumber_WhenNotProvided", async () => {
        const user = userEvent.setup();

        renderWithProviders(
            <CitationMarker citation={minimalCitation} />
        );

        await user.click(screen.getByTestId("citation-marker-2"));

        await waitFor(() => {
            expect(screen.getByTestId("citation-popover-2")).toBeInTheDocument();
        });

        // "Page" text should not appear anywhere in the popover
        expect(screen.queryByText(/^Page\s/)).not.toBeInTheDocument();
    });

    // ─────────────────────────────────────────────────────────────────────
    // Test 6: Popover_ShowsExcerpt_Truncated
    // ─────────────────────────────────────────────────────────────────────

    it("Popover_ShowsExcerpt_Truncated", async () => {
        const user = userEvent.setup();

        renderWithProviders(
            <CitationMarker citation={longExcerptCitation} />
        );

        await user.click(screen.getByTestId("citation-marker-3"));

        await waitFor(() => {
            expect(screen.getByTestId("citation-popover-3")).toBeInTheDocument();
        });

        // The excerpt should be truncated to 199 chars + ellipsis (200 total with \u2026)
        const truncated = "A".repeat(199) + "\u2026";
        expect(screen.getByText(truncated)).toBeInTheDocument();

        // The full 250-character string should NOT appear
        expect(screen.queryByText("A".repeat(250))).not.toBeInTheDocument();
    });

    // ─────────────────────────────────────────────────────────────────────
    // Test 7: Popover_ShowsOpenSourceLink_WhenUrlProvided
    // ─────────────────────────────────────────────────────────────────────

    it("Popover_ShowsOpenSourceLink_WhenUrlProvided", async () => {
        const user = userEvent.setup();

        renderWithProviders(
            <CitationMarker citation={fullCitation} />
        );

        await user.click(screen.getByTestId("citation-marker-1"));

        await waitFor(() => {
            const link = screen.getByTestId("citation-link-1");
            expect(link).toBeInTheDocument();
            expect(link).toHaveAttribute("href", "https://example.com/policy-handbook");
            expect(link).toHaveAttribute("target", "_blank");
            expect(link).toHaveAttribute("rel", "noopener noreferrer");
        });

        // The link text should say "Open Source"
        expect(screen.getByText("Open Source")).toBeInTheDocument();
    });

    // ─────────────────────────────────────────────────────────────────────
    // Test 8: Popover_HidesLink_WhenNoUrl
    // ─────────────────────────────────────────────────────────────────────

    it("Popover_HidesLink_WhenNoUrl", async () => {
        const user = userEvent.setup();

        renderWithProviders(
            <CitationMarker citation={minimalCitation} />
        );

        await user.click(screen.getByTestId("citation-marker-2"));

        await waitFor(() => {
            expect(screen.getByTestId("citation-popover-2")).toBeInTheDocument();
        });

        // No "Open Source" link should appear
        expect(screen.queryByText("Open Source")).not.toBeInTheDocument();
        expect(screen.queryByTestId("citation-link-2")).not.toBeInTheDocument();
    });

    // ─────────────────────────────────────────────────────────────────────
    // Test 9: Dismiss_EscapeKey_ClosesPopover
    // ─────────────────────────────────────────────────────────────────────

    it("Dismiss_EscapeKey_ClosesPopover", async () => {
        const user = userEvent.setup();

        renderWithProviders(
            <CitationMarker citation={fullCitation} />
        );

        // Open the popover
        await user.click(screen.getByTestId("citation-marker-1"));

        await waitFor(() => {
            expect(screen.getByTestId("citation-popover-1")).toBeInTheDocument();
        });

        // Press Escape to close the popover
        await user.keyboard("{Escape}");

        await waitFor(() => {
            expect(screen.queryByTestId("citation-popover-1")).not.toBeInTheDocument();
        });
    });

    // ─────────────────────────────────────────────────────────────────────
    // Test 10: Accessibility_MarkerHasAriaLabel
    // ─────────────────────────────────────────────────────────────────────

    it("Accessibility_MarkerHasAriaLabel", () => {
        renderWithProviders(
            <CitationMarker citation={fullCitation} />
        );

        const marker = screen.getByTestId("citation-marker-1");
        expect(marker).toHaveAttribute(
            "aria-label",
            "Citation 1, source: Company Policy Handbook"
        );
        expect(marker).toHaveAttribute("role", "button");
        expect(marker).toHaveAttribute("tabIndex", "0");
    });
});
