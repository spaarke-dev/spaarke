import { IInputs, IOutputs } from "./generated/ManifestTypes";
import {
    DocumentRelationshipViewer as DocumentRelationshipViewerComponent,
    IDocumentRelationshipViewerProps,
} from "./DocumentRelationshipViewer";
import { initializeAuth } from "./authInit";
import * as React from "react";

/**
 * DocumentRelationshipViewer PCF Control
 *
 * Displays an interactive graph visualization of document relationships
 * based on vector similarity from Azure AI Search.
 *
 * Authentication is handled by @spaarke/auth (initialized via authInit.ts).
 * API calls use authenticatedFetch() for transparent token management.
 *
 * Follows:
 * - ADR-006: PCF for all custom UI
 * - ADR-021: Fluent UI v9 with dark mode support
 * - ADR-022: React 16 APIs with platform libraries
 */
export class DocumentRelationshipViewer
    implements ComponentFramework.ReactControl<IInputs, IOutputs>
{
    private notifyOutputChanged: () => void;
    private selectedDocumentId: string | undefined;
    private authInitialized = false;

    /**
     * Initialize the control instance.
     * Sets up @spaarke/auth with configuration from PCF manifest parameters.
     */
    public init(
        context: ComponentFramework.Context<IInputs>,
        notifyOutputChanged: () => void,
        state: ComponentFramework.Dictionary
    ): void {
        this.notifyOutputChanged = notifyOutputChanged;

        // Extract auth configuration from manifest parameters
        // These default to the dev environment values from the original msalConfig.ts
        const tenantId = context.parameters.tenantId?.raw ?? "a221a95e-6abc-4434-aecc-e48338a1b2f2";
        const clientAppId = (context.parameters as Record<string, { raw?: string }>).clientAppId?.raw
            ?? "170c98e1-d486-4355-bcbe-170454e0207c";
        const bffAppId = (context.parameters as Record<string, { raw?: string }>).bffAppId?.raw
            ?? "1e40baad-e065-4aea-a8d4-4b7ab273458c";
        const apiBaseUrl = context.parameters.apiBaseUrl?.raw ?? "https://spe-api-dev-67e2xz.azurewebsites.net";

        // Initialize @spaarke/auth asynchronously (don't block init)
        void initializeAuth(tenantId, clientAppId, bffAppId, apiBaseUrl)
            .then(() => {
                this.authInitialized = true;
                console.info("[DocumentRelationshipViewer] @spaarke/auth initialized");
            })
            .catch((error) => {
                console.error("[DocumentRelationshipViewer] @spaarke/auth initialization failed:", error);
            });
    }

    /**
     * Called when any value in the property bag has changed.
     * Returns a React element (ReactControl pattern - ADR-022).
     */
    public updateView(
        context: ComponentFramework.Context<IInputs>
    ): React.ReactElement {
        const props: IDocumentRelationshipViewerProps = {
            context,
            notifyOutputChanged: this.notifyOutputChanged,
            onDocumentSelect: this.handleDocumentSelect.bind(this),
        };

        return React.createElement(DocumentRelationshipViewerComponent, props);
    }

    /**
     * Handle document selection from the graph visualization.
     * Updates the output property for Power Apps consumption.
     */
    private handleDocumentSelect(documentId: string): void {
        this.selectedDocumentId = documentId;
        this.notifyOutputChanged();
    }

    /**
     * Return output properties for Power Apps binding.
     */
    public getOutputs(): IOutputs {
        return {
            selectedDocumentId: this.selectedDocumentId,
        };
    }

    /**
     * Cleanup when control is removed from DOM.
     */
    public destroy(): void {
        // No cleanup needed for this control
    }
}
