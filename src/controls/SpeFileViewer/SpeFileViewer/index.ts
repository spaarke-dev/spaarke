/**
 * SPE File Viewer PCF Control
 *
 * Entry point that wires together:
 * - MSAL authentication (with named scope api://<BFF_APP_ID>/SDAP.Access)
 * - BFF API client
 * - React FilePreview component
 */

import { IInputs, IOutputs } from "./generated/ManifestTypes";
import * as React from 'react';
import { createRoot, Root } from 'react-dom/client';
import { AuthService } from './AuthService';
import { FilePreview } from './FilePreview';
import { v4 as uuidv4 } from 'uuid';

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

            // Initial render
            this.renderControl(context);

        } catch (error) {
            console.error('[SpeFileViewer] Initialization failed:', error);
            this.renderError(error instanceof Error ? error.message : String(error));
        }
    }

    /**
     * Update view when context changes
     * - Detects documentId changes
     * - Re-renders React component
     */
    public updateView(context: ComponentFramework.Context<IInputs>): void {
        // Re-render with updated context
        this.renderControl(context);
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

        console.log(`[SpeFileViewer] Rendering preview for document: ${documentId || '(none)'}`);

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
                correlationId: this.correlationId
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
