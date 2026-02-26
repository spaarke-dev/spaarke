/**
 * Integration tests for App (root component)
 *
 * Tests the root layout of the AnalysisWorkspace Code Page. Covers:
 *   - Valid params: renders 2-panel layout when authenticated and loaded
 *   - Missing params: shows appropriate error state
 *   - Auth loading: shows spinner while authenticating
 *   - Auth error: shows error with retry button
 *   - Xrm unavailable: shows "Dataverse Required" message
 *
 * Mock strategy: Mock the hooks (useAuth, useAnalysisLoader, etc.) so
 * the App component can be tested in isolation without the full provider tree.
 *
 * @see App.tsx
 */

import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import { FluentProvider, webLightTheme } from "@fluentui/react-components";
import { App } from "../App";
import {
    TEST_ANALYSIS_ID,
    TEST_DOCUMENT_ID,
    TEST_TENANT_ID,
    TEST_TOKEN,
    buildAnalysisRecord,
    buildDocumentMetadata,
} from "./mocks/fixtures";

// ---------------------------------------------------------------------------
// Mock hooks
// ---------------------------------------------------------------------------

// Mock useAuth hook
const mockUseAuth = jest.fn();
jest.mock("../hooks/useAuth", () => ({
    useAuth: () => mockUseAuth(),
}));

// Mock useAnalysisLoader hook
const mockUseAnalysisLoader = jest.fn();
jest.mock("../hooks/useAnalysisLoader", () => ({
    useAnalysisLoader: (opts: unknown) => mockUseAnalysisLoader(opts),
}));

// Mock useAutoSave hook
const mockUseAutoSave = jest.fn();
jest.mock("../hooks/useAutoSave", () => ({
    useAutoSave: (opts: unknown) => mockUseAutoSave(opts),
}));

// Mock useExportAnalysis hook
const mockUseExportAnalysis = jest.fn();
jest.mock("../hooks/useExportAnalysis", () => ({
    useExportAnalysis: (opts: unknown) => mockUseExportAnalysis(opts),
}));

// Mock useSelectionBroadcast hook (void return, just needs to not crash)
jest.mock("../hooks/useSelectionBroadcast", () => ({
    useSelectionBroadcast: jest.fn(),
}));

// Mock usePanelResize hook
jest.mock("../hooks/usePanelResize", () => ({
    usePanelResize: () => ({
        leftPanelWidth: 600,
        rightPanelWidth: 400,
        isDragging: false,
        containerRef: { current: null },
        onSplitterMouseDown: jest.fn(),
        onSplitterKeyDown: jest.fn(),
        resetToDefault: jest.fn(),
        currentRatio: 0.6,
    }),
}));

// Mock child components to simplify rendering
jest.mock("../components/EditorPanel", () => ({
    EditorPanel: React.forwardRef(function MockEditorPanel(
        props: Record<string, unknown>,
        _ref: React.Ref<unknown>
    ) {
        return React.createElement("div", { "data-testid": "editor-panel" }, "Editor Panel");
    }),
}));

jest.mock("../components/SourceViewerPanel", () => ({
    SourceViewerPanel: function MockSourceViewerPanel(props: { isCollapsed: boolean }) {
        return React.createElement(
            "div",
            { "data-testid": "source-viewer-panel" },
            props.isCollapsed ? "Collapsed" : "Expanded"
        );
    },
}));

jest.mock("../components/PanelSplitter", () => ({
    PanelSplitter: function MockPanelSplitter() {
        return React.createElement("div", { "data-testid": "panel-splitter" }, "Splitter");
    },
}));

jest.mock("../components/DocumentStreamBridge", () => ({
    DocumentStreamBridge: function MockDocStreamBridge() {
        return null;
    },
}));

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function renderApp(overrides?: Partial<Parameters<typeof App>[0]>) {
    return render(
        <FluentProvider theme={webLightTheme}>
            <App
                analysisId={overrides?.analysisId ?? TEST_ANALYSIS_ID}
                documentId={overrides?.documentId ?? TEST_DOCUMENT_ID}
                tenantId={overrides?.tenantId ?? TEST_TENANT_ID}
            />
        </FluentProvider>
    );
}

/** Set up hooks for an authenticated + loaded state */
function setupAuthenticatedAndLoaded() {
    mockUseAuth.mockReturnValue({
        token: TEST_TOKEN,
        isAuthenticated: true,
        isAuthenticating: false,
        authError: null,
        isXrmUnavailable: false,
        refreshToken: jest.fn(),
        retryAuth: jest.fn(),
    });

    mockUseAnalysisLoader.mockReturnValue({
        analysis: buildAnalysisRecord(),
        document: buildDocumentMetadata(),
        isAnalysisLoading: false,
        isDocumentLoading: false,
        isLoading: false,
        analysisError: null,
        documentError: null,
        retry: jest.fn(),
    });

    mockUseAutoSave.mockReturnValue({
        saveState: "idle",
        lastSavedAt: null,
        saveError: null,
        forceSave: jest.fn(),
        notifyContentChanged: jest.fn(),
    });

    mockUseExportAnalysis.mockReturnValue({
        exportState: "idle",
        exportError: null,
        doExport: jest.fn(),
    });
}

// ---------------------------------------------------------------------------
// Test Suite
// ---------------------------------------------------------------------------

