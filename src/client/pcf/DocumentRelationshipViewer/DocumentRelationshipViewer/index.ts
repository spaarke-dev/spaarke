import { IInputs, IOutputs } from "./generated/ManifestTypes";
import {
    DocumentRelationshipViewer as DocumentRelationshipViewerComponent,
    IDocumentRelationshipViewerProps,
} from "./DocumentRelationshipViewer";
import { MsalAuthProvider } from "./services/auth/MsalAuthProvider";
import * as React from "react";

/**
 * DocumentRelationshipViewer PCF Control
 *
 * Displays an interactive graph visualization of document relationships
 * based on vector similarity from Azure AI Search.
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
    private authProvider: MsalAuthProvider;

    constructor() {
        // Initialize MSAL auth provider singleton
        this.authProvider = MsalAuthProvider.getInstance();
    }

    /**
     * Initialize the control instance.
     */
    public init(
        context: ComponentFramework.Context<IInputs>,
        notifyOutputChanged: () => void,
        state: ComponentFramework.Dictionary
    ): void {
        this.notifyOutputChanged = notifyOutputChanged;

        // Initialize MSAL asynchronously (don't block init)
        void this.authProvider.initialize().catch((error) => {
            console.error("[DocumentRelationshipViewer] MSAL initialization failed:", error);
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
            authProvider: this.authProvider,
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
