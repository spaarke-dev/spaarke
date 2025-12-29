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

        // Apply responsive height styling (matches SpeFileViewer pattern)
        // minHeight ensures minimum space, height: 100% allows expansion to fill container
        const controlHeight = context.parameters.controlHeight?.raw ?? 600;
        this.container.style.minHeight = `${controlHeight}px`;
        this.container.style.height = '100%';
        this.container.style.display = 'flex';
        this.container.style.flexDirection = 'column';
        console.log(`[SpeDocumentViewer] Control height: ${controlHeight}px (min) with 100% responsive expansion`);

        console.log('[SpeDocumentViewer] Initializing control...');

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
                enableDownload: this.enableDownload
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
            this._errorMessage = error instanceof Error ? error.message : String(error);
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
