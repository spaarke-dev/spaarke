/**
 * Analysis Builder PCF Control
 *
 * AI Analysis configuration dialog with:
 * - Playbook selector (card-based)
 * - Tab-based scope configuration (Action, Skills, Knowledge, Tools, Output)
 * - Checkbox selection for each scope type
 * - Execute analysis with streaming response
 *
 * Architecture:
 * - Receives document context via Custom Page parameters
 * - Renders Fluent UI v9 modal-style dialog
 * - Outputs selected configuration for analysis execution
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
declare const Xrm: ComponentFramework.Xrm;

import { IInputs, IOutputs } from "./generated/ManifestTypes";
import * as React from "react";
import * as ReactDOM from "react-dom/client";
import {
    FluentProvider,
    webLightTheme,
    webDarkTheme,
    Theme
} from "@fluentui/react-components";
import { AnalysisBuilderApp } from "./components/AnalysisBuilderApp";
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

export class AnalysisBuilder implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    private _container: HTMLDivElement;
    private _context: ComponentFramework.Context<IInputs>;
    private _notifyOutputChanged: () => void;
    private _root: ReactDOM.Root | null = null;

    // Output values
    private _selectedPlaybookId: string = "";
    private _selectedActionId: string = "";
    private _selectedSkillIds: string = "";
    private _selectedKnowledgeIds: string = "";
    private _selectedToolIds: string = "";
    private _createdAnalysisId: string = "";
    private _shouldClose: boolean = false;

    // Theme state
    private _currentTheme: Theme = webLightTheme;
    private _themeMediaQuery: MediaQueryList | null = null;

    // Environment variable loaded API URL
    private _apiBaseUrl: string = "";
    private _configLoaded: boolean = false;

    constructor() {
        logInfo("AnalysisBuilder", "Constructor called");
    }

    public init(
        context: ComponentFramework.Context<IInputs>,
        notifyOutputChanged: () => void,
        _state: ComponentFramework.Dictionary,
        container: HTMLDivElement
    ): void {
        logInfo("AnalysisBuilder", "init() called");

        this._context = context;
        this._container = container;
        this._notifyOutputChanged = notifyOutputChanged;

        // Set up theme
        this._currentTheme = getResolvedTheme();
        this.setupThemeListeners();

        // Load configuration from environment variables asynchronously
        this.loadConfiguration().then(() => {
            logInfo("AnalysisBuilder", `Configuration loaded, API URL: ${this._apiBaseUrl}`);
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
                this._configLoaded = true;
                this.renderComponent();
                return;
            }

            // Load from Dataverse environment variable
            this._apiBaseUrl = await getApiBaseUrl(this._context.webAPI);
            this._configLoaded = true;
            this.renderComponent();
        } catch (error) {
            logError("AnalysisBuilder", "Failed to load configuration from environment variables", error);
            // Use default fallback for development
            this._apiBaseUrl = "https://spe-api-dev-67e2xz.azurewebsites.net/api";
            this._configLoaded = true;
            this.renderComponent();
        }
    }

    public updateView(context: ComponentFramework.Context<IInputs>): void {
        logInfo("AnalysisBuilder", "updateView() called");
        this._context = context;
        this.renderComponent();
    }

    public getOutputs(): IOutputs {
        return {
            selectedPlaybookId: this._selectedPlaybookId,
            selectedActionId: this._selectedActionId,
            selectedSkillIds: this._selectedSkillIds,
            selectedKnowledgeIds: this._selectedKnowledgeIds,
            selectedToolIds: this._selectedToolIds,
            createdAnalysisId: this._createdAnalysisId,
            shouldClose: this._shouldClose
        };
    }

    public destroy(): void {
        logInfo("AnalysisBuilder", "destroy() called");
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
        // Get input parameters
        const documentId = this._context.parameters.documentId?.raw || "";
        const documentName = this._context.parameters.documentName?.raw || "";
        const containerId = this._context.parameters.containerId?.raw || "";
        const fileId = this._context.parameters.fileId?.raw || "";

        // Use loaded API URL from environment variable, fallback to input parameter or default
        const apiBaseUrl = this._apiBaseUrl ||
            this._context.parameters.apiBaseUrl?.raw ||
            "https://spe-api-dev-67e2xz.azurewebsites.net/api";

        logInfo("AnalysisBuilder", `Rendering with documentId: ${documentId}, apiUrl: ${apiBaseUrl.substring(0, 30)}...`);

        // Create or update React root
        if (!this._root) {
            this._root = ReactDOM.createRoot(this._container);
        }

        this._root.render(
            React.createElement(
                FluentProvider,
                { theme: this._currentTheme },
                React.createElement(AnalysisBuilderApp, {
                    documentId,
                    documentName,
                    containerId,
                    fileId,
                    apiBaseUrl,
                    webApi: this._context.webAPI,
                    onPlaybookSelect: this.handlePlaybookSelect.bind(this),
                    onActionSelect: this.handleActionSelect.bind(this),
                    onSkillsSelect: this.handleSkillsSelect.bind(this),
                    onKnowledgeSelect: this.handleKnowledgeSelect.bind(this),
                    onToolsSelect: this.handleToolsSelect.bind(this),
                    onExecute: this.handleExecute.bind(this),
                    onCancel: this.handleCancel.bind(this)
                })
            )
        );
    }

    private handlePlaybookSelect(playbookId: string): void {
        this._selectedPlaybookId = playbookId;
        this._notifyOutputChanged();
    }

    private handleActionSelect(actionId: string): void {
        this._selectedActionId = actionId;
        this._notifyOutputChanged();
    }

    private handleSkillsSelect(skillIds: string[]): void {
        this._selectedSkillIds = skillIds.join(",");
        this._notifyOutputChanged();
    }

    private handleKnowledgeSelect(knowledgeIds: string[]): void {
        this._selectedKnowledgeIds = knowledgeIds.join(",");
        this._notifyOutputChanged();
    }

    private handleToolsSelect(toolIds: string[]): void {
        this._selectedToolIds = toolIds.join(",");
        this._notifyOutputChanged();
    }

    private handleExecute(analysisId: string): void {
        this._createdAnalysisId = analysisId;
        this._shouldClose = true;
        this._notifyOutputChanged();
    }

    private handleCancel(): void {
        this._shouldClose = true;
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
