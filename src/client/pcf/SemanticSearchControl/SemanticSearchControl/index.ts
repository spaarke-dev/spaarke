import { IInputs, IOutputs } from './generated/ManifestTypes';
import * as React from 'react';
import { FluentProvider, Theme, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { resolveTheme, setupThemeListener } from './services/ThemeService';
import { SemanticSearchControl as SemanticSearchControlComponent } from './SemanticSearchControl';
import { ISemanticSearchControlProps } from './types';
import { initializeAuth } from './authInit';
import { getEnvironmentVariable, getApiBaseUrl } from '../../shared/utils/environmentVariables';

/**
 * SemanticSearchControl PCF Control
 *
 * Provides semantic document search with natural language queries and filters.
 * Integrates with the BFF Semantic Search API.
 *
 * Authentication configuration is resolved at runtime from Dataverse environment
 * variables (sprk_BffApiBaseUrl, sprk_MsalClientId, sprk_BffApiAppId, sprk_TenantId)
 * and PCF manifest parameters. No hardcoded CLIENT_ID or BFF URL in source.
 *
 * Follows:
 * - ADR-006: PCF for all custom UI
 * - ADR-021: Fluent UI v9 with dark mode support
 * - ADR-022: React 16 APIs with platform libraries
 */
export class SemanticSearchControl implements ComponentFramework.ReactControl<IInputs, IOutputs> {
  private notifyOutputChanged: () => void;
  private selectedDocumentId: string | undefined;
  private _theme: Theme = webLightTheme;
  private _cleanupThemeListener?: () => void;
  private _context: ComponentFramework.Context<IInputs>;
  private _authInitialized = false;
  private _resolvedApiBaseUrl = '';

  constructor() {
    // Constructor - initialization happens in init()
  }

  /**
   * Initialize the control instance.
   * Resolves auth configuration from Dataverse environment variables at runtime.
   */
  public init(
    context: ComponentFramework.Context<IInputs>,
    notifyOutputChanged: () => void,
    state: ComponentFramework.Dictionary
  ): void {
    this.notifyOutputChanged = notifyOutputChanged;
    this._context = context;

    // Resolve initial theme from context
    this._theme = resolveTheme(context);

    // Set up theme change listener for dynamic updates
    this._cleanupThemeListener = setupThemeListener(isDark => {
      this._theme = isDark ? webDarkTheme : webLightTheme;
      // Force re-render with new theme
      this.notifyOutputChanged();
    }, context);

    // Resolve auth configuration from Dataverse environment variables at runtime.
    // Manifest parameters serve as optional overrides; environment variables are canonical.
    this.resolveAndInitAuth(context);
  }

  /**
   * Resolve auth configuration from Dataverse environment variables and
   * initialize @spaarke/auth. Falls back to manifest parameters if set.
   */
  private resolveAndInitAuth(context: ComponentFramework.Context<IInputs>): void {
    const webApi = context.webAPI;

    // Read manifest parameter overrides (may be empty)
    const manifestApiBaseUrl = context.parameters.apiBaseUrl?.raw ?? '';
    const manifestTenantId = context.parameters.tenantId?.raw ?? '';
    const manifestClientAppId = context.parameters.clientAppId?.raw ?? '';
    const manifestBffAppId = context.parameters.bffAppId?.raw ?? '';

    // Resolve Dataverse org URL for redirect URI (runtime, no hardcoding)
    let dataverseUrl: string;
    try {
      if (typeof Xrm !== 'undefined' && Xrm.Utility?.getGlobalContext) {
        dataverseUrl = Xrm.Utility.getGlobalContext().getClientUrl();
      } else {
        dataverseUrl = window.location.origin;
      }
    } catch {
      dataverseUrl = window.location.origin;
    }

    // Resolve config: prefer manifest parameters when set, otherwise query env vars
    const resolveConfig = async (): Promise<{
      tenantId: string;
      clientAppId: string;
      bffAppId: string;
      apiBaseUrl: string;
    }> => {
      // BFF API Base URL -- from env var (canonical) or manifest override
      const apiBaseUrl = manifestApiBaseUrl || (await getApiBaseUrl(webApi));

      // Tenant ID -- from manifest or env var
      const tenantId = manifestTenantId || (await getEnvironmentVariable(webApi, 'sprk_TenantId'));

      // Client App ID -- from manifest or env var
      const clientAppId =
        manifestClientAppId || (await getEnvironmentVariable(webApi, 'sprk_MsalClientId'));

      // BFF App ID -- from manifest or env var
      const bffAppId =
        manifestBffAppId || (await getEnvironmentVariable(webApi, 'sprk_BffApiAppId'));

      return { tenantId, clientAppId, bffAppId, apiBaseUrl };
    };

    void resolveConfig()
      .then(async ({ tenantId, clientAppId, bffAppId, apiBaseUrl }) => {
        this._resolvedApiBaseUrl = apiBaseUrl;
        await initializeAuth(tenantId, clientAppId, bffAppId, apiBaseUrl, dataverseUrl);
        this._authInitialized = true;
        console.info('[SemanticSearchControl] Auth initialized with runtime config');
        // Force re-render so the component picks up auth-ready state
        this.notifyOutputChanged();
        return undefined;
      })
      .catch(error => {
        console.error('[SemanticSearchControl] Auth initialization failed:', error);
      });
  }

  /**
   * Called when any value in the property bag has changed.
   * Returns a React element (ReactControl pattern - ADR-022).
   *
   * Wraps content in FluentProvider with theme from context.
   */
  public updateView(context: ComponentFramework.Context<IInputs>): React.ReactElement {
    // Update context reference and re-resolve theme
    this._context = context;
    this._theme = resolveTheme(context);

    // Create props for the main component
    const props: ISemanticSearchControlProps = {
      context,
      notifyOutputChanged: this.notifyOutputChanged,
      onDocumentSelect: this.handleDocumentSelect.bind(this),
      isDarkMode: this._theme === webDarkTheme,
      authInitialized: this._authInitialized,
      resolvedApiBaseUrl: this._resolvedApiBaseUrl,
    };

    // Create the main SemanticSearchControl component
    const content = React.createElement(SemanticSearchControlComponent, props);

    // Wrap in FluentProvider with resolved theme (ADR-021).
    // Explicit width/height/flex ensure the provider fills the PCF-allocated container.
    return React.createElement(
      FluentProvider,
      {
        theme: this._theme,
        style: {
          width: '100%',
          height: '100%',
          display: 'flex',
          flexDirection: 'column',
        },
      },
      content
    );
  }

  /**
   * Handle document selection from search results.
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
    // Clean up theme listener
    this._cleanupThemeListener?.();
  }
}
