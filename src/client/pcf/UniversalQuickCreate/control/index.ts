/**
 * Universal Document Upload PCF Control
 *
 * Custom Page-based control for uploading multiple documents to SharePoint Embedded
 * and creating Dataverse records.
 *
 * Architecture:
 * - Receives parent context via Custom Page parameters
 * - Renders Fluent UI v9 form for file selection and metadata
 * - Uploads files to SPE via MultiFileUploadService
 * - Creates Document records via DocumentRecordService using context.webAPI
 * - Queries metadata dynamically for case-sensitive navigation properties
 *
 * ADR Compliance:
 * - ADR-001: Fluent UI v9 Components
 * - ADR-002: TypeScript Strict Mode
 * - ADR-003: Separation of Concerns
 * - ADR-010: Configuration Over Code
 *
 * @version 3.0.3 (Custom Page Dialog Support + Phase 7 Dynamic Metadata + param binding fix)
 */

// Declare global Xrm (used for navigation/dialog management only)
declare const Xrm: any;

import { IInputs, IOutputs } from "./generated/ManifestTypes";
import * as React from "react";
import * as ReactDOM from "react-dom/client";
import { logInfo, logError, logWarn } from "./utils/logger";
import { MsalAuthProvider } from "./services/auth/MsalAuthProvider";
import { MultiFileUploadService } from "./services/MultiFileUploadService";
import { DocumentRecordService } from "./services/DocumentRecordService";
import { FileUploadService } from "./services/FileUploadService";
import { SdapApiClientFactory } from "./services/SdapApiClientFactory";
import { NavMapClient } from "./services/NavMapClient"; // Phase 7
import { getEntityDocumentConfig, isEntitySupported } from "./config/EntityDocumentConfig";
import { ParentContext } from "./types";
import { DocumentUploadForm } from "./components/DocumentUploadForm";

/**
 * PCF Control for Custom Page Document Upload
 */
