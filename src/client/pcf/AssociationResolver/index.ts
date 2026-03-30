/**
 * AssociationResolver PCF Control
 *
 * Allows users to select a parent entity type and record for Events.
 * After selection, auto-populates Event fields via the Field Mapping Framework.
 *
 * Supports 8 entity types:
 * - Matter, Project, Invoice, Analysis, Account, Contact, Work Assignment, Budget
 *
 * ADR Compliance:
 * - ADR-021: Fluent UI v9 with dark mode support
 * - ADR-022: React 16 APIs (ReactDOM.render, not createRoot)
 *
 * STUB: [AUTH] - S009: No authentication token handling for BFF API calls
 * Currently relies on browser session cookies. May need MSAL integration for
 * bearer token authentication when calling BFF API endpoints.
 *
 * @version 1.0.0
 */

import { IInputs, IOutputs } from './generated/ManifestTypes';
import * as React from 'react';
import * as ReactDOM from 'react-dom'; // React 16 - NOT react-dom/client
import { FluentProvider } from '@fluentui/react-components';
import { resolveThemeWithUserPreference } from '@spaarke/ui-components';
import { AssociationResolverApp } from './AssociationResolverApp';
import { getApiBaseUrl } from '../shared/utils/environmentVariables';

// Control version for footer display
const CONTROL_VERSION = '1.0.6';

/**
 * Record Type lookup reference extracted from bound property
 */
interface RecordTypeReference {
  id: string;
  name: string;
  entityLogicalName?: string;
}

/**
 * AssociationResolver PCF Control
 */
export class AssociationResolver implements ComponentFramework.StandardControl<IInputs, IOutputs> {
  private container: HTMLDivElement | null = null;
  private context: ComponentFramework.Context<IInputs>;
  private notifyOutputChanged: () => void;

  // Output values
  private _regardingRecordId = '';
  private _regardingRecordName = '';

  // Runtime-resolved API base URL (from Dataverse environment variable)
  private _resolvedApiBaseUrl = '';

  constructor() {
    // Constructor
  }

  /**
   * Initialize the control
   */
  public init(
    context: ComponentFramework.Context<IInputs>,
    notifyOutputChanged: () => void,
    state: ComponentFramework.Dictionary,
    container: HTMLDivElement
  ): void {
    this.context = context;
    this.notifyOutputChanged = notifyOutputChanged;
    this.container = container;

    // Enable responsive container sizing
    context.mode.trackContainerResize(true);

    // Resolve API base URL from Dataverse environment variable at runtime
    this.resolveApiBaseUrl();

    // Initial render
    this.renderComponent();
  }

  /**
   * Resolve BFF API base URL from Dataverse environment variable.
   * Falls back to manifest input property if env var query fails.
   * No hardcoded dev URLs — fails loudly if not configured.
   */
  private async resolveApiBaseUrl(): Promise<void> {
    try {
      // Primary: Dataverse environment variable (sprk_BffApiBaseUrl)
      this._resolvedApiBaseUrl = await getApiBaseUrl(this.context.webAPI);
    } catch {
      // Fallback: manifest input property (configured per-form, no hardcoded default)
      const manifestValue = this.context.parameters.apiBaseUrl?.raw;
      if (manifestValue) {
        this._resolvedApiBaseUrl = manifestValue;
      } else {
        console.error(
          '[AssociationResolver] BFF API base URL not configured. ' +
          'Set the sprk_BffApiBaseUrl Dataverse environment variable or configure the apiBaseUrl control property.'
        );
      }
    }
    // Re-render with resolved URL
    this.renderComponent();
  }

  /**
   * Update view when context changes
   */
  public updateView(context: ComponentFramework.Context<IInputs>): void {
    this.context = context;
    this.renderComponent();
  }

  /**
   * Get output values
   */
  public getOutputs(): IOutputs {
    return {
      regardingRecordId: this._regardingRecordId,
      regardingRecordName: this._regardingRecordName,
    };
  }

  /**
   * Cleanup on destroy
   */
  public destroy(): void {
    // React 16: unmountComponentAtNode (NOT root.unmount())
    if (this.container) {
      ReactDOM.unmountComponentAtNode(this.container);
      this.container = null;
    }
  }

  /**
   * Handle record selection from child component
   */
  private handleRecordSelected = (recordId: string, recordName: string): void => {
    this._regardingRecordId = recordId;
    this._regardingRecordName = recordName;
    this.notifyOutputChanged();
  };

  /**
   * Extract Record Type reference from bound lookup property
   * For Lookup.Simple, raw value is an EntityReference with id, name, entityType
   */
  private getRecordTypeReference(): RecordTypeReference | null {
    const rawValue = this.context.parameters.regardingRecordType?.raw;

    // Lookup.Simple raw value is an EntityReference array or single reference
    if (!rawValue) {
      return null;
    }

    // Handle both array (multiple) and single reference formats
    const ref = Array.isArray(rawValue) ? rawValue[0] : rawValue;
    if (!ref || !ref.id) {
      return null;
    }

    return {
      id: ref.id,
      name: ref.name || '',
      entityLogicalName: ref.entityType || 'sprk_recordtype_ref',
    };
  }

  /**
   * Render the React component
   */
  private renderComponent(): void {
    if (!this.container) return;

    const theme = resolveThemeWithUserPreference(this.context);
    const regardingRecordType = this.getRecordTypeReference();
    const apiBaseUrl = this._resolvedApiBaseUrl || this.context.parameters.apiBaseUrl?.raw || '';

    // React 16: ReactDOM.render (NOT createRoot().render())
    ReactDOM.render(
      React.createElement(
        FluentProvider,
        { theme, style: { height: '100%', width: '100%' } },
        React.createElement(AssociationResolverApp, {
          context: this.context,
          regardingRecordType,
          apiBaseUrl,
          onRecordSelected: this.handleRecordSelected,
          version: CONTROL_VERSION,
        })
      ),
      this.container
    );
  }
}
