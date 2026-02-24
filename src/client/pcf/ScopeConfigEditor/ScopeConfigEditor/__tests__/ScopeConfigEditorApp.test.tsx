/**
 * Unit tests for ScopeConfigEditorApp component
 *
 * Tests:
 * - Renders ActionEditor for entity 'sprk_analysisaction'
 * - Renders SkillEditor for entity 'sprk_analysisskill'
 * - Renders KnowledgeSourceEditor for entity 'sprk_analysisknowledge'
 * - Renders ToolEditor for entity 'sprk_analysistool'
 * - Renders unknown entity warning for unrecognized entity types
 * - Version footer is present (CLAUDE.md version footer requirement)
 */

import * as React from "react";
import { render, screen, waitFor, fireEvent } from "@testing-library/react";
import { FluentProvider, webLightTheme } from "@fluentui/react-components";
import { ScopeConfigEditorApp } from "../components/ScopeConfigEditorApp";

// ─────────────────────────────────────────────────────────────────────────────
// Mocks
// ─────────────────────────────────────────────────────────────────────────────

// Mock global fetch for ToolEditor handler calls
const mockFetch = jest.fn();
global.fetch = mockFetch;

if (!("timeout" in AbortSignal)) {
    // @ts-expect-error - polyfill for test environment
    AbortSignal.timeout = jest.fn().mockReturnValue(new AbortController().signal);
}

// ─────────────────────────────────────────────────────────────────────────────
// Test helpers
// ─────────────────────────────────────────────────────────────────────────────

const renderWithProvider = (ui: React.ReactElement) =>
    render(
        <FluentProvider theme={webLightTheme}>
            {ui}
        </FluentProvider>
    );

function makeDefaultProps(entityLogicalName: string) {
    return {
        entityLogicalName,
        fieldValue: "",
        apiBaseUrl: "https://test.api.com",
        onValueChange: jest.fn(),
    };
}

// ─────────────────────────────────────────────────────────────────────────────
// Tests
// ─────────────────────────────────────────────────────────────────────────────

describe("ScopeConfigEditorApp — entity routing", () => {
    beforeEach(() => {
        mockFetch.mockReset();
        // Default fetch response for ToolEditor handler API
        mockFetch.mockResolvedValue({
            ok: true,
            status: 200,
            json: () => Promise.resolve([]),
        } as Response);
    });

    it("renders ActionEditor for sprk_analysisaction entity", () => {
        renderWithProvider(
            <ScopeConfigEditorApp {...makeDefaultProps("sprk_analysisaction")} />
        );

        // ActionEditor specific elements
        expect(screen.getByText(/system prompt/i)).toBeInTheDocument();
        expect(screen.getByLabelText(/system prompt editor/i)).toBeInTheDocument();
        expect(screen.getByTestId("char-count")).toBeInTheDocument();
        expect(screen.getByTestId("token-count")).toBeInTheDocument();
    });

    it("renders ActionEditor for sprk_analysisaction regardless of case", () => {
        renderWithProvider(
            <ScopeConfigEditorApp {...makeDefaultProps("SPRK_SYSTEMPROMPT")} />
        );

        // Should still find ActionEditor (entityLogicalName is lowercased)
        expect(screen.getByLabelText(/system prompt editor/i)).toBeInTheDocument();
    });

    it("renders SkillEditor for sprk_analysisskill entity", () => {
        renderWithProvider(
            <ScopeConfigEditorApp {...makeDefaultProps("sprk_analysisskill")} />
        );

        // SkillEditor specific elements — use getByLabelText to avoid ambiguity
        expect(screen.getByLabelText(/skill prompt fragment editor/i)).toBeInTheDocument();
        expect(screen.getByTestId("skill-char-count")).toBeInTheDocument();
        // Label element specifically (not the preview text)
        expect(screen.getByText(/^Prompt Fragment$/)).toBeInTheDocument();
    });

    it("renders SkillEditor injection preview section", () => {
        renderWithProvider(
            <ScopeConfigEditorApp
                {...makeDefaultProps("sprk_analysisskill")}
                fieldValue="Use context documents"
            />
        );

        expect(screen.getByTestId("injection-preview")).toBeInTheDocument();
    });

    it("renders KnowledgeSourceEditor for sprk_analysisknowledge entity", () => {
        renderWithProvider(
            <ScopeConfigEditorApp {...makeDefaultProps("sprk_analysisknowledge")} />
        );

        // KnowledgeSourceEditor specific elements
        expect(screen.getByText(/knowledge content/i)).toBeInTheDocument();
        expect(screen.getByLabelText(/knowledge source markdown content editor/i)).toBeInTheDocument();
        expect(screen.getByTestId("upload-file-button")).toBeInTheDocument();
    });

    it("renders ToolEditor for sprk_analysistool entity", async () => {
        renderWithProvider(
            <ScopeConfigEditorApp {...makeDefaultProps("sprk_analysistool")} />
        );

        // ToolEditor specific elements
        expect(screen.getByText(/tool configuration/i)).toBeInTheDocument();
        expect(screen.getByTestId("codemirror-editor")).toBeInTheDocument();

        // Handler section loads asynchronously
        await waitFor(() => {
            const fallbackOrDropdown =
                screen.queryByTestId("handler-dropdown") ||
                screen.queryByTestId("handler-fallback-input");
            expect(fallbackOrDropdown).toBeInTheDocument();
        });
    });

    it("renders unknown entity warning for unrecognized entity", () => {
        renderWithProvider(
            <ScopeConfigEditorApp {...makeDefaultProps("sprk_unknown_entity")} />
        );

        expect(screen.getByText(/unknown entity type/i)).toBeInTheDocument();
        expect(screen.getByText(/sprk_unknown_entity/)).toBeInTheDocument();
    });

    it("renders unknown entity warning for empty entity name", () => {
        renderWithProvider(
            <ScopeConfigEditorApp {...makeDefaultProps("")} />
        );

        expect(screen.getByText(/unknown entity type/i)).toBeInTheDocument();
    });

    it("renders version footer (CLAUDE.md version footer requirement)", () => {
        renderWithProvider(
            <ScopeConfigEditorApp {...makeDefaultProps("sprk_analysisaction")} />
        );

        // Version footer must be present per CLAUDE.md requirement
        expect(screen.getByText(/v1\.0\.0/)).toBeInTheDocument();
    });

    it("calls onValueChange when ActionEditor changes", () => {
        const onValueChange = jest.fn();
        renderWithProvider(
            <ScopeConfigEditorApp
                entityLogicalName="sprk_analysisaction"
                fieldValue=""
                apiBaseUrl="https://test.api.com"
                onValueChange={onValueChange}
            />
        );

        const textarea = screen.getByLabelText(/system prompt editor/i);
        fireEvent.change(textarea, { target: { value: "New prompt" } });

        expect(onValueChange).toHaveBeenCalledWith("New prompt");
    });
});
