/**
 * SpeDocumentViewer PCF Control
 *
 * Entry point that wires together:
 * - MSAL authentication
 * - BFF API client
 * - React SpeDocumentViewer component
 * - Theme management
 */

import { IInputs, IOutputs } from "./generated/ManifestTypes";
import * as React from 'react';
import { createRoot, Root } from 'react-dom/client';
import { AuthService } from './AuthService';
import { DocumentViewerApp } from './SpeDocumentViewer';
import { DocumentViewerState } from './types';
import { v4 as uuidv4 } from 'uuid';

// Theme storage utilities
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

function getEffectiveDarkMode(context?: ComponentFramework.Context<IInputs>): boolean {
    const preference = getUserThemePreference();
    if (preference === 'dark') return true;
    if (preference === 'light') return false;

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    if ((context as any)?.fluentDesignLanguage?.isDarkTheme !== undefined) {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        return (context as any).fluentDesignLanguage.isDarkTheme;
    }

    const navbar = document.querySelector("[data-id='navbar-container']");
    if (navbar) {
        const bg = getComputedStyle(navbar).backgroundColor;
        if (bg === "rgb(10, 10, 10)") return true;
        if (bg === "rgb(240, 240, 240)") return false;
    }

    return window.matchMedia?.('(prefers-color-scheme: dark)').matches ?? false;
}

interface ThemeChangeHandler {
    (isDark: boolean): void;
}

