/**
 * SPE File Viewer PCF Control
 *
 * Entry point that wires together:
 * - MSAL authentication (with named scope api://<BFF_APP_ID>/SDAP.Access)
 * - BFF API client
 * - React FilePreview component
 * - Theme management via shared themeStorage utilities
 */

import { IInputs, IOutputs } from "./generated/ManifestTypes";
import * as React from 'react';
import { createRoot, Root } from 'react-dom/client';
import { AuthService } from './AuthService';
import { FilePreview } from './FilePreview';
import { FileViewerState } from './types';
import { v4 as uuidv4 } from 'uuid';

// ============================================================================
// Theme Storage Utilities (from Spaarke.UI.Components)
// TODO: Import from '@spaarke/ui-components' when package is published
// See: ADR-012, projects/mda-darkmode-theme/spec.md Section 3.4
// ============================================================================

const THEME_STORAGE_KEY = 'spaarke-theme';
const THEME_CHANGE_EVENT = 'spaarke-theme-change';

type ThemePreference = 'light' | 'dark' | 'auto';

function getUserThemePreference(): ThemePreference {
    const stored = localStorage.getItem(THEME_STORAGE_KEY);
    if (stored === 'light' || stored === 'dark' || stored === 'auto') {
        return stored;
    }
    return 'auto';
}

function getEffectiveDarkMode(context?: any): boolean {
    const preference = getUserThemePreference();

    // Explicit user choice
    if (preference === 'dark') return true;
    if (preference === 'light') return false;

    // Auto mode: check Power Platform context first
    if (context?.fluentDesignLanguage?.isDarkTheme !== undefined) {
        return context.fluentDesignLanguage.isDarkTheme;
    }

    // Fallback for Custom Pages: check navbar background color
    const navbar = document.querySelector("[data-id='navbar-container']");
    if (navbar) {
        const bg = getComputedStyle(navbar).backgroundColor;
        if (bg === "rgb(10, 10, 10)") return true;
        if (bg === "rgb(240, 240, 240)") return false;
    }

    // Final fallback to system preference
    return window.matchMedia?.('(prefers-color-scheme: dark)').matches ?? false;
}

interface ThemeChangeHandler {
    (isDark: boolean): void;
}

function setupThemeListener(
    onChange: ThemeChangeHandler,
    context?: any
): () => void {
    const handleStorageChange = (event: StorageEvent) => {
        if (event.key === THEME_STORAGE_KEY) {
            onChange(getEffectiveDarkMode(context));
        }
    };

    const handleThemeEvent = () => {
        onChange(getEffectiveDarkMode(context));
    };

    const handleSystemChange = (event: MediaQueryListEvent) => {
        if (getUserThemePreference() === 'auto') {
            onChange(event.matches);
        }
    };

    window.addEventListener('storage', handleStorageChange);
    window.addEventListener(THEME_CHANGE_EVENT, handleThemeEvent);

    const mediaQuery = window.matchMedia?.('(prefers-color-scheme: dark)');
    mediaQuery?.addEventListener('change', handleSystemChange);

    return () => {
        window.removeEventListener('storage', handleStorageChange);
        window.removeEventListener(THEME_CHANGE_EVENT, handleThemeEvent);
        mediaQuery?.removeEventListener('change', handleSystemChange);
    };
}

// ============================================================================

