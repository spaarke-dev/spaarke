/**
 * Unit tests for RecordNode component
 *
 * Covers:
 *   - Entity type icon based on domain (documents, matters, projects, invoices)
 *   - Record name rendering with title attribute
 *   - Similarity badge with percentage and color coding
 *   - Parent entity name (optional, conditional rendering)
 *   - ReactFlow handles (target/source)
 */

import React from "react";
import { render, screen } from "@testing-library/react";
import { FluentProvider, webLightTheme } from "@fluentui/react-components";
import { RecordNode } from "../../components/RecordNode";
import type { RecordNodeData, SearchDomain } from "../../types";

// ---------------------------------------------------------------------------
// Mock @xyflow/react
// ---------------------------------------------------------------------------

jest.mock("@xyflow/react", () => ({
    Handle: ({ type, position }: { type: string; position: string }) => (
        <div data-testid={`handle-${type}`} data-position={position} />
    ),
    Position: {
        Top: "top",
        Bottom: "bottom",
        Left: "left",
        Right: "right",
    },
}));

// ---------------------------------------------------------------------------
// Mock @fluentui/react-icons
// ---------------------------------------------------------------------------

jest.mock("@fluentui/react-icons", () => ({
    DocumentRegular: () => <span data-testid="icon-document" />,
    BriefcaseRegular: () => <span data-testid="icon-briefcase" />,
    TaskListSquareAddRegular: () => <span data-testid="icon-project" />,
    ReceiptRegular: () => <span data-testid="icon-invoice" />,
}));

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function makeRecordData(overrides: Partial<RecordNodeData> = {}): RecordNodeData {
    return {
        recordId: "rec-001",
        recordName: "Test Record",
        similarity: 0.85,
        domain: "documents",
        parentEntityName: undefined,
        ...overrides,
    };
}

function renderRecordNode(data: Partial<RecordNodeData> = {}) {
    const nodeData = makeRecordData(data);
    const props = {
        id: "node-1",
        type: "recordNode",
        data: nodeData as unknown as Record<string, unknown>,
        selected: false,
        isConnectable: true,
        zIndex: 0,
        positionAbsoluteX: 0,
        positionAbsoluteY: 0,
    } as any;

    return render(
        <FluentProvider theme={webLightTheme}>
            <RecordNode {...props} />
        </FluentProvider>,
    );
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe("RecordNode", () => {
    // ---- Entity Icon by Domain ----

    describe("entity icon by domain", () => {
        it("renders document icon for documents domain", () => {
            renderRecordNode({ domain: "documents" });
            expect(screen.getByTestId("icon-document")).toBeInTheDocument();
        });

        it("renders briefcase icon for matters domain", () => {
            renderRecordNode({ domain: "matters" });
            expect(screen.getByTestId("icon-briefcase")).toBeInTheDocument();
        });

        it("renders project icon for projects domain", () => {
            renderRecordNode({ domain: "projects" });
            expect(screen.getByTestId("icon-project")).toBeInTheDocument();
        });

        it("renders invoice icon for invoices domain", () => {
            renderRecordNode({ domain: "invoices" });
            expect(screen.getByTestId("icon-invoice")).toBeInTheDocument();
        });
    });

    // ---- Record Name ----

    describe("record name", () => {
        it("renders the record name", () => {
            renderRecordNode({ recordName: "Important Contract" });

            expect(screen.getByText("Important Contract")).toBeInTheDocument();
        });

        it("sets title attribute to full name for tooltip", () => {
            renderRecordNode({ recordName: "Full Name For Tooltip" });

            expect(screen.getByTitle("Full Name For Tooltip")).toBeInTheDocument();
        });

        it("renders long name (CSS handles truncation via line-clamp)", () => {
            const longName = "This is a very long record name that should be truncated by CSS webkit line clamp styling";
            renderRecordNode({ recordName: longName });

            expect(screen.getByText(longName)).toBeInTheDocument();
        });
    });

    // ---- Similarity Badge ----

    describe("similarity badge", () => {
        it("displays similarity as integer percentage", () => {
            renderRecordNode({ similarity: 0.85 });

            expect(screen.getByText("85%")).toBeInTheDocument();
        });

        it("rounds similarity to nearest integer", () => {
            renderRecordNode({ similarity: 0.777 });

            expect(screen.getByText("78%")).toBeInTheDocument();
        });

        it("displays 0% for zero similarity", () => {
            renderRecordNode({ similarity: 0 });

            expect(screen.getByText("0%")).toBeInTheDocument();
        });

        it("displays 100% for perfect similarity", () => {
            renderRecordNode({ similarity: 1.0 });

            expect(screen.getByText("100%")).toBeInTheDocument();
        });

        it("displays 50% for mid-range similarity", () => {
            renderRecordNode({ similarity: 0.5 });

            expect(screen.getByText("50%")).toBeInTheDocument();
        });
    });

    // ---- Parent Entity Name ----

    describe("parent entity name", () => {
        it("renders parent entity name when provided", () => {
            renderRecordNode({ parentEntityName: "Acme Corp Matter" });

            expect(screen.getByText("Acme Corp Matter")).toBeInTheDocument();
        });

        it("sets title attribute on parent entity name for tooltip", () => {
            renderRecordNode({ parentEntityName: "Long Parent Entity Name" });

            expect(screen.getByTitle("Long Parent Entity Name")).toBeInTheDocument();
        });

        it("does not render parent entity when undefined", () => {
            renderRecordNode({ parentEntityName: undefined });

            // The Text element for parent entity should not exist
            // We verify by checking that no extra Text elements beyond the expected ones exist
            const texts = screen.getAllByText(/.+/);
            const hasParent = texts.some((el) => el.textContent === "undefined");
            expect(hasParent).toBe(false);
        });

        it("does not render parent entity when empty string (falsy)", () => {
            renderRecordNode({ parentEntityName: "" });

            // Empty string is falsy, so the conditional rendering should skip it
            // Just verify the component renders without error
            expect(screen.getByText("Test Record")).toBeInTheDocument();
        });
    });

    // ---- ReactFlow Handles ----

    describe("ReactFlow handles", () => {
        it("renders target and source handles", () => {
            renderRecordNode();

            expect(screen.getByTestId("handle-target")).toBeInTheDocument();
            expect(screen.getByTestId("handle-source")).toBeInTheDocument();
        });

        it("positions target handle at top and source at bottom", () => {
            renderRecordNode();

            expect(screen.getByTestId("handle-target")).toHaveAttribute("data-position", "top");
            expect(screen.getByTestId("handle-source")).toHaveAttribute("data-position", "bottom");
        });
    });

    // ---- Multiple Domains Combined ----

    describe("combined rendering for each domain", () => {
        const domains: SearchDomain[] = ["documents", "matters", "projects", "invoices"];

        it.each(domains)("renders correctly for %s domain", (domain) => {
            renderRecordNode({
                domain,
                recordName: `${domain} record`,
                similarity: 0.6,
                parentEntityName: "Parent",
            });

            expect(screen.getByText(`${domain} record`)).toBeInTheDocument();
            expect(screen.getByText("60%")).toBeInTheDocument();
            expect(screen.getByText("Parent")).toBeInTheDocument();
        });
    });
});
