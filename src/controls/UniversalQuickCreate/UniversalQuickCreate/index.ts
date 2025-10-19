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
 * @version 2.2.0
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

    // Services
    private authProvider: MsalAuthProvider;
    private multiFileService: MultiFileUploadService | null = null;
    private documentRecordService: DocumentRecordService | null = null;

    // Parent Context (from Custom Page parameters)
    private parentContext: ParentContext | null = null;

    // UI State
    private selectedFiles: File[] = [];
    private isUploading = false;

    constructor() {
        logInfo('UniversalDocumentUpload', 'Constructor called');
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
        logInfo('UniversalDocumentUpload', 'Initializing PCF control v2.2.0');

        this.context = context;
        this.notifyOutputChanged = notifyOutputChanged;

        // Create container
        this.container = document.createElement("div");
        this.container.className = "universal-document-upload-container";
        container.appendChild(this.container);

        // Version badge for debugging
        const versionBadge = document.createElement("div");
        versionBadge.textContent = "✓ UNIVERSAL DOCUMENT UPLOAD V2.2.0 - METADATA FIX - " + new Date().toLocaleTimeString();
        versionBadge.style.cssText = "padding: 12px; background: #107c10; color: white; font-size: 14px; font-weight: bold; border-radius: 4px; margin-bottom: 8px; text-align: center;";
        this.container.appendChild(versionBadge);

        // Create React root
        this.reactRoot = ReactDOM.createRoot(this.container);

        // Async initialization
        this.initializeAsync(context);
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
     * Initialize services
     */
    private initializeServices(context: ComponentFramework.Context<IInputs>): void {
        // Get API base URL
        const rawApiUrl = context.parameters.sdapApiBaseUrl?.raw || 'spe-api-dev-67e2xz.azurewebsites.net/api';
        const apiBaseUrl = rawApiUrl.startsWith('http://') || rawApiUrl.startsWith('https://')
            ? rawApiUrl
            : `https://${rawApiUrl}`;

        logInfo('UniversalDocumentUpload', 'Initializing services', { apiBaseUrl });

        // Create services
        const apiClient = SdapApiClientFactory.create(apiBaseUrl);
        const fileUploadService = new FileUploadService(apiClient);
        this.multiFileService = new MultiFileUploadService(fileUploadService);
        this.documentRecordService = new DocumentRecordService(context); // Pass context for webAPI access

        logInfo('UniversalDocumentUpload', 'Services initialized');
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
                onClose: this.handleClose.bind(this)
            })
        );
    }

    /**
     * Handle dialog close
     */
    private handleClose(): void {
        logInfo('UniversalDocumentUpload', 'Dialog closed by user');

        // Close Custom Page dialog
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
     * Update view
     */
    public updateView(context: ComponentFramework.Context<IInputs>): void {
        this.context = context;
        // Re-render if needed
    }

    /**
     * Get outputs
     */
    public getOutputs(): IOutputs {
        return {};
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
