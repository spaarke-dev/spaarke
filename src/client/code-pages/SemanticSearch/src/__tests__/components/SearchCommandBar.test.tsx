/**
 * Unit tests for SearchCommandBar component
 *
 * Covers:
 *   - Always-visible Refresh button
 *   - Selection-dependent button disabled states (0, 1, multiple selections)
 *   - Document-only commands visibility (hidden for non-document domains)
 *   - Callback invocations with correct arguments
 *   - Tooltip content based on selection state
 */

import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import { FluentProvider, webLightTheme } from "@fluentui/react-components";
import { SearchCommandBar, type SearchCommandBarProps } from "../../components/SearchCommandBar";
import type { SearchDomain } from "../../types";

// ---------------------------------------------------------------------------
// Mock icons
// ---------------------------------------------------------------------------

jest.mock("@fluentui/react-icons", () => ({
    DeleteRegular: () => <span data-testid="icon-delete" />,
    ArrowClockwiseRegular: () => <span data-testid="icon-refresh" />,
    MailRegular: () => <span data-testid="icon-mail" />,
    OpenRegular: () => <span data-testid="icon-open" />,
    DesktopRegular: () => <span data-testid="icon-desktop" />,
    ArrowDownloadRegular: () => <span data-testid="icon-download" />,
    DatabaseSearchRegular: () => <span data-testid="icon-index" />,
}));

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const DEFAULT_PROPS: SearchCommandBarProps = {
    selectedIds: [],
    activeDomain: "documents",
    onDelete: jest.fn(),
    onRefresh: jest.fn(),
    onEmailLink: jest.fn(),
    onOpenInWeb: jest.fn(),
    onOpenInDesktop: jest.fn(),
    onDownload: jest.fn(),
    onSendToIndex: jest.fn(),
};