export class UniversalDocumentUpload implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    private container: HTMLDivElement;
    private reactRoot: ReactDOM.Root | null = null;
    private notifyOutputChanged: () => void;
    private context: ComponentFramework.Context<IInputs>;

    // Hosting context detection (Custom Page vs Quick Create Form)
    private isCustomPageMode: boolean = false;

    // Services
    private authProvider: MsalAuthProvider;
    private multiFileService: MultiFileUploadService | null = null;
    private documentRecordService: DocumentRecordService | null = null;

    // Parent Context (from Custom Page parameters)
    private parentContext: ParentContext | null = null;

    // Initialization state (Phase 4 fix - idempotent updateView)
    private _initialized = false;

    // UI State
    private selectedFiles: File[] = [];
    private isUploading = false;

    // Output property for signaling dialog close
    private shouldClose: boolean = false;

    constructor() {
        logInfo('UniversalDocumentUpload', 'Constructor called');
    }

    /**
     * Detect hosting context - Custom Page vs Quick Create Form
     * @returns true if running in Custom Page mode, false otherwise
     */
    private detectHostingContext(context: ComponentFramework.Context<IInputs>): boolean {
        // Check if context.page exists and has type='custom'
        // Custom Pages have context.page.type === 'custom'
        // Quick Create forms don't have context.page or have different type
        const contextAny = context as any;
        if (contextAny.page && contextAny.page.type === 'custom') {
            logInfo('UniversalDocumentUpload', 'Detected Custom Page context', {
                pageType: contextAny.page.type,
                hasNavigationClose: !!(contextAny.navigation && contextAny.navigation.close)
            });
            return true;
        }

        logInfo('UniversalDocumentUpload', 'Detected Quick Create Form context');
        return false;
    }

    /**
     * Initialize PCF Control
     */
    public init(
        context: ComponentFramework.Context<IInputs>,
        notifyOutputChanged: () => void,
        state: ComponentFramework.Dictionary,
        container: HTMLDivElement
    ): void {
    logInfo('UniversalDocumentUpload', 'Initializing PCF control v3.0.6 (File URL field)');

        this.context = context;
        this.notifyOutputChanged = notifyOutputChanged;

    // Always use Custom Page mode (v3.0.4 - Quick Create Form deprecated)
        this.isCustomPageMode = true;

        // Create container
        this.container = document.createElement("div");
        this.container.className = "universal-document-upload-container";
        container.appendChild(this.container);

        // Version badge for debugging
        const versionBadge = document.createElement("div");
    versionBadge.textContent = "✓ V3.0.6 - FILE URL - " + new Date().toLocaleTimeString();
        versionBadge.style.cssText = "padding: 12px; background: #107c10; color: white; font-size: 14px; font-weight: bold; border-radius: 4px; margin-bottom: 8px; text-align: center;";
        this.container.appendChild(versionBadge);

        // Create React root
        this.reactRoot = ReactDOM.createRoot(this.container);

        // NOTE: Do NOT call initializeAsync here - updateView() will call it when params are ready
        // This allows Custom Page parameters to hydrate before initialization
    }

    /**
     * Async initialization
     */
    private async initializeAsync(context: ComponentFramework.Context<IInputs>): Promise<void> {
        try {
            // Step 1: Load and validate parent context
            this.loadParentContext(context);

            // Step 2: Validate parent entity is supported
            this.validateParentEntity();

            // Step 3: Initialize MSAL authentication
            await this.initializeMsalAsync();

            // Step 4: Initialize services
            this.initializeServices(context);

            // Step 5: Render React UI
            this.renderReactComponent();

            logInfo('UniversalDocumentUpload', 'Initialization complete', {
                parentEntityName: this.parentContext?.parentEntityName,
                parentRecordId: this.parentContext?.parentRecordId,
                containerId: this.parentContext?.containerId
            });

        } catch (error) {
            logError('UniversalDocumentUpload', 'Initialization failed', error);
            this.showError((error as Error).message);
        }
    }

    /**
     * Load parent context from bound form fields or Custom Page parameters
     */
    private loadParentContext(context: ComponentFramework.Context<IInputs>): void {
        logInfo('UniversalDocumentUpload', 'Loading parent context from parameters');

        // Read parameters (might be bound to form fields or passed via Custom Page)
        const parentEntityName = context.parameters.parentEntityName?.raw;
        const parentRecordId = context.parameters.parentRecordId?.raw;
        const containerId = context.parameters.containerId?.raw;
        const parentDisplayName = context.parameters.parentDisplayName?.raw;

        logInfo('UniversalDocumentUpload', 'Raw parameter values', {
            parentEntityName,
            parentRecordId,
            containerId,
            parentDisplayName
        });

        // If values are missing, show a user-friendly message instead of throwing
        // This allows the form to load even if fields are empty initially
        if (!parentEntityName || !parentRecordId || !containerId) {
            logWarn('UniversalDocumentUpload', 'Parameters not yet populated - waiting for user input');

            // Show instructions to user
            this.showInfo(
                'Please fill in the required fields: Parent Entity Name, Parent Record ID, and Container ID. ' +
                'Then save the form to activate the upload control.'
            );

            return; // Exit gracefully - don't throw error
        }

        // Validate GUID format for parentRecordId
        const guidRegex = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
        if (!guidRegex.test(parentRecordId)) {
            throw new Error(
                `Invalid parentRecordId format: "${parentRecordId}". ` +
                'Expected GUID format without curly braces (e.g., "abc12345-def6-7890-ghij-klmnopqrstuv")'
            );
        }

        // Create parent context
        this.parentContext = {
            parentEntityName,
            parentRecordId,
            containerId,
            parentDisplayName: parentDisplayName || parentEntityName
        };

        logInfo('UniversalDocumentUpload', 'Parent context loaded', this.parentContext);
    }

    /**
     * Validate that parent entity is supported
     */
    private validateParentEntity(): void {
        if (!this.parentContext) {
            throw new Error('Parent context not loaded');
        }

        const entityName = this.parentContext.parentEntityName;

        if (!isEntitySupported(entityName)) {
            const supportedEntities = ['sprk_matter', 'sprk_project', 'sprk_invoice', 'account', 'contact'];
            throw new Error(
                `Unsupported parent entity: "${entityName}". ` +
                `Supported entities: ${supportedEntities.join(', ')}`
            );
        }

        const config = getEntityDocumentConfig(entityName);
        logInfo('UniversalDocumentUpload', 'Entity configuration found', config);
    }

    /**
     * Initialize MSAL authentication
     */
    private async initializeMsalAsync(): Promise<void> {
        try {
            logInfo('UniversalDocumentUpload', 'Initializing MSAL authentication...');

            this.authProvider = MsalAuthProvider.getInstance();
            await this.authProvider.initialize();

            logInfo('UniversalDocumentUpload', 'MSAL authentication initialized ✅');

            if (this.authProvider.isAuthenticated()) {
                const accountInfo = this.authProvider.getAccountDebugInfo();
                logInfo('UniversalDocumentUpload', 'User authenticated', accountInfo);
            }

        } catch (error) {
            logError('UniversalDocumentUpload', 'MSAL initialization failed', error);
            throw new Error('Authentication initialization failed. Please refresh and try again.');
        }
    }

    /**
     * Initialize services (Phase 7: includes NavMapClient)
     */
    private initializeServices(context: ComponentFramework.Context<IInputs>): void {
        // Get API base URL
        const rawApiUrl = context.parameters.sdapApiBaseUrl?.raw || 'spe-api-dev-67e2xz.azurewebsites.net/api';
        const apiBaseUrl = rawApiUrl.startsWith('http://') || rawApiUrl.startsWith('https://')
            ? rawApiUrl
            : `https://${rawApiUrl}`;

        // NavMapClient needs base URL without /api suffix (it adds /api/navmap internally)
        const navMapBaseUrl = apiBaseUrl.endsWith('/api')
            ? apiBaseUrl.substring(0, apiBaseUrl.length - 4)  // Remove trailing /api
            : apiBaseUrl;

        logInfo('UniversalDocumentUpload', 'Initializing services (Phase 7)', { apiBaseUrl, navMapBaseUrl });

        // Create API clients
        const apiClient = SdapApiClientFactory.create(apiBaseUrl);
        const navMapClient = new NavMapClient(
            navMapBaseUrl,
            async () => {
                // Reuse same MSAL auth as file operations
                // Use same OAuth scope as msalConfig.ts (full application ID URI)
                const token = await this.authProvider.getToken(['api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation']);
                return token;
            }
        );

        // Create services
        const fileUploadService = new FileUploadService(apiClient);
        this.multiFileService = new MultiFileUploadService(fileUploadService);
        this.documentRecordService = new DocumentRecordService(context, navMapClient); // Pass navMapClient for metadata queries

        logInfo('UniversalDocumentUpload', 'Services initialized (including NavMapClient)');
    }

    /**
     * Render React component
     */
    private renderReactComponent(): void {
        logInfo('UniversalDocumentUpload', 'Rendering React component');

        if (!this.reactRoot || !this.parentContext || !this.multiFileService || !this.documentRecordService) {
            logError('UniversalDocumentUpload', 'Cannot render: missing dependencies', {
                hasReactRoot: !!this.reactRoot,
                hasParentContext: !!this.parentContext,
                hasMultiFileService: !!this.multiFileService,
                hasDocumentRecordService: !!this.documentRecordService
            });
            return;
        }

        // Render DocumentUploadForm
        this.reactRoot.render(
            React.createElement(DocumentUploadForm, {
                parentContext: this.parentContext,
                multiFileService: this.multiFileService,
                documentRecordService: this.documentRecordService,
                onClose: this.closeDialog.bind(this) // Use closeDialog for both Custom Page and Quick Create Form
            })
        );
    }

    /**
     * Close dialog - supports both Custom Page and Quick Create Form modes
     *
     * Called after successful upload completion:
     * - Custom Page: Sets shouldClose output property to signal Custom Page Timer to call Exit()
     * - Quick Create Form: Does nothing (form handles close on save)
     */
    private closeDialog(): void {
        logInfo('UniversalDocumentUpload', 'closeDialog() called after successful upload', {
            isCustomPageMode: this.isCustomPageMode
        });

        if (this.isCustomPageMode) {
            // Custom Page mode - signal dialog to close via output property
            // The Custom Page Timer watches this property and calls Exit() when it becomes true
            this.shouldClose = true;
            this.notifyOutputChanged();
            logInfo('UniversalDocumentUpload', 'shouldClose set to true - Custom Page Timer will close dialog');
        } else {
            // Quick Create form mode - do NOT close programmatically
            // Form will handle close behavior when user saves the form
            logInfo('UniversalDocumentUpload', 'Quick Create Form mode - not closing dialog (form handles close on save)');
        }
    }

    /**
     * Handle dialog close (Quick Create Form mode only - backward compatibility)
     */
    private handleClose(): void {
        logInfo('UniversalDocumentUpload', 'Dialog closed by user (Quick Create Form mode)');

        // Close Quick Create Form dialog
        if (typeof Xrm !== 'undefined' && Xrm.Navigation) {
            // Refresh parent grid if available
            try {
                const parentXrm = (window as any).parent?.Xrm;
                if (parentXrm?.Page?.getControl) {
                    const documentsGrid = parentXrm.Page.getControl('Documents');
                    if (documentsGrid?.refresh) {
                        documentsGrid.refresh();
                        logInfo('UniversalDocumentUpload', 'Documents subgrid refreshed');
                    }
                }
            } catch (error) {
                logWarn('UniversalDocumentUpload', 'Could not refresh Documents subgrid', error);
            }

            // Close dialog
            window.close();
        }
    }

    /**
     * Show error message
     */
    private showError(message: string): void {
        const errorDiv = document.createElement('div');
        errorDiv.style.cssText = 'padding: 20px; color: #a4262c; background: #fde7e9; border: 1px solid #a4262c; border-radius: 4px; margin: 10px;';
        errorDiv.innerHTML = `<strong>Error:</strong> ${message}`;
        this.container.appendChild(errorDiv);
    }

    /**
     * Show info message
     */
    private showInfo(message: string): void {
        const infoDiv = document.createElement('div');
        infoDiv.style.cssText = 'padding: 20px; color: #323130; background: #f3f2f1; border: 1px solid #8a8886; border-radius: 4px; margin: 10px;';
        infoDiv.innerHTML = `<strong>Info:</strong> ${message}`;
        this.container.appendChild(infoDiv);
    }

    /**
     * Update view - Idempotent (Phase 4 fix)
     * Waits for parameters to hydrate before initializing
     */
    public updateView(context: ComponentFramework.Context<IInputs>): void {
        this.context = context;

        // Extract parameter values (may be empty on first call while params hydrate)
        const parentEntityName = context.parameters.parentEntityName?.raw ?? "";
        const parentRecordId = context.parameters.parentRecordId?.raw ?? "";
        const containerId = context.parameters.containerId?.raw ?? "";
        const parentDisplayName = context.parameters.parentDisplayName?.raw ?? "";

        // Only initialize once when all required params are present
        if (!this._initialized) {
            if (parentEntityName && parentRecordId && containerId) {
                logInfo('UniversalDocumentUpload', 'Parameters hydrated - initializing async', {
                    parentEntityName,
                    parentRecordId,
                    containerId
                });

                this._initialized = true;

                // Reinitialize with actual parameters
                this.initializeAsync(context);
            } else {
                // Params not ready yet - wait for next updateView call
                logInfo('UniversalDocumentUpload', 'Waiting for parameters to hydrate', {
                    hasEntityName: !!parentEntityName,
                    hasRecordId: !!parentRecordId,
                    hasContainerId: !!containerId
                });
                return;
            }
        }

        // After initialization, re-render if needed
    }

    /**
     * Get outputs
     */
    public getOutputs(): IOutputs {
        return {
            shouldClose: this.shouldClose
        };
    }

    /**
     * Destroy
     */
    public destroy(): void {
        logInfo('UniversalDocumentUpload', 'Destroying PCF control');

        if (this.reactRoot) {
            this.reactRoot.unmount();
            this.reactRoot = null;
        }

        if (this.authProvider) {
            this.authProvider.clearCache();
        }
    }
}