export class SpeFileViewer implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    // PCF container element
    private container: HTMLDivElement;

    // React root for rendering (React 19+)
    private root: Root | null = null;

    // MSAL authentication service
    private authService: AuthService | null = null;

    // Current access token (cached)
    private accessToken: string | null = null;

    // Configuration from manifest properties
    private bffApiUrl = '';
    private clientAppId = '';
    private bffAppId = '';
    private tenantId = '';

    // Correlation ID for current session
    private correlationId: string;

    // State machine for component lifecycle
    private _state: FileViewerState = FileViewerState.Loading;

    // PCF notification callback (for triggering re-renders)
    private _notifyOutputChanged: (() => void) | null = null;

    // Current context reference (for re-rendering)
    private _context: ComponentFramework.Context<IInputs> | null = null;

    // Error message when in Error state
    private _errorMessage: string | null = null;

    // AbortController for cancelling in-flight requests (Task 022)
    private _abortController: AbortController | null = null;

    // Previous document ID for detecting changes
    private _previousDocumentId: string | null = null;

    // Theme listener cleanup function (Dark Mode Theme Toggle feature)
    private _cleanupThemeListener: (() => void) | null = null;

    constructor() {
        // Generate correlation ID for this control instance
        this.correlationId = uuidv4();
        console.log(`[SpeFileViewer] Control instance created. Correlation ID: ${this.correlationId}`);
    }

    /**
     * Initialize the control
     * - Set up container
     * - Initialize MSAL
     * - Acquire access token
     */
    public async init(
        context: ComponentFramework.Context<IInputs>,
        notifyOutputChanged: () => void,
        state: ComponentFramework.Dictionary,
        container: HTMLDivElement
    ): Promise<void> {
        this.container = container;
        this._notifyOutputChanged = notifyOutputChanged;
        this._context = context;

        // IMMEDIATELY set state to Loading and render loading UI
        // (Must happen within 200ms per task spec)
        this.transitionTo(FileViewerState.Loading);
        this.renderBasedOnState();

        // Apply responsive height styling
        const controlHeight = context.parameters.controlHeight?.raw ?? 600;
        this.container.style.minHeight = `${controlHeight}px`;
        this.container.style.height = '100%';
        this.container.style.display = 'flex';
        this.container.style.flexDirection = 'column';

        console.log('[SpeFileViewer] Initializing control...');
        console.log(`[SpeFileViewer] Configured height: ${controlHeight}px (min) with 100% responsive expansion`);

        try {
            // Extract configuration from manifest properties
            this.tenantId = context.parameters.tenantId.raw || '';
            this.clientAppId = context.parameters.clientAppId.raw || '';
            this.bffAppId = context.parameters.bffAppId.raw || '';
            this.bffApiUrl = context.parameters.bffApiUrl.raw || 'https://spe-api-dev-67e2xz.azurewebsites.net';

            // Validate configuration
            if (!this.tenantId || !this.clientAppId || !this.bffAppId) {
                throw new Error('Missing required configuration: tenantId, clientAppId, and bffAppId must be provided');
            }

            console.log(`[SpeFileViewer] Configuration:`, {
                tenantId: this.tenantId,
                clientAppId: this.clientAppId,
                bffAppId: this.bffAppId,
                bffApiUrl: this.bffApiUrl
            });

            // Initialize MSAL auth service with both app IDs
            this.authService = new AuthService(this.tenantId, this.clientAppId, this.bffAppId);
            await this.authService.initialize();

            console.log(`[SpeFileViewer] MSAL initialized. Scope: ${this.authService.getScope()}`);

            // Acquire access token (needed for BFF calls)
            this.accessToken = await this.authService.getAccessToken();
            console.log('[SpeFileViewer] Access token acquired');

            // Create AbortController for this session (Task 022)
            this._abortController = new AbortController();

            // Track initial document ID for change detection
            try {
                this._previousDocumentId = this.extractDocumentId(context);
                console.log(`[SpeFileViewer] Initial document ID: ${this._previousDocumentId || '(none)'}`);
            } catch {
                // extractDocumentId may throw for invalid GUIDs - handle gracefully in init
                this._previousDocumentId = null;
            }

            // Set up theme listener for global theme changes
            this._cleanupThemeListener = setupThemeListener(
                (isDark) => {
                    console.log(`[SpeFileViewer] Theme changed: isDark=${isDark}`);
                    if (this._context && this._state === FileViewerState.Ready) {
                        this.renderControl(this._context);
                    }
                },
                context
            );
            console.log('[SpeFileViewer] Theme listener initialized');

            // Transition to Ready and render React component
            this.transitionTo(FileViewerState.Ready);
            this.renderBasedOnState();

        } catch (error) {
            console.error('[SpeFileViewer] Initialization failed:', error);
            this._errorMessage = error instanceof Error ? error.message : String(error);
            this.transitionTo(FileViewerState.Error);
            this.renderBasedOnState();
        }
    }

    /**
     * Update view when context changes
     * - Detects documentId changes
     * - Aborts in-flight requests if document changes (Task 022)
     * - Re-renders React component (only when in Ready state)
     */
    public updateView(context: ComponentFramework.Context<IInputs>): void {
        this._context = context;

        // Only process if in Ready state
        if (this._state !== FileViewerState.Ready) {
            return;
        }

        // Extract current document ID (handle extraction errors gracefully)
        let currentDocumentId: string | null = null;
        try {
            currentDocumentId = this.extractDocumentId(context);
        } catch {
            currentDocumentId = null;
        }

        // Check if document ID changed (Task 022 - cancel previous, start new)
        if (currentDocumentId !== this._previousDocumentId) {
            console.log(`[SpeFileViewer] Document ID changed: ${this._previousDocumentId} → ${currentDocumentId}`);

            // Abort any in-flight requests from previous document
            if (this._abortController) {
                this._abortController.abort();
                console.log('[SpeFileViewer] Aborted previous request due to document change');
            }

            // Create new AbortController for new document
            this._abortController = new AbortController();
            this._previousDocumentId = currentDocumentId;
        }

        // Re-render React component
        this.renderControl(context);
    }

    /**
     * Transition to a new state with logging
     *
     * @param newState The state to transition to
     */
    private transitionTo(newState: FileViewerState): void {
        const previousState = this._state;
        this._state = newState;

        console.log(`[SpeFileViewer] State: ${previousState} → ${newState}`);

        // Notify PCF framework of state change (triggers updateView)
        this._notifyOutputChanged?.();
    }

    /**
     * Get current component state
     *
     * @returns Current FileViewerState
     */
    public getState(): FileViewerState {
        return this._state;
    }

    /**
     * Render UI based on current state
     */
    private renderBasedOnState(): void {
        switch (this._state) {
            case FileViewerState.Loading:
                this.renderLoading();
                break;
            case FileViewerState.Ready:
                if (this._context) {
                    this.renderControl(this._context);
                }
                break;
            case FileViewerState.Error:
                this.renderError(this._errorMessage || 'An unknown error occurred');
                break;
        }
    }

    /**
     * Render loading overlay with spinner (shown during init)
     *
     * Accessibility: Uses role="status" and aria-busy for screen readers.
     * The loading indicator is announced when it appears.
     */
    private renderLoading(): void {
        // Create loading overlay element programmatically for better control
        const overlay = document.createElement('div');
        overlay.className = 'spe-file-viewer-loading-overlay';
        overlay.setAttribute('role', 'status');
        overlay.setAttribute('aria-busy', 'true');
        overlay.setAttribute('aria-label', 'Loading document');

        // Create spinner element
        const spinner = document.createElement('div');
        spinner.className = 'spe-file-viewer-loading-spinner';

        // Create loading text
        const text = document.createElement('span');
        text.className = 'spe-file-viewer-loading-text';
        text.textContent = 'Loading document...';

        // Assemble the overlay
        overlay.appendChild(spinner);
        overlay.appendChild(text);

        // Clear container and add overlay
        this.container.innerHTML = '';
        this.container.appendChild(overlay);
    }

    /**
     * Show the loading overlay (used when re-entering Loading state)
     */
    public showLoading(): void {
        this.transitionTo(FileViewerState.Loading);
        this.renderBasedOnState();
    }

    /**
     * Hide the loading overlay by transitioning to Ready state
     * Note: This is typically called internally after successful init
     */
    public hideLoading(): void {
        if (this._state === FileViewerState.Loading && this._context) {
            this.transitionTo(FileViewerState.Ready);
            this.renderBasedOnState();
        }
    }

    /**
     * Extract document ID from the documentId input property or form record ID
     * - If documentId is provided (manually entered GUID), use it
     * - If documentId is empty/blank, use the current form record ID (default behavior)
     *
     * **CRITICAL VALIDATION:** Only accepts GUID format (Dataverse Document ID).
     * Rejects SharePoint Item IDs (e.g., 01LBYCMX...) to prevent root cause error.
     */
    private extractDocumentId(context: ComponentFramework.Context<IInputs>): string {
        const rawValue = context.parameters.documentId.raw;

        // Option 1: User manually configured document ID
        if (rawValue && typeof rawValue === 'string' && rawValue.trim() !== '') {
            const trimmed = rawValue.trim();

            // Validate GUID format (prevent sending driveItemId by accident)
            if (!this.isValidGuid(trimmed)) {
                const errorMsg = 'Document ID must be a GUID format (Dataverse primary key). Do not use SharePoint Item IDs.';
                console.error('[SpeFileViewer] Configured documentId is not a valid GUID:', trimmed);
                throw new Error(errorMsg);
            }

            console.log('[SpeFileViewer] Using configured document ID:', trimmed);
            return trimmed;
        }

        // Option 2: Use form record ID (default behavior)
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const recordId = (context.mode as any).contextInfo?.entityId;
        if (recordId && typeof recordId === 'string') {
            if (!this.isValidGuid(recordId)) {
                const errorMsg = 'Form context did not provide a valid GUID.';
                console.error('[SpeFileViewer] Form record ID is not a valid GUID:', recordId);
                throw new Error(errorMsg);
            }

            console.log('[SpeFileViewer] Using form record ID:', recordId);
            return recordId;
        }

        console.warn('[SpeFileViewer] No document ID available from input or form context');
        return '';
    }

    /**
     * Validate GUID format (prevents accidentally sending driveItemId)
     *
     * Valid GUID: ad1b0c34-52a5-f011-bbd3-7c1e5215b8b5
     * Invalid (SharePoint ItemId): 01LBYCMX76QPLGITR47BB355T4G2CVDL2B
     *
     * @param value String to validate
     * @returns true if valid GUID format, false otherwise
     */
    private isValidGuid(value: string): boolean {
        const guidRegex = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;
        return guidRegex.test(value);
    }

    /**
     * Render the React FilePreview component
     */
    private renderControl(context: ComponentFramework.Context<IInputs>): void {
        const documentId = this.extractDocumentId(context);

        // Check if we have access token
        if (!this.accessToken) {
            console.warn('[SpeFileViewer] No access token available, skipping render');
            return;
        }

        // Get effective dark mode from shared theme utilities
        // Priority: localStorage → Platform context → DOM navbar → system preference
        const isDarkTheme = getEffectiveDarkMode(context);

        console.log(`[SpeFileViewer] Rendering preview for document: ${documentId || '(none)'}`);
        console.log(`[SpeFileViewer] Dark mode: ${isDarkTheme} (preference: ${getUserThemePreference()})`);

        // Create root on first render (React 19+)
        if (!this.root) {
            this.root = createRoot(this.container);
        }

        // Render React component
        this.root.render(
            React.createElement(FilePreview, {
                documentId: documentId,
                bffApiUrl: this.bffApiUrl,
                accessToken: this.accessToken,
                correlationId: this.correlationId,
                isDarkTheme: isDarkTheme,
                onRefresh: () => {
                    console.log('[SpeFileViewer] Refresh requested from component');
                }
            })
        );
    }

    /**
     * Render error message (when initialization fails)
     */
    private renderError(errorMessage: string): void {
        this.container.innerHTML = `
            <div style="padding: 20px; border: 2px solid #d32f2f; background-color: #ffebee; color: #c62828; border-radius: 4px;">
                <strong>SPE File Viewer Error</strong>
                <p>${this.escapeHtml(errorMessage)}</p>
                <p><small>Correlation ID: ${this.correlationId}</small></p>
            </div>
        `;
    }

    /**
     * Escape HTML to prevent XSS
     */
    private escapeHtml(text: string): string {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    /**
     * No outputs for this control
     */
    public getOutputs(): IOutputs {
        return {};
    }

    /**
     * Cleanup when control is removed
     */
    public destroy(): void {
        console.log('[SpeFileViewer] Destroying control...');

        // Clean up theme listener (Dark Mode Theme Toggle feature)
        if (this._cleanupThemeListener) {
            this._cleanupThemeListener();
            this._cleanupThemeListener = null;
            console.log('[SpeFileViewer] Theme listener cleaned up');
        }

        // Abort any in-flight requests (Task 022)
        if (this._abortController) {
            this._abortController.abort();
            this._abortController = null;
            console.log('[SpeFileViewer] Aborted in-flight requests on destroy');
        }

        // Unmount React component (React 19+)
        if (this.root) {
            this.root.unmount();
            this.root = null;
        }

        // Clear tokens (optional - MSAL handles cleanup)
        this.accessToken = null;
        this.authService = null;
    }
}
