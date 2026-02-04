/**
 * Universal Dataset Grid PCF Control
 * Version 2.0.7 - Single React Root Architecture with Fluent UI v9
 */

import * as React from 'react';
import * as ReactDOM from 'react-dom/client';
import { FluentProvider } from '@fluentui/react-components';
import { IInputs, IOutputs } from "./generated/ManifestTypes";
import { UniversalDatasetGridRoot } from "./components/UniversalDatasetGridRoot";
import { ErrorBoundary } from "./components/ErrorBoundary";
import { resolveTheme, setupThemeListener } from "./providers/ThemeProvider";
import { DEFAULT_GRID_CONFIG, GridConfiguration, CalendarFilter, parseCalendarFilter } from "./types";
import { logger } from "./utils/logger";
import { MsalAuthProvider } from "./services/auth/MsalAuthProvider";

export class UniversalDatasetGrid implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    private root: ReactDOM.Root | null = null;
    private notifyOutputChanged: () => void;
    private config: GridConfiguration;
    private authProvider: MsalAuthProvider;
    private _cleanupThemeListener: (() => void) | null = null;
    private _context: ComponentFramework.Context<IInputs> | null = null;
    private _calendarFilter: CalendarFilter | null = null;
    /** Selected event date from row click (Task 012 - bi-directional sync) */
    private _selectedEventDate: string | null = null;

    constructor() {
        logger.info('Control', 'Constructor called');
        this.config = DEFAULT_GRID_CONFIG;
        // Bind the row click handler to preserve 'this' context
        this.handleRowClick = this.handleRowClick.bind(this);
    }

    /**
     * Handle row click for bi-directional calendar sync (Task 012).
     * Stores the due date and triggers output update.
     * @param date - ISO date string (YYYY-MM-DD) or null if no date
     */
    private handleRowClick(date: string | null): void {
        logger.info('Control', `Row clicked - date: ${date}`);
        this._selectedEventDate = date;
        this.notifyOutputChanged();
    }

    public init(
        context: ComponentFramework.Context<IInputs>,
        notifyOutputChanged: () => void,
        state: ComponentFramework.Dictionary,
        container: HTMLDivElement
    ): void {
        try {
            logger.info('Control', 'Init - Creating single React root');

            this.notifyOutputChanged = notifyOutputChanged;
            this._context = context;

            // Initialize MSAL authentication (Phase 1)
            // This will be async in the background; token acquisition happens in Phase 2
            this.initializeMsalAsync(container);

            // Create single React root
            this.root = ReactDOM.createRoot(container);

            // Set up theme listener for dynamic theme changes
            this._cleanupThemeListener = setupThemeListener(
                (isDark) => {
                    logger.info('Control', `Theme changed: isDark=${isDark}`);
                    if (this._context && this.root) {
                        this.renderReactTree(this._context);
                    }
                },
                context
            );

            // Render React tree
            this.renderReactTree(context);

            logger.info('Control', 'Init complete');
        } catch (error) {
            logger.error('Control', 'Init failed', error);
            throw error;
        }
    }

    public updateView(context: ComponentFramework.Context<IInputs>): void {
        try {
            // Store latest context for theme listener callback
            this._context = context;

            const dataset = context.parameters.dataset;
            const recordCount = Object.keys(dataset.records || {}).length;
            const isLoading = dataset.loading;

            // Parse calendar filter from property (Task 010)
            const calendarFilterJson = context.parameters.calendarFilter?.raw;
            this._calendarFilter = parseCalendarFilter(calendarFilterJson);

            console.log('[UniversalDatasetGrid] UpdateView called', {
                recordCount,
                isLoading,
                hasDataset: !!dataset,
                selectedRecordIds: dataset.getSelectedRecordIds(),
                calendarFilter: this._calendarFilter
            });

            logger.debug('Control', 'UpdateView - Re-rendering with new props');

            // Just re-render with new context - React handles the updates
            this.renderReactTree(context);
        } catch (error) {
            logger.error('Control', 'UpdateView failed', error);
        }
    }

    public destroy(): void {
        try {
            logger.info('Control', 'Destroy - Unmounting React root');

            // Clean up theme listener
            if (this._cleanupThemeListener) {
                this._cleanupThemeListener();
                this._cleanupThemeListener = null;
            }

            // Clear MSAL token cache (optional - sessionStorage will be cleared on tab close)
            if (this.authProvider) {
                logger.info('Control', 'Destroy - Clearing MSAL token cache');
                this.authProvider.clearCache();
            }

            if (this.root) {
                this.root.unmount();
                this.root = null;
            }

            this._context = null;
        } catch (error) {
            logger.error('Control', 'Destroy failed', error);
        }
    }

    public getOutputs(): IOutputs {
        return {
            // Task 012: Emit selected event date for calendar highlighting
            selectedEventDate: this._selectedEventDate ?? undefined
        };
    }

    /**
     * Render the React component tree.
     * Called from init() and updateView().
     */
    private renderReactTree(context: ComponentFramework.Context<IInputs>): void {
        if (!this.root) {
            logger.error('Control', 'Cannot render - root not initialized');
            return;
        }

        try {
            const theme = resolveTheme(context);

            this.root.render(
                React.createElement(
                    FluentProvider,
                    { theme },
                    React.createElement(
                        ErrorBoundary,
                        null,
                        React.createElement(UniversalDatasetGridRoot, {
                            context,
                            notifyOutputChanged: this.notifyOutputChanged,
                            config: this.config,
                            calendarFilter: this._calendarFilter,
                            // Task 012: Pass row click handler for bi-directional calendar sync
                            onRowClick: this.handleRowClick
                        })
                    )
                )
            );
        } catch (error) {
            logger.error('Control', 'Render failed', error);
            throw error;
        }
    }

    /**
     * Initialize MSAL authentication provider (Phase 1)
     *
     * Runs asynchronously in background. If initialization fails, displays error to user.
     * Token acquisition will be implemented in Phase 2.
     *
     * @param container - PCF container element for error display
     */
    private initializeMsalAsync(container: HTMLDivElement): void {
        (async () => {
            try {
                logger.info('Control', 'Initializing MSAL authentication...');

                // Get singleton instance of MsalAuthProvider
                this.authProvider = MsalAuthProvider.getInstance();

                // Initialize MSAL (validates config, creates PublicClientApplication)
                await this.authProvider.initialize();

                logger.info('Control', 'MSAL authentication initialized successfully ✅');

                // Check if user is authenticated (for logging only - Phase 1)
                const isAuth = this.authProvider.isAuthenticated();
                logger.info('Control', `User authenticated: ${isAuth}`);

                if (isAuth) {
                    const accountInfo = this.authProvider.getAccountDebugInfo();
                    logger.info('Control', 'Account info:', accountInfo);
                }

            } catch (error) {
                logger.error('Control', 'Failed to initialize MSAL:', error);

                // Show user-friendly error message
                this.showError(
                    container,
                    'Authentication initialization failed. Please refresh the page and try again. ' +
                    'If the problem persists, contact your administrator.'
                );
            }
        })();
    }

    /**
     * Display error message in control
     *
     * Shows user-friendly error when initialization or operations fail.
     * Used for MSAL errors, API errors, etc.
     *
     * @param container - PCF container element
     * @param message - Error message to display (user-friendly, no technical details)
     */
    private showError(container: HTMLDivElement, message: string): void {
        // Create error div
        const errorDiv = document.createElement('div');
        errorDiv.style.padding = '20px';
        errorDiv.style.color = '#a4262c'; // Office UI Fabric error red
        errorDiv.style.backgroundColor = '#fde7e9'; // Light red background
        errorDiv.style.border = '1px solid #a4262c';
        errorDiv.style.borderRadius = '4px';
        errorDiv.style.fontFamily = "'Segoe UI', Tahoma, Geneva, Verdana, sans-serif";
        errorDiv.style.fontSize = '14px';
        errorDiv.style.margin = '10px';

        // Add error icon + message
        errorDiv.innerHTML = `
            <div style="display: flex; align-items: center; gap: 10px;">
                <svg width="20" height="20" viewBox="0 0 20 20" fill="none" xmlns="http://www.w3.org/2000/svg">
                    <circle cx="10" cy="10" r="9" fill="#a4262c"/>
                    <path d="M10 6v4M10 14h.01" stroke="#fff" stroke-width="2" stroke-linecap="round"/>
                </svg>
                <div>
                    <strong>Error</strong><br/>
                    ${message}
                </div>
            </div>
        `;

        // Prepend to container (show at top)
        container.insertBefore(errorDiv, container.firstChild);
    }
}
