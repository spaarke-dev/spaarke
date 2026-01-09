/**
 * Email Processing Monitor PCF Control
 *
 * Admin monitoring control for email-to-document processing statistics.
 * Displays metrics from BFF API including:
 * - Conversion rates (success/failure)
 * - Webhook statistics
 * - Polling statistics
 * - Filter rule hits
 * - Job processing metrics
 *
 * @version 1.0.0
 */

import { IInputs, IOutputs } from "./generated/ManifestTypes";
import * as React from 'react';
import * as ReactDOM from 'react-dom';
import { AuthService } from './AuthService';
import { EmailProcessingDashboard } from './EmailProcessingDashboard';
import { MonitorState } from './types';

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

export class EmailProcessingMonitor implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    // PCF container element
    private container: HTMLDivElement;

    // Track if React has been mounted (for React 16 API)
    private isReactMounted = false;

    // MSAL authentication service
    private authService: AuthService | null = null;

    // Current access token (cached)
    private accessToken: string | null = null;

    // Configuration from manifest properties
    private bffApiUrl = '';
    private clientAppId = '';
    private bffAppId = '';
    private tenantId = '';
    private refreshIntervalSeconds = 30;

    // State machine for component lifecycle
    private _state: MonitorState = MonitorState.Loading;

    // PCF notification callback
    private _notifyOutputChanged: (() => void) | null = null;

    // Current context reference
    private _context: ComponentFramework.Context<IInputs> | null = null;

    // Error message when in Error state
    private _errorMessage: string | null = null;

    // Theme listener cleanup function
    private _cleanupThemeListener: (() => void) | null = null;

    // Control version for footer display
    private readonly VERSION = '1.0.0';

    constructor() {
        console.log(`[EmailProcessingMonitor] Control instance created. Version: ${this.VERSION}`);
    }

    /**
     * Initialize the control
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
        this.transitionTo(MonitorState.Loading);
        this.renderBasedOnState();

        // Apply responsive height styling
        const controlHeight = context.parameters.controlHeight?.raw ?? 400;
        this.container.style.minHeight = `${controlHeight}px`;
        this.container.style.height = '100%';
        this.container.style.display = 'flex';
        this.container.style.flexDirection = 'column';

        console.log('[EmailProcessingMonitor] Initializing control...');

        try {
            // Extract configuration from manifest properties
            this.tenantId = context.parameters.tenantId.raw || '';
            this.clientAppId = context.parameters.clientAppId.raw || '';
            this.bffAppId = context.parameters.bffAppId.raw || '';
            this.bffApiUrl = context.parameters.bffApiUrl.raw || 'https://spe-api-dev-67e2xz.azurewebsites.net';
            this.refreshIntervalSeconds = context.parameters.refreshIntervalSeconds?.raw ?? 30;

            // Validate configuration
            if (!this.tenantId || !this.clientAppId || !this.bffAppId) {
                throw new Error('Missing required configuration: tenantId, clientAppId, and bffAppId must be provided');
            }

            console.log(`[EmailProcessingMonitor] Configuration:`, {
                tenantId: this.tenantId,
                clientAppId: this.clientAppId,
                bffAppId: this.bffAppId,
                bffApiUrl: this.bffApiUrl,
                refreshIntervalSeconds: this.refreshIntervalSeconds
            });

            // Initialize MSAL auth service
            this.authService = new AuthService(this.tenantId, this.clientAppId, this.bffAppId);
            await this.authService.initialize();

            console.log(`[EmailProcessingMonitor] MSAL initialized. Scope: ${this.authService.getScope()}`);

            // Acquire access token
            this.accessToken = await this.authService.getAccessToken();
            console.log('[EmailProcessingMonitor] Access token acquired');

            // Set up theme listener for global theme changes
            this._cleanupThemeListener = setupThemeListener(
                (isDark) => {
                    console.log(`[EmailProcessingMonitor] Theme changed: isDark=${isDark}`);
                    if (this._context && this._state === MonitorState.Ready) {
                        this.renderControl(this._context);
                    }
                },
                context
            );
            console.log('[EmailProcessingMonitor] Theme listener initialized');

            // Transition to Ready and render React component
            this.transitionTo(MonitorState.Ready);
            this.renderBasedOnState();

        } catch (error) {
            console.error('[EmailProcessingMonitor] Initialization failed:', error);
            this._errorMessage = error instanceof Error ? error.message : String(error);
            this.transitionTo(MonitorState.Error);
            this.renderBasedOnState();
        }
    }

    /**
     * Update view when context changes
     */
    public updateView(context: ComponentFramework.Context<IInputs>): void {
        this._context = context;

        // Only process if in Ready state
        if (this._state !== MonitorState.Ready) {
            return;
        }

        // Re-render React component
        this.renderControl(context);
    }

    /**
     * Transition to a new state with logging
     */
    private transitionTo(newState: MonitorState): void {
        const previousState = this._state;
        this._state = newState;

        console.log(`[EmailProcessingMonitor] State: ${previousState} → ${newState}`);

        // Notify PCF framework of state change
        this._notifyOutputChanged?.();
    }

    /**
     * Render UI based on current state
     */
    private renderBasedOnState(): void {
        switch (this._state) {
            case MonitorState.Loading:
                this.renderLoading();
                break;
            case MonitorState.Ready:
                if (this._context) {
                    this.renderControl(this._context);
                }
                break;
            case MonitorState.Error:
                this.renderError(this._errorMessage || 'An unknown error occurred');
                break;
        }
    }

    /**
     * Render loading overlay with spinner
     */
    private renderLoading(): void {
        const overlay = document.createElement('div');
        overlay.className = 'email-monitor-loading-overlay';
        overlay.setAttribute('role', 'status');
        overlay.setAttribute('aria-busy', 'true');
        overlay.setAttribute('aria-label', 'Loading email processing statistics');

        const spinner = document.createElement('div');
        spinner.className = 'email-monitor-loading-spinner';

        const text = document.createElement('span');
        text.className = 'email-monitor-loading-text';
        text.textContent = 'Loading statistics...';

        overlay.appendChild(spinner);
        overlay.appendChild(text);

        this.container.innerHTML = '';
        this.container.appendChild(overlay);
    }

    /**
     * Render the React EmailProcessingDashboard component
     */
    private renderControl(context: ComponentFramework.Context<IInputs>): void {
        if (!this.accessToken) {
            console.warn('[EmailProcessingMonitor] No access token available, skipping render');
            return;
        }

        // Get effective dark mode from shared theme utilities
        const isDarkTheme = getEffectiveDarkMode(context);

        console.log(`[EmailProcessingMonitor] Rendering dashboard. Dark mode: ${isDarkTheme}`);

        // React 16 API: Use ReactDOM.render instead of createRoot
        ReactDOM.render(
            React.createElement(EmailProcessingDashboard, {
                bffApiUrl: this.bffApiUrl,
                accessToken: this.accessToken,
                isDarkTheme: isDarkTheme,
                refreshIntervalSeconds: this.refreshIntervalSeconds,
                version: this.VERSION,
                onError: (error: string) => {
                    console.error('[EmailProcessingMonitor] Dashboard error:', error);
                }
            }),
            this.container
        );
        this.isReactMounted = true;
    }

    /**
     * Render error message
     */
    private renderError(errorMessage: string): void {
        this.container.innerHTML = `
            <div style="padding: 20px; border: 2px solid #d32f2f; background-color: #ffebee; color: #c62828; border-radius: 4px;">
                <strong>Email Processing Monitor Error</strong>
                <p>${this.escapeHtml(errorMessage)}</p>
                <p><small>Version: ${this.VERSION}</small></p>
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
        console.log('[EmailProcessingMonitor] Destroying control...');

        // Clean up theme listener
        if (this._cleanupThemeListener) {
            this._cleanupThemeListener();
            this._cleanupThemeListener = null;
        }

        // React 16 API: Use unmountComponentAtNode instead of root.unmount()
        if (this.isReactMounted && this.container) {
            ReactDOM.unmountComponentAtNode(this.container);
            this.isReactMounted = false;
        }

        // Clear tokens
        this.accessToken = null;
        this.authService = null;
    }
}
