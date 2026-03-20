import { IInputs, IOutputs } from './generated/ManifestTypes';
import {
  DocumentRelationshipViewer as DocumentRelationshipViewerComponent,
  IDocumentRelationshipViewerProps,
} from './DocumentRelationshipViewer';
import { initializeAuth } from './authInit';
import {
  getApiBaseUrl,
  getMsalClientId,
  getTenantId,
  getBffApiAppId,
} from '../../shared/utils/environmentVariables';
import * as React from 'react';

/**
 * Resolve the Dataverse org URL from Xrm global context.
 * Returns empty string if Xrm is not available (e.g., in test harness).
 */
function getDataverseUrl(): string {
  try {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const xrm = (window as any).Xrm;
    const url = xrm?.Utility?.getGlobalContext?.()?.getClientUrl?.() as string | undefined;
    return url || '';
  } catch {
    return '';
  }
}

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
  private authInitialized = false;

  /**
   * Initialize the control instance.
   * Resolves auth config from Dataverse environment variables at runtime,
   * then initializes @spaarke/auth.
   */
  public init(
    context: ComponentFramework.Context<IInputs>,
    notifyOutputChanged: () => void,
    state: ComponentFramework.Dictionary
  ): void {
    this.notifyOutputChanged = notifyOutputChanged;

    // Resolve all auth configuration from Dataverse environment variables at runtime
    void this.initializeAuthFromEnvVars(context)
      .then(() => {
        this.authInitialized = true;
        console.info('[DocumentRelationshipViewer] @spaarke/auth initialized from environment variables');
        return undefined;
      })
      .catch((error: unknown) => {
        console.error('[DocumentRelationshipViewer] @spaarke/auth initialization failed:', error);
      });
  }

  /**
   * Resolve auth configuration from Dataverse environment variables and initialize @spaarke/auth.
   */
  private async initializeAuthFromEnvVars(
    context: ComponentFramework.Context<IInputs>
  ): Promise<void> {
    const webApi = context.webAPI;

    // Resolve all config from Dataverse environment variables (fail loudly if missing)
    const [tenantId, clientAppId, bffAppId, bffApiUrl] = await Promise.all([
      getTenantId(webApi),
      getMsalClientId(webApi),
      getBffApiAppId(webApi),
      getApiBaseUrl(webApi),
    ]);

    // Resolve Dataverse org URL from Xrm context for redirect URI
    const dataverseUrl = getDataverseUrl();
    if (!dataverseUrl) {
      throw new Error(
        '[DocumentRelationshipViewer] Cannot resolve Dataverse URL from Xrm.Utility.getGlobalContext().getClientUrl(). ' +
        'Ensure the control is running inside a Dataverse model-driven app.'
      );
    }

    await initializeAuth(tenantId, clientAppId, bffAppId, bffApiUrl, dataverseUrl);
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
