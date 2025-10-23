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
 * - Creates Document records via DocumentRecordService using Xrm.WebApi
 *
 * ADR Compliance:
 * - ADR-001: Fluent UI v9 Components
 * - ADR-002: TypeScript Strict Mode
 * - ADR-003: Separation of Concerns
 * - ADR-010: Configuration Over Code
 *
 * @version 2.0.0.0
 */

// Declare global Xrm (available at runtime in Dataverse)
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

type ParentContextLoadState = "missing" | "unchanged" | "updated";

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
    private waitingBanner: HTMLDivElement | null = null;
    private isBootstrapInProgress = false;
    private isMsalInitialized = false;

    // UI State
    private selectedFiles: File[] = [];
    private isUploading = false;

    constructor() {
        logInfo('UniversalDocumentUploadPCF', 'Constructor called');
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
        logInfo('UniversalDocumentUploadPCF', 'Initializing PCF control v2.0.0');

        this.context = context;
        this.notifyOutputChanged = notifyOutputChanged;

        // Create container
        this.container = document.createElement("div");
        this.container.className = "universal-document-upload-container";
        container.appendChild(this.container);

        // Version badge for debugging
        const versionBadge = document.createElement("div");
        versionBadge.textContent = "✓ UNIVERSAL DOCUMENT UPLOAD V2.0.0 - CUSTOM PAGE - " + new Date().toLocaleTimeString();
        versionBadge.style.cssText = "padding: 12px; background: #107c10; color: white; font-size: 14px; font-weight: bold; border-radius: 4px; margin-bottom: 8px; text-align: center;";
        this.container.appendChild(versionBadge);

        // Create React root
        this.reactRoot = ReactDOM.createRoot(this.container);

    // Async initialization
    void this.initializeAsync(context);
    }

    /**
     * Async initialization
     */
    private async initializeAsync(context: ComponentFramework.Context<IInputs>): Promise<void> {
        try {
            await this.bootstrapAsync(context, true);
        } catch (error) {
            logError('UniversalDocumentUploadPCF', 'Initialization failed', error);
            this.showError((error as Error).message);
        }
    }

    /**
     * Attempt to fully initialize the control when the host provides context.
     */
    private async bootstrapAsync(
        context: ComponentFramework.Context<IInputs>,
        forceServiceInitialization = false
    ): Promise<void> {
        const contextState = this.updateParentContext(context);

        if (contextState === "missing") {
            logWarn('UniversalDocumentUploadPCF', 'Parent context not yet available - waiting for Custom Page parameters');
            return;
        }

        if (this.isBootstrapInProgress) {
            logInfo('UniversalDocumentUploadPCF', 'Bootstrap already in progress - skipping duplicate call');
            return;
        }

        this.isBootstrapInProgress = true;

        try {
            this.validateParentEntity();

            if (!this.isMsalInitialized) {
                await this.initializeMsalAsync();
            }

            if (forceServiceInitialization || contextState === "updated" || !this.multiFileService || !this.documentRecordService) {
                this.initializeServices(context);
            }

            this.renderReactComponent();

            logInfo('UniversalDocumentUploadPCF', 'Bootstrap complete', {
                parentEntityName: this.parentContext?.parentEntityName,
                parentRecordId: this.parentContext?.parentRecordId,
                containerId: this.parentContext?.containerId
            });

        } catch (error) {
            logError('UniversalDocumentUploadPCF', 'Bootstrap failed', error);
            this.showError((error as Error).message);
        } finally {
            this.isBootstrapInProgress = false;
        }
    }

    /**
     * Evaluate incoming parameters and determine whether context is ready/changed.
     */
    private updateParentContext(context: ComponentFramework.Context<IInputs>): ParentContextLoadState {
        logInfo('UniversalDocumentUploadPCF', 'Evaluating parent context from parameters');

        const parentEntityNameRaw = context.parameters.parentEntityName?.raw?.trim();
        const parentRecordIdRaw = context.parameters.parentRecordId?.raw?.trim();
        const containerIdRaw = context.parameters.containerId?.raw?.trim();
        const parentDisplayNameRaw = context.parameters.parentDisplayName?.raw?.trim();

        logInfo('UniversalDocumentUploadPCF', 'Raw parameter values', {
            parentEntityName: parentEntityNameRaw,
            parentRecordId: parentRecordIdRaw,
            containerId: containerIdRaw,
            parentDisplayName: parentDisplayNameRaw
        });

        if (!parentEntityNameRaw || !parentRecordIdRaw || !containerIdRaw) {
            this.displayWaitingForContextMessage();
            return "missing";
        }

        this.removeWaitingBanner();

        const parentEntityName = parentEntityNameRaw;
        const cleanParentRecordId = parentRecordIdRaw.replace(/[{}]/g, '').toLowerCase();
        const containerId = containerIdRaw;
        const parentDisplayName = parentDisplayNameRaw || parentEntityName;

        const guidRegex = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
        if (!guidRegex.test(cleanParentRecordId)) {
            throw new Error(
                `Invalid parentRecordId format: "${cleanParentRecordId}". ` +
                'Expected GUID format without curly braces (e.g., "abc12345-def6-7890-ghij-klmnopqrstuv")'
            );
        }

        if (
            this.parentContext &&
            this.parentContext.parentEntityName === parentEntityName &&
            this.parentContext.parentRecordId === cleanParentRecordId &&
            this.parentContext.containerId === containerId &&
            this.parentContext.parentDisplayName === parentDisplayName
        ) {
            return "unchanged";
        }

        this.parentContext = {
            parentEntityName,
            parentRecordId: cleanParentRecordId,
            containerId,
            parentDisplayName
        };

        logInfo('UniversalDocumentUploadPCF', 'Parent context loaded', this.parentContext);
        return "updated";
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
        logInfo('UniversalDocumentUploadPCF', 'Entity configuration found', config);
    }

    /**
     * Initialize MSAL authentication
     */
    private async initializeMsalAsync(): Promise<void> {
        try {
            logInfo('UniversalDocumentUploadPCF', 'Initializing MSAL authentication...');

            this.authProvider = MsalAuthProvider.getInstance();
            await this.authProvider.initialize();
            this.isMsalInitialized = true;

            logInfo('UniversalDocumentUploadPCF', 'MSAL authentication initialized ✅');

            if (this.authProvider.isAuthenticated()) {
                const accountInfo = this.authProvider.getAccountDebugInfo();
                logInfo('UniversalDocumentUploadPCF', 'User authenticated', accountInfo);
            }

        } catch (error) {
            logError('UniversalDocumentUploadPCF', 'MSAL initialization failed', error);
            this.isMsalInitialized = false;
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

        logInfo('UniversalDocumentUploadPCF', 'Initializing services (Phase 7)', { apiBaseUrl, navMapBaseUrl });

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

        logInfo('UniversalDocumentUploadPCF', 'Services initialized (including NavMapClient)');
    }

    /**
     * Render React component
     */
    private renderReactComponent(): void {
        logInfo('UniversalDocumentUploadPCF', 'Rendering React component');

        if (!this.reactRoot || !this.parentContext || !this.multiFileService || !this.documentRecordService) {
            logError('UniversalDocumentUploadPCF', 'Cannot render: missing dependencies', {
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
        logInfo('UniversalDocumentUploadPCF', 'Dialog closed by user');

        // Close Custom Page dialog
        if (typeof Xrm !== 'undefined' && Xrm.Navigation) {
            // Refresh parent grid if available
            try {
                const parentXrm = (window as any).parent?.Xrm;
                if (parentXrm?.Page?.getControl) {
                    const documentsGrid = parentXrm.Page.getControl('Documents');
                    if (documentsGrid?.refresh) {
                        documentsGrid.refresh();
                        logInfo('UniversalDocumentUploadPCF', 'Documents subgrid refreshed');
                    }
                }
            } catch (error) {
                logWarn('UniversalDocumentUploadPCF', 'Could not refresh Documents subgrid', error);
            }

            // Close dialog
            window.close();
        }
    }

    /**
     * Show error message
     */
    private showError(message: string): void {
        this.removeWaitingBanner();
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
     * Display banner while waiting for host context to arrive
     */
    private displayWaitingForContextMessage(): void {
        if (!this.waitingBanner) {
            this.waitingBanner = document.createElement('div');
            this.waitingBanner.style.cssText = 'padding: 20px; color: #323130; background: #f3f2f1; border: 1px solid #8a8886; border-radius: 4px; margin: 10px;';
            this.container.appendChild(this.waitingBanner);
        }

        this.waitingBanner.innerHTML = '<strong>Loading:</strong> Waiting for host to provide upload context...';
    }

    /**
     * Remove pending banner when context is available
     */
    private removeWaitingBanner(): void {
        if (this.waitingBanner?.parentElement) {
            this.waitingBanner.parentElement.removeChild(this.waitingBanner);
        }

        this.waitingBanner = null;
    }

    /**
     * Update view
     */
    public updateView(context: ComponentFramework.Context<IInputs>): void {
        this.context = context;
        void this.bootstrapAsync(context);
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
        logInfo('UniversalDocumentUploadPCF', 'Destroying PCF control');

        if (this.reactRoot) {
            this.reactRoot.unmount();
            this.reactRoot = null;
        }

        if (this.authProvider) {
            this.authProvider.clearCache();
        }
    }
}