describe("App", () => {
    beforeEach(() => {
        jest.clearAllMocks();
    });

    // -----------------------------------------------------------------------
    // 1. Valid Params - Authenticated Layout
    // -----------------------------------------------------------------------

    it("render_ValidParamsAndAuthenticated_ShowsTwoPanelLayout", () => {
        // Arrange
        setupAuthenticatedAndLoaded();

        // Act
        renderApp();

        // Assert: both panels are rendered
        expect(screen.getByTestId("editor-panel")).toBeInTheDocument();
        expect(screen.getByTestId("source-viewer-panel")).toBeInTheDocument();
        expect(screen.getByTestId("panel-splitter")).toBeInTheDocument();
    });

    // -----------------------------------------------------------------------
    // 2. Auth Loading State
    // -----------------------------------------------------------------------

    it("render_Authenticating_ShowsSpinner", () => {
        // Arrange
        mockUseAuth.mockReturnValue({
            token: null,
            isAuthenticated: false,
            isAuthenticating: true,
            authError: null,
            isXrmUnavailable: false,
            refreshToken: jest.fn(),
            retryAuth: jest.fn(),
        });

        // useAnalysisLoader etc. still need to be mocked even though they won't render
        mockUseAnalysisLoader.mockReturnValue({
            analysis: null, document: null, isAnalysisLoading: false,
            isDocumentLoading: false, isLoading: false, analysisError: null,
            documentError: null, retry: jest.fn(),
        });
        mockUseAutoSave.mockReturnValue({
            saveState: "idle", lastSavedAt: null, saveError: null,
            forceSave: jest.fn(), notifyContentChanged: jest.fn(),
        });
        mockUseExportAnalysis.mockReturnValue({
            exportState: "idle", exportError: null, doExport: jest.fn(),
        });

        // Act
        renderApp();

        // Assert: auth loading spinner is shown
        expect(screen.getByTestId("auth-loading")).toBeInTheDocument();
        expect(screen.getByText("Authenticating...")).toBeInTheDocument();

        // Assert: panels are NOT rendered
        expect(screen.queryByTestId("editor-panel")).not.toBeInTheDocument();
    });

    // -----------------------------------------------------------------------
    // 3. Auth Error with Retry
    // -----------------------------------------------------------------------

    it("render_AuthErrorRetryable_ShowsErrorWithRetryButton", () => {
        // Arrange
        const retryMock = jest.fn();
        mockUseAuth.mockReturnValue({
            token: null,
            isAuthenticated: false,
            isAuthenticating: false,
            authError: new Error("Token acquisition failed"),
            isXrmUnavailable: false,
            refreshToken: jest.fn(),
            retryAuth: retryMock,
        });

        mockUseAnalysisLoader.mockReturnValue({
            analysis: null, document: null, isAnalysisLoading: false,
            isDocumentLoading: false, isLoading: false, analysisError: null,
            documentError: null, retry: jest.fn(),
        });
        mockUseAutoSave.mockReturnValue({
            saveState: "idle", lastSavedAt: null, saveError: null,
            forceSave: jest.fn(), notifyContentChanged: jest.fn(),
        });
        mockUseExportAnalysis.mockReturnValue({
            exportState: "idle", exportError: null, doExport: jest.fn(),
        });

        // Act
        renderApp();

        // Assert: error state with retry
        expect(screen.getByTestId("auth-error-token")).toBeInTheDocument();
        expect(screen.getByText("Authentication Failed")).toBeInTheDocument();
        expect(screen.getByText("Token acquisition failed")).toBeInTheDocument();

        const retryButton = screen.getByTestId("auth-retry-button");
        expect(retryButton).toBeInTheDocument();

        // Act: click retry
        fireEvent.click(retryButton);

        // Assert
        expect(retryMock).toHaveBeenCalledTimes(1);
    });

    // -----------------------------------------------------------------------
    // 4. Xrm Unavailable
    // -----------------------------------------------------------------------

    it("render_XrmUnavailable_ShowsDataverseRequiredMessage", () => {
        // Arrange
        mockUseAuth.mockReturnValue({
            token: null,
            isAuthenticated: false,
            isAuthenticating: false,
            authError: new Error("Xrm SDK not available"),
            isXrmUnavailable: true,
            refreshToken: jest.fn(),
            retryAuth: jest.fn(),
        });

        mockUseAnalysisLoader.mockReturnValue({
            analysis: null, document: null, isAnalysisLoading: false,
            isDocumentLoading: false, isLoading: false, analysisError: null,
            documentError: null, retry: jest.fn(),
        });
        mockUseAutoSave.mockReturnValue({
            saveState: "idle", lastSavedAt: null, saveError: null,
            forceSave: jest.fn(), notifyContentChanged: jest.fn(),
        });
        mockUseExportAnalysis.mockReturnValue({
            exportState: "idle", exportError: null, doExport: jest.fn(),
        });

        // Act
        renderApp();

        // Assert
        expect(screen.getByTestId("auth-error-xrm")).toBeInTheDocument();
        expect(screen.getByText("Dataverse Required")).toBeInTheDocument();
        expect(
            screen.getByText(/must be opened from within Dataverse/i)
        ).toBeInTheDocument();
    });

    // -----------------------------------------------------------------------
    // 5. Source Panel Collapsed
    // -----------------------------------------------------------------------

    it("render_Authenticated_SourcePanelInitiallyExpanded", () => {
        // Arrange
        setupAuthenticatedAndLoaded();

        // Act
        renderApp();

        // Assert: source viewer starts expanded
        expect(screen.getByTestId("source-viewer-panel")).toHaveTextContent("Expanded");
    });
});
