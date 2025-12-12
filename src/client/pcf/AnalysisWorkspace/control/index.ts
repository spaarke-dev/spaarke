/**
 * Analysis Workspace PCF Control
 *
 * AI Document Analysis Workspace with:
 * - Two-column layout (Working Document + Source Preview)
 * - Monaco Editor for markdown editing
 * - AI Chat panel with SSE streaming
 * - Auto-save functionality
 *
 * Architecture:
 * - Receives analysis context via Custom Page parameters
 * - Renders Fluent UI v9 workspace layout
 * - Streams AI responses via SSE from BFF API
 * - Auto-saves working document to Dataverse
 *
 * ADR Compliance:
 * - ADR-001: Fluent UI v9 Components
 * - ADR-002: TypeScript Strict Mode
 * - ADR-003: Separation of Concerns
 * - ADR-006: PCF over webresources
 * - ADR-010: Configuration Over Code
 *
 * @version 1.0.0
 */

// Declare global Xrm
declare const Xrm: any;

import { IInputs, IOutputs } from "./generated/ManifestTypes";
import * as React from "react";
import * as ReactDOM from "react-dom/client";
import {
    FluentProvider,
    webLightTheme,
    webDarkTheme,
    Theme
} from "@fluentui/react-components";
import { AnalysisWorkspaceApp } from "./components/AnalysisWorkspaceApp";
import { logInfo, logError } from "./utils/logger";
import { getApiBaseUrl } from "./utils/environmentVariables";

// ─────────────────────────────────────────────────────────────────────────────
// Theme Utilities
// ─────────────────────────────────────────────────────────────────────────────

const STORAGE_KEY = 'spaarke-theme';
type ThemePreference = 'auto' | 'light' | 'dark';

function getUserThemePreference(): ThemePreference {
    try {
        const stored = localStorage.getItem(STORAGE_KEY);
        if (stored === 'light' || stored === 'dark' || stored === 'auto') {
            return stored;
        }
        try {
            const parentStored = window.parent?.localStorage?.getItem(STORAGE_KEY);
            if (parentStored === 'light' || parentStored === 'dark' || parentStored === 'auto') {
                return parentStored;
            }
        } catch { /* Cross-origin blocked */ }
    } catch { /* localStorage not available */ }
    return 'auto';
}

function detectDarkModeFromUrl(): boolean | null {
    try {
        if (window.location.href.includes('themeOption%3Ddarkmode') ||
            window.location.href.includes('themeOption=darkmode')) {
            return true;
        }
        try {
            const parentUrl = window.parent?.location?.href;
            if (parentUrl?.includes('themeOption%3Ddarkmode') ||
                parentUrl?.includes('themeOption=darkmode')) {
                return true;
            }
        } catch { /* Cross-origin blocked */ }
    } catch { /* Error */ }
    return null;
}

function getResolvedTheme(): Theme {
    const preference = getUserThemePreference();
    if (preference === 'dark') return webDarkTheme;
    if (preference === 'light') return webLightTheme;

    // Auto mode - check URL flag first
    const urlDarkMode = detectDarkModeFromUrl();
    if (urlDarkMode !== null) {
        return urlDarkMode ? webDarkTheme : webLightTheme;
    }

    // Fallback to system preference
    if (typeof window !== 'undefined' && window.matchMedia) {
        return window.matchMedia('(prefers-color-scheme: dark)').matches
            ? webDarkTheme
            : webLightTheme;
    }
    return webLightTheme;
}

// ─────────────────────────────────────────────────────────────────────────────
// PCF Control Class
// ─────────────────────────────────────────────────────────────────────────────