function setupThemeListener(
    onChange: ThemeChangeHandler,
    context?: ComponentFramework.Context<IInputs>
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

export class SpeDocumentViewer implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    private container: HTMLDivElement;
    private root: Root | null = null;
    private authService: AuthService | null = null;
    private accessToken: string | null = null;

    // Configuration
    private bffApiUrl = '';
    private clientAppId = '';
    private bffAppId = '';
    private tenantId = '';

    // Feature flags
    private enableEdit = true;
    private enableDelete = false;
    private enableDownload = true;
    private showToolbar = false;

    // Correlation ID
    private correlationId: string;

    // State
    private _state: DocumentViewerState = DocumentViewerState.Loading;
    private _notifyOutputChanged: (() => void) | null = null;
    private _context: ComponentFramework.Context<IInputs> | null = null;
    private _errorMessage: string | null = null;
    private _previousDocumentId: string | null = null;
    private _cleanupThemeListener: (() => void) | null = null;

    constructor() {
        this.correlationId = uuidv4();
        console.log(`[SpeDocumentViewer] Control instance created. Correlation ID: ${this.correlationId}`);
    }

    /**
     * Detect if we're running in the form designer (design mode).
     * In design mode, we should show a placeholder instead of trying to authenticate.
     */
    private isDesignMode(context: ComponentFramework.Context<IInputs>): boolean {
        // Check 1: URL contains form designer indicators
        const url = window.location.href.toLowerCase();
        if (url.includes('/designer/') ||
            url.includes('formtype=main') ||
            url.includes('pagetype=entityrecord&cmdbar=false') ||
            url.includes('/edit/')) {
            // Could be form designer - check other indicators
        }

        // Check 2: No entity ID available (new record or design mode)
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const contextInfo = (context.mode as any).contextInfo;
        const entityId = contextInfo?.entityId;

        // Check 3: Check if we're in a frame with design-related parent
        try {
            if (window.parent !== window) {
                const parentUrl = window.parent.location.href.toLowerCase();
                if (parentUrl.includes('/designer/') ||
                    parentUrl.includes('/formeditor/') ||
                    parentUrl.includes('appdesigner')) {
                    console.log('[SpeDocumentViewer] Design mode detected via parent URL');
                    return true;
                }
            }
        } catch {
            // Cross-origin - can't access parent URL, continue with other checks
        }

        // Check 4: Form designer often renders controls in a preview iframe
        // where the control is disabled but we still try to render
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const isAuthoringMode = (context as any).mode?.isAuthoringMode;
        if (isAuthoringMode === true) {
            console.log('[SpeDocumentViewer] Design mode detected via isAuthoringMode');
            return true;
        }

        // Check 5: Designer preview - allocated dimensions are often 0 or very small
        const allocatedHeight = context.mode.allocatedHeight;
        const allocatedWidth = context.mode.allocatedWidth;
        if ((allocatedHeight === 0 || allocatedHeight === -1) &&
            (allocatedWidth === 0 || allocatedWidth === -1)) {
            console.log('[SpeDocumentViewer] Design mode suspected via zero dimensions');
            // Don't return true yet - could be legitimate zero dimensions
        }

        return false;
    }

    /**
     * Render a placeholder for design mode (form editor preview)
     */
    private renderDesignModePlaceholder(): void {
        const isDark = getEffectiveDarkMode(this._context ?? undefined);
        const bgColor = isDark ? '#1f1f1f' : '#f5f5f5';
        const textColor = isDark ? '#ffffff' : '#333333';
        const borderColor = isDark ? '#444444' : '#cccccc';

        this.container.innerHTML = `
            <div style="
                display: flex;
                flex-direction: column;
                align-items: center;
                justify-content: center;
                height: 100%;
                min-height: 200px;
                background-color: ${bgColor};
                border: 2px dashed ${borderColor};
                border-radius: 8px;
                padding: 20px;
                text-align: center;
            ">
                <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="${textColor}" stroke-width="1.5">
                    <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
                    <polyline points="14,2 14,8 20,8"/>
                    <line x1="16" y1="13" x2="8" y2="13"/>
                    <line x1="16" y1="17" x2="8" y2="17"/>
                    <polyline points="10,9 9,9 8,9"/>
                </svg>
                <div style="margin-top: 16px; color: ${textColor}; font-weight: 600; font-size: 14px;">
                    SPE Document Viewer
                </div>
                <div style="margin-top: 8px; color: ${textColor}; opacity: 0.7; font-size: 12px;">
                    Document preview will appear at runtime
                </div>
                <div style="margin-top: 4px; color: ${textColor}; opacity: 0.5; font-size: 11px;">
                    v1.0.14
                </div>
            </div>
        `;
    }

    public async init(
        context: ComponentFramework.Context<IInputs>,
        notifyOutputChanged: () => void,
        state: ComponentFramework.Dictionary,
        container: HTMLDivElement
    ): Promise<void> {
        this.container = container;
        this._notifyOutputChanged = notifyOutputChanged;
        this._context = context;

        // Immediately show loading state
        this.transitionTo(DocumentViewerState.Loading);
        this.renderLoading();

        // Apply height styling
        // Use explicit height value instead of relying on parent height (Dataverse forms often don't set explicit height)
        const controlHeight = context.parameters.controlHeight?.raw ?? 600;
        this.container.style.height = `${controlHeight}px`;
        this.container.style.minHeight = `${controlHeight}px`;
        this.container.style.maxHeight = `${controlHeight}px`;
        this.container.style.display = 'flex';
        this.container.style.flexDirection = 'column';
        this.container.style.overflow = 'hidden';
        console.log(`[SpeDocumentViewer] Control height: ${controlHeight}px`);

        console.log('[SpeDocumentViewer] Initializing control...');

        // Check for design mode FIRST - before any authentication
        if (this.isDesignMode(context)) {
            console.log('[SpeDocumentViewer] Running in design mode - showing placeholder');
            this.renderDesignModePlaceholder();
            return;
        }

        try {
            // Extract configuration
            this.tenantId = context.parameters.tenantId.raw || '';
            this.clientAppId = context.parameters.clientAppId.raw || '';
            this.bffAppId = context.parameters.bffAppId.raw || '';
            this.bffApiUrl = context.parameters.bffApiUrl.raw || 'https://spe-api-dev-67e2xz.azurewebsites.net';

            // Feature flags
            this.enableEdit = context.parameters.enableEdit?.raw ?? true;
            this.enableDelete = context.parameters.enableDelete?.raw ?? false;
            this.enableDownload = context.parameters.enableDownload?.raw ?? true;
            this.showToolbar = context.parameters.showToolbar?.raw ?? false;

            // Validate configuration
            if (!this.tenantId || !this.clientAppId || !this.bffAppId) {
                throw new Error('Missing required configuration: tenantId, clientAppId, and bffAppId must be provided');
            }

            console.log('[SpeDocumentViewer] Configuration:', {
                tenantId: this.tenantId,
                clientAppId: this.clientAppId,
                bffAppId: this.bffAppId,
                bffApiUrl: this.bffApiUrl,
                enableEdit: this.enableEdit,
                enableDelete: this.enableDelete,
                enableDownload: this.enableDownload,
                showToolbar: this.showToolbar
            });

            // Initialize MSAL
            this.authService = new AuthService(this.tenantId, this.clientAppId, this.bffAppId);
            await this.authService.initialize();
            console.log(`[SpeDocumentViewer] MSAL initialized. Scope: ${this.authService.getScope()}`);

            // Acquire access token
            this.accessToken = await this.authService.getAccessToken();
            console.log('[SpeDocumentViewer] Access token acquired');

            // Track initial document ID
            try {
                this._previousDocumentId = this.extractDocumentId(context);
            } catch {
                this._previousDocumentId = null;
            }

            // Set up theme listener
            this._cleanupThemeListener = setupThemeListener(
                (isDark) => {
                    console.log(`[SpeDocumentViewer] Theme changed: isDark=${isDark}`);
                    if (this._context && this._state === DocumentViewerState.Ready) {
                        this.renderControl(this._context);
                    }
                },
                context
            );

            // Transition to Ready
            this.transitionTo(DocumentViewerState.Ready);
            this.renderControl(context);

        } catch (error) {
            console.error('[SpeDocumentViewer] Initialization failed:', error);
            const errorMessage = error instanceof Error ? error.message : String(error);

            // Check if this is a popup-blocked error (likely in form designer)
            if (errorMessage.toLowerCase().includes('popup') ||
                errorMessage.toLowerCase().includes('blocked') ||
                errorMessage.toLowerCase().includes('interaction_required')) {
                console.log('[SpeDocumentViewer] Popup blocked - likely in design mode, showing placeholder');
                this.renderDesignModePlaceholder();
                return;
            }

            this._errorMessage = errorMessage;
            this.transitionTo(DocumentViewerState.Error);
            this.renderError(this._errorMessage);
        }
    }

    public updateView(context: ComponentFramework.Context<IInputs>): void {
        this._context = context;

        if (this._state !== DocumentViewerState.Ready) {
            return;
        }

        // Update feature flags
        this.enableEdit = context.parameters.enableEdit?.raw ?? true;
        this.enableDelete = context.parameters.enableDelete?.raw ?? false;
        this.enableDownload = context.parameters.enableDownload?.raw ?? true;
        this.showToolbar = context.parameters.showToolbar?.raw ?? false;

        // Check if document ID changed
        let currentDocumentId: string | null = null;
        try {
            currentDocumentId = this.extractDocumentId(context);
        } catch {
            currentDocumentId = null;
        }

        if (currentDocumentId !== this._previousDocumentId) {
            console.log(`[SpeDocumentViewer] Document ID changed: ${this._previousDocumentId} -> ${currentDocumentId}`);
            this._previousDocumentId = currentDocumentId;
        }

        this.renderControl(context);
    }

    private transitionTo(newState: DocumentViewerState): void {
        const previousState = this._state;
        this._state = newState;
        console.log(`[SpeDocumentViewer] State: ${previousState} -> ${newState}`);
        this._notifyOutputChanged?.();
    }

    private extractDocumentId(context: ComponentFramework.Context<IInputs>): string {
        const rawValue = context.parameters.documentId.raw;

        if (rawValue && typeof rawValue === 'string' && rawValue.trim() !== '') {
            const trimmed = rawValue.trim();
            if (!this.isValidGuid(trimmed)) {
                throw new Error('Document ID must be a GUID format.');
            }
            return trimmed;
        }

        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const recordId = (context.mode as any).contextInfo?.entityId;
        if (recordId && typeof recordId === 'string') {
            if (!this.isValidGuid(recordId)) {
                throw new Error('Form context did not provide a valid GUID.');
            }
            return recordId;
        }

        return '';
    }

    private isValidGuid(value: string): boolean {
        const guidRegex = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;
        return guidRegex.test(value);
    }

    private renderLoading(): void {
        const overlay = document.createElement('div');
        overlay.className = 'spe-document-viewer-loading-overlay';
        overlay.setAttribute('role', 'status');
        overlay.setAttribute('aria-busy', 'true');

        const spinner = document.createElement('div');
        spinner.className = 'spe-document-viewer-loading-spinner';

        const text = document.createElement('span');
        text.className = 'spe-document-viewer-loading-text';
        text.textContent = 'Loading document viewer...';

        overlay.appendChild(spinner);
        overlay.appendChild(text);

        this.container.innerHTML = '';
        this.container.appendChild(overlay);
    }

    private renderControl(context: ComponentFramework.Context<IInputs>): void {
        const documentId = this.extractDocumentId(context);

        if (!this.accessToken) {
            console.warn('[SpeDocumentViewer] No access token available');
            return;
        }

        const isDarkTheme = getEffectiveDarkMode(context);

        console.log(`[SpeDocumentViewer] Rendering for document: ${documentId || '(none)'}`);

        if (!this.root) {
            this.root = createRoot(this.container);
        }

        this.root.render(
            React.createElement(DocumentViewerApp, {
                documentId: documentId,
                bffApiUrl: this.bffApiUrl,
                accessToken: this.accessToken,
                correlationId: this.correlationId,
                isDarkTheme: isDarkTheme,
                enableEdit: this.enableEdit,
                enableDelete: this.enableDelete,
                enableDownload: this.enableDownload,
                showToolbar: this.showToolbar,
                onRefresh: () => {
                    console.log('[SpeDocumentViewer] Refresh requested');
                },
                onDeleted: () => {
                    console.log('[SpeDocumentViewer] Document deleted');
                }
            })
        );
    }

    private renderError(errorMessage: string): void {
        this.container.innerHTML = `
            <div style="padding: 20px; border: 2px solid #d32f2f; background-color: #ffebee; color: #c62828; border-radius: 4px;">
                <strong>SpeDocumentViewer Error</strong>
                <p>${this.escapeHtml(errorMessage)}</p>
                <p><small>Correlation ID: ${this.correlationId}</small></p>
            </div>
        `;
    }

    private escapeHtml(text: string): string {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    public getOutputs(): IOutputs {
        return {};
    }

    public destroy(): void {
        console.log('[SpeDocumentViewer] Destroying control...');

        if (this._cleanupThemeListener) {
            this._cleanupThemeListener();
            this._cleanupThemeListener = null;
        }

        if (this.root) {
            this.root.unmount();
            this.root = null;
        }

        this.accessToken = null;
        this.authService = null;
    }
}