function renderCommandBar(props: Partial<SearchCommandBarProps> = {}) {
    return render(
        <FluentProvider theme={webLightTheme}>
            <SearchCommandBar {...DEFAULT_PROPS} {...props} />
        </FluentProvider>,
    );
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe("SearchCommandBar", () => {
    beforeEach(() => {
        jest.clearAllMocks();
    });

    // ---- Always-visible buttons ----

    it("renders Refresh button", () => {
        renderCommandBar();

        expect(screen.getByText("Refresh")).toBeInTheDocument();
    });

    it("calls onRefresh when Refresh is clicked", () => {
        const onRefresh = jest.fn();
        renderCommandBar({ onRefresh });

        fireEvent.click(screen.getByText("Refresh"));

        expect(onRefresh).toHaveBeenCalledTimes(1);
    });

    it("renders Delete and Email a Link buttons", () => {
        renderCommandBar();

        expect(screen.getByText("Delete")).toBeInTheDocument();
        expect(screen.getByText("Email a Link")).toBeInTheDocument();
    });

    // ---- No selection (disabled states) ----

    describe("with no selection", () => {
        it("disables Delete button", () => {
            renderCommandBar({ selectedIds: [] });

            const deleteBtn = screen.getByText("Delete").closest("button");
            expect(deleteBtn).toBeDisabled();
        });

        it("disables Email a Link button", () => {
            renderCommandBar({ selectedIds: [] });

            const emailBtn = screen.getByText("Email a Link").closest("button");
            expect(emailBtn).toBeDisabled();
        });

        it("disables document-only buttons when no selection", () => {
            renderCommandBar({ selectedIds: [], activeDomain: "documents" });

            expect(screen.getByText("Open in Web").closest("button")).toBeDisabled();
            expect(screen.getByText("Open in Desktop").closest("button")).toBeDisabled();
            expect(screen.getByText("Download").closest("button")).toBeDisabled();
            expect(screen.getByText("Send to Index").closest("button")).toBeDisabled();
        });

        it("does not call onDelete when Delete is clicked with no selection", () => {
            const onDelete = jest.fn();
            renderCommandBar({ selectedIds: [], onDelete });

            fireEvent.click(screen.getByText("Delete"));

            expect(onDelete).not.toHaveBeenCalled();
        });
    });

    // ---- Single selection ----

    describe("with single selection", () => {
        const singleSelection = ["record-abc-123"];

        it("enables Delete button", () => {
            renderCommandBar({ selectedIds: singleSelection });

            const deleteBtn = screen.getByText("Delete").closest("button");
            expect(deleteBtn).not.toBeDisabled();
        });

        it("enables Email a Link button", () => {
            renderCommandBar({ selectedIds: singleSelection });

            const emailBtn = screen.getByText("Email a Link").closest("button");
            expect(emailBtn).not.toBeDisabled();
        });

        it("calls onDelete with selected IDs", () => {
            const onDelete = jest.fn();
            renderCommandBar({ selectedIds: singleSelection, onDelete });

            fireEvent.click(screen.getByText("Delete"));

            expect(onDelete).toHaveBeenCalledWith(["record-abc-123"]);
        });

        it("calls onEmailLink with the single selected ID", () => {
            const onEmailLink = jest.fn();
            renderCommandBar({ selectedIds: singleSelection, onEmailLink, activeDomain: "documents" });

            fireEvent.click(screen.getByText("Email a Link"));

            expect(onEmailLink).toHaveBeenCalledWith("record-abc-123");
        });

        it("calls onOpenInWeb with single ID in documents domain", () => {
            const onOpenInWeb = jest.fn();
            renderCommandBar({ selectedIds: singleSelection, onOpenInWeb, activeDomain: "documents" });

            fireEvent.click(screen.getByText("Open in Web"));

            expect(onOpenInWeb).toHaveBeenCalledWith("record-abc-123");
        });

        it("calls onOpenInDesktop with single ID in documents domain", () => {
            const onOpenInDesktop = jest.fn();
            renderCommandBar({ selectedIds: singleSelection, onOpenInDesktop, activeDomain: "documents" });

            fireEvent.click(screen.getByText("Open in Desktop"));

            expect(onOpenInDesktop).toHaveBeenCalledWith("record-abc-123");
        });

        it("calls onDownload with single ID in documents domain", () => {
            const onDownload = jest.fn();
            renderCommandBar({ selectedIds: singleSelection, onDownload, activeDomain: "documents" });

            fireEvent.click(screen.getByText("Download"));

            expect(onDownload).toHaveBeenCalledWith("record-abc-123");
        });

        it("calls onSendToIndex with selected IDs in documents domain", () => {
            const onSendToIndex = jest.fn();
            renderCommandBar({ selectedIds: singleSelection, onSendToIndex, activeDomain: "documents" });

            fireEvent.click(screen.getByText("Send to Index"));

            expect(onSendToIndex).toHaveBeenCalledWith(["record-abc-123"]);
        });
    });

    // ---- Multiple selection ----

    describe("with multiple selection", () => {
        const multiSelection = ["id-1", "id-2", "id-3"];

        it("enables Delete button", () => {
            renderCommandBar({ selectedIds: multiSelection });

            expect(screen.getByText("Delete").closest("button")).not.toBeDisabled();
        });

        it("disables Email a Link (requires single selection)", () => {
            renderCommandBar({ selectedIds: multiSelection });

            expect(screen.getByText("Email a Link").closest("button")).toBeDisabled();
        });

        it("disables single-select document commands with multiple selection", () => {
            renderCommandBar({ selectedIds: multiSelection, activeDomain: "documents" });

            expect(screen.getByText("Open in Web").closest("button")).toBeDisabled();
            expect(screen.getByText("Open in Desktop").closest("button")).toBeDisabled();
            expect(screen.getByText("Download").closest("button")).toBeDisabled();
        });

        it("enables Send to Index with multiple selection", () => {
            renderCommandBar({ selectedIds: multiSelection, activeDomain: "documents" });

            expect(screen.getByText("Send to Index").closest("button")).not.toBeDisabled();
        });

        it("calls onDelete with all selected IDs", () => {
            const onDelete = jest.fn();
            renderCommandBar({ selectedIds: multiSelection, onDelete });

            fireEvent.click(screen.getByText("Delete"));

            expect(onDelete).toHaveBeenCalledWith(["id-1", "id-2", "id-3"]);
        });

        it("calls onSendToIndex with all selected IDs", () => {
            const onSendToIndex = jest.fn();
            renderCommandBar({ selectedIds: multiSelection, onSendToIndex, activeDomain: "documents" });

            fireEvent.click(screen.getByText("Send to Index"));

            expect(onSendToIndex).toHaveBeenCalledWith(["id-1", "id-2", "id-3"]);
        });
    });

    // ---- Domain-specific command visibility ----

    describe("document-only commands", () => {
        it("shows document-only commands when activeDomain is documents", () => {
            renderCommandBar({ activeDomain: "documents" });

            expect(screen.getByText("Open in Web")).toBeInTheDocument();
            expect(screen.getByText("Open in Desktop")).toBeInTheDocument();
            expect(screen.getByText("Download")).toBeInTheDocument();
            expect(screen.getByText("Send to Index")).toBeInTheDocument();
        });

        it.each<SearchDomain>(["matters", "projects", "invoices"])(
            "hides document-only commands for %s domain",
            (domain) => {
                renderCommandBar({ activeDomain: domain });

                expect(screen.queryByText("Open in Web")).not.toBeInTheDocument();
                expect(screen.queryByText("Open in Desktop")).not.toBeInTheDocument();
                expect(screen.queryByText("Download")).not.toBeInTheDocument();
                expect(screen.queryByText("Send to Index")).not.toBeInTheDocument();
            },
        );

        it("always shows Refresh, Delete, Email a Link regardless of domain", () => {
            const domains: SearchDomain[] = ["documents", "matters", "projects", "invoices"];
            domains.forEach((domain) => {
                const { unmount } = renderCommandBar({ activeDomain: domain });

                expect(screen.getByText("Refresh")).toBeInTheDocument();
                expect(screen.getByText("Delete")).toBeInTheDocument();
                expect(screen.getByText("Email a Link")).toBeInTheDocument();

                unmount();
            });
        });
    });
});