export class AnalysisWorkspace implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    private _container: HTMLDivElement;
    private _context: ComponentFramework.Context<IInputs>;
    private _notifyOutputChanged: () => void;
    private _root: ReactDOM.Root | null = null;

    // Output values
    private _workingDocumentContent: string = "";
    private _chatHistory: string = "[]";
    private _analysisStatus: string = "Draft";

    // Theme state
    private _currentTheme: Theme = webLightTheme;
    private _themeMediaQuery: MediaQueryList | null = null;

    // Environment variable loaded API URL
    private _apiBaseUrl: string = "";

    constructor() {
        logInfo("AnalysisWorkspace", "Constructor called");
    }

    public init(
        context: ComponentFramework.Context<IInputs>,
        notifyOutputChanged: () => void,
        _state: ComponentFramework.Dictionary,
        container: HTMLDivElement
    ): void {
        logInfo("AnalysisWorkspace", "init() called");

        this._context = context;
        this._container = container;
        this._notifyOutputChanged = notifyOutputChanged;

        // Set up theme
        this._currentTheme = getResolvedTheme();
        this.setupThemeListeners();

        // Load configuration from environment variables asynchronously
        this.loadConfiguration().then(() => {
            logInfo("AnalysisWorkspace", `Configuration loaded, API URL: ${this._apiBaseUrl}`);
        });

        // Initial render
        this.renderComponent();
    }

    /**
     * Load configuration from Dataverse environment variables
     * This implements the PCF Environment Variable Access Pattern (Task 052)
     */
    private async loadConfiguration(): Promise<void> {
        try {
            // Check for input parameter override first (from Custom Page)
            const inputUrl = this._context.parameters.apiBaseUrl?.raw;
            if (inputUrl && inputUrl.trim() !== "") {
                this._apiBaseUrl = inputUrl;
                this.renderComponent();
                return;
            }

            // Load from Dataverse environment variable
            this._apiBaseUrl = await getApiBaseUrl(this._context.webAPI);
            this.renderComponent();
        } catch (error) {
            logError("AnalysisWorkspace", "Failed to load configuration from environment variables", error);
            // Use default fallback for development
            this._apiBaseUrl = "https://spe-api-dev-67e2xz.azurewebsites.net/api";
            this.renderComponent();
        }
    }

    public updateView(context: ComponentFramework.Context<IInputs>): void {
        logInfo("AnalysisWorkspace", "updateView() called");
        this._context = context;
        this.renderComponent();
    }

    public getOutputs(): IOutputs {
        return {
            workingDocumentContent: this._workingDocumentContent,
            chatHistory: this._chatHistory,
            analysisStatus: this._analysisStatus
        };
    }

    public destroy(): void {
        logInfo("AnalysisWorkspace", "destroy() called");
        this.cleanupThemeListeners();
        if (this._root) {
            this._root.unmount();
            this._root = null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private Methods
    // ─────────────────────────────────────────────────────────────────────────

    private renderComponent(): void {
        // Get Analysis ID - prefer form context (entityId) over bound field value
        // When on a form, entityId gives us the record GUID directly
        let analysisId = "";
        const contextInfo = (this._context.mode as any).contextInfo;
        if (contextInfo?.entityId) {
            // On a form - use the record ID from form context
            analysisId = contextInfo.entityId.replace(/[{}]/g, "");
            logInfo("AnalysisWorkspace", `Got analysisId from form context: ${analysisId}`);
        } else if (this._context.parameters.analysisId?.raw) {
            // Custom Page or other context - use the parameter
            const rawValue = this._context.parameters.analysisId.raw;
            // Check if it looks like a GUID (contains hyphens and is 36 chars)
            if (rawValue.includes("-") && rawValue.length >= 32) {
                analysisId = rawValue.replace(/[{}]/g, "");
            }
            logInfo("AnalysisWorkspace", `Got analysisId from parameter: ${analysisId}`);
        }

        // Get other input parameters
        const documentId = this._context.parameters.documentId?.raw || "";
        const containerId = this._context.parameters.containerId?.raw || "";
        const fileId = this._context.parameters.fileId?.raw || "";

        // Use loaded API URL from environment variable, fallback to input parameter or default
        const apiBaseUrl = this._apiBaseUrl ||
            this._context.parameters.apiBaseUrl?.raw ||
            "https://spe-api-dev-67e2xz.azurewebsites.net/api";

        logInfo("AnalysisWorkspace", `Rendering with analysisId: ${analysisId}, apiUrl: ${apiBaseUrl.substring(0, 30)}...`);

        // Create or update React root
        if (!this._root) {
            this._root = ReactDOM.createRoot(this._container);
        }

        this._root.render(
            React.createElement(
                FluentProvider,
                { theme: this._currentTheme },
                React.createElement(AnalysisWorkspaceApp, {
                    analysisId,
                    documentId,
                    containerId,
                    fileId,
                    apiBaseUrl,
                    webApi: this._context.webAPI,
                    onWorkingDocumentChange: this.handleWorkingDocumentChange.bind(this),
                    onChatHistoryChange: this.handleChatHistoryChange.bind(this),
                    onStatusChange: this.handleStatusChange.bind(this)
                })
            )
        );
    }

    private handleWorkingDocumentChange(content: string): void {
        this._workingDocumentContent = content;
        this._notifyOutputChanged();
    }

    private handleChatHistoryChange(history: string): void {
        this._chatHistory = history;
        this._notifyOutputChanged();
    }

    private handleStatusChange(status: string): void {
        this._analysisStatus = status;
        this._notifyOutputChanged();
    }

    private setupThemeListeners(): void {
        // Listen for system theme changes (auto mode)
        if (typeof window !== 'undefined' && window.matchMedia) {
            this._themeMediaQuery = window.matchMedia('(prefers-color-scheme: dark)');
            this._themeMediaQuery.addEventListener('change', this.handleSystemThemeChange.bind(this));
        }

        // Listen for custom theme change events
        window.addEventListener('spaarke-theme-change', this.handleThemeChange.bind(this));
    }

    private cleanupThemeListeners(): void {
        if (this._themeMediaQuery) {
            this._themeMediaQuery.removeEventListener('change', this.handleSystemThemeChange.bind(this));
        }
        window.removeEventListener('spaarke-theme-change', this.handleThemeChange.bind(this));
    }

    private handleSystemThemeChange(): void {
        if (getUserThemePreference() === 'auto') {
            this._currentTheme = getResolvedTheme();
            this.renderComponent();
        }
    }

    private handleThemeChange(): void {
        this._currentTheme = getResolvedTheme();
        this.renderComponent();
    }
}
