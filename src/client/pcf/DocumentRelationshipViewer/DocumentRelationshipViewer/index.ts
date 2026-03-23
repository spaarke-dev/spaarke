import { IInputs, IOutputs } from './generated/ManifestTypes';
import {
  DocumentRelationshipViewer as DocumentRelationshipViewerComponent,
  IDocumentRelationshipViewerProps,
} from './DocumentRelationshipViewer';
import * as React from 'react';

/**
 * DocumentRelationshipViewer PCF Control
 *
 * Displays an interactive graph visualization of document relationships
 * based on vector similarity from Azure AI Search.
 *
 * Authentication is handled by @spaarke/auth (initialized via authInit.ts).
 * All configuration is resolved at runtime from Dataverse environment variables:
 *   - sprk_MsalClientId -> MSAL Client Application ID
 *   - sprk_TenantId -> Azure AD Tenant ID
 *   - sprk_BffApiAppId -> BFF Application ID (for scope construction)
 *   - sprk_BffApiBaseUrl -> BFF API base URL
 *
 * Follows:
 * - ADR-006: PCF for all custom UI
 * - ADR-021: Fluent UI v9 with dark mode support
 * - ADR-022: React 16 APIs with platform libraries
 */
export class DocumentRelationshipViewer implements ComponentFramework.ReactControl<IInputs, IOutputs> {
  private notifyOutputChanged: () => void;
  private selectedDocumentId: string | undefined;

  /**
   * Initialize the control instance.
   * Auth is initialized by the React component via useEffect + useState.
   * This pattern ensures re-renders after async auth completion (ReactControl
   * cannot rely on notifyOutputChanged() to trigger updateView()).
   * See: .claude/patterns/pcf/control-initialization.md
   */
  public init(
    context: ComponentFramework.Context<IInputs>,
    notifyOutputChanged: () => void,
    state: ComponentFramework.Dictionary
  ): void {
    this.notifyOutputChanged = notifyOutputChanged;
  }

  /**
   * Called when any value in the property bag has changed.
   * Returns a React element (ReactControl pattern - ADR-022).
   */
  public updateView(context: ComponentFramework.Context<IInputs>): React.ReactElement {
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
