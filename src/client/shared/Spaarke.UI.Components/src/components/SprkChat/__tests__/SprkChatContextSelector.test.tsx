/**
 * SprkChatContextSelector Component Tests
 *
 * Tests document/playbook dropdown rendering, selection behavior, and disabled state.
 *
 * @see ADR-022 - React 16 APIs only
 */

import * as React from "react";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { SprkChatContextSelector } from "../SprkChatContextSelector";
import { IDocumentOption, IPlaybookOption } from "../types";
import { renderWithProviders } from "../../../__mocks__/pcfMocks";

describe("SprkChatContextSelector", () => {
    const mockDocuments: IDocumentOption[] = [
        { id: "doc-1", name: "Contract.pdf" },
        { id: "doc-2", name: "Agreement.docx" },
    ];

    const mockPlaybooks: IPlaybookOption[] = [
        { id: "pb-1", name: "Legal Review" },
        { id: "pb-2", name: "Financial Analysis" },
    ];

    let mockOnDocumentChange: jest.Mock;
    let mockOnPlaybookChange: jest.Mock;

    beforeEach(() => {
        mockOnDocumentChange = jest.fn();
        mockOnPlaybookChange = jest.fn();
    });

    afterEach(() => {
        jest.clearAllMocks();
    });

    describe("Rendering", () => {
        it("should render document dropdown when documents are provided", () => {
            renderWithProviders(
                <SprkChatContextSelector
                    documents={mockDocuments}
                    playbooks={[]}
                    onDocumentChange={mockOnDocumentChange}
                    onPlaybookChange={mockOnPlaybookChange}
                />
            );

            expect(screen.getByText("Document:")).toBeInTheDocument();
            expect(screen.getByTestId("context-document-select")).toBeInTheDocument();
        });

        it("should render playbook dropdown when playbooks are provided", () => {
            renderWithProviders(
                <SprkChatContextSelector
                    documents={[]}
                    playbooks={mockPlaybooks}
                    onDocumentChange={mockOnDocumentChange}
                    onPlaybookChange={mockOnPlaybookChange}
                />
            );

            expect(screen.getByText("Playbook:")).toBeInTheDocument();
            expect(screen.getByTestId("context-playbook-select")).toBeInTheDocument();
        });

        it("should render both dropdowns when both are provided", () => {
            renderWithProviders(
                <SprkChatContextSelector
                    documents={mockDocuments}
                    playbooks={mockPlaybooks}
                    onDocumentChange={mockOnDocumentChange}
                    onPlaybookChange={mockOnPlaybookChange}
                />
            );

            expect(screen.getByText("Document:")).toBeInTheDocument();
            expect(screen.getByText("Playbook:")).toBeInTheDocument();
        });

        it("should not render document dropdown when documents is empty", () => {
            renderWithProviders(
                <SprkChatContextSelector
                    documents={[]}
                    playbooks={mockPlaybooks}
                    onDocumentChange={mockOnDocumentChange}
                    onPlaybookChange={mockOnPlaybookChange}
                />
            );

            expect(screen.queryByText("Document:")).not.toBeInTheDocument();
        });

        it("should not render playbook dropdown when playbooks is empty", () => {
            renderWithProviders(
                <SprkChatContextSelector
                    documents={mockDocuments}
                    playbooks={[]}
                    onDocumentChange={mockOnDocumentChange}
                    onPlaybookChange={mockOnPlaybookChange}
                />
            );

            expect(screen.queryByText("Playbook:")).not.toBeInTheDocument();
        });

        it("should render with toolbar role", () => {
            renderWithProviders(
                <SprkChatContextSelector
                    documents={mockDocuments}
                    playbooks={mockPlaybooks}
                    onDocumentChange={mockOnDocumentChange}
                    onPlaybookChange={mockOnPlaybookChange}
                />
            );

            expect(screen.getByRole("toolbar")).toBeInTheDocument();
        });
    });

    describe("Document Selection", () => {
        it("should include None option in document dropdown", () => {
            renderWithProviders(
                <SprkChatContextSelector
                    documents={mockDocuments}
                    playbooks={[]}
                    onDocumentChange={mockOnDocumentChange}
                    onPlaybookChange={mockOnPlaybookChange}
                />
            );

            const select = screen.getByTestId("context-document-select");
            const nativeSelect = select.querySelector("select") || select;
            const options = nativeSelect.querySelectorAll("option");
            // None + 2 documents = 3
            expect(options.length).toBe(3);
        });

        it("should call onDocumentChange when selection changes", async () => {
            const user = userEvent.setup();
            renderWithProviders(
                <SprkChatContextSelector
                    documents={mockDocuments}
                    playbooks={[]}
                    onDocumentChange={mockOnDocumentChange}
                    onPlaybookChange={mockOnPlaybookChange}
                />
            );

            const select = screen.getByTestId("context-document-select");
            const nativeSelect = select.querySelector("select") || select;
            await user.selectOptions(nativeSelect, "doc-1");

            expect(mockOnDocumentChange).toHaveBeenCalledWith("doc-1");
        });

        it("should show selected document", () => {
            renderWithProviders(
                <SprkChatContextSelector
                    documents={mockDocuments}
                    playbooks={[]}
                    selectedDocumentId="doc-1"
                    onDocumentChange={mockOnDocumentChange}
                    onPlaybookChange={mockOnPlaybookChange}
                />
            );

            const select = screen.getByTestId("context-document-select");
            const nativeSelect = (select.querySelector("select") || select) as HTMLSelectElement;
            expect(nativeSelect.value).toBe("doc-1");
        });
    });

    describe("Playbook Selection", () => {
        it("should call onPlaybookChange when selection changes", async () => {
            const user = userEvent.setup();
            renderWithProviders(
                <SprkChatContextSelector
                    documents={[]}
                    playbooks={mockPlaybooks}
                    onDocumentChange={mockOnDocumentChange}
                    onPlaybookChange={mockOnPlaybookChange}
                />
            );

            const select = screen.getByTestId("context-playbook-select");
            const nativeSelect = select.querySelector("select") || select;
            await user.selectOptions(nativeSelect, "pb-2");

            expect(mockOnPlaybookChange).toHaveBeenCalledWith("pb-2");
        });
    });

    describe("Disabled State", () => {
        it("should disable document select when disabled is true", () => {
            renderWithProviders(
                <SprkChatContextSelector
                    documents={mockDocuments}
                    playbooks={[]}
                    onDocumentChange={mockOnDocumentChange}
                    onPlaybookChange={mockOnPlaybookChange}
                    disabled={true}
                />
            );

            const select = screen.getByTestId("context-document-select");
            const nativeSelect = select.querySelector("select") || select;
            expect(nativeSelect).toBeDisabled();
        });

        it("should disable playbook select when disabled is true", () => {
            renderWithProviders(
                <SprkChatContextSelector
                    documents={[]}
                    playbooks={mockPlaybooks}
                    onDocumentChange={mockOnDocumentChange}
                    onPlaybookChange={mockOnPlaybookChange}
                    disabled={true}
                />
            );

            const select = screen.getByTestId("context-playbook-select");
            const nativeSelect = select.querySelector("select") || select;
            expect(nativeSelect).toBeDisabled();
        });
    });
});
