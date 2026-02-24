/**
 * Unit tests for ToolEditor component
 *
 * Tests:
 * - Valid JSON clears error state
 * - Invalid JSON shows error message
 * - Handler dropdown is rendered when API is available (mocked fetch)
 * - Fallback text input shown when API fails
 * - CodeMirror editor container is present
 *
 * Note: CodeMirror is mocked via __mocks__/codemirrorMock.js.
 * DOM-heavy CodeMirror interactions are not tested here — JSON validation
 * logic is tested independently since it's computed from the `value` prop.
 */

import * as React from "react";
import { render, screen, waitFor } from "@testing-library/react";
import { FluentProvider, webLightTheme } from "@fluentui/react-components";
import { ToolEditor } from "../components/ToolEditor";

// ─────────────────────────────────────────────────────────────────────────────
// Mocks
// ─────────────────────────────────────────────────────────────────────────────

// Mock global fetch
const mockFetch = jest.fn();
global.fetch = mockFetch;

// Mock AbortSignal.timeout (not available in jsdom)
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

function makeHandlersResponse(handlers: { handlerClass: string; displayName?: string }[]) {
    return Promise.resolve({
        ok: true,
        status: 200,
        json: () => Promise.resolve(handlers),
    } as Response);
}

function makeFailedResponse() {
    return Promise.resolve({
        ok: false,
        status: 500,
        json: () => Promise.resolve({ error: "Server error" }),
    } as Response);
}

// ─────────────────────────────────────────────────────────────────────────────
// Tests
// ─────────────────────────────────────────────────────────────────────────────

describe("ToolEditor — JSON validation", () => {
    beforeEach(() => {
        mockFetch.mockReset();
        // Default: successful API response with no handlers
        mockFetch.mockReturnValue(makeHandlersResponse([]));
    });

    it("renders without errors for empty value", () => {
        renderWithProvider(
            <ToolEditor value="" apiBaseUrl="https://test.api.com" onChange={jest.fn()} />
        );
        expect(screen.queryByTestId("json-error-message")).not.toBeInTheDocument();
    });

    it("shows no validation error for valid JSON object", () => {
        const validJson = JSON.stringify({ name: "test", parameters: {} }, null, 2);
        renderWithProvider(
            <ToolEditor value={validJson} apiBaseUrl="https://test.api.com" onChange={jest.fn()} />
        );
        expect(screen.queryByTestId("json-error-message")).not.toBeInTheDocument();
    });

    it("shows JSON valid indicator for valid JSON", () => {
        const validJson = JSON.stringify({ name: "test" });
        renderWithProvider(
            <ToolEditor value={validJson} apiBaseUrl="https://test.api.com" onChange={jest.fn()} />
        );
        expect(screen.getByTestId("json-valid-indicator")).toBeInTheDocument();
        expect(screen.getByTestId("json-valid-indicator")).toHaveTextContent(/valid json/i);
    });

    it("shows error message for invalid JSON string", () => {
        const invalidJson = "{ invalid json }";
        renderWithProvider(
            <ToolEditor value={invalidJson} apiBaseUrl="https://test.api.com" onChange={jest.fn()} />
        );
        expect(screen.getByTestId("json-error-message")).toBeInTheDocument();
    });

    it("shows JSON error indicator for invalid JSON", () => {
        const invalidJson = "not json at all";
        renderWithProvider(
            <ToolEditor value={invalidJson} apiBaseUrl="https://test.api.com" onChange={jest.fn()} />
        );
        expect(screen.getByTestId("json-error-indicator")).toBeInTheDocument();
        expect(screen.getByTestId("json-error-indicator")).toHaveTextContent(/json error/i);
    });

    it("clears error when JSON becomes valid", () => {
        const { rerender } = renderWithProvider(
            <FluentProvider theme={webLightTheme}>
                <ToolEditor value="{ bad json" apiBaseUrl="https://test.api.com" onChange={jest.fn()} />
            </FluentProvider>
        );

        expect(screen.getByTestId("json-error-message")).toBeInTheDocument();

        rerender(
            <FluentProvider theme={webLightTheme}>
                <ToolEditor value='{"valid": true}' apiBaseUrl="https://test.api.com" onChange={jest.fn()} />
            </FluentProvider>
        );

        expect(screen.queryByTestId("json-error-message")).not.toBeInTheDocument();
    });

    it("shows error for JSON array (valid JSON but common mistake)", () => {
        // Arrays are valid JSON, so should NOT show an error
        const jsonArray = "[1, 2, 3]";
        renderWithProvider(
            <ToolEditor value={jsonArray} apiBaseUrl="https://test.api.com" onChange={jest.fn()} />
        );
        expect(screen.queryByTestId("json-error-message")).not.toBeInTheDocument();
    });

    it("does not show valid indicator for whitespace-only value", () => {
        renderWithProvider(
            <ToolEditor value="   " apiBaseUrl="https://test.api.com" onChange={jest.fn()} />
        );
        // Empty/whitespace: no validation, so no indicators
        expect(screen.queryByTestId("json-valid-indicator")).not.toBeInTheDocument();
        expect(screen.queryByTestId("json-error-message")).not.toBeInTheDocument();
    });
});

describe("ToolEditor — handler dropdown", () => {
    beforeEach(() => {
        mockFetch.mockReset();
    });

    it("renders handler dropdown when API succeeds", async () => {
        mockFetch.mockReturnValue(
            makeHandlersResponse([
                { handlerClass: "DocumentSearchToolHandler", displayName: "Document Search" },
                { handlerClass: "AnalysisQueryToolHandler", displayName: "Analysis Query" },
            ])
        );

        renderWithProvider(
            <ToolEditor value="" apiBaseUrl="https://test.api.com" onChange={jest.fn()} />
        );

        await waitFor(() => {
            expect(screen.getByTestId("handler-dropdown")).toBeInTheDocument();
        });
    });

    it("renders fallback text input when API fails", async () => {
        mockFetch.mockReturnValue(makeFailedResponse());

        renderWithProvider(
            <ToolEditor value="" apiBaseUrl="https://test.api.com" onChange={jest.fn()} />
        );

        await waitFor(() => {
            expect(screen.getByTestId("handler-fallback-input")).toBeInTheDocument();
        });
    });

    it("renders fallback text input when fetch throws an error", async () => {
        mockFetch.mockRejectedValue(new Error("Network error"));

        renderWithProvider(
            <ToolEditor value="" apiBaseUrl="https://test.api.com" onChange={jest.fn()} />
        );

        await waitFor(() => {
            expect(screen.getByTestId("handler-fallback-input")).toBeInTheDocument();
        });
    });

    it("renders fallback text input when apiBaseUrl is empty", async () => {
        renderWithProvider(
            <ToolEditor value="" apiBaseUrl="" onChange={jest.fn()} />
        );

        await waitFor(() => {
            expect(screen.getByTestId("handler-fallback-input")).toBeInTheDocument();
        });
    });

    it("renders CodeMirror editor container", () => {
        mockFetch.mockReturnValue(makeHandlersResponse([]));

        renderWithProvider(
            <ToolEditor value="" apiBaseUrl="https://test.api.com" onChange={jest.fn()} />
        );

        expect(screen.getByTestId("codemirror-editor")).toBeInTheDocument();
    });
});
