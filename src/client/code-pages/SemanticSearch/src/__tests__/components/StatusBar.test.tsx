/**
 * Unit tests for StatusBar component
 *
 * Covers:
 *   - "Ready" state when no search has been run (totalCount is null)
 *   - "No results found" state when totalCount is 0
 *   - Result count with correct pluralization (1 result, N results)
 *   - Search time display (shown only when results exist)
 *   - Version string display
 *   - Middot separator visibility
 */

import React from "react";
import { render, screen } from "@testing-library/react";
import { FluentProvider, webLightTheme } from "@fluentui/react-components";
import { StatusBar, type StatusBarProps } from "../../components/StatusBar";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const DEFAULT_PROPS: StatusBarProps = {
    totalCount: null,
    searchTime: null,
    version: "1.0.0",
};

function renderStatusBar(props: Partial<StatusBarProps> = {}) {
    return render(
        <FluentProvider theme={webLightTheme}>
            <StatusBar {...DEFAULT_PROPS} {...props} />
        </FluentProvider>,
    );
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe("StatusBar", () => {
    // ---- Ready State (no search executed) ----

    it("displays 'Ready' when totalCount is null", () => {
        renderStatusBar({ totalCount: null });

        expect(screen.getByText("Ready")).toBeInTheDocument();
    });

    it("does not display search time when totalCount is null", () => {
        renderStatusBar({ totalCount: null, searchTime: 150 });

        expect(screen.queryByText("150ms")).not.toBeInTheDocument();
    });

    // ---- No Results ----

    it("displays 'No results found' when totalCount is 0", () => {
        renderStatusBar({ totalCount: 0 });

        expect(screen.getByText("No results found")).toBeInTheDocument();
    });

    it("does not display search time when totalCount is 0", () => {
        renderStatusBar({ totalCount: 0, searchTime: 42 });

        expect(screen.queryByText("42ms")).not.toBeInTheDocument();
    });

    // ---- Results Found ----

    it("displays singular '1 result found' when totalCount is 1", () => {
        renderStatusBar({ totalCount: 1 });

        expect(screen.getByText("1 result found")).toBeInTheDocument();
    });

    it("displays plural 'N results found' when totalCount > 1", () => {
        renderStatusBar({ totalCount: 25 });

        expect(screen.getByText("25 results found")).toBeInTheDocument();
    });

    it("displays large result count correctly", () => {
        renderStatusBar({ totalCount: 1234 });

        expect(screen.getByText("1234 results found")).toBeInTheDocument();
    });

    // ---- Search Time ----

    it("displays search time when results are present", () => {
        renderStatusBar({ totalCount: 10, searchTime: 350 });

        expect(screen.getByText("350ms")).toBeInTheDocument();
    });

    it("displays middot separator between count and time", () => {
        renderStatusBar({ totalCount: 5, searchTime: 100 });

        // The middot is rendered as &middot; which is the Unicode character \u00B7
        expect(screen.getByText("\u00B7")).toBeInTheDocument();
    });

    it("does not display search time when searchTime is null even with results", () => {
        renderStatusBar({ totalCount: 10, searchTime: null });

        // No middot separator should be present
        expect(screen.queryByText("\u00B7")).not.toBeInTheDocument();
    });

    // ---- Version ----

    it("displays version string", () => {
        renderStatusBar({ version: "2.1.3" });

        expect(screen.getByText("2.1.3")).toBeInTheDocument();
    });

    it("displays custom version string", () => {
        renderStatusBar({ version: "0.1.0-beta" });

        expect(screen.getByText("0.1.0-beta")).toBeInTheDocument();
    });

    // ---- Combined States ----

    it("renders Ready state with version correctly", () => {
        renderStatusBar({ totalCount: null, searchTime: null, version: "1.0.0" });

        expect(screen.getByText("Ready")).toBeInTheDocument();
        expect(screen.getByText("1.0.0")).toBeInTheDocument();
        expect(screen.queryByText("\u00B7")).not.toBeInTheDocument();
    });

    it("renders full status with count, time, and version", () => {
        renderStatusBar({ totalCount: 42, searchTime: 200, version: "3.0.0" });

        expect(screen.getByText("42 results found")).toBeInTheDocument();
        expect(screen.getByText("200ms")).toBeInTheDocument();
        expect(screen.getByText("3.0.0")).toBeInTheDocument();
    });
});
